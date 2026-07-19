# Production Readiness — AdvGenNoSQL Server

**Assessment date**: July 2026
**Scope**: TCP server (`AdvGenNoSqlServer.Host` entry point, plus `AdvGenNoSqlServer.Server` as a
supported alternate host) and its Core/Network/Storage/Query dependencies.

This document records the production-hardening work (four commits on `master`), the residual
known issues, and what remains for a fully hardened service. Deployment/ops instructions live
in [Deployment.md](Deployment.md).

## What was hardened

### Phase 1 — Security (`fix(security)`)
- BulkOperation TCP messages required authentication + write authorization (was: unauthenticated
  write/delete path). Host's fake bulk-op stub now fails honestly (`UNSUPPORTED`).
- Host HTTP login issued Admin JWTs to any authenticated user; it now issues real roles, and
  destructive HTTP endpoints require the Admin role.
- Removed shipped secrets (`admin123`, dev JWT key, dev cert password) from tracked config.
  Production requires secrets via environment and fails fast otherwise.
- `appsettings.{ENV}.json` overlay loading + startup validation (`ServerConfiguration.Validate`):
  bad ports, missing TLS certs, disabled auth, weak/default secrets are fatal in Production;
  invalid hot-reloads are rejected without stopping the server.
- Login lockout (5 attempts / 15 min, configurable) and constant-time unknown-user responses.
- PBKDF2 iterations 100k → 600k (OWASP); per-hash iteration counts keep old hashes verifiable.
- Pre-auth memory-exhaustion DoS closed: frames capped at 64 KB until authenticated.
- `CommandAuthorizer` is fail-closed; `dropcollection` requires Admin; anonymous connections
  get a configurable least-privilege role (never Admin by default).
- CORS locked to configured origins in Production; HTTP error responses no longer leak
  exception details; Server's HTTP admin API requires an `X-Api-Key`; fake multi-database
  endpoints return honest 501s; TLS cert is loaded once and validated at startup;
  `users.json` written owner-only on Unix.

### Phase 2 — Data safety & stability (`fix(server)`)
- **Host no longer loses queued writes on graceful shutdown** — `DatabaseManager` flushes and
  disposes every store on stop/dispose (regression-tested).
- TCP listener start is awaited (bind failures surface instead of pretending healthy).
- async-void timer callbacks (audit flush, WAL checkpoint) are guarded; crash/unobserved-task
  hooks log fatal background failures.
- Shutdown drain: in-flight message handlers get a bounded grace period before connections close.
- Background storage writer failures are logged with a `WriteFailureCount` (was: silent data loss).
- Index maintenance failures logged (index/store divergence visibility); several null-deref
  runtime risks fixed; dead timer fields removed (TTL cleanup verified running).

### Phase 3 — Operability (`feat(server)`)
- Real health endpoints: `/health` (liveness) and `/health/ready` (readiness: storage, disk,
  memory, TCP listener; 503 when unhealthy) replacing the static stub.
- Prometheus `/metrics` endpoint; per-command counters + latency histograms, message/error
  counters, connection gauges in the TCP path; pool stats in `/api/stats`.
- JSON console logging in Production; TCP layer logs route into `ILogger`.
- Audit trail covers mutating data commands; audit path fixed to the data directory; retention
  sweeps files older than `AuditRetentionDays` (default 30).

### Phase 4 — Deployment (`feat(deploy)`)
- Multi-stage `Dockerfile` (sdk:9.0 → aspnet:9.0, non-root, `/data` + `/certs` volumes,
  ports 9191/9192) and `.dockerignore`; example `docker-compose.yml`.
- `appsettings.Production.json` overlay with Linux paths; admin API HTTPS/plain-HTTP policy
  configurable (`AdminApiUseHttps`/`AdminApiAllowPlainHttp`); admin port configurable.
- CI fixed: .NET 9 SDK, builds/tests the server-path projects on Ubuntu (the full solution
  can't build on Linux because of the net9.0-windows WPF example).
- [Deployment.md](Deployment.md): env-var reference, health/metrics, reverse proxy, backup,
  production checklist.

## Verification

- `dotnet build` full solution: 0 errors.
- `AdvGenNoSqlServer.Tests`: 3296 passed, 0 failed (30 skipped, see below).
- `AdvGenNoSqlServer.Embedded.Tests`: 152 passed, 0 failed.
- New regression tests: lockout, legacy hash verification, config validation, env overlay,
  pre-auth frame cap, shutdown durability, command authorizer matrix.
- Docker image builds and the container boots (see Phase 4 commit).

## Residual known issues (deferred, not blockers for single-node use)

| Issue | Where | Note |
|---|---|---|
| B-tree deletion/rebalancing edge cases | `BTreeIndexTests.cs:470,501,820,845` (skipped) | 4 skipped tests; needs a dedicated storage fix |
| ETag/timestamp concurrency bugs | `ETagTests.cs:272,508,539,854,893` (skipped) | Get→Update validation can falsely conflict |
| MVCC write-write conflict detection | `MvccTests.cs:779` (skipped) | Serializable isolation falls back to locking |
| Unique-constraint tree-wide check | `IndexManagerTests.cs:549` (skipped) | Dup check is not tree-wide under concurrency |
| Nested-field projection | `QueryProjectionTests.cs:275` (skipped) | Not implemented |
| Replication full-sync is a stub | `Core/Clustering/ReplicationManager.cs:342` | Clustering is not production-validated |
| Two flaky timing tests | `BackgroundIndexBuilderTests`, `HybridDocumentStoreTests` | Fail intermittently under parallel load, pass on rerun; timing-sensitive by design |
| ~380 CS1591 missing-XML-doc warnings | Core/Storage/Query | Cosmetic |

## Explicitly not covered

- **Clustering** (Raft/gossip/replication/sharding) is implemented but not production-validated;
  run single-node until it is.
- Admin/AdminClient Blazor apps, benchmarks, examples — dev tools, not part of the server SLA.
- Client library integration testing against the hardened server (manual: use `TestConnection`).

## Recommended first production steps

1. Deploy via the container image with real secrets (see Deployment.md checklist).
2. Terminate admin API TLS at a reverse proxy; restrict CORS.
3. Wire `/health` + `/health/ready` into the orchestrator; scrape `/metrics`.
4. Schedule `/data` backups; rehearse a restore.
5. Load-test in staging (the `LoadTests`/`StressTests` classes are runnable manually —
   they are skipped in CI because they take minutes).
