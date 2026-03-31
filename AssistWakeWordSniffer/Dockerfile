# --- STAGE 1: Base Runtime ---
# Alpine provides a tiny footprint and easy package management
FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine AS base
WORKDIR /app

# Install FFmpeg and ALSA libs (essential for audio capture/Method A)
RUN apk add --no-cache ffmpeg alsa-lib

# --- STAGE 2: Build ---
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AssistWakeWordSniffer.csproj", "."]
RUN dotnet restore "./AssistWakeWordSniffer.csproj"
COPY . .
RUN dotnet build "./AssistWakeWordSniffer.csproj" -c $BUILD_CONFIGURATION -o /app/build

# --- STAGE 3: Publish ---
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AssistWakeWordSniffer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# --- STAGE 4: Final ---
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create the captures directory so volume mapping works smoothly
RUN mkdir -p /app/captures

# We DO NOT use 'USER $APP_UID' here because the sniffer needs 
# root/high-level permissions to access /dev/snd (audio drivers).
ENTRYPOINT ["dotnet", "AssistWakeWordSniffer.dll"]