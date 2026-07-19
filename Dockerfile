# syntax=docker/dockerfile:1
# AdvGenNoSQL Server — Host application (TCP wire protocol + admin API)
#
# Build:  docker build -t advgen-nosql-server .
# Run:    docker run -p 9191:9191 -p 9192:9192 -v nosql-data:/data \
#           -e NOSQL_MASTER_PASSWORD=<strong> -e NOSQL_JWT_SECRET_KEY=<64+ chars> \
#           advgen-nosql-server
#
# Secrets are supplied via environment variables — never baked into the image.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first (layer caching): project files only
COPY AdvGenNoSqlServer.Core/AdvGenNoSqlServer.Core.csproj AdvGenNoSqlServer.Core/
COPY AdvGenNoSqlServer.Network/AdvGenNoSqlServer.Network.csproj AdvGenNoSqlServer.Network/
COPY AdvGenNoSqlServer.Storage/AdvGenNoSqlServer.Storage.csproj AdvGenNoSqlServer.Storage/
COPY AdvGenNoSqlServer.Query/AdvGenNoSqlServer.Query.csproj AdvGenNoSqlServer.Query/
COPY AdvGenNoSqlServer.Host/AdvGenNoSqlServer.Host.csproj AdvGenNoSqlServer.Host/
RUN dotnet restore AdvGenNoSqlServer.Host/AdvGenNoSqlServer.Host.csproj

# Publish
COPY AdvGenNoSqlServer.Core/ AdvGenNoSqlServer.Core/
COPY AdvGenNoSqlServer.Network/ AdvGenNoSqlServer.Network/
COPY AdvGenNoSqlServer.Storage/ AdvGenNoSqlServer.Storage/
COPY AdvGenNoSqlServer.Query/ AdvGenNoSqlServer.Query/
COPY AdvGenNoSqlServer.Host/ AdvGenNoSqlServer.Host/
RUN dotnet publish AdvGenNoSqlServer.Host/AdvGenNoSqlServer.Host.csproj \
      -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Data (databases, users.json, WAL, audit logs) and TLS certificates are volumes.
# The image's built-in non-root user (app) must own them.
USER root
RUN mkdir -p /data /certs && chown -R app /data /certs
USER app

ENV DOTNET_ENVIRONMENT=Production \
    NOSQL_STORAGE_PATH=/data

VOLUME ["/data", "/certs"]

# 9191: TCP wire protocol (TLS), 9192: admin HTTP API (plain HTTP — terminate TLS at a proxy)
EXPOSE 9191 9192

# Health probes: point your orchestrator at GET /health (liveness) and /health/ready
# (readiness) on port 9192. The base image ships no curl, so no HEALTHCHECK is baked in.

ENTRYPOINT ["dotnet", "AdvGenNoSqlServer.Host.dll"]
