# syntax=docker/dockerfile:1

# Builds the web/ SPA (Vite) and the .NET API into a single runtime image
# that serves both — see Program.cs's UseStaticFiles/MapFallbackToFile.
# Build context must be the repository root (server/), not docker/ — e.g.
# `docker build -f docker/Dockerfile .` — since both src/ and web/ need to
# be in the build context.

FROM node:22-alpine AS web-build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-build
WORKDIR /src
COPY src/UpdateWatch2.Server/UpdateWatch2.Server.csproj UpdateWatch2.Server/
RUN dotnet restore UpdateWatch2.Server/UpdateWatch2.Server.csproj
COPY src/UpdateWatch2.Server/ UpdateWatch2.Server/
RUN dotnet publish UpdateWatch2.Server/UpdateWatch2.Server.csproj \
    --no-restore -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=server-build /app/publish .
COPY --from=web-build /web/dist ./wwwroot

# Matches server/data/ and server/certs/ in the source tree — see
# CLAUDE.md's "never commit secrets" gitignore rules for those same paths.
# Database:Path is relative to the content root (this WORKDIR), so
# "data/updatewatch2.sqlite" here means /app/data/updatewatch2.sqlite,
# unlike the dev default (../../data/updatewatch2.sqlite, relative to
# src/UpdateWatch2.Server) — see appsettings.json.
ENV ASPNETCORE_URLS=http://+:8080 \
    Database__Path=data/updatewatch2.sqlite
EXPOSE 8080

# Runs as the non-root "app" user the aspnet base image already provides —
# volume mount points are created and chowned here, before switching user
# and declaring VOLUME, since a fresh bind/named-mount otherwise shows up
# owned by root and "app" can't write into it.
RUN mkdir -p /app/data /app/certs && chown -R app:app /app/data /app/certs
VOLUME ["/app/data", "/app/certs"]
USER app

ENTRYPOINT ["dotnet", "UpdateWatch2.Server.dll"]
