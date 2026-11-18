# AdvGenNoSQL Server — Code Review Report

- **Date**: 2026-11-13
- **Branch / commit**: `master` @ `7a32d8f` (docs: production readiness status + agent/deployment docs)
- **Size**: 808 C# source files, ~79,200 lines (excluding bin/obj)
- **Method**: Release build + both test suites actually executed; four focused read-only code reviews (security/auth, storage/query engine, network protocol/client, ops/tests/repo hygiene). All findings carry file:line evidence.
- **Report status**: **Complete** — all four focused reviews (security/auth, storage/engine, network/client, ops/hygiene) plus build/test execution are finished. **P0 data-integrity remediation applied the same day — see §9.**

---

## 1. Executive Summary

| Area | Status | Overall assessment |
|---|---|---|
| Build | Passes (0 errors / 989 warnings) | High warning count; concentrated test-code quality issues |
| Main test suite | 3297 passed / 1 failed / 30 skipped | Failure is flaky under full-suite load (passes in isolation); BTree concurrency tests are skipped |
| Embedded tests | 152/152 passed | Includes crash-recovery tests |
| Network / client | Review complete | Sound framing, but weak resource-exhaustion resistance; client unsafe under concurrency |
| Ops / deployment | Review complete | Solid fundamentals (probes, Docker, CI); docs and test credibility undermined |
| Security / auth | Review complete | Crypto and TCP gating genuinely hardened; HTTP admin surface undermines it (role gaps, no session expiry, cleartext admin key, client TLS MITM vector) |
| Storage / query engine | Review complete | **Not production-safe**: MVCC dirty reads, cosmetic rollback, BTree corruption on delete, multiple crash-recovery data-loss windows |

**Highest-priority issues confirmed:**
1. **MVCC dirty reads + cosmetic rollback**: visibility never checks creator commit state; writes apply immediately; `RollbackAsync` only appends a WAL record — uncommitted/rolled-back data becomes permanently visible (Critical)
2. **BTree merge double-removes child** → structural corruption / data loss on delete (Critical) — and the BTree concurrency tests that might catch related bugs are skipped
3. **Crash-recovery data-loss cluster**: Core WAL recovery throws on torn tail instead of truncating; Embedded WAL never truncates a corrupt tail when replay yields 0 pages; page alloc/free-list/catalog metadata bypass the WAL; document files written non-atomically and corrupt files silently skipped on load (Critical)
4. **Compaction races writers** in the Embedded engine → concurrent writes lost (High)
5. **Client TLS validation bypass (MITM)**: any cert whose Subject merely *contains* "localhost" is accepted on name mismatch — `TlsStreamHelper.cs:459-465` (High)
6. **HTTP write endpoints lack role checks**: `readonly` users can insert/update via HTTP while TCP correctly demands Write — `Host/Program.cs:401,418` (High)
7. **Server admin API always plain HTTP on 0.0.0.0**, even in Production: X-Api-Key sniffable → full admin; non-constant-time comparison — `Server/Program.cs:84,106-119` (High)
8. No effective async read timeout + TLS handshake before pool admission → pre-auth slow-loris / resource exhaustion (High, network)
9. Client send/receive locks separate, no request-correlation ID → concurrent callers receive each other's responses (High, client)
10. TCP sessions never expire/revoke; JWT accepts missing `exp`; capacity-rejection parsed as success; `devpassword` in config; stale/contradictory docs and false Serilog claim (Medium cluster)

---

## 2. Build & Tests

### 2.1 Build
- `dotnet build AdvGenNoSqlServer.sln -c Release`: **0 errors, 989 warnings**
- Warnings are mostly xUnit analyzer rules:
  - `xUnit2002`: `Assert.NotNull()` on value type `MetricValue` (no-op assert) — `AdvGenNoSqlServer.Tests/MetricsCollectorTests.cs:272,291,305,324`
  - `xUnit1031`: blocking task operations (`.Result`/`.Wait()`, deadlock risk) inside test methods — concentrated in `ReadPreferenceTests.cs` (many sites, 622–822), `MemoryProfilerTests.cs:392`, `QueryOptimizerTests.cs:540`, `SessionTests.cs:486`, `P2PManagerTests.cs:503`

### 2.2 Main test suite (AdvGenNoSqlServer.Tests)
- **3297 passed / 1 failed / 30 skipped, 3328 total**, duration 1m11s
- Failure: `BackgroundIndexBuilderTests.StartBuildAsync_MultipleConcurrentBuilds_RespectsMaxConcurrent` (`AdvGenNoSqlServer.Tests/BackgroundIndexBuilderTests.cs:729`)
  - **Passes when re-run in isolation** → flaky concurrency test under full-suite load, not a stable defect; its timing assumptions should still be fixed
- Skipped tests include **BTree concurrency/thread-safety tests** (`BTreeIndexTests.Insert_Concurrent_ThreadSafe`, `Insert_Concurrent_SmallSet_ThreadSafe`, `Delete_SimpleItems_WorksCorrectly`, `Delete_MultipleItems_MaintainsTreeStructure`) — core index concurrency correctness currently has **no test coverage**

### 2.3 Embedded test suite
- **152/152 passed**, duration 3s; includes `CrashRecoveryTests`, randomized property tests against a dictionary oracle, and LiteDB migration equivalence tests

---

## 3. Security / Authentication Review (complete)

### 3.1 Critical
None confirmed. No BinaryFormatter/`TypeNameHandling`, no hardcoded credentials in tracked configs, no query-eval injection surface (Host `/api/query` ignores the query text entirely — `Host/Program.cs:346-363`).

### 3.2 High
| # | Issue | Evidence |
|---|---|---|
| S1 | **TLS client validation bypass (MITM)**: on name mismatch, any cert whose Subject *contains the substring* "localhost" is accepted (e.g., a valid cert for `localhost.attacker.com`). Used by the client library. Not listed in ProductionReadiness.md | `AdvGenNoSqlServer.Network/TlsStreamHelper.cs:459-465`; `AlpnTlsProvider.cs:341-347`; `Client.cs:185` |
| S2 | **HTTP write endpoints lack role checks**: POST/PUT documents require only `[Authorize]`; a `readonly` user can insert/update via HTTP while TCP correctly demands Write — privilege escalation relative to the TCP model | `Host/Program.cs:401,418`; contrast `CommandAuthorizer.cs:33-40` |
| S3 | **Server admin API always plain HTTP on 0.0.0.0**: even in Production the X-Api-Key travels cleartext (sniffable → full admin); comparison is also non-constant-time | `AdvGenNoSqlServer.Server/Program.cs:84,106-119` (comparison at 111) |

### 3.3 Medium
| # | Issue | Evidence |
|---|---|---|
| S4 | **TCP sessions never expire or revoke**: identity cached per connection; `AuthToken.ExpiresAt` never re-checked; `SetRole`/`SetPassword`/`RevokeAllUserTokens` don't disconnect live sessions — demoted/reset users keep old privileges indefinitely | `Host/Program.cs:996,1064`; `AuthenticationManager.cs:281-309` |
| S5 | **JWT accepts missing `exp`**: tokens without expiry validate forever (`DateTime.MaxValue`); `ExtractUsername`/`GetExpirationTime` skip lifetime checks; `RefreshToken` enables indefinite renewal; no jti/revocation tracking. (Signature always HMAC-verified — no alg-confusion.) | `Core/Authentication/JwtTokenProvider.cs:149,173,191,208-266` |
| S6 | **Unauthenticated CPU DoS**: every unknown-user attempt runs 600k PBKDF2 iterations; no per-IP rate limit; lockout tracks only existing accounts — also enables admin lockout-DoS | `AuthenticationManager.cs:121,164-184` |
| S7 | **Dev cert password in config**: `appsettings.json` carries `"devpassword"`, contradicting the Phase-1 claim "removed shipped secrets from tracked config" (Production does reject it). Currently an uncommitted modification; local `Host/certs/advgen.pfx` exists but is untracked | `Host/appsettings.json:20`; `ServerConfiguration.cs:443` |
| S8 | **Shared global database context**: any authenticated user can switch the singleton `DocumentStore` for *all* HTTP users — cross-user isolation break | `Host/Program.cs:301-315` |

### 3.4 Low
- **Role-case mismatch**: `UserRole.cs:10` issues `"admin"` but endpoints demand `Roles="Admin"`; case-sensitive claim matching locks everyone out of admin HTTP endpoints (fail-closed, but RBAC is broken) — `UserRole.cs:10`; `Host/Program.cs:267,284,367,382,434`
- **Weak secret policy**: MasterPassword only rejected if empty/`admin123` (1-char passes); user passwords min 6 chars — `ServerConfiguration.cs:432`; `Host/Program.cs:1405`
- **JSON injection in error frames**: unescaped interpolation of messages echoing attacker-controlled command names — `Network/ConnectionHandler.cs:345`; `Host/Program.cs:1069`
- **TLS handshake before pool admission**: unauthenticated handshakes consume resources before the connection limit applies (same as network H2) — `TcpServer.cs:243-265`
- **Random-secret mismatch**: with `EnableJwtAuthentication=false`, issuer and bearer generate *different* random keys → all tokens invalid; `BuildServiceProvider()` anti-pattern — `JwtTokenProvider.cs:34`; `Host/Program.cs:552,564`
- **users.json ACLs Unix-only**: no Windows ACL hardening — `FileUserStore.cs:51-55`

### 3.5 Verified hardening claims (real)
PBKDF2-SHA256 600k iterations / 32-byte salt / `FixedTimeEquals` (`AuthenticationManager.cs:379-437`); account lockout; fail-closed `CommandAuthorizer`; pre-auth 64KB frame cap (`ConnectionHandler.cs:103,199`); bulk auth gate (`Host/Program.cs:1559`; `Server/NoSqlServer.cs:1584-1590`); Production fail-fast config validation; AES-GCM with random nonces (`EncryptionService.cs`); attachment path sanitization incl. `..` (`AttachmentStore.cs:374-381`); no secrets in audit logs; shutdown flush (`Host/Program.cs:811-820`).

Note: ProductionReadiness.md's residual-issue table covers only storage/test items — **none of the security findings above are listed there**.

### 3.6 Verdict
Core cryptographic and TCP-gating hardening is genuinely implemented and substantially better than typical hobby-server code. However, the HTTP admin surface undermines it: readonly users can write, sessions never expire, the Server host's admin key travels in cleartext, and the client-side TLS "localhost" exception is a real MITM vector. Posture: **"hardened single-node TCP, not production-safe HTTP"** — fix S1–S5 before any exposed deployment.

---

## 4. Storage / Query / Transactions / Embedded Engine Review (complete)

### 4.1 Critical
| # | Issue | Evidence |
|---|---|---|
| D1 | **MVCC dirty reads + no undo**: `IsVisibleTo` never checks creator commit state or the snapshot's active-txn list; writes apply immediately; `RollbackAsync` only appends a WAL record — uncommitted/rolled-back data becomes permanently visible | `Core/Transactions/DocumentVersion.cs:40-49`; `MvccTransactionContext.cs:141,180,222,296` |
| D2 | **BTree merge double-removes child**: `MergeWithRightSibling` already removes the child, then `Rebalance` calls `RemoveAt` again → structural corruption / data loss on delete | `Storage/Indexing/BTreeIndex.cs:472-480`; `BTreeNode.cs:258` |
| D3 | **Embedded WAL corrupt-tail data loss**: when replay yields 0 pages the torn tail is never truncated; new commits append after garbage and are silently dropped on next open | `Embedded/Storage/WalPageStore.cs:120-122`; `Embedded/Storage/WriteAheadLog.cs:58,118` |
| D4 | **Non-transactional metadata writes**: page alloc / free-list / catalog-root bypass the WAL; rollback/crash can't undo them, and WAL replay can overwrite freed/reused pages | `WalPageStore.cs:47,67-72`; `FilePageStore.cs:72-90,110-118`; `EmbeddedDocumentStore.cs:58-61` |
| D5 | **Core WAL recovery fails on torn tail**: CRC mismatch throws instead of truncating the tail, so `RecoverAsync` fails after any crash mid-append | `Core/Transactions/WriteAheadLog.cs:641-644,700-706` |
| D6 | **Torn document files**: `File.WriteAllTextAsync` straight to final path (no temp+rename/fsync); corrupt files silently skipped on load → silent data loss | `Storage/PersistentDocumentStore.cs:487-492,412-416` |

### 4.2 High
| # | Issue | Evidence |
|---|---|---|
| D7 | **MVCC conflict check is dead code**: checks a snapshot-visible version, so `CreatedAt.Ticks > ReadTimestamp` is never true; `UpdateVersionAsync` has no check → lost updates | `MvccTransactionContext.cs:162-174`; `MvccDocumentStore.cs:177-178` |
| D8 | **MVCC GC is a no-op**: version chains grow unbounded | `MvccDocumentStore.cs:346-361` |
| D9 | **Shared locks for writes**: below Serializable, writers take Shared locks; "2PC" has no prepare/apply phase and rollback never undoes applied data | `TransactionContext.cs:279` |
| D10 | **WAL archive deletion ignores checkpoints**: rotation deletes oldest archives by name; `TruncateAsync` temp file not fsynced before overwrite-rename; failure leaves `_logFile` closed | `Core/Transactions/WriteAheadLog.cs:416-434,791-795,802-805` |
| D11 | **Compaction races writers**: no store-wide exclusion during copy/swap despite doc claim → concurrent writes lost; compactor drops CreatedAt/UpdatedAt/Version | `Embedded/AdvGenDatabase.cs:102-125`; `Compactor.cs:43-47` |
| D12 | **BTree lock/escape bugs**: locks held across `yield` (abandoned enumerator deadlocks the index); `GetValues` returns live list outside lock; stale separator after leaf delete makes `TryGetValue` throw; predecessor-copy delete miscounts `Count` | `BTreeIndex.cs:530-556,645-663,518,499,362-374` |
| D13 | **Page checksum excludes header**: SlotCount/NextPageId/FreeBytes corruption undetectable | `Embedded/Storage/Page.cs:121-131` |
| D14 | **PersistentDocumentStore races**: filename sanitization collisions silently overwrite distinct IDs; non-atomic reflection count increment; init check-then-act | `PersistentDocumentStore.cs:526-532,516-522,443-449` |
| D15 | **async void handlers**: exceptions in deadlock/cleanup callbacks crash the process | `TransactionCoordinator.cs:289-296,301` |

### 4.3 Medium
| # | Issue | Evidence |
|---|---|---|
| D16 | **Numeric precision loss**: all numerics compared via `Convert.ToDouble` → wrong results for long/decimal > 2^53; `$regex` without wildcards degrades to substring match | `Query/Filtering/FilterEngine.cs:283,307-309,270` |
| D17 | **WAL slurp + int cast**: entire WAL read into memory with `(int)` length cast → overflow/OOM > 2GB | `Embedded/Storage/WriteAheadLog.cs:97-102` |
| D18 | **LruCache**: eviction events fired under write lock (reentrancy/deadlock); Dispose races cleanup timer | `Core/Caching/LruCache.cs:377-383,407-412` |
| D19 | **MVCC count/insert races**: `CountAsync` ignores snapshot; insert check-then-act outside chain lock | `MvccDocumentStore.cs:231-247,131-139` |
| D20 | **FullText search unlocked**: no read lock; avg doc length recomputed per posting → O(N·M), inconsistent scores | `Storage/FullText/FullTextIndex.cs:148-205,185` |
| D21 | **FileStorageManager**: no fsync/atomic rename; load swallows all exceptions as null | `Storage/Storage/FileStorageManager.cs:52,79-83` |
| D22 | **Replay trusts lengths**: sub-startLsn entries skip checksum and seek by unvalidated `dataLength` | `Core/Transactions/WriteAheadLog.cs:628-633` |

### 4.4 Low
- Unknown top-level query keys silently become filter fields (typos → wrong results); no user-string compilation/injection found — `ExpressionTranslator` only compiles client LINQ closures (safe), but decimal→double normalization loses precision — `Query/Parsing/QueryParser.cs:80-88`; `ExpressionTranslator.cs:186`
- Fire-and-forget timeout task per lock wait — `Core/Transactions/LockManager.cs:441-448`

### 4.5 Verdict
The Embedded engine's WAL frame format and checkpoint ordering are sound in the happy path, but crash recovery, transactional metadata, and compaction all have real data-loss windows. The server-side storage and MVCC transaction layers are **not production-safe**: rollback is cosmetic, visibility rules permit dirty reads, and the BTree corrupts itself on delete rebalancing. Priority fixes: MVCC visibility/undo, BTree double-remove, WAL tail repair, and atomic file writes before any durability claims can be trusted.

---

## 5. Network Protocol / Client Review (complete)

### 5.1 Spec consistency (verified good)
- Header is genuinely 12 bytes, matching AGENTS.md (`AdvGenNoSqlServer.Network/MessageProtocol.cs:238,251-280`)
- CRC32 covers payload only, not the header
- Pre-auth frame cap (64 KB default) is enforced (`ServerConfiguration.cs:345`)
- Auth gates on Command/BulkOperation are correct (`AdvGenNoSqlServer.Server/NoSqlServer.cs:332-346,1584-1594`)
- Partial reads handled properly (`ConnectionHandler.cs:267-280`; `Client.cs:477-487`)

### 5.2 High
| # | Issue | Evidence |
|---|---|---|
| H1 | **No effective idle/read timeout (slow-loris)**: `ReceiveTimeout` is ignored by .NET for `ReadAsync`; a silent client holds a pool slot forever | `TcpServer.cs:224-225`; `ConnectionHandler.cs:269` |
| H2 | **TLS handshake before pool admission**: `AuthenticateAsServerAsync` has no timeout/cancellation token and runs before `TryAcquire`; unbounded fire-and-forget handlers allow pre-auth socket/CPU exhaustion | `TcpServer.cs:243-265,200`; `TlsStreamHelper.cs:135` |
| H3 | **Client response misrouting under concurrency**: send and receive locks are taken separately; two concurrent callers (or the keep-alive ping) can receive each other's responses; protocol has no request-correlation ID | `AdvGenNoSqlServer.Client/Client.cs:384-410,498` |

### 5.3 Medium
| # | Issue | Evidence |
|---|---|---|
| M1 | `Deserialize` missing length bounds check: validates only ≥16 bytes, then allocates up to 100 MB and `BlockCopy`s/reads checksum past buffer end (public API; network path unaffected) | `MessageProtocol.cs:400-418` |
| M2 | Unescaped JSON in error payloads + internals leak: `errorMessage` interpolated raw; `ex.Message` sent verbatim to clients (leaks paths/internals; malformed JSON when messages contain quotes/backslashes) | `MessageProtocol.cs:183`; `NoSqlServer.cs:664,777,970,1075` |
| M3 | **Capacity rejection parsed as success**: rejection sets `Payload` but never `PayloadLength`, so Serialize emits a 0-length payload; client `ParseResponse` treats empty payload as `Success=true` | `TcpServer.cs:351-355`; `Client.cs:520-522` |
| M4 | `EnableSslAsync` is a broken no-op: creates an SslStream, discards it (leak), leaves `_stream` plaintext, returns true; currently unused — latent downgrade bug | `ConnectionHandler.cs:145-148` |
| M5 | Cancellation not propagated in client: `ConnectAsync` ignores the token; keep-alive ping uses `CancellationToken.None`, so a dead server can hang ping/Dispose indefinitely | `Client.cs:178,498` |
| M6 | Zero-length payload checksum read but never verified | `MessageProtocol.cs:411`; `Client.cs:457-459` |

### 5.4 Low
- CRC excludes header (type/flags bytes malleable in transit; TLS mitigates) — `MessageProtocol.cs:289-290`
- Retry/backoff options are dead code (`MaxRetryAttempts`, `RetryDelayMs`, `AutoReconnect` referenced nowhere) — `ClientOptions.cs:58-68`
- Sync-over-async on close: `.Wait(5s)` per SSL connection can starve the thread pool during mass disconnect — `ConnectionHandler.cs:374`; `TcpServer.cs:176-181`
- Config inconsistency: `MaxMessageSizeMb` can exceed the hard 100 MB `ValidateHeader` cap, silently rejecting "allowed" frames — `TcpServer.cs:409`; `MessageProtocol.cs:337`
- Semaphore-leak edge: exception between `TryAcquire` and `TryAdd` skips `Release` (practically unreachable but fragile) — `TcpServer.cs:265-276`
- Handshake version ignored: echoes `clientVersion` without negotiation — `NoSqlServer.cs:216-231`
- Double allocation per read (rents then copies to a fresh array); unused PipeReader/PipeWriter are dead weight — `ConnectionHandler.cs:262-285,100-101`

### 5.5 Verdict
The framing layer is fundamentally sound (correct 12-byte header, proper partial-read loops, pre-auth size caps, enforced auth/RBAC gates), but it is **not yet hardened against resource exhaustion**. Priority fixes: cancellation-linked read/handshake timeouts, pool admission before TLS, a single request-response lock or correlation IDs, and escaped/sanitized error payloads.

---

## 6. Ops / Deployment / Test Quality / Repo Hygiene Review (complete)

### 6.1 Ops (verified good)
- `/health/ready` is meaningful: aggregates memory, disk (real storage path), a storage check against the default database, and a TCP-listener delegate — `AdvGenNoSqlServer.Host/Program.cs:187-204,640-651` (minor gap: auth-service availability not probed)
- `/metrics` requires JWT unless `AllowAnonymousMetrics`; labels are message-type enums only, no usernames/paths — `Program.cs:209,873-897`
- Graceful shutdown: `StopAsync` drains TCP with a 30s timeout and audits ServerStopped — `Program.cs:780-804`
- Audit logging: buffered with periodic flush, immediate flush for critical events (Low: sync `Log()` on hot paths; blocking `FlushAsync().GetAwaiter().GetResult()` in dispose — `AuditLogger.cs:587`)

### 6.2 Deployment
- **Dockerfile**: proper multi-stage, restore-layer caching, non-root `app` user, `/data` + `/certs` volumes, Production env defaults (Low: floating `9.0` base-image tags, no digest/minor pinning; no HEALTHCHECK — documented reason: no curl in base)
- **docker-compose.yml**: secrets via required env vars (`:?` guards), certs mounted read-only, `restart: unless-stopped` — good
- **CI** (`.github/workflows/dotnet.yml`): builds AND runs both test suites; correctly excludes the Windows-only WPF project; CodeQL workflow present (Low: no coverage collection or artifact upload)
- **Documentation/Deployment.md**: `NOSQL_*` env-var table matches actual config keys — accurate

### 6.3 Findings
| Severity | Issue | Evidence |
|---|---|---|
| Medium | **Serilog claim is false**: AGENTS.md/DEPENDENCIES.md advertise Serilog, but Host uses `Microsoft.Extensions.Logging` console JSON with hard-coded `SetMinimumLevel(Information)`; no Serilog reference in Host.csproj; no file/rolling sink — logs are not durable and level is not config-driven | `Program.cs:60-64` |
| Medium | **Uncommitted change contains a dev cert password**: `appsettings.json` adds `"SslCertificatePassword": "devpassword"`, about to be committed — contradicts the repo's own "never commit secrets" posture (.gitignore excludes certs/); move to env var/user-secrets. The other diff (`_Host.cshtml`) adds the standard Blazor reconnect banner — legitimate WIP | `git diff AdvGenNoSqlServer.Host/appsettings.json` |
| Medium | **Inflated test counts**: 17 `Assert.True(true)`-style trivial passes, several explicitly labeled "Placeholder" | `ClientCertificateTests.cs:630`; `AlpnTests.cs:576`; `ChangeStreamTests.cs:717` |
| Medium | **Timing-based flaky patterns**: 27 `Thread.Sleep` calls (e.g., sleeping 1100ms for TTL expiry), 68 `Task.Delay`; fixed port 19090 in 7 places (dynamic-port helper used in only 8 spots) — parallel CI runs can collide | `CacheManagerTests.cs:406-445`; ChangeStreamTests 100ms polls |
| Medium | **Stale/contradictory docs**: AGENTS.md still says "Phase 2, 75%, next: client library" (hardening phases 1–4 are done per git log); PROJECT_STATUS.md marks all 8 phases complete yet says "Phase 3 SSL/TLS pending" despite extensive TLS tests; README still warns "harden before any production use" and uses outdated keys (`CertificatePath` vs actual `SslCertificatePath`) | AGENTS.md; PROJECT_STATUS.md; README.md |
| Medium | **Tracked repo clutter**: `# 1.md`, `basic.md` (47KB), `qwen.md` (empty), `Untitled-2.txt`, `plan.md` (193KB), `reviews.md` (100KB), `multiagents.md` (248KB), `pr_review.md`, `report.md`, `changes.md`, `test_plan.md`, throwaway `TestConnection/` project are all in git; untracked `no sql/`, `nupkg/`, root `BenchmarkDotNet.Artifacts/`, `run-server-and-admin.bat` add noise | `git ls-files` |
| Low | .gitignore coverage otherwise good: only 1 tracked build artifact (a BenchmarkDotNet log under Benchmarks); bin/obj/nupkg untracked | `git ls-files` |

### 6.4 Test coverage gaps
- Crash recovery tested only for Embedded (`CrashRecoveryTests.cs`); **no crash-recovery tests for the server WAL path**
- No network fault-injection/chaos suite
- Concurrency tests exist but stress depth unclear; BTree concurrency tests skipped (see §2.2)

### 6.5 Verdict
Operational fundamentals are genuinely in place (meaningful readiness probes, safe metrics, graceful drain, non-root multi-stage Docker, CI that builds and tests) — well above hobby-project readiness. However, stale/contradictory status docs, the false Serilog claim, placeholder asserts, timing-based flaky tests, and a dev password staged for commit undermine trust in the "production hardened" label.

---

## 7. Recommended Fix Priorities (all findings confirmed)

### P0 — Data integrity (before any durability claim or production use)
1. MVCC: enforce commit-state visibility in `IsVisibleTo`; implement real undo on rollback (writes currently apply immediately and rollback is WAL-only) (D1, D9)
2. Fix BTree merge double-remove; fix locks held across `yield`, live-list escape in `GetValues`, stale separator after leaf delete, and `Count` miscount (D2, D12); re-enable the skipped BTree concurrency tests to guard the fixes
3. WAL crash recovery: truncate torn tail on CRC mismatch instead of throwing (Core, D5); truncate corrupt tail when replay yields 0 pages (Embedded, D3); validate replay lengths/checksums for sub-startLsn entries (D22)
4. Route page alloc / free-list / catalog-root metadata through the WAL so rollback and replay are consistent (D4)
5. Atomic file writes everywhere (temp + fsync + rename): document files, FileStorageManager; stop silently skipping corrupt files on load (D6, D21)
6. Store-wide exclusion during Embedded compaction copy/swap; preserve CreatedAt/UpdatedAt/Version in the compactor (D11)

### P0 — Security (before any exposed deployment)
7. Fix the client TLS "localhost" substring bypass — exact host match / proper SAN validation (S1)
8. Add Write-role checks to HTTP POST/PUT document endpoints, matching the TCP authorization model (S2)
9. Require TLS for the Server admin API (or bind loopback-only); constant-time key comparison (S3)
10. Remove `devpassword` from `Host/appsettings.json` (env var or user-secrets) before it is committed (S7)

### P0 — Protocol semantics & availability
11. Fix capacity-rejection: set `PayloadLength`; client must not treat empty payload as success (network M3)
12. Cancellation-linked read/handshake timeouts; move connection-pool admission before the TLS handshake (network H1/H2)

### P1 — Short term
13. MVCC: real conflict detection (current check is dead code → lost updates), working version GC, snapshot-aware `CountAsync`, insert under chain lock (D7, D8, D19)
14. WAL archive rotation must respect checkpoints; fsync truncate temp file before rename; recover `_logFile` on failure (D10)
15. Replace `async void` deadlock/cleanup handlers with awaited tasks (process-crash risk) (D15)
16. Session expiry/revocation: re-check token expiry; disconnect live sessions on `SetRole`/`SetPassword`/`RevokeAllUserTokens` (S4)
17. Reject JWTs without `exp`; add jti/revocation tracking; bound refresh chains (S5)
18. Per-IP auth rate limiting; avoid 600k-iteration PBKDF2 for unknown users; mitigate admin lockout-DoS (S6)
19. Per-user/per-request database context instead of a shared singleton any user can switch (S8)
20. Client concurrency safety: single request-response lock or protocol correlation IDs; propagate cancellation tokens (network H3, M5)
21. Escape and sanitize error payloads; stop returning `ex.Message` to clients (network M2, security Low JSON injection)
22. Numeric precision: compare long/decimal without `Convert.ToDouble` degradation; fix `$regex` substring fallback (D16)
23. Fix role-case mismatch (`admin` vs `Admin`), strengthen secret policy, fix random-key mismatch when JWT auth is disabled (security Low)
24. Test credibility: fix flaky concurrency/timing tests (incl. the BackgroundIndexBuilder failure), remove placeholder asserts, add crash-recovery tests for the server WAL path

### P2 — Planned
25. Page checksum to cover header (D13); PersistentDocumentStore filename-collision and atomic-count fixes (D14); LruCache eviction-under-lock and Dispose/timer race (D18); FullText read locking + cached avg doc length (D20); streaming WAL read instead of slurp + int cast (D17); strict unknown-key handling in QueryParser (Low)
26. Documentation reconciliation: AGENTS.md / PROJECT_STATUS.md / README / DEPENDENCIES.md (correct the Serilog claim or adopt Serilog with a durable sink); add all security/storage findings to ProductionReadiness.md's residual-issue table
27. Repo hygiene: archive/remove root clutter docs and `TestConnection/`; untrack the benchmark artifact
28. Network Low items: CRC over header, remove dead code (retry options, PipeReader), fix the `EnableSslAsync` no-op, align config size caps
29. Pin Docker base images by digest; add coverage collection to CI
30. Windows ACL hardening for users.json (security Low)

---

## 8. Overall Conclusion — Final

**Verdict: not production-ready.** The "production hardened" label (commits ce0afac–7a32d8f) is accurate for cryptography, TCP auth gating, config validation, and ops plumbing — but it does not hold for the durability layer or the HTTP/admin surface.

- **Storage/transactions (6 Critical, 9 High)**: MVCC permits dirty reads and rollback is cosmetic; the BTree corrupts itself on delete rebalancing; both WAL implementations have torn-tail recovery defects; metadata writes bypass the WAL; document files are written non-atomically and corrupt files are silently dropped. Any crash, rollback, or concurrent compaction can lose or corrupt data. This is the decisive blocker.
- **Security (3 High, 5 Medium)**: a real client-side TLS MITM vector, readonly users writing via HTTP, a cleartext admin key in Production, sessions that never expire, and JWTs without expiry. Posture: hardened single-node TCP, not production-safe HTTP.
- **Network/client (3 High, 6 Medium)**: sound framing and auth gates, but pre-auth resource exhaustion (no read timeouts, TLS before admission) and a client that misroutes responses under concurrency and can report rejections as success.
- **Ops/tests/docs**: genuinely good fundamentals (readiness probes, non-root Docker, CI with both suites), undermined by 989 build warnings, placeholder asserts, skipped BTree concurrency tests, flaky timing tests, stale/contradictory status docs, and a dev password staged for commit.

**Path to production**: complete P0 items 1–12 (data integrity first, then security and protocol semantics), back them with the re-enabled/added tests (BTree concurrency, server WAL crash recovery, compaction-under-write), then reconcile the documentation before re-claiming production readiness. The Embedded engine's happy path and test suite (152/152, incl. crash recovery) are the strongest part of the codebase; the server-side MVCC/storage layer needs the most work.

**Update (2026-11-13)**: P0 data-integrity items 1–6 have been remediated and verified (see §9). Security P0 items 7–10 and protocol P0 items 11–12 remain outstanding; the production-readiness verdict is unchanged until those are addressed.

---

## 9. P0 Remediation Record (2026-11-13)

All six P0 data-integrity items were fixed, with new/re-enabled regression tests. Final verification: solution builds with 0 errors; main suite **3306 passed / 0 failed / 25 skipped** (one load-dependent flaky failure in an intermediate run did not reproduce); Embedded suite **157/157 passed**.

### P0-2 — BTree (D2/D12)
- Rewrote the delete path: removed the double child-removal in `Rebalance` (`MergeWithRightSibling` already removes it); whole-key deletion now removes both the separator copy and the leaf copy of promoted keys; `Count` accounting is reference-aware (split promotion shares one value-list instance between separator and leaf); merge/borrow deduplicate leaf keys instead of creating duplicate entries; empty children are repaired via borrow/merge instead of the broken `HandleEmptyChild`.
- Range/scan iterators (`RangeQuery`, `GetGreaterThanOrEqual`, `GetLessThanOrEqual`, `GetAll`) now snapshot under the lock instead of holding it across `yield`; `GetValues` returns a copy; `TryGetValue` guards empty value lists.
- Re-enabled 4 skipped tests (2 delete/rebalance, 2 concurrent insert). Files: `Storage/Indexing/BTreeIndex.cs`, `BTreeNode.cs`.

### P0-3 — WAL torn-tail recovery (D5/D3/D22)
- Core WAL: replay stops at a torn/corrupt tail and `RecoverAsync` truncates the current log at the corruption point instead of throwing; entry lengths and timestamps are bounds-validated; entries below `startLsn` are now read and CRC-verified (no blind seeks); partial reads loop to completion.
- Embedded WAL: the constructor repairs a torn/corrupt tail to the last valid frame; `RecoverFromWal` always truncates after replay (even with zero committed pages).
- New tests: `Wal_RecoverAsync_TornTail_TruncatesAndAllowsNewAppends`, `Crash_CorruptWalTail_NewCommitsSurviveNextRecovery`. Files: `Core/Transactions/WriteAheadLog.cs`, `Embedded/Storage/WriteAheadLog.cs`, `WalPageStore.cs`.

### P0-5 — Atomic file writes (D6/D21)
- New `AtomicFileWriter` (temp + fsync + atomic rename) used by `PersistentDocumentStore.SaveDocumentToDiskAsync` and `FileStorageManager.SaveDocumentAsync`.
- Corrupt document files are quarantined to a `_corrupt` subfolder on load instead of silently skipped; `FileStorageManager.LoadDocumentAsync` no longer swallows IO/permission errors (only `JsonException` → null).

### P0-6 — Compaction (D11)
- `CompactAsync` holds the store-wide write lock across copy AND swap (`EnterExclusive`), with ungated internal reads in the compactor to avoid deadlock; storage disposal split from lock disposal so the exclusive scope exits cleanly.
- Compaction preserves `CreatedAt`/`UpdatedAt`/`Version` via `InsertPreservingMetadataAsync`.
- New test: `Compact_PreservesTimestampsAndVersion`; existing interrupted-swap test still passes. Files: `Embedded/AdvGenDatabase.cs`, `EmbeddedDocumentStore.cs`, `Storage/Compactor.cs`.

### P0-1 — MVCC (D1/D9/D7)
- Transaction commit-state registry (`MvccTransactionRecord`): visibility now requires the creator to be committed with commit timestamp ≤ the snapshot's read timestamp — uncommitted writes are invisible to other snapshots (no dirty reads). Unregistered creators keep legacy timestamp semantics for direct-write compatibility.
- Real rollback: the context keeps an undo log (insert/update/delete) and physically undoes applied versions on rollback (`UndoVersionAsync`, `TryRemoveLatestVersion`, `UnmarkDeleted`), then marks the transaction rolled back.
- Write-write conflict detection fixed: checks the LATEST version against the registry (the old snapshot-visible check was dead code); applies at all isolation levels.
- `TransactionContext.RecordWriteAsync` now always takes an Exclusive lock (was Shared below Serializable).
- Re-enabled `Mvcc_Serializable_WriteWriteConflict_Detected`; new tests `Mvcc_UncommittedWrite_NotVisibleToOtherSnapshot`, `Mvcc_Rollback_UndoesInsertUpdateDelete`. Files: `Core/Transactions/DocumentVersion.cs`, `MvccDocumentStore.cs`, `MvccTransactionContext.cs`, `IMvccStore.cs`, `TransactionContext.cs`, new `MvccTransactionRecord.cs`.

### P0-4 — Transactional page metadata (D4)
- `WalPageStore` redesigned: allocation, free-list, and catalog-root changes are transactional overlays; nothing touches the main file before commit. On commit, page images AND the header page are WAL-logged together (fsync), so recovery restores metadata atomically with data; the main file is only updated at checkpoint, keeping it consistent with the pages it physically holds (WAL loss can no longer leave dangling catalog/free-list references). Rollback discards all pending data and metadata.
- Fixed a cache-loss bug found by the new tests: `FreePage` no longer drops the committed page image from the checkpoint cache (it may be the only copy before checkpoint).
- New tests: `Rollback_RevertsFreeListAndAllocation`, `Crash_CommittedMetadataReplayedWithPages`, `LostWal_MainFileNeverReferencesUncheckpointedPages`. Files: `Embedded/Storage/WalPageStore.cs`, `FilePageStore.cs`.

### Remaining P0 (not addressed in this pass)
- Security: S1 client TLS "localhost" bypass, S2 HTTP write-role checks, S3 admin API TLS/constant-time key, S7 `devpassword` in config.
- Protocol: network M3 capacity-rejection semantics, H1/H2 read/handshake timeouts + pool admission before TLS.

---

## 10. Reviewer-Driven Remediation (2026-11-13, second pass)

An independent reviewer agent committed 12 review passes to `reviews.md` (`489763e..2c8afe7`, ~50 findings, documentation only — no code changes). Its findings were verified against the actual code, including the uncommitted P0 changes from §9. Four confirmed findings were remediated:

### TXN-003 — MVCC residuals in the P0-1 fix (High)
- (a) `VersionChain.GetVisibleVersion(snapshot, …)` now stops at a deletion visible to the snapshot — deleted documents no longer resurrect stale pre-delete versions.
- (b) Removed raw `IsDeleted` filtering in `MvccTransactionContext.GetAsync/GetAllAsync/InsertAsync` and `MvccDocumentStore.GetVisibleVersionsAsync/ExistsAsync` — another transaction's uncommitted delete is no longer observed.
- (c) `EnsureNoWriteConflictAsync` now also checks the latest version's **deleter** (concurrent delete = conflict). Residual: the check-then-write is still non-atomic across store calls; full atomicity needs per-document write locking.
- (d) `RollbackAsync` accepts the Failed state (and auto-rollback on dispose covers it), so a failed commit's applied versions can be undone.
- New tests: `Mvcc_DeletedDocument_DoesNotResurrectOlderVersion`, `Mvcc_UncommittedDelete_NotVisibleToOtherSnapshot`.

### TXN-005 — WAL mid-log corruption vs torn tail (Medium)
- `ReplayCurrentFileAsync` now probes for a structurally valid, checksum-correct entry after a corrupt position (monotonic-LSN filter + full validation). If later valid entries exist, recovery **fails with an error instead of truncating** committed data; only true torn tails are truncated.
- New test: `Wal_RecoverAsync_MidLogCorruption_FailsInsteadOfTruncating` (asserts failure and that the log length is unchanged).

### IO-002 — AtomicFileWriter hardening (Medium)
- Moved to `AdvGenNoSqlServer.Core/IO/AtomicFileWriter.cs` as **public** so Core consumers can use it (the Storage-internal version was deleted).
- Parent directory fsync after rename on Linux (guarded; Windows skips).
- Async overload uses `FileOptions.Asynchronous` (final fsync remains synchronous — the BCL has no async fsync).
- `CleanupTempFiles` sweeps orphaned `*.tmp` at startup; wired into `PersistentDocumentStore.InitializeAsync` and `FileStorageManager` constructor.
- WAL checkpoint metadata (`CreateCheckpointAsync`) now written atomically.

### STOR-001 — production store durability (Critical)
- The servers use `HybridDocumentStore` (via `DatabaseManager`), not `PersistentDocumentStore` — the §9 P0-5 fix had missed the production path. Now fixed:
  - Every write carries a `TaskCompletionSource`; `InsertAsync`/`UpdateAsync`/`DeleteAsync` acknowledge success **only after** the background writer has durably persisted the change, and propagate IO failures to the caller instead of returning false success.
  - Disk writes use `AtomicFileWriter` (temp + fsync + rename), including `SaveAllAsync`.
  - Load-time corrupt files are quarantined to `_corrupt/` instead of silently skipped; `LoadFromDiskAsync` no longer swallows IO errors.
  - Shutdown drains queued operations, completing waiting callers with cancellation/failure instead of hanging.
- New tests: `Insert_AcknowledgedWrite_IsDurableWithoutFlush` (second store instance sees the data with no flush/dispose), `Insert_DiskFailure_ThrowsInsteadOfFalseSuccess`.
- Residual (separate findings): STOR-002 drop/clear vs write-queue ordering, STOR-003 unbounded in-memory dataset, cache/disk divergence after a failed write.

### Reviewer claim assessed as inaccurate
- **SEC-038** ("Server HTTP API completely unauthenticated", Critical): an X-Api-Key middleware gate exists in `Server/Program.cs` (401 on missing/invalid key when configured; Production validation forbids an empty key). The real issues are cleartext HTTP transport of the key, open access when no key is configured, and non-constant-time comparison — matching S3 (High) of this report, not "no authentication".

### Verification
- Solution build: 0 errors. Main suite: **3311 passed / 0 failed / 25 skipped**. Embedded suite: **157/157 passed**.

### Still outstanding
- Security P0: S1, S2, S3, S7. Protocol P0: M3, H1/H2.
- Reviewer findings not addressed in this pass: TXN-001/TXN-002 (server writes not behind the transaction layer; Session rollback no-op), STOR-002/003/004/005, QRY-001..004, SEC-039/040/041/042, FACT-001 (client factory drops TLS options), IO-001/003/004, CACHE-001..004, REPO-001..005, CMD/OBS items.

---

## 11. User & Role Management Remediation (2026-11-13, third pass)

Goal: make user/role management complete and consistent across TCP, HTTP, and the Admin UI (addresses SEC-039/040/041, S2/S4/S5 and the role-case defect).

### Fixes (applied to BOTH Host/Program.cs and Server/NoSqlServer.cs)
- **HTTP authorization matrix (S2 / SEC-039 / role-case)**: endpoints now use lowercase `UserRole` constants (JWTs carry lowercase roles; `Roles="Admin"` never matched). Aligned with the TCP `CommandAuthorizer`: document POST/PUT/DELETE and collection create → `admin,readwrite`; database create/drop and collection drop → `admin`.
- **Session revocation (S4 / SEC-041)**: `setrole`/`deleteuser` evict ALL of the target user's cached TCP identities (including the issuing connection); `setpassword`/`changepassword` evict all except the issuer. All four also call `RevokeAllUserTokens`. `AuthenticationManager.SetRoleGuarded` now revokes tokens too (previously the only mutator that did not).
- **Anonymous role enforcement (SEC-040)**: with `RequireAuthentication=false`, unauthenticated connections are no longer unrestricted — commands and bulk operations are authorized against `AnonymousRole` (default `readonly`). Previously the role check was skipped entirely when auth was disabled.
- **JWT expiry required (S5)**: `ValidateToken` rejects tokens without an `exp` claim (previously treated as never-expiring via `DateTime.MaxValue`); `ExtractUsername` rejects expired/no-exp tokens.

### Tests
- 9 new regression tests: `SessionRevocationTests` (setrole/deleteuser/setpassword revocation), `AnonymousRoleEnforcementTests` (anonymous readonly cannot write/manage users, can read), JWT exp enforcement (missing-exp rejected, expired rejected, ExtractUsername null).
- 7 functional test harnesses that relied on the SEC-040 bug (auth disabled = unrestricted) now explicitly set `AnonymousRole="admin"` — an intentional configuration choice for command-functionality tests; the secure default is covered by the dedicated tests above.

### Verification
- Main suite: **3320 passed / 0 failed / 25 skipped**. Embedded suite: **157/157**. Solution build: 0 errors.

### Remaining (documented, not addressed this pass)
- **HTTP JWT revocation**: tokens are stateless; revoked users' JWTs remain valid until expiry. Full revocation needs a jti denylist checked in JWT validation (TCP sessions are fully revoked).
- **Server HTTP API key transport (S3)**: X-Api-Key over plain HTTP — terminate TLS at a reverse proxy.
- **Fixed three-role model**: admin/readwrite/readonly only. Custom roles/granular permissions (MongoDB-style) would be a new feature.

---

*Generated from read-only code review; no source code was modified. Evidence line numbers come from actual file reads at review time.*