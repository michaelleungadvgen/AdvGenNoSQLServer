# Production Readiness — AdvGenNoSQL Server

**Assessment date**: July 2026 (residual issues completed 2026-11-17 — see completion record below)
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

## Residual known issues — completion record (2026-11-17)

All eight items from the July 2026 assessment are resolved or implemented:

| Original issue | Resolution |
|---|---|
| B-tree deletion/rebalancing edge cases (4 skipped tests) | Fixed in `BTreeIndex`/`BTreeNode`; all 4 tests unskipped — `BTreeIndexTests` 60/60 |
| ETag/timestamp concurrency bugs (5 skipped tests) | `InMemoryDocumentCollection` now returns defensive clones on read, clones caller data on write, and retries the compare-and-swap so unconditional updates are last-writer-wins; `ETagDocumentStore` serializes validate+update/delete per document so concurrent writers with the same ETag cannot both pass. All 5 tests unskipped — `ETagTests` 77/77 |
| MVCC write-write conflict detection (1 skipped test) | Implemented; test unskipped (`MvccTests`) |
| Unique-constraint tree-wide check (1 skipped test) | `BTreeIndex.Insert` performs a tree-wide existence search under the index lock before any structural mutation; `IndexManager` re-throws `DuplicateKeyException` instead of swallowing it. Test unskipped — `IndexManagerTests` 34/34 |
| Nested-field projection (1 skipped test) | `QueryExecutor.ApplyProjection` resolves dotted paths for inclusion and exclusion projections. Test unskipped — `QueryProjectionTests` 20/20 |
| Replication full-sync stub | Snapshot + WAL replay implemented (`Core/Clustering/ReplicationFullSync.cs`, `WalFullSyncProvider`, `IFullSyncProvider`/`IFullSyncTransport`); 16 new tests in `ReplicationFullSyncTests`. **Remaining:** wire `IFullSyncTransport` over the P2P network transport; clustering is still not production-validated |
| Two flaky timing tests | `BackgroundIndexBuilderTests` polls for event delivery instead of fixed delays; fixed a real product bug — `BackgroundIndexBuilder` marked jobs `Running` before acquiring a concurrency slot, so `RunningBuildCount` could exceed `MaxConcurrentBuilds`. 8 consecutive runs of both classes green |
| `MemoryTuner` aggressive-GC crash (found during final verification) | `PerformAggressiveCleanup` called `GC.Collect` in `Aggressive` mode on generations 0/1, which throws `ArgumentException` (Aggressive requires `GC.MaxGeneration`). Only triggered under critical memory pressure, so it surfaced intermittently. Fixed to a full blocking compacting collect |
| ~380 CS1591 missing-XML-doc warnings | 395 members documented across Core/Storage/Query plus 68 in Admin/Client/Network — 0 CS1591 solution-wide |

### Verification (2026-11-17, Release)

- `AdvGenNoSqlServer.Tests`: **3377 passed, 0 failed, 18 skipped** (intentional skips: manual
  load/stress tests, SSL-cert-environment tests, TTL-timing tests, and similar).
- `AdvGenNoSqlServer.Embedded.Tests`: **157 passed, 0 failed**.
- Full solution compiles with 0 code errors; the Host builds cleanly to a separate output
  directory while a live instance holds its default `bin` outputs.

## Explicitly not covered

- **Clustering** (Raft/gossip/replication/sharding) is implemented but not production-validated;
  run single-node until it is. Replication full-sync (snapshot + WAL replay) is implemented and
  unit-tested, but not yet wired to the P2P network transport.
- Admin/AdminClient Blazor apps, benchmarks, examples — dev tools, not part of the server SLA.
- Client library integration testing against the hardened server (manual: use `TestConnection`).

## Recommended first production steps

1. Deploy via the container image with real secrets (see Deployment.md checklist).
2. Terminate admin API TLS at a reverse proxy; restrict CORS.
3. Wire `/health` + `/health/ready` into the orchestrator; scrape `/metrics`.
4. Schedule `/data` backups; rehearse a restore.
5. Load-test in staging (the `LoadTests`/`StressTests` classes are runnable manually —
   they are skipped in CI because they take minutes).
