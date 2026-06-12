# 使用 .NET 8.0 SDK 作為建置映像
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 複製 csproj 並還原相依性（優化 Docker 快取層）
COPY MusicBot2/MusicBot2.csproj MusicBot2/
RUN dotnet restore MusicBot2/MusicBot2.csproj -r linux-x64

# 複製所有專案檔案
COPY MusicBot2/ MusicBot2/

# 建置和發布（指定 linux-x64 平台）
WORKDIR /src/MusicBot2
RUN dotnet publish MusicBot2.csproj -c Release -o /app/publish -r linux-x64 --self-contained false /p:Platform=x64

# 使用 runtime 映像
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# 一次性安裝所有依賴
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

# 下載並安裝 libdave（從 zip 解壓縮）
# Download and install libdave - use OpenSSL variant, not BoringSSL
RUN wget https://github.com/discord/libdave/releases/download/v1.1.1%2Fcpp/libdave-Linux-X64-openssl.zip \
    -O /tmp/libdave.zip && \
    unzip -q /tmp/libdave.zip -d /tmp/libdave && \
    echo "Extracted files:" && find /tmp/libdave -type f && \
    # Find any .so file and copy it as libdave.so
    find /tmp/libdave -name "*.so*" | head -1 | xargs -I{} cp {} /usr/local/lib/libdave.so && \
    chmod 755 /usr/local/lib/libdave.so && \
    ldconfig && \
    rm -rf /tmp/libdave /tmp/libdave.zip && \
    echo "libdave installed"

    RUN cp /usr/local/lib/libdave.so /app/libdave.so && \
    chmod 755 /app/libdave.so


# 驗證所有語音庫
RUN ldconfig && \
    echo "Checking libsodium:" && ldconfig -p | grep libsodium && \
    echo "Checking libopus:" && ldconfig -p | grep libopus && \
    echo "Checking libdave:" && ldconfig -p | grep libdave && \
    echo "All voice libraries installed successfully"

# 安裝最新版 yt-dlp
RUN pip3 install --break-system-packages --upgrade yt-dlp && \
    yt-dlp --version

# 驗證 FFmpeg
RUN ffmpeg -version | head -n 1

# 複製建置產物
COPY --from=build /app/publish .

# 複製 libdave 到應用程式目錄（供 .NET NativeLibrary 載入）
RUN cp /usr/lib/x86_64-linux-gnu/libdave.so /app/libdave.so && \
    cp /usr/lib/x86_64-linux-gnu/libdave.so /app/runtimes/linux-x64/native/libdave.so 2>/dev/null || \
    (mkdir -p /app/runtimes/linux-x64/native && cp /usr/lib/x86_64-linux-gnu/libdave.so /app/runtimes/linux-x64/native/libdave.so) && \
    chmod 755 /app/libdave.so /app/runtimes/linux-x64/native/libdave.so && \
    echo "libdave copied to app directory"

# 建立必要資料夾
RUN mkdir -p temp cookies

# 建立啟動腳本來驗證 libdave
RUN echo '#!/bin/bash' > /app/check_libs.sh && \
    echo 'echo "=== Checking Voice Libraries ==="' >> /app/check_libs.sh && \
    echo 'ldconfig -p | grep libsodium || echo "libsodium NOT FOUND"' >> /app/check_libs.sh && \
    echo 'ldconfig -p | grep libopus || echo "libopus NOT FOUND"' >> /app/check_libs.sh && \
    echo 'ldconfig -p | grep libdave || echo "libdave NOT FOUND"' >> /app/check_libs.sh && \
    echo 'echo "=== Library Paths ==="' >> /app/check_libs.sh && \
    echo 'echo "LD_LIBRARY_PATH: $LD_LIBRARY_PATH"' >> /app/check_libs.sh && \
    echo 'ls -lh /usr/lib/x86_64-linux-gnu/libdave.so 2>/dev/null || echo "libdave.so NOT in /usr/lib/x86_64-linux-gnu/"' >> /app/check_libs.sh && \
    echo 'ls -lh /usr/lib/libdave.so 2>/dev/null || echo "libdave.so NOT in /usr/lib/"' >> /app/check_libs.sh && \
    echo 'ls -lh /lib/x86_64-linux-gnu/libdave.so 2>/dev/null || echo "libdave.so NOT in /lib/x86_64-linux-gnu/"' >> /app/check_libs.sh && \
    echo 'ls -lh /app/libdave.so 2>/dev/null || echo "libdave.so NOT in /app/"' >> /app/check_libs.sh && \
    echo 'ls -lh /app/runtimes/linux-x64/native/libdave.so 2>/dev/null || echo "libdave.so NOT in /app/runtimes/linux-x64/native/"' >> /app/check_libs.sh && \
    echo 'echo "=== Starting Application ==="' >> /app/check_libs.sh && \
    echo 'exec dotnet MusicBot2.dll' >> /app/check_libs.sh && \
    chmod +x /app/check_libs.sh

# 設定環境變數（加入當前目錄）
ENV LD_LIBRARY_PATH=/app:/app/runtimes/linux-x64/native:/usr/lib/x86_64-linux-gnu:/usr/lib:/lib/x86_64-linux-gnu

ENTRYPOINT ["/app/check_libs.sh"]
