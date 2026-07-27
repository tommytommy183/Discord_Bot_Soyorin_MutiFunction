# ============================================================
# 階段一：編譯 libdave（從原始碼建置）
# ============================================================
FROM debian:bookworm-slim AS libdave-builder

# 安裝 libdave 編譯所需工具
RUN apt-get update && \
    apt-get install -y \
        git \
        cmake \
        ninja-build \
        make \
        curl \
        zip \
        unzip \
        tar \
        pkg-config \
        libssl-dev \
        ca-certificates \
        g++ \
        gcc \
        python3 \
        python3-pip && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Clone libdave（含 vcpkg submodule）
RUN git clone --recurse-submodules --depth=1 \
    https://github.com/discord/libdave.git /libdave

WORKDIR /libdave/cpp

# 用 vcpkg + openssl_3 編譯成 shared library（.so）
RUN cmake -B build \
        -G Ninja \
        -DCMAKE_BUILD_TYPE=Release \
        -DBUILD_SHARED_LIBS=ON \
        -DTESTING=OFF \
        -DCMAKE_INSTALL_PREFIX=/libdave/install \
        -DVCPKG_MANIFEST_DIR=vcpkg-alts/openssl_3 \
        -DCMAKE_TOOLCHAIN_FILE=vcpkg/scripts/buildsystems/vcpkg.cmake && \
    cmake --build build --target libdave --config Release && \
    cmake --install build --config Release

# 確認產出的 .so 位置
RUN echo "=== libdave 編譯產出 ===" && \
    find /libdave/install -name "*.so*" -o -name "libdave*" | sort && \
    find /libdave/cpp/build -name "libdave*.so*" | sort

# ============================================================
# 階段二：編譯 .NET 應用程式
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
# 階段三：執行映像
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

# 從 libdave-builder 複製編譯好的 .so
# 先嘗試從 install 目錄，找不到則從 build 目錄
COPY --from=libdave-builder /libdave/install /tmp/libdave-install
COPY --from=libdave-builder /libdave/cpp/build /tmp/libdave-build

RUN echo "=== 安裝 libdave.so ===" && \
    # 優先從 install 目錄找，再從 build 目錄找
    SO_FILE=$(find /tmp/libdave-install -name "libdave*.so*" -type f | head -1) && \
    if [ -z "$SO_FILE" ]; then \
        SO_FILE=$(find /tmp/libdave-build -name "libdave*.so*" -type f | head -1); \
    fi && \
    if [ -z "$SO_FILE" ]; then \
        echo "❌ 找不到 libdave.so，請檢查編譯步驟" && exit 1; \
    fi && \
    echo "找到：$SO_FILE" && \
    # 安裝到系統路徑
    cp "$SO_FILE" /usr/lib/x86_64-linux-gnu/libdave.so && \
    chmod 755 /usr/lib/x86_64-linux-gnu/libdave.so && \
    ldconfig && \
    # 同時複製到應用程式目錄（.NET NativeLibrary 優先搜尋此處）
    cp /usr/lib/x86_64-linux-gnu/libdave.so /app/libdave.so && \
    mkdir -p /app/runtimes/linux-x64/native && \
    cp /usr/lib/x86_64-linux-gnu/libdave.so /app/runtimes/linux-x64/native/libdave.so && \
    chmod 755 /app/libdave.so /app/runtimes/linux-x64/native/libdave.so && \
    rm -rf /tmp/libdave-install /tmp/libdave-build && \
    echo "=== libdave 安裝完成 ==="

# 複製 .NET 發布產物
COPY --from=build /app/publish .

# 驗證所有函式庫
RUN ldconfig && \
    echo "=== 函式庫驗證 ===" && \
    echo -n "libsodium：" && (ldconfig -p | grep libsodium | head -1 || echo "❌ 找不到") && \
    echo -n "libopus：  " && (ldconfig -p | grep libopus   | head -1 || echo "❌ 找不到") && \
    echo -n "libdave：  " && (ldconfig -p | grep libdave   | head -1 || echo "❌ 找不到") && \
    echo "=== 應用程式目錄 ===" && \
    ls -lh /app/libdave.so && \
    ls -lh /app/runtimes/linux-x64/native/libdave.so

# ============================================================
# 修正 opus 載入問題
# .NET Discord.Net 會依序嘗試以下名稱：
#   opus.so / libopus.so / opus / libopus
# 系統實際檔案是 libopus.so.0，名稱對不上導致 DllNotFoundException
# 解法：在 /app 目錄建立所有 .NET 會嘗試的名稱
# ============================================================
RUN OPUS_REAL=$(ldconfig -p | grep libopus | awk '{print $NF}' | head -1) && \
    echo "系統 opus 實際路徑：$OPUS_REAL" && \
    # 複製到 /app，並建立所有可能的名稱
    cp "$OPUS_REAL" /app/libopus.so && \
    ln -sf /app/libopus.so /app/opus.so && \
    ln -sf /app/libopus.so /app/libopus && \
    ln -sf /app/libopus.so /app/opus && \
    echo "opus 軟連結建立完成：" && \
    ls -lh /app/opus.so /app/libopus.so /app/libopus /app/opus

# 同樣修正 libsodium（預防同類問題）
RUN SODIUM_REAL=$(ldconfig -p | grep libsodium | awk '{print $NF}' | head -1) && \
    echo "系統 sodium 實際路徑：$SODIUM_REAL" && \
    cp "$SODIUM_REAL" /app/libsodium.so && \
    ln -sf /app/libsodium.so /app/sodium.so && \
    ln -sf /app/libsodium.so /app/libsodium && \
    ln -sf /app/libsodium.so /app/sodium && \
    echo "sodium 軟連結建立完成"

# 建立必要資料夾
RUN mkdir -p temp cookies

# 設定函式庫搜尋路徑（/app 優先，確保 .NET 能找到所有函式庫）
ENV LD_LIBRARY_PATH=/app:/app/runtimes/linux-x64/native:/usr/lib/x86_64-linux-gnu:/usr/lib:/lib/x86_64-linux-gnu

# 啟動腳本
RUN printf '#!/bin/bash\nset -e\necho "=== 啟動前函式庫檢查 ==="\nldconfig -p | grep libsodium || echo "⚠️  libsodium 未找到"\nldconfig -p | grep libopus   || echo "⚠️  libopus 未找到"\nldconfig -p | grep libdave   || echo "⚠️  libdave 未找到"\necho "--- /app 目錄 ---"\nls -lh /app/*.so /app/libopus /app/opus /app/libsodium /app/sodium 2>/dev/null || true\necho "LD_LIBRARY_PATH=${LD_LIBRARY_PATH}"\necho "=== 啟動 MusicBot2 ==="\nexec dotnet MusicBot2.dll\n' > /app/start.sh && \
    chmod +x /app/start.sh

ENTRYPOINT ["/app/start.sh"]