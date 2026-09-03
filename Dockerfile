# ============================================================
# 階段一：編譯 .NET 應用程式
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 還原 NuGet 套件（優化 Docker 快取層）
COPY MusicBot2/MusicBot2.csproj MusicBot2/
RUN dotnet restore MusicBot2/MusicBot2.csproj -r linux-x64

# 編譯並發布
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

# 安裝執行期所需套件
RUN apt-get update && \
    apt-get install -y \
        ffmpeg \
        python3 \
        python3-pip \
        libsodium23 \
        libopus0 \
        libssl3 \
        ca-certificates && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# 安裝最新版 yt-dlp
RUN pip3 install --break-system-packages --upgrade yt-dlp && \
    echo "yt-dlp 版本：$(yt-dlp --version)"

# 複製 .NET 發布產物
COPY --from=build /app/publish .

# 驗證所有函式庫
RUN ldconfig && \
    echo "=== 函式庫驗證 ===" && \
    echo -n "libsodium：" && (ldconfig -p | grep libsodium | head -1 || echo "❌ 找不到") && \
    echo -n "libopus：  " && (ldconfig -p | grep libopus   | head -1 || echo "❌ 找不到")

# ============================================================
# 修正 opus 載入問題
# ============================================================
RUN OPUS_REAL=$(ldconfig -p | grep libopus | awk '{print $NF}' | head -1) && \
    echo "系統 opus 實際路徑：$OPUS_REAL" && \
    cp "$OPUS_REAL" /app/libopus.so && \
    ln -sf /app/libopus.so /app/opus.so && \
    ln -sf /app/libopus.so /app/libopus && \
    ln -sf /app/libopus.so /app/opus && \
    echo "opus 軟連結建立完成"

# 修正 libsodium 載入問題
RUN SODIUM_REAL=$(ldconfig -p | grep libsodium | awk '{print $NF}' | head -1) && \
    echo "系統 sodium 實際路徑：$SODIUM_REAL" && \
    cp "$SODIUM_REAL" /app/libsodium.so && \
    ln -sf /app/libsodium.so /app/sodium.so && \
    ln -sf /app/libsodium.so /app/libsodium && \
    ln -sf /app/libsodium.so /app/sodium && \
    echo "sodium 軟連結建立完成"

# 建立必要資料夾
RUN mkdir -p temp cookies

# 設定函式庫搜尋路徑
ENV LD_LIBRARY_PATH=/app:/usr/lib/x86_64-linux-gnu:/usr/lib:/lib/x86_64-linux-gnu

# 啟動腳本
RUN printf '#!/bin/bash\nset -e\necho "=== 啟動前函式庫檢查 ==="\nldconfig -p | grep libsodium || echo "⚠️  libsodium 未找到"\nldconfig -p | grep libopus   || echo "⚠️  libopus 未找到"\necho "--- /app 目錄 ---"\nls -lh /app/*.so /app/libopus /app/opus /app/libsodium /app/sodium 2>/dev/null || true\necho "LD_LIBRARY_PATH=${LD_LIBRARY_PATH}"\necho "=== 啟動 MusicBot2 ==="\nexec dotnet MusicBot2.dll\n' > /app/start.sh && \
    chmod +x /app/start.sh

ENTRYPOINT ["/app/start.sh"]
