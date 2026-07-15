# AdvGenNoSqlServer.Embedded — Local Embedded Database (LiteDB-style) — Design

**Date:** 2026-07-15
**Status:** Approved by user (brainstorming session)

## Goal

Provide a **local, in-process, embedded version of AdvGenNoSQL** — lightweight and fast, in the spirit of LiteDB — so desktop/local apps (first consumer: AdvGenPriceComparer, which currently uses LiteDB) can use the AdvGen document database **without running the server**: no TCP, no HTTP, no authentication, no host process. One `.agdb` file on disk, one `AdvGenDatabase` object in code.

```csharp
using var db = new AdvGenDatabase("prices.agdb");
var items = db.GetCollection<Item>("items");
items.EnsureIndex(x => x.Barcode);
items.Insert(new Item { Name = "Milk 2L", Barcode = "93052001" });
var cheap = items.Find(x => x.Price < 3.0m && x.IsActive);
```

Non-goals (this iteration): multi-process access to one file, replication/clustering, change streams, full-text search, field encryption, server-side sessions/auth, multi-operation transactions (see Deferred).

## Background

- The solution already has the building blocks, all server-independent:
  - `AdvGenNoSqlServer.Core` — `Document` model (`Id`, `Dictionary<string, object> Data`, timestamps, `Version`), `IDocumentStore` CRUD abstraction (`Core/Abstractions/IDocumentStore.cs`).
  - `AdvGenNoSqlServer.Storage` — in-memory `BTreeIndex<TKey,TValue>` + `IndexManager` (`Storage/Indexing/`), various `IDocumentStore` decorators.
  - `AdvGenNoSqlServer.Query` — `QueryParser`, `FilterEngine`, `QueryExecutor` (`IQueryExecutor`: Execute/Count/Exists/Distinct/Explain) operating on `Document`s.
- Existing persistence (`FileStorageManager`) writes **one pretty-printed JSON file per document** — unacceptable for an embedded engine (thousands of small files, slow scans). A new single-file storage engine is the core new work.
- AdvGenPriceComparer's `Data.LiteDB` layer uses this exact LiteDB surface: `new LiteDatabase(connString)`, `GetCollection<T>(name)`, `EnsureIndex(x => x.Prop)`, `Insert`, `Update`, `Upsert`, `Delete`, `FindById`, `FindOne(pred)`, `Find(pred)`, `FindAll`, `Query()`, `Count`. The typed API below deliberately mirrors it so migration is near-mechanical.
- There is a separate plan for an embedded **SQL** engine (`Embedded_SQL_Lite_Plan.md`); this design is the embedded **document/NoSQL** counterpart and is independent of it.

## Architecture decisions

### AD-1: New project `AdvGenNoSqlServer.Embedded`, heavy reuse of Core/Storage/Query

One new class library (net9.0, matching the solution) referencing `Core`, `Storage`, and `Query` only — never `Network`, `Server`, or `Host`. It contributes exactly two new subsystems (single-file storage, typed mapper layer) and reuses everything else:

| Concern | Source |
|---|---|
| Document model, store abstraction, exceptions | reuse `Core` (`Document`, `IDocumentStore`) |
| Secondary indexes (B-tree, range queries) | reuse `Storage` (`BTreeIndex`, `IndexManager`) |
| Filtering, query execution, distinct, explain | reuse `Query` (`FilterEngine`, `QueryExecutor`) |
| Single-file paged storage + WAL | **new** (`Embedded/Storage/`) |
| Typed collections, POCO mapper, expression → filter translation | **new** (`Embedded/Typed/`) |

Packaged as NuGet `AdvGenNoSqlServer.Embedded` (pulls Core/Storage/Query as dependencies). Pure managed C#, no native binaries.

### AD-2: Single-file page-based storage with WAL

One main data file + one WAL file (`prices.agdb` + `prices.agdb.wal`).

**Page layout** — fixed 8 KB pages:

```
[Page header — 32 bytes]
  magic      uint32   0xAD6DB001
  pageType   byte     header=0, catalog=1, data=2, overflow=3, free=4
  pageId     uint32
  nextPageId uint32   overflow / free-list chaining (0 = none)
  slotCount  uint16
  freeBytes  uint16
  checksum   uint32   CRC32 of page body
[Slot array — 4 bytes/slot: offset uint16, length uint16]
[Records — grow toward slot array]
```

- **Header page (0):** file magic, format version, catalog root page id, free-list head, last checkpoint LSN.
- **Catalog pages:** collection name → first data page id + index definitions (name, field, unique) as compact JSON records.
- **Data pages:** each record is one document: `[docId length-prefixed UTF-8][flags byte][body]` where body is compact (non-indented) UTF-8 JSON of the full `Document` (data + timestamps + version). Documents larger than a page spill into an **overflow page chain**.
- **Record address:** `(pageId, slot)`; the in-memory primary index maps `docId → address`. Updates that fit in place overwrite the slot; otherwise write-new + tombstone-old (space reclaimed by compaction).

**WAL / durability:**

- Every mutation appends frames to the WAL: `[lsn][pageId][pageImage][frameChecksum]`, then a commit frame; the WAL is flushed (`FileStream.Flush(flushToDisk: true)`) before the operation returns. The main file is never written mid-operation.
- **Checkpoint** copies committed frames into the main file and truncates the WAL. Triggered when the WAL exceeds a size threshold (default 4 MB), on `Checkpoint()`, and on `Dispose()`.
- **Recovery on open:** replay WAL frames with valid checksums up to the last commit frame; discard the tail. A torn write can never corrupt the main file.

Rationale: this is the same model SQLite/LiteDB use; it gives crash safety with sequential-write performance and keeps the main file always consistent.

### AD-3: Indexes are in-memory, rebuilt on open (v1)

The primary index (`docId → record address`) and all secondary `BTreeIndex` instances live in memory and are **rebuilt by one sequential scan of the data file when the database opens**. Index definitions (from `EnsureIndex`) persist in the catalog so rebuild is automatic.

- Rationale: keeps the file format radically simpler (no index pages, no B-tree splits on disk, no index/WAL interplay). At the target scale — local apps, up to low hundreds of thousands of documents — open-time rebuild is one sequential file read, well under a second.
- Trade-off (accepted): open time grows with data size; documents are read from disk pages on demand, but index memory grows with document count.
- Future upgrade (out of scope): persisted index pages behind the same `IndexManager` surface; format version field in the header page allows this without breaking existing files.

### AD-4: `EmbeddedDocumentStore : IDocumentStore` so Query works unmodified

The engine's document layer implements the existing `IDocumentStore` interface from Core. The reused `QueryExecutor`/`FilterEngine` therefore run against embedded storage with zero changes, and any existing decorator that only depends on `IDocumentStore` remains applicable later.

### AD-5: Two API layers — Document core + typed LiteDB-like surface

**Layer 1 — Document API** (for dynamic/schema-less use and internal reuse):

```csharp
var col = db.GetCollection("orders");                    // EmbeddedCollection (untyped)
await col.InsertAsync(new Document { Id = "...", Data = ... });
var docs = await col.FindAsync(queryFilter);             // Query-project filter model
```

**Layer 2 — Typed API** (primary consumer surface, mirrors LiteDB):

```csharp
public interface IEmbeddedCollection<T> where T : class
{
    string Name { get; }
    string Insert(T entity);                      // returns id
    int InsertBulk(IEnumerable<T> entities);
    bool Update(T entity);
    bool Upsert(T entity);
    bool Delete(string id);
    int DeleteMany(Expression<Func<T, bool>> predicate);
    T? FindById(string id);
    T? FindOne(Expression<Func<T, bool>> predicate);
    IEnumerable<T> Find(Expression<Func<T, bool>> predicate);
    IEnumerable<T> FindAll();
    long Count();
    long Count(Expression<Func<T, bool>> predicate);
    bool EnsureIndex<TField>(Expression<Func<T, TField>> field, bool unique = false);
    IEmbeddedQueryable<T> Query();                // fluent: Where/OrderBy/Skip/Limit/ToList/First
    // ...Async variants of all of the above
}
```

Both sync and async methods are exposed; the engine is natively async (matches `IDocumentStore`), sync methods are safe blocking wrappers (documented; acceptable for an embedded desktop scenario, same as LiteDB being sync-only).

### AD-6: POCO mapper + expression translation with in-memory fallback

- **Mapper:** POCO ↔ `Document` via System.Text.Json (serialize entity → `Dictionary<string, object>` data; `Id` convention: string property named `Id`, or `[EmbeddedId]` attribute; missing id on insert → GUID assigned). No BsonMapper-style global registry in v1; per-database `JsonSerializerOptions` hook for customization.
- **Expression translation:** predicate expressions are translated into the Query project's filter model when they use the supported subset — member access, constants/captured variables, `==`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `||`, `!`, `string.Contains/StartsWith/EndsWith`, and `collection.Contains(x.Field)` (IN). Translated filters flow through `QueryExecutor`, which uses secondary indexes where available.
- **Fallback:** any expression outside the subset causes that predicate (or sub-predicate) to be evaluated **in memory over the deserialized candidates** via `expression.Compile()`. Results are always correct; only performance differs. Diagnostics counter records fallback occurrences so hot paths can be surfaced.

### AD-7: Concurrency model — single process, thread-safe

- The data file is opened with an exclusive lock (`FileShare.None`); a second open of the same path throws a clear `EmbeddedDatabaseLockedException`.
- Within the process, `AdvGenDatabase` is thread-safe: one writer at a time per collection, concurrent readers, via per-collection `ReaderWriterLockSlim` (async-safe wrapper). WAL append + index update happen under the write lock, so readers always see committed state.
- Each operation is individually atomic and durable. Multi-operation transactions are deferred (see below).

### AD-8: In-memory mode

`new AdvGenDatabase(":memory:")` uses the same engine with a `MemoryPageStore` (pages in a `Dictionary<uint, byte[]>`, WAL disabled). Intended for tests — including AdvGenPriceComparer's test suite and this solution's own tests.

## Component structure

```
AdvGenNoSqlServer.Embedded/
├── AdvGenDatabase.cs              // facade: open/dispose, GetCollection, Checkpoint, Compact
├── EmbeddedDatabaseOptions.cs     // page cache size, WAL threshold, JsonSerializerOptions hook
├── Storage/
│   ├── IPageStore.cs              // ReadPage/WritePage/Allocate/Free — file vs memory
│   ├── FilePageStore.cs           // 8 KB pages, header, free list, exclusive lock
│   ├── MemoryPageStore.cs         // :memory: backend
│   ├── WriteAheadLog.cs           // frame append, fsync, replay, checkpoint, truncate
│   ├── PageCache.cs               // bounded LRU of recently read pages
│   ├── RecordFile.cs              // slot/record encode-decode, overflow chains, tombstones
│   ├── Catalog.cs                 // collection + index definitions on catalog pages
│   └── Compactor.cs               // rewrite file dropping tombstones (Compact())
├── EmbeddedDocumentStore.cs       // IDocumentStore over RecordFile + primary index
├── Indexing/
│   └── IndexRegistry.cs           // wires Storage.IndexManager: rebuild-on-open, maintain-on-write
├── Typed/
│   ├── IEmbeddedCollection.cs / EmbeddedCollection.cs
│   ├── DocumentMapper.cs          // POCO ↔ Document
│   ├── ExpressionTranslator.cs    // predicate → QueryFilter (+ supported-subset detection)
│   └── EmbeddedQueryable.cs       // fluent Where/OrderBy/Skip/Limit
└── Exceptions.cs

AdvGenNoSqlServer.Embedded.Tests/  // new test project
```

Unit boundaries worth naming: `IPageStore` isolates file vs memory backends; `WriteAheadLog` is testable against a temp file without the rest of the engine; `ExpressionTranslator` is a pure function (expression in, filter-or-null out) and gets exhaustive unit tests; `EmbeddedDocumentStore` is tested through the `IDocumentStore` contract so it can be compared behavior-for-behavior with the existing in-memory `DocumentStore` as an oracle.

## Data flow

**Write (`Insert`):** map POCO → `Document` → serialize record → under write lock: append WAL frames (data page image, catalog if new collection) → fsync → apply to page cache → update primary + secondary indexes → return. Checkpoint if WAL over threshold.

**Read (`Find(predicate)`):** translate predicate → if translatable, `QueryExecutor` consults `IndexRegistry` for candidate ids, loads matching documents via `EmbeddedDocumentStore.GetManyAsync` (page cache → disk) → map back to POCOs. If not translatable: enumerate collection (streaming scan), deserialize, filter with compiled predicate.

**Open:** read header → recover WAL if present → load catalog → sequential scan of data pages rebuilding primary index and declared secondary indexes.

## Error handling

- Corrupt page (checksum mismatch) outside WAL replay → `EmbeddedDataCorruptionException` naming page id; WAL tail corruption is silently discarded (expected on crash).
- Unique index violation → existing `DuplicateKeyException` from Storage.
- Duplicate id insert / missing id update → existing Core exceptions (`DocumentAlreadyExistsException` etc.).
- File locked by another process → `EmbeddedDatabaseLockedException` with the path in the message.
- Disk full during WAL append → operation fails atomically (commit frame never written; replay discards), exception propagates.

## Testing

1. **Contract tests:** run the same `IDocumentStore` test suite against `EmbeddedDocumentStore` (file + memory) and the existing in-memory `DocumentStore` — identical observable behavior.
2. **Crash-recovery tests:** write N documents, truncate/corrupt the WAL at every frame boundary (simulated torn writes), reopen, assert the store equals the last committed state. No process-kill needed — inject through `IPageStore`/WAL file manipulation.
3. **Property tests:** random operation sequences (insert/update/delete/query) mirrored against a plain `Dictionary` oracle; assert equality after every step and after close/reopen.
4. **Expression translator tests:** each supported construct, plus untranslatable expressions asserting fallback (correct results + fallback counter incremented).
5. **Migration smoke test:** a repository modeled on AdvGenPriceComparer's `ItemRepository` compiled against the typed API.
6. **Benchmarks** (`AdvGenNoSqlServer.Benchmarks`): insert 100k, point lookup, indexed range query, cold open (index rebuild), side-by-side with LiteDB for reference.

## Deferred (explicitly out of scope for v1)

- Multi-operation transactions (`BeginTrans/Commit/Rollback`) — the WAL design leaves room (transaction id in frames), API added later.
- Persisted index pages (faster open on large files) — format version bump.
- Full-text, geospatial, TTL collections, change streams — the decorator pattern over `IDocumentStore` keeps these addable.
- Cross-process shared access.
