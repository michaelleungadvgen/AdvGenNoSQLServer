# Cache-Only Mode and Redis-Style KV Cache — Design

**Date:** 2026-07-14
**Status:** Approved by user (brainstorming session)

## Goal

Upgrade AdvGenNoSqlServer with high-performance, Redis-like caching:

1. **CacheOnly server mode** — a whole-server configuration switch under which the server runs entirely in memory: no disk reads or writes, data volatile across restarts. The existing document API keeps working, backed by RAM only.
2. **Redis-style KV cache API** — a flat key-value cache surface (GET/SET/DEL/EXPIRE/INCR, etc.) with TTL and eviction, available in **both** server modes, exposed over the binary TCP protocol and the client library.

Non-goals (this iteration): Redis data structures (hashes, lists, sets), RESP protocol compatibility, HTTP/REST cache endpoints, Admin UI cache browser, per-database or per-collection storage modes, cache persistence/AOF.

## Background

- The server currently hardwires `HybridDocumentStore` (memory + disk) in `NoSqlServer.StartAsync` (`AdvGenNoSqlServer.Server/NoSqlServer.cs`).
- `AdvGenNoSqlServer.Core/MemoryManagement/` already provides the cache engine foundation: `IMemoryStorageEngine` (TryGet/Set-with-TTL/Remove/Clear/Stats), `MemoryEngineFactory` producing `Native`, `Managed`, or `Mixed` engines, `EvictionManager` with LRU/LFU/TTL policies, and `MemoryManagementConfiguration` (MaxMemoryMB, MaxMemoryPercent, DefaultTtlSeconds).
- An in-memory `IDocumentStore` implementation already exists: `AdvGenNoSqlServer.Storage/DocumentStore.cs` (thread-safe, `ConcurrentDictionary` of `InMemoryDocumentCollection`).
- TCP commands are length-framed `NoSqlMessage`s; document commands are JSON payloads dispatched by an `operation` string in `NoSqlServer.HandleCommandAsync`.

## Design

### 1. CacheOnly server mode

**Configuration** (`ServerConfiguration` + appsettings binding):

```json
{
  "Server": {
    "StorageMode": "CacheOnly",        // "Hybrid" (default) | "CacheOnly"
    "MemoryManagement": {
      "Plan": "Managed",               // Native | Managed | Mixed
      "MaxMemoryMB": 1024,
      "MaxMemoryPercent": 75,
      "EvictionPolicy": "LRU",         // LRU | LFU | TTL
      "DefaultTtlSeconds": 3600
    }
  }
}
```

- `StorageMode` parsing is case-insensitive; unknown values fall back to `Hybrid` with a warning log.
- `MemoryManagement` binds the existing `MemoryManagementConfiguration` type and configures the KV cache engine (both modes).

**Server wiring** (`NoSqlServer`):

- `_documentStore` field type changes from `HybridDocumentStore?` to `IDocumentStore?`.
- Startup selects the store:
  - `Hybrid` → `HybridDocumentStore(storagePath)` + `InitializeAsync()` — current behavior, unchanged.
  - `CacheOnly` → `new DocumentStore()` — no storage path resolution, no disk I/O. Startup logs a prominent warning: data is volatile and lost on restart.
- Shutdown: persistence-only calls (`FlushAsync`, async dispose of the hybrid store) are guarded by capability checks (`is IPersistentDocumentStore` / `is IAsyncDisposable`), so Hybrid keeps its flush-on-stop behavior and CacheOnly skips it.
- `ApiDataService.DocumentStore` (and any other consumer typed to `HybridDocumentStore`) is retyped to `IDocumentStore`; HTTP endpoints that expose persistence-specific stats degrade gracefully (report mode instead).

**Document API in CacheOnly mode:** all existing document commands (insert, get, find, count, collections, etc.) work unchanged against the in-memory store. No command is disabled.

### 2. CacheStore (KV engine wrapper)

New class `CacheStore` in `AdvGenNoSqlServer.Storage`, constructed from `MemoryManagementConfiguration` via `MemoryEngineFactory`. It adds Redis semantics on top of `IMemoryStorageEngine`:

| Operation | Semantics |
|---|---|
| `Get(key)` | miss → null |
| `Set(key, value, ttl?, flags)` | flags: `None`, `NX` (only if absent), `XX` (only if present); ttl null → engine default |
| `Delete(key)` / `Exists(key)` | bool result |
| `Expire(key, ttl)` | re-set TTL on existing key; false if missing |
| `Ttl(key)` | remaining TTL; null = no expiry; missing key distinguished |
| `Incr/Decr/IncrBy(key, delta)` | atomic; value stored as ASCII integer; non-numeric value → `WrongType` error; missing key starts at 0 |
| `MGet(keys)` / `MSet(pairs)` | batch; MSet is not atomic across keys (documented) |
| `Keys(pattern)` / `Scan(pattern, cursor, count)` | glob patterns (`*`, `?`, `[...]`); Scan is cursor-based for large keyspaces |
| `Flush()` | engine `Clear()` |
| `Stats()` | surfaces `MemoryEngineStats` (entries, bytes, hits, misses, evictions) |

**Engine extension.** `IMemoryStorageEngine` gets a small optional extension interface (implemented by the existing engines):

```csharp
public interface IIntrospectableMemoryStorageEngine : IMemoryStorageEngine
{
    bool TryGetTtl(string key, out TimeSpan? remaining); // null = no expiry
    IEnumerable<string> EnumerateKeys();                  // snapshot semantics
}
```

`CacheStore` requires this interface for `EXPIRE`/`TTL`/`KEYS`/`SCAN`; all three shipped engines implement it.

**Atomicity.** Counters use striped locks (e.g. 64 stripes keyed by key hash) around get-modify-set. `SET NX/XX` uses the same stripes. Plain GET/SET/DEL go straight to the engine (already thread-safe).

**Memory pressure.** `SET` never fails for capacity — the engine evicts per its configured policy, matching Redis `maxmemory-policy` behavior.

**Lifetime.** One `CacheStore` per server, created at startup in both modes from the `MemoryManagement` config, disposed on shutdown. It is independent of the document store (no shared keyspace).

### 3. Protocol: binary CacheOperation frame

Cache ops bypass JSON. A new `MessageType.CacheOperation` (and `CacheResponse`) carries a compact binary payload inside the existing `NoSqlMessage` framing:

```
Request payload:
[op: byte] [flags: byte] [ttlSeconds: int32 (-1 = none)]
[keyLen: uint16] [key: utf8 bytes]
[valueLen: int32 (-1 = none)] [value: raw bytes]
(batch ops: [count: uint16] followed by repeated key/value records)

Response payload:
[status: byte (Ok | NotFound | WrongType | Error)]
[valueLen: int32 (-1 = none)] [value: raw bytes]
(INCR family returns the counter as int64 value; batch returns repeated records)
```

- Op codes: `Get=1, Set=2, Del=3, Exists=4, Expire=5, Ttl=6, Incr=7, Decr=8, IncrBy=9, MGet=10, MSet=11, Keys=12, Scan=13, Flush=14, Stats=15`.
- Values are raw bytes end to end — no base64, no JSON allocation per op.
- Limits: key ≤ 1 KB and non-empty; value ≤ configurable max (default 16 MB). Violations → `Error` status with message, connection stays open.
- Malformed frames → `Error` response; the connection is not terminated unless framing itself is corrupt (existing behavior).
- Dispatch: a `HandleCacheOperationAsync` beside the existing handlers in `NoSqlServer`, routing to `CacheStore`.
- Auth: cache operations require the same authenticated session state as document commands.

### 4. Client API

`NoSqlClient` gains a `Cache` property returning a `CacheClient` that encodes/decodes the binary frames:

```csharp
await client.Cache.SetAsync("user:42", bytes, TimeSpan.FromMinutes(5));
await client.Cache.SetStringAsync("greeting", "hello", ttl);           // UTF-8 convenience
byte[]? v   = await client.Cache.GetAsync("user:42");                  // null on miss
string? s   = await client.Cache.GetStringAsync("greeting");
long hits   = await client.Cache.IncrAsync("hits:page1");
bool ok     = await client.Cache.ExpireAsync("user:42", TimeSpan.FromMinutes(10));
TimeSpan? t = await client.Cache.TtlAsync("user:42");
IReadOnlyDictionary<string, byte[]?> m = await client.Cache.MGetAsync(keys);
IReadOnlyList<string> ks = await client.Cache.ScanAsync("user:*");
await client.Cache.FlushAsync();
CacheStats st = await client.Cache.StatsAsync();
```

Byte arrays are the primitive; string (UTF-8) and JSON-typed overloads are thin wrappers. `WrongType`/`Error` statuses surface as typed exceptions (`CacheWrongTypeException`, `CacheException`); `NotFound` maps to null/false returns, never exceptions.

### 5. Error handling summary

- `INCR` on non-numeric value → `WrongType` status → `CacheWrongTypeException` client-side; connection unaffected.
- Key/value limit violations → `Error` status with descriptive message.
- Eviction is silent and policy-driven; never an error.
- CacheOnly mode: document behavior identical to today minus persistence.

### 6. Testing

- **Unit — CacheStore:** TTL expiry, default TTL application, NX/XX flags, INCR atomicity under `Parallel.For`, WrongType on non-numeric INCR, glob matching (`*`, `?`, `[...]`), Scan cursor completeness, eviction at memory cap, Flush, Stats counters.
- **Unit — protocol:** CacheOperation frame encode/decode round-trips including empty value, max-size value, batch records, malformed frame rejection.
- **Integration:** server in CacheOnly mode serves document commands with no files created under the storage path; restart loses data; cache commands work in both Hybrid and CacheOnly modes; auth required for cache ops.
- **Benchmarks (BenchmarkDotNet, existing project):** CacheStore direct GET/SET/INCR throughput per engine plan (Managed/Native/Mixed); end-to-end TCP GET/SET latency and ops/sec.

## Decisions log

| Question | Decision |
|---|---|
| Scope | Both: CacheOnly server mode and Redis-style KV API |
| Mode granularity | Whole-server `StorageMode` config switch |
| KV command set | Core + counters + batch (GET/SET/DEL/EXISTS/EXPIRE/TTL/INCR family/MGET/MSET/KEYS/SCAN/FLUSH) |
| Client surface | TCP binary protocol + client library (HTTP/Admin later) |
| Document API in CacheOnly | Still available, RAM-backed |
| Implementation approach | Reuse existing MemoryManagement engines + in-memory DocumentStore |
| Cache wire format | Dedicated binary `CacheOperation` frame (raw bytes, no JSON) |
