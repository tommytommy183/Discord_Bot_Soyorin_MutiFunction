FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish MusicBot2/MusicBot2.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MusicBot2.dll"]