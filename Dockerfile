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

# 一次性安裝所有依賴（避免多次 apt-get update）
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

# 驗證 libsodium 和 libopus 安裝
RUN ldconfig && \
    ldconfig -p | grep libsodium && \
    ldconfig -p | grep libopus && \
    echo "? libsodium 和 libopus 已安裝"

# 安裝最新版 yt-dlp
RUN pip3 install --break-system-packages --upgrade yt-dlp && \
    yt-dlp --version && \
    echo "? yt-dlp 版本: $(yt-dlp --version)"

# 驗證 FFmpeg
RUN ffmpeg -version && \
    echo "? FFmpeg 已安裝"

# 複製建置產物
COPY --from=build /app/publish .

# 建立必要資料夾
RUN mkdir -p temp cookies

# 設定環境變數（確保 Discord.Net 能找到 libsodium）
ENV LD_LIBRARY_PATH=/usr/lib/x86_64-linux-gnu:$LD_LIBRARY_PATH

ENTRYPOINT ["dotnet", "MusicBot2.dll"]
