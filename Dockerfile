# ============================================================
# 階段一：建置映像
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 複製 csproj 並還原相依性（優化 Docker 快取層）
COPY MusicBot2/MusicBot2.csproj MusicBot2/
RUN dotnet restore MusicBot2/MusicBot2.csproj -r linux-x64

# 複製所有專案檔案並發布
COPY MusicBot2/ MusicBot2/
WORKDIR /src/MusicBot2
RUN dotnet publish MusicBot2.csproj \
    -c Release \
    -o /app/publish \
    -r linux-x64 \
    --self-contained false \
    /p:Platform=x64

# ============================================================
# 階段二：執行映像
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# 一次性安裝所有系統依賴
RUN apt-get update && \
    apt-get install -y \
        ffmpeg \
        python3 \
        python3-pip \
        curl \
        wget \
        unzip \
        libsodium23 \
        libsodium-dev \
        libopus0 \
        libopus-dev \
        libssl3 \
        ca-certificates && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# ============================================================
# 安裝 libdave
# 使用方式：請先在本機下載 zip，放在 Dockerfile 同層目錄：
#   wget "https://github.com/discord/libdave/releases/download/v1.1.1%2Fcpp/libdave-Linux-X64-boringssl.zip"
# ============================================================
COPY libdave-Linux-X64-boringssl.zip /tmp/libdave.zip

RUN echo "=== 解壓縮 libdave ===" && \
    unzip -q /tmp/libdave.zip -d /tmp/libdave && \
    echo "解壓縮完成，內容如下：" && \
    find /tmp/libdave -type f && \
    \
    # 尋找 .so 檔案並安裝到系統路徑
    echo "=== 安裝 libdave.so 到系統路徑 ===" && \
    find /tmp/libdave -name "*.so*" -type f | head -1 | \
        xargs -I{} cp {} /usr/lib/x86_64-linux-gnu/libdave.so && \
    chmod 755 /usr/lib/x86_64-linux-gnu/libdave.so && \
    ldconfig && \
    \
    # 同時複製到應用程式目錄（.NET NativeLibrary 優先從此載入）
    echo "=== 複製 libdave.so 到應用程式目錄 ===" && \
    cp /usr/lib/x86_64-linux-gnu/libdave.so /app/libdave.so && \
    mkdir -p /app/runtimes/linux-x64/native && \
    cp /usr/lib/x86_64-linux-gnu/libdave.so /app/runtimes/linux-x64/native/libdave.so && \
    chmod 755 /app/libdave.so /app/runtimes/linux-x64/native/libdave.so && \
    \
    # 清理暫存
    rm -rf /tmp/libdave /tmp/libdave.zip && \
    echo "=== libdave 安裝完成 ==="

# 安裝最新版 yt-dlp
RUN pip3 install --break-system-packages --upgrade yt-dlp && \
    echo "yt-dlp 版本：$(yt-dlp --version)"

# 驗證 FFmpeg
RUN echo "FFmpeg 版本：$(ffmpeg -version | head -n 1)"

# 複製建置產物
COPY --from=build /app/publish .

# 驗證所有語音函式庫是否正確安裝
RUN ldconfig && \
    echo "=== 驗證語音函式庫 ===" && \
    echo -n "libsodium：" && (ldconfig -p | grep libsodium | head -1 || echo "❌ 找不到") && \
    echo -n "libopus：  " && (ldconfig -p | grep libopus   | head -1 || echo "❌ 找不到") && \
    echo -n "libdave：  " && (ldconfig -p | grep libdave   | head -1 || echo "❌ 找不到") && \
    echo "=== 驗證應用程式目錄 ===" && \
    ls -lh /app/libdave.so && \
    ls -lh /app/runtimes/linux-x64/native/libdave.so && \
    echo "=== 所有函式庫驗證完成 ==="

# 建立必要資料夾
RUN mkdir -p temp cookies

# 設定函式庫搜尋路徑（應用程式目錄優先）
ENV LD_LIBRARY_PATH=/app:/app/runtimes/linux-x64/native:/usr/lib/x86_64-linux-gnu:/usr/lib:/lib/x86_64-linux-gnu

# 建立啟動腳本（啟動時再次確認函式庫狀態）
RUN echo '#!/bin/bash' > /app/start.sh && \
    echo 'set -e' >> /app/start.sh && \
    echo 'echo "=== 啟動前函式庫檢查 ==="' >> /app/start.sh && \
    echo 'ldconfig -p | grep libsodium || echo "⚠️  libsodium 未找到"' >> /app/start.sh && \
    echo 'ldconfig -p | grep libopus   || echo "⚠️  libopus 未找到"' >> /app/start.sh && \
    echo 'ldconfig -p | grep libdave   || echo "⚠️  libdave 未找到"' >> /app/start.sh && \
    echo 'echo "LD_LIBRARY_PATH=${LD_LIBRARY_PATH}"' >> /app/start.sh && \
    echo 'echo "=== 啟動 MusicBot2 ==="' >> /app/start.sh && \
    echo 'exec dotnet MusicBot2.dll' >> /app/start.sh && \
    chmod +x /app/start.sh

ENTRYPOINT ["/app/start.sh"]