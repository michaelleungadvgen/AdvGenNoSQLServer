# AdvGenNoSQL Server — Deployment Guide

This guide covers running the AdvGenNoSQL Server (`AdvGenNoSqlServer.Host`) in production,
with a focus on Linux/Docker.

## Architecture at a glance

| Component | Port (default) | Notes |
|---|---|---|
| TCP wire protocol | 9191 | Binary protocol, TLS when `EnableSsl` (required in Production) |
| Admin HTTP API | 9192 | REST + health/metrics. Plain HTTP in the container profile — terminate TLS at a reverse proxy |
| Data volume | `/data` | Databases, `users.json`, WAL, audit logs |
| Cert volume | `/certs` | TLS PFX for the TCP listener |

## Quick start (Docker)

```bash
# 1. Build the image
docker build -t advgen-nosql-server .

# 2. Provide a TLS certificate for the TCP listener
mkdir -p certs
# (put your PFX at certs/advgen.pfx — or generate a dev one, see below)

# 3. Run
docker run -d --name nosql \
  -p 9191:9191 -p 9192:9192 \
  -v nosql-data:/data \
  -v $(pwd)/certs:/certs:ro \
  -e NOSQL_MASTER_PASSWORD='change-me-strong' \
  -e NOSQL_JWT_SECRET_KEY='change-me-to-a-long-random-string-32plus' \
  -e NOSQL_SSL_CERT_PASSWORD='pfx-password-if-any' \
  advgen-nosql-server
```

Or use the compose file (secrets via environment or a local `.env`):

```bash
NOSQL_MASTER_PASSWORD=... NOSQL_JWT_SECRET_KEY=... docker compose up -d
```

### Generate a development PFX

```bash
openssl req -x509 -newkey rsa:4096 -keyout key.pem -out cert.pem -days 365 -nodes \
  -subj "/CN=localhost"
openssl pkcs12 -export -out certs/advgen.pfx -inkey key.pem -in cert.pem -password pass:devpassword
```

Do not use this cert in production — get a real one (or terminate TLS at a proxy and set
`EnableSsl: false` with the proxy doing TLS for the TCP port too — note the wire protocol
expects TLS when `EnableSsl` is true).

## Configuration

Configuration loads in this order (later wins):

1. `AdvGenNoSqlServer.Host/appsettings.json` (base, tracked in git — contains no secrets)
2. `appsettings.{DOTNET_ENVIRONMENT}.json` (overlay; the image sets `Production`)
3. Environment variables (`NOSQL_*`)

**Production validation fails startup** when: authentication is disabled, the master
password is missing/`admin123`, the JWT secret is missing/shorter than 32 chars/the old
dev default, the TLS cert path doesn't exist when SSL is enabled, or values are out of
range. Malformed JSON is fatal in Production. Invalid hot-reloads are rejected without
stopping the running server.

### Environment variable reference

| Variable | Maps to | Notes |
|---|---|---|
| `NOSQL_HOST` | `Host` | Bind address (default `0.0.0.0`) |
| `NOSQL_PORT` | `Port` | TCP wire port (default 9191) |
| `NOSQL_STORAGE_PATH` | `StoragePath` | Data directory (`/data` in image) |
| `NOSQL_MASTER_PASSWORD` | `MasterPassword` | Seeds the `admin` user on first boot; **required in Production** |
| `NOSQL_JWT_SECRET_KEY` | `JwtSecretKey` | JWT signing key, 32+ chars; **required in Production** |
| `NOSQL_REQUIRE_AUTHENTICATION` | `RequireAuthentication` | `false` is rejected in Production |
| `NOSQL_ENABLE_SSL` | `EnableSsl` | TCP TLS on/off |
| `NOSQL_SSL_CERT_PATH` | `SslCertificatePath` | PFX path (`/certs/advgen.pfx` in image) |
| `NOSQL_SSL_CERT_PASSWORD` | `SslCertificatePassword` | PFX password |
| `NOSQL_ADMIN_HTTP_PORT` | `AdminApiPort` | Admin API port (default 9192) |
| `NOSQL_ADMIN_HTTP_USE_HTTPS` | `AdminApiUseHttps` | Set `false` only behind a TLS proxy |
| `NOSQL_ADMIN_API_KEY` | `AdminApiKey` | `X-Api-Key` gate for the Server project's HTTP API |
| `NOSQL_ANONYMOUS_ROLE` | `AnonymousRole` | Role for unauthenticated connections when auth is off (default `Reader`) |
| `NOSQL_CORS_ORIGINS` | `CorsAllowedOrigins` | Semicolon-separated origins for the admin API |
| `NOSQL_MAX_MESSAGE_SIZE_MB` | `MaxMessageSizeMb` | Post-auth frame limit (default 100) |
| `NOSQL_PBKDF2_ITERATIONS` | `Pbkdf2Iterations` | Password hashing cost (default 600000) |
| `NOSQL_MAX_CONNECTIONS` | `MaxConcurrentConnections` | Connection pool size |
| `NOSQL_TOKEN_EXPIRATION_HOURS` | `TokenExpirationHours` | TCP token lifetime |

## Health & metrics

| Endpoint | Auth | Purpose |
|---|---|---|
| `GET /health` | anonymous | Liveness — process is alive |
| `GET /health/ready` | anonymous* | Readiness — storage, disk, memory, TCP listener; 503 when unhealthy |
| `GET /metrics` | JWT* | Prometheus text format |
| `GET /api/stats` | JWT | Uptime, memory, document/connection counts, pool stats |

\* `AllowAnonymousReadiness` / `AllowAnonymousMetrics` in config control these.

Kubernetes example:

```yaml
livenessProbe:
  httpGet: { path: /health, port: 9192 }
  periodSeconds: 15
readinessProbe:
  httpGet: { path: /health/ready, port: 9192 }
  periodSeconds: 10
```

Metrics recorded: `nosql_commands_total{command}`, `nosql_command_duration_seconds{command}`,
`nosql_messages_total{type}`, `nosql_message_duration_seconds{type}`,
`nosql_errors_total{type}`, `nosql_connections_total`, `nosql_connections_active`, plus
cache gauges.

## Reverse proxy (admin API TLS)

The container profile runs the admin API on plain HTTP 9192 (`AdminApiUseHttps: false`
with `AdminApiAllowPlainHttp: true`). Example nginx termination:

```nginx
server {
    listen 443 ssl;
    server_name db-admin.example.com;
    ssl_certificate     /etc/nginx/tls.crt;
    ssl_certificate_key /etc/nginx/tls.key;
    location / {
        proxy_pass http://nosql:9192;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto https;
    }
}
```

If you prefer the admin API to do TLS itself, set `AdminApiUseHttps: true` and provide
`HttpsCertificatePath`/`HttpsCertificatePassword` via the ASP.NET Core configuration
(`builder.Configuration` keys).

## Backup

1. Issue a checkpoint/flush: graceful `SIGTERM` (docker stop) flushes all write-behind
   queues and disposes stores; the TCP `stats` command shows activity draining.
2. Copy the `/data` volume contents.
3. Keep `users.json` (password hashes) with the same care as the data itself.

Restore: place the files back in the volume and start the container.

## Production checklist

- [ ] `NOSQL_MASTER_PASSWORD` set (strong) — admin user is seeded on first boot
- [ ] `NOSQL_JWT_SECRET_KEY` set (32+ random chars)
- [ ] Real TLS certificate in `/certs` + `NOSQL_SSL_CERT_PASSWORD` if protected
- [ ] `NOSQL_ADMIN_API_KEY` set if you also run the `Server` binary's HTTP API
- [ ] CORS origins restricted (`NOSQL_CORS_ORIGINS`) if a browser admin is used
- [ ] Reverse proxy TLS in front of the admin API
- [ ] `/health` + `/health/ready` wired into your orchestrator
- [ ] Prometheus scraping `/metrics` (with a JWT, or `AllowAnonymousMetrics: true` on a private network)
- [ ] `/data` on a persistent volume with a backup schedule
- [ ] Audit logs reviewed (`/data/logs/audit/`, retained `AuditRetentionDays`, default 30)

## Non-container (bare metal / systemd)

```bash
dotnet publish AdvGenNoSqlServer.Host/AdvGenNoSqlServer.Host.csproj -c Release -o /opt/nosql
DOTNET_ENVIRONMENT=Production \
NOSQL_MASTER_PASSWORD=... NOSQL_JWT_SECRET_KEY=... \
/opt/nosql/AdvGenNoSqlServer.Host
```

The process handles SIGTERM/SIGINT gracefully (connection drain + storage flush).
