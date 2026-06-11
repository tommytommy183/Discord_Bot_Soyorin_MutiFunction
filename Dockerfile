# 使用 .NET 8.0 SDK 作為建置映像
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# 複製 csproj 並還原相依性
COPY MusicBot2/*.csproj ./MusicBot2/
WORKDIR /app/MusicBot2
RUN dotnet restore

# 複製所有檔案並建置
WORKDIR /app
COPY MusicBot2/ ./MusicBot2/
WORKDIR /app/MusicBot2
RUN dotnet publish -c Release -o out

# 使用 runtime 映像
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# 安裝 ffmpeg 和 yt-dlp（用於音訊處理）
RUN apt-get update && \
    apt-get install -y ffmpeg python3 python3-pip && \
    pip3 install --break-system-packages yt-dlp && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# 安裝 libsodium（Discord 語音加密需要）
RUN apt-get update && \
    apt-get install -y libsodium-dev libopus0 && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# 複製建置產物
COPY --from=build /app/MusicBot2/out .

# 建立必要資料夾
RUN mkdir -p temp cookies

ENTRYPOINT ["dotnet", "MusicBot2.dll"]
