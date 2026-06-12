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
        libsodium23 \
        libsodium-dev \
        libopus0 \
        libopus-dev && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# 複製建置產物（包含 runtimes 資料夾中的 libdave）
COPY --from=build /app/publish .

# 如果 libdave.so 存在於 runtimes 中，複製到系統庫目錄
RUN if [ -f "runtimes/linux-x64/native/libdave.so" ]; then \
        echo "Found libdave.so in build output, copying to system library path..."; \
        cp runtimes/linux-x64/native/libdave.so /usr/lib/x86_64-linux-gnu/libdave.so; \
        chmod +x /usr/lib/x86_64-linux-gnu/libdave.so; \
        ldconfig; \
        echo "libdave.so installed successfully"; \
    else \
        echo "libdave.so not found in build output, checking NuGet packages..."; \
        find ~/.nuget -name "libdave.so" -exec cp {} /usr/lib/x86_64-linux-gnu/libdave.so \; 2>/dev/null || \
        echo "WARNING: libdave.so not found, voice encryption will not be available"; \
    fi

# 驗證必需的庫
RUN ldconfig && \
    ldconfig -p | grep libsodium && \
    ldconfig -p | grep libopus && \
    echo "Essential voice libraries installed"

# 檢查 libdave（可選）
RUN if ldconfig -p | grep -q libdave; then \
        echo "libdave is available"; \
    else \
        echo "WARNING: libdave not available, will use compatibility mode"; \
    fi

# 安裝最新版 yt-dlp
RUN pip3 install --break-system-packages --upgrade yt-dlp && \
    yt-dlp --version

# 驗證 FFmpeg
RUN ffmpeg -version | head -n 1

# 建立必要資料夾
RUN mkdir -p temp cookies

# 設定環境變數
ENV LD_LIBRARY_PATH=/usr/lib/x86_64-linux-gnu

ENTRYPOINT ["dotnet", "MusicBot2.dll"]
