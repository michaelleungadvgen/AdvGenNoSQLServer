# AdvGenNoSqlServer.Embedded Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `AdvGenNoSqlServer.Embedded` — an in-process, single-file, LiteDB-style embedded document database usable by local apps (first consumer: AdvGenPriceComparer), per the approved spec `docs/superpowers/specs/2026-07-15-embedded-nosql-design.md`.

**Architecture:** New class library with two new subsystems — a single-file page-based storage engine with WAL (`Embedded/Storage/`) and a typed LiteDB-like API layer (`Embedded/Typed/`) — glued to the existing, reused pieces: `Core.Models.Document` + `Core.Abstractions.IDocumentStore`, `Storage.Indexing.BTreeIndex`/`IndexManager`, and `Query`'s `FilterEngine`/`QueryExecutor` (constructor `QueryExecutor(IDocumentStore, IFilterEngine, IndexManager?)` — verified in `AdvGenNoSqlServer.Query/Execution/QueryExecutor.cs:31`). Indexes are in-memory, rebuilt on open. Never reference Network/Server/Host.

**Tech Stack:** .NET 9 (`net9.0`, matches solution), xUnit (existing test conventions), System.Text.Json, `System.IO.Hashing.Crc32` (add package `System.IO.Hashing`), BenchmarkDotNet (existing Benchmarks project), LiteDB (test/benchmark reference only, never a library dependency).

**Read the spec first:** `docs/superpowers/specs/2026-07-15-embedded-nosql-design.md` — it defines the page layout, WAL frame format, API surfaces, and error semantics this plan implements.

**Conventions used throughout:**
- Repo root: `E:\Projects\AdvGenNoSQLServer`. All commands run from there.
- Test command template: `dotnet test AdvGenNoSqlServer.Embedded.Tests/AdvGenNoSqlServer.Embedded.Tests.csproj -c Release --filter "FullyQualifiedName~<TestClass>"`
- Every new file starts with the repo's standard header comment (`// Copyright (c) 2026 AdvanGeneration Pty. Ltd. ...`) — copy from a neighboring file.
- All multi-byte integers in file formats are **little-endian** (`BinaryPrimitives.*LittleEndian`), consistent within the new format.
- Follow @superpowers:test-driven-development: failing test → minimal implementation → pass → commit. Steps below assume that rhythm; where a task lists several tests, iterate test-by-test.
- File-based tests write under a unique temp dir (`Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"))`) and delete it in `Dispose()`.

---

## File Structure Overview

| File | Action | Responsibility |
|---|---|---|
| `AdvGenNoSqlServer.Embedded/AdvGenNoSqlServer.Embedded.csproj` | Create | net9.0 library; refs Core, Storage, Query; `System.IO.Hashing` |
| `AdvGenNoSqlServer.Embedded/Exceptions.cs` | Create | `EmbeddedDatabaseLockedException`, `EmbeddedDataCorruptionException` |
| `AdvGenNoSqlServer.Embedded/Storage/Page.cs` | Create | 8 KB page buffer, header encode/decode, slot array, checksum |
| `AdvGenNoSqlServer.Embedded/Storage/IPageStore.cs` | Create | `ReadPage/WritePage/AllocatePage/FreePage/PageCount/Flush` |
| `AdvGenNoSqlServer.Embedded/Storage/MemoryPageStore.cs` | Create | Dictionary-backed page store (`:memory:` + tests) |
| `AdvGenNoSqlServer.Embedded/Storage/FilePageStore.cs` | Create | File-backed pages, header page 0, free list, exclusive lock |
| `AdvGenNoSqlServer.Embedded/Storage/WriteAheadLog.cs` | Create | Frame append + fsync, commit frames, replay, checkpoint, truncate |
| `AdvGenNoSqlServer.Embedded/Storage/RecordFile.cs` | Create | Document records in data pages: insert/read/update/delete/enumerate, overflow chains, tombstones |
| `AdvGenNoSqlServer.Embedded/Storage/Catalog.cs` | Create | Collection + index definitions on catalog pages |
| `AdvGenNoSqlServer.Embedded/Storage/Compactor.cs` | Create | Rewrite file dropping tombstones/free pages |
| `AdvGenNoSqlServer.Embedded/EmbeddedDocumentStore.cs` | Create | `IDocumentStore` over RecordFile + primary index + WAL |
| `AdvGenNoSqlServer.Embedded/Indexing/IndexRegistry.cs` | Create | Rebuild-on-open + maintain-on-write over `Storage.Indexing.IndexManager` |
| `AdvGenNoSqlServer.Embedded/AdvGenDatabase.cs` | Create | Facade: open/dispose, GetCollection (typed/untyped), Checkpoint, Compact |
| `AdvGenNoSqlServer.Embedded/EmbeddedDatabaseOptions.cs` | Create | WAL checkpoint threshold, page cache size, JsonSerializerOptions hook |
| `AdvGenNoSqlServer.Embedded/EmbeddedCollection.cs` | Create | Untyped Document-level collection API |
| `AdvGenNoSqlServer.Embedded/Typed/DocumentMapper.cs` | Create | POCO ↔ Document (System.Text.Json), Id convention |
| `AdvGenNoSqlServer.Embedded/Typed/ExpressionTranslator.cs` | Create | Predicate `Expression` → `QueryFilter` or null (fallback) |
| `AdvGenNoSqlServer.Embedded/Typed/IEmbeddedCollection.cs` | Create | Typed API interface (spec AD-5) |
| `AdvGenNoSqlServer.Embedded/Typed/EmbeddedCollection.cs` | Create | `EmbeddedCollection<T>` implementation |
| `AdvGenNoSqlServer.Embedded/Typed/EmbeddedQueryable.cs` | Create | Fluent `Query()`: Where/OrderBy/Skip/Limit/ToList/First |
| `AdvGenNoSqlServer.Embedded.Tests/*` | Create | Test project (xUnit, net9.0); one test class per component |
| `AdvGenNoSqlServer.Benchmarks/EmbeddedBenchmarks.cs` | Create | Insert/lookup/range/open benchmarks vs LiteDB |
| `AdvGenNoSqlServer.sln` | Modify | Add the two new projects |

Dependency order of tasks: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12 → 13 → 14 → 15 → 16 → 17 → 18 → 19 → 20 → 21. Tasks 13/14 are independent of each other (parallelizable).

---

## Phase 1 — Storage engine core

### Task 1: Project scaffolding

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/AdvGenNoSqlServer.Embedded.csproj`
- Create: `AdvGenNoSqlServer.Embedded.Tests/AdvGenNoSqlServer.Embedded.Tests.csproj`
- Modify: `AdvGenNoSqlServer.sln`

- [ ] **Step 1.1: Create the library project**

```xml
<!-- AdvGenNoSqlServer.Embedded/AdvGenNoSqlServer.Embedded.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\AdvGenNoSqlServer.Core\AdvGenNoSqlServer.Core.csproj" />
    <ProjectReference Include="..\AdvGenNoSqlServer.Storage\AdvGenNoSqlServer.Storage.csproj" />
    <ProjectReference Include="..\AdvGenNoSqlServer.Query\AdvGenNoSqlServer.Query.csproj" />
    <PackageReference Include="System.IO.Hashing" Version="9.0.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 1.2: Create the test project** — copy the xUnit package set from `AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj` (same versions), reference the Embedded project. Also add `PackageReference` to `LiteDB` (latest 5.x) — used only by the later migration/benchmark comparison tests.

- [ ] **Step 1.3: Wire into solution and verify build**

```powershell
dotnet sln AdvGenNoSqlServer.sln add AdvGenNoSqlServer.Embedded/AdvGenNoSqlServer.Embedded.csproj AdvGenNoSqlServer.Embedded.Tests/AdvGenNoSqlServer.Embedded.Tests.csproj
dotnet build AdvGenNoSqlServer.sln -c Release
```
Expected: build succeeds.

- [ ] **Step 1.4: Commit** — `git add ...; git commit -m "feat(embedded): scaffold AdvGenNoSqlServer.Embedded projects"`

---

### Task 2: Page primitives

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/Storage/Page.cs`
- Create: `AdvGenNoSqlServer.Embedded/Exceptions.cs`
- Test: `AdvGenNoSqlServer.Embedded.Tests/PageTests.cs`

A `Page` wraps a `byte[8192]`. Header layout (32 bytes, little-endian; offsets fixed):

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | magic `0xAD6DB001` |
| 4 | 1 | pageType (`Header=0, Catalog=1, Data=2, Overflow=3, Free=4`) |
| 5 | 4 | pageId |
| 9 | 4 | nextPageId (0 = none) |
| 13 | 2 | slotCount |
| 15 | 2 | freeBytes |
| 17 | 4 | checksum (CRC32 of bytes 32..8191) |
| 21 | 11 | reserved (zero) |

Slot array grows down from offset 8192 (4 bytes per slot: `offset uint16, length uint16`); records grow up from offset 32. `length == 0` marks a tombstoned slot.

- [ ] **Step 2.1: Write failing tests** — header round-trip; checksum computed on `Seal()` and verified on `Validate()` (flip a body byte → `Validate()` returns false); `TryAddRecord(ReadOnlySpan<byte>)` returns slot index and `ReadRecord(slot)` returns the bytes; `TryAddRecord` returns -1 when the record + slot entry doesn't fit in `FreeBytes`; `DeleteRecord(slot)` tombstones (subsequent `ReadRecord` throws, `IsSlotDeleted(slot)` true).

```csharp
[Fact]
public void HeaderRoundTrip()
{
    var page = Page.CreateNew(pageId: 7, PageType.Data);
    page.NextPageId = 42;
    page.Seal();

    var reloaded = Page.FromBuffer(page.Buffer);
    Assert.Equal(7u, reloaded.PageId);
    Assert.Equal(PageType.Data, reloaded.Type);
    Assert.Equal(42u, reloaded.NextPageId);
    Assert.True(reloaded.Validate());
}
```

- [ ] **Step 2.2: Run tests, verify fail** — `dotnet test ... --filter "FullyQualifiedName~PageTests"` → FAIL (types missing).
- [ ] **Step 2.3: Implement `Page` + exceptions minimal** — public members: `const int PageSize = 8192`, `byte[] Buffer`, `uint PageId`, `PageType Type`, `uint NextPageId`, `ushort SlotCount`, `ushort FreeBytes`, `static Page CreateNew(uint, PageType)`, `static Page FromBuffer(byte[])`, `Seal()`, `bool Validate()`, `int TryAddRecord(ReadOnlySpan<byte>)`, `ReadOnlyMemory<byte> ReadRecord(int slot)`, `DeleteRecord(int slot)`, `bool IsSlotDeleted(int slot)`. Use `System.IO.Hashing.Crc32.Hash` for checksum.
- [ ] **Step 2.4: Run tests, verify pass.**
- [ ] **Step 2.5: Commit** — `feat(embedded): 8KB slotted page with CRC32 checksum`

---

### Task 3: IPageStore + MemoryPageStore

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/Storage/IPageStore.cs`
- Create: `AdvGenNoSqlServer.Embedded/Storage/MemoryPageStore.cs`
- Test: `AdvGenNoSqlServer.Embedded.Tests/MemoryPageStoreTests.cs`

```csharp
public interface IPageStore : IDisposable
{
    uint PageCount { get; }
    uint AllocatePage(PageType type);      // reuses free list before growing
    Page ReadPage(uint pageId);            // throws EmbeddedDataCorruptionException on bad checksum
    void WritePage(Page page);             // page.Seal() then persist
    void FreePage(uint pageId);            // adds to free list
    void Flush(bool toDisk);               // no-op for memory
}
```

- [ ] **Step 3.1: Write failing tests** — allocate returns increasing ids starting at 1 (page 0 reserved for header); write-then-read round-trips content; `FreePage` then `AllocatePage` reuses the freed id; reading an unallocated id throws.
- [ ] **Step 3.2: Verify fail.**
- [ ] **Step 3.3: Implement `MemoryPageStore`** — `Dictionary<uint, byte[]>` + `Stack<uint>` free list. Not thread-safe by itself (engine-level locking comes later; document this on the class).
- [ ] **Step 3.4: Verify pass. Commit** — `feat(embedded): IPageStore abstraction and in-memory store`

---

### Task 4: FilePageStore

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/Storage/FilePageStore.cs`
- Test: `AdvGenNoSqlServer.Embedded.Tests/FilePageStoreTests.cs`

Header page 0 body (after the 32-byte page header): `formatVersion uint16 = 1`, `catalogRootPageId uint32`, `freeListHeadPageId uint32`, `lastCheckpointLsn uint64`. Free pages form a chain via `NextPageId`. File opened `FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None`.

- [ ] **Step 4.1: Write failing tests**
  - Create store on a fresh path → file exists, `PageCount == 1`, header page valid.
  - Allocate/write/read round-trip survives Dispose + reopen.
  - Free list persists across reopen (free a page, reopen, allocate returns it).
  - Second `FilePageStore` on the same open path → `EmbeddedDatabaseLockedException` (wrap `IOException` from the exclusive share).
  - Corrupt a data page on disk (flip one byte with `FileStream` after dispose), reopen, `ReadPage` → `EmbeddedDataCorruptionException` naming the page id.
- [ ] **Step 4.2: Verify fail.**
- [ ] **Step 4.3: Implement** — single `FileStream`, `Seek(pageId * 8192)`; header page rewritten via `WriteHeader()` whenever catalog root / free-list head changes; `Flush(toDisk: true)` → `FileStream.Flush(flushToDisk: true)`.
- [ ] **Step 4.4: Verify pass. Commit** — `feat(embedded): single-file page store with free list and exclusive lock`

---

### Task 5: Write-ahead log

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/Storage/WriteAheadLog.cs`
- Test: `AdvGenNoSqlServer.Embedded.Tests/WriteAheadLogTests.cs`

WAL file = sibling path `<dbpath>.wal`. Frame format (little-endian):

```
[frameType byte]      Page=1, Commit=2
[lsn uint64]
Page frame:  [pageId uint32][pageImage 8192 bytes][crc32 uint32 over type..image]
Commit frame:[crc32 uint32 over type..lsn]
```

API:

```csharp
public sealed class WriteAheadLog : IDisposable
{
    public WriteAheadLog(string walPath);
    public long SizeBytes { get; }
    public ulong LastCommittedLsn { get; }
    public void Append(Page page);                     // buffers frame with next LSN
    public void Commit();                              // commit frame + Flush(flushToDisk: true)
    public void DiscardUncommitted();                  // drop buffered frames (op failed pre-commit)
    // Replay: committed frames only, in order; torn/corrupt tail silently ignored.
    public IReadOnlyDictionary<uint, byte[]> ReadCommittedPages();
    public void Truncate();                            // after checkpoint
}
```

- [ ] **Step 5.1: Write failing tests**
  - Append 3 pages + Commit → `ReadCommittedPages()` on a fresh instance over the same file returns all 3 latest images.
  - Append 2 pages, **no** commit → `ReadCommittedPages()` empty.
  - Two committed batches touching the same pageId → replay returns the later image.
  - Torn tail: commit batch A, append batch B, then truncate the file mid-frame (`FileStream.SetLength(len - 10)`) → replay returns exactly batch A, no throw.
  - Corrupt a byte inside a committed frame → that batch and everything after it is discarded (checksum guards replay).
  - `Truncate()` → `SizeBytes == 0`, replay empty.
- [ ] **Step 5.2: Verify fail.**
- [ ] **Step 5.3: Implement.** Keep it simple: `Append` writes to an in-memory buffer list; `Commit` writes all buffered frames + commit frame to the file stream then fsyncs (single writer; engine locking guarantees no interleaving).
- [ ] **Step 5.4: Verify pass. Commit** — `feat(embedded): write-ahead log with checksummed frames and torn-tail recovery`

---

### Task 6: RecordFile (documents in pages)

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/Storage/RecordFile.cs`
- Test: `AdvGenNoSqlServer.Embedded.Tests/RecordFileTests.cs`

`RecordFile` stores/retrieves opaque byte records for one collection over an `IPageStore`, tracking the collection's data-page chain (first page id lives in the catalog). Record encoding inside a slot: `[idLen varint][id utf8][flags byte][bodyLen varint][body]`; `flags` bit 0 = has-overflow. Records larger than ~7.5 KB spill: slot holds `[... flags(overflow) ...][firstOverflowPageId uint32]`, body chunks fill overflow pages chained by `NextPageId`.

Address struct: `readonly record struct RecordAddress(uint PageId, int Slot)`.

**Important:** all page mutations go through a `Func<Page, ...>` write path that the caller (EmbeddedDocumentStore, Task 9) routes through the WAL — RecordFile itself takes an `Action<Page> persistPage` delegate so it stays WAL-agnostic and unit-testable against the raw store.

- [ ] **Step 6.1: Write failing tests**
  - Insert record → returns address; `Read(address)` round-trips id + body.
  - Insert until a page fills → second page allocated and chained; enumeration returns all records in insertion order with addresses.
  - Delete → tombstone; enumeration skips it; page space is not reused in v1 (compaction handles reclaim).
  - Update smaller/equal size → same address; update larger → new address returned, old tombstoned.
  - 20 KB record → overflow chain; round-trips; delete frees overflow pages back to the store.
- [ ] **Step 6.2: Verify fail.**
- [ ] **Step 6.3: Implement.**
- [ ] **Step 6.4: Verify pass. Commit** — `feat(embedded): record file with page chains, overflow and tombstones`

---

### Task 7: Catalog

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/Storage/Catalog.cs`
- Test: `AdvGenNoSqlServer.Embedded.Tests/CatalogTests.cs`

Catalog entries are compact-JSON records on catalog pages (chain rooted at header's `catalogRootPageId`), reusing `RecordFile` mechanics with entry type discriminators:

```json
{ "t": "col", "name": "items", "firstPage": 12 }
{ "t": "idx", "col": "items", "field": "Barcode", "name": "idx_items_Barcode", "unique": false }
```

API: `IReadOnlyList<CollectionDef> Collections`, `AddCollection(name) → CollectionDef`, `RemoveCollection(name)`, `AddIndex(IndexDef)`, `RemoveIndexes(collection)`, `Load()`.

- [ ] **Step 7.1: Write failing tests** — add collections + indexes, reload from store, definitions round-trip; remove collection removes its index defs.
- [ ] **Step 7.2: Verify fail. Step 7.3: Implement. Step 7.4: Verify pass. Commit** — `feat(embedded): on-file catalog of collections and index definitions`

---

## Phase 2 — Document store, WAL wiring, indexes, query

### Task 8: EmbeddedDocumentStore (CRUD, no WAL yet)

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/EmbeddedDocumentStore.cs`
- Test: `AdvGenNoSqlServer.Embedded.Tests/EmbeddedDocumentStoreContractTests.cs`

Implements `Core.Abstractions.IDocumentStore` fully (all 12 methods) over `IPageStore` + `Catalog` + one `RecordFile` per collection + in-memory primary index (`Dictionary<string, RecordAddress>` per collection). Document serialization: compact JSON of `{id, data, createdAt, updatedAt, version}` — reuse a small internal `DocumentSerializer` (System.Text.Json, camelCase, no indentation; deserialize `Data` values to CLR primitives the same way `QueryFilter.JsonElementToObject` does: long/double/string/bool/list/dict).

Semantics must match the existing in-memory `AdvGenNoSqlServer.Storage.DocumentStore`: insert assigns GUID id when `Id` empty, sets `CreatedAt/UpdatedAt/Version=1`; duplicate insert → `DocumentAlreadyExistsException`; update missing → `DocumentNotFoundException`; update bumps `Version` and `UpdatedAt`; `GetAsync` returns a clone (mutating the result must not affect the store — documents are re-read from pages, so this holds naturally).

- [ ] **Step 8.1: Write the contract test suite as an abstract class**

```csharp
public abstract class DocumentStoreContractTests
{
    protected abstract IDocumentStore CreateStore();
    // ~20 [Fact]s: insert/get/getmany/getall/update/delete/exists/count/
    // create-drop-clear collection/list collections/duplicate insert throws/
    // update missing throws/get returns independent copy...
}
public class InMemoryOracleTests : DocumentStoreContractTests   // existing DocumentStore
{ protected override IDocumentStore CreateStore() => new DocumentStore(); }
public class EmbeddedMemoryStoreTests : DocumentStoreContractTests
{ protected override IDocumentStore CreateStore() => EmbeddedDocumentStore.CreateInMemory(); }
public class EmbeddedFileStoreTests : DocumentStoreContractTests, IDisposable
{ /* file-backed via temp dir */ }
```

Running the suite against the existing `DocumentStore` first **validates the tests themselves** against known-good behavior.

- [ ] **Step 8.2: Verify the oracle class passes and the embedded classes fail.**
- [ ] **Step 8.3: Implement `EmbeddedDocumentStore`** (constructor takes `IPageStore`; `InitializeAsync()` loads catalog and scans data pages to rebuild primary indexes). Concurrency: one `SemaphoreSlim(1,1)` per collection for writes; reads lock-free against immutable page snapshots via `PageCache` — v1 simplification: a single `ReaderWriterLockSlim` around the whole store is acceptable; note it and move on.
- [ ] **Step 8.4: Verify all three test classes pass.**
- [ ] **Step 8.5: Also verify reopen persistence** — file-backed: insert 100 docs, dispose, reopen, `CountAsync == 100`, spot-check contents.
- [ ] **Step 8.6: Commit** — `feat(embedded): EmbeddedDocumentStore implementing IDocumentStore over paged file`

---

### Task 9: WAL integration + crash recovery

**Files:**
- Modify: `AdvGenNoSqlServer.Embedded/EmbeddedDocumentStore.cs`
- Modify: `AdvGenNoSqlServer.Embedded/Storage/FilePageStore.cs` (accept recovered pages on open)
- Test: `AdvGenNoSqlServer.Embedded.Tests/CrashRecoveryTests.cs`

Wiring: every mutating operation collects its dirty pages (data, overflow, catalog, header), appends them to the WAL, `Commit()`s (fsync), **then** applies them to the page store in memory/page cache. The main file is only written at **checkpoint** (WAL ≥ threshold, `Checkpoint()`, or `Dispose()`): copy committed WAL page images into `FilePageStore`, fsync main file, `Truncate()` WAL, update `lastCheckpointLsn`. On open: if WAL non-empty, replay `ReadCommittedPages()` into the main file (a checkpoint) before catalog load.

- [ ] **Step 9.1: Write failing tests**
  - Insert 50 docs with a huge checkpoint threshold (nothing checkpointed), dispose **without** checkpoint by simulating crash: dispose the underlying `FileStream`s abruptly via a test hook (`SimulateCrash()` internal method that skips checkpoint) → reopen → all 50 docs present (WAL replayed).
  - Torn tail: after `SimulateCrash()`, truncate the `.wal` mid-frame → reopen → exactly the documents from complete committed batches are present, no exception.
  - Checkpoint threshold: set threshold to 64 KB, insert until exceeded → `.wal` file shrinks to 0 and main file contains the data (reopen with WAL deleted manually still sees all docs).
  - Failed operation atomicity: force a serializer failure mid-update (test document with an unserializable value injected via internal hook) → store state unchanged, WAL has no partial batch.
- [ ] **Step 9.2: Verify fail. Step 9.3: Implement. Step 9.4: Verify pass.**
- [ ] **Step 9.5: Run the full Task 8 contract suite again** (now WAL-backed) — still green.
- [ ] **Step 9.6: Commit** — `feat(embedded): WAL-backed durability with checkpoint and crash recovery`

---

### Task 10: IndexRegistry (secondary indexes)

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/Indexing/IndexRegistry.cs`
- Modify: `AdvGenNoSqlServer.Embedded/EmbeddedDocumentStore.cs` (hooks: after insert/update/delete/drop/clear)
- Test: `AdvGenNoSqlServer.Embedded.Tests/IndexRegistryTests.cs`

Read `AdvGenNoSqlServer.Storage/Indexing/IndexManager.cs` first and reuse it as-is; `IndexRegistry` is the thin adapter that (a) creates indexes in the `IndexManager` from catalog `IndexDef`s on open, (b) feeds every document mutation to the manager, (c) persists new defs via `Catalog` when `EnsureIndexAsync(collection, field, unique)` is called, (d) rebuilds by enumerating existing documents when an index is created on a non-empty collection. Match `IndexManager`'s actual API when implementing — adjust the adapter, not the manager.

- [ ] **Step 10.1: Write failing tests** — EnsureIndex on empty then insert → lookup by field returns doc ids; EnsureIndex on populated collection → backfilled; update moves index entry (old key removed); delete removes entry; unique index duplicate insert → `DuplicateKeyException` **and the document is not stored** (index check before WAL commit); definitions survive reopen and are rebuilt (insert, reopen, indexed lookup works).
- [ ] **Step 10.2: Verify fail. Step 10.3: Implement. Step 10.4: Verify pass. Commit** — `feat(embedded): secondary index registry with rebuild-on-open`

---

### Task 11: Query wiring + untyped collection API

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/EmbeddedCollection.cs`
- Test: `AdvGenNoSqlServer.Embedded.Tests/EmbeddedQueryTests.cs`

Construct the reused pipeline once per database: `new QueryExecutor(embeddedStore, new FilterEngine(), indexRegistry.Manager)`. Untyped surface:

```csharp
public class EmbeddedCollection
{
    public string Name { get; }
    public Task<Document> InsertAsync(Document doc, CancellationToken ct = default);
    public Task<Document> UpdateAsync(Document doc, CancellationToken ct = default);
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    public Task<Document?> FindByIdAsync(string id, CancellationToken ct = default);
    public Task<IReadOnlyList<Document>> FindAsync(QueryFilter? filter = null,
        List<SortField>? sort = null, int? skip = null, int? limit = null, CancellationToken ct = default);
    public Task<long> CountAsync(QueryFilter? filter = null, CancellationToken ct = default);
    public Task<bool> EnsureIndexAsync(string field, bool unique = false, CancellationToken ct = default);
}
```

- [ ] **Step 11.1: Write failing tests** — seed 1,000 docs (mixed fields); `$gt`/`$lte`/`$in`/`$and`/`$or` filters return correct sets (assert against LINQ over the seed data); sort + skip/limit paging correct; count-with-filter correct; a filtered query on an indexed field returns identical results to the same query without the index (equivalence check — proves index path correctness).
- [ ] **Step 11.2: Verify fail. Step 11.3: Implement (thin delegation to QueryExecutor). Step 11.4: Verify pass. Commit** — `feat(embedded): document-level query API via reused QueryExecutor`

---

### Task 12: AdvGenDatabase facade

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/AdvGenDatabase.cs`
- Create: `AdvGenNoSqlServer.Embedded/EmbeddedDatabaseOptions.cs`
- Test: `AdvGenNoSqlServer.Embedded.Tests/AdvGenDatabaseTests.cs`

```csharp
public sealed class AdvGenDatabase : IDisposable, IAsyncDisposable
{
    public AdvGenDatabase(string path, EmbeddedDatabaseOptions? options = null); // ":memory:" supported
    public EmbeddedCollection GetCollection(string name);
    public IEmbeddedCollection<T> GetCollection<T>(string name) where T : class;  // Task 15
    public IReadOnlyList<string> GetCollectionNames();
    public bool DropCollection(string name);
    public void Checkpoint();
    public Task CompactAsync();   // Task 17
}
```

Options: `WalCheckpointBytes` (default 4 MB), `PageCacheSize` (default 1,024 pages), `JsonSerializerOptions? SerializerOptions`.

- [ ] **Step 12.1: Write failing tests** — ctor creates file lazily-but-immediately-openable; `:memory:` works and persists nothing; double-open same path throws `EmbeddedDatabaseLockedException`; dispose checkpoints (`.wal` size 0 after dispose); `GetCollection` returns the same instance per name; end-to-end smoke: open → insert → query → dispose → reopen → data present.
- [ ] **Step 12.2: Verify fail. Step 12.3: Implement (owns page store, WAL, catalog, store, registry, executor; ctor blocks on `InitializeAsync`). Step 12.4: Verify pass. Commit** — `feat(embedded): AdvGenDatabase facade with :memory: mode`

---

## Phase 3 — Typed LiteDB-like layer

### Task 13: DocumentMapper

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/Typed/DocumentMapper.cs`
- Test: `AdvGenNoSqlServer.Embedded.Tests/DocumentMapperTests.cs`

`Document ToDocument<T>(T entity)` / `T ToEntity<T>(Document doc)`. Id convention: public string property named `Id` (fallback: property carrying `[EmbeddedId]`, a new attribute in this file); null/empty id preserved as empty (store assigns GUID on insert) and written back onto the entity after insert. Everything except `Id` serializes into `Document.Data` via System.Text.Json (respecting `options.SerializerOptions`), then element-converted to plain CLR values (same long/double/string/bool/list/dict shape the Query filter engine compares against).

- [ ] **Step 13.1: Write failing tests** — round-trip POCO with string/int/decimal/bool/DateTime/nullable/list/nested-object properties (assert value equality after `ToDocument → ToEntity`); entity without `Id` property and without attribute → `InvalidOperationException` with clear message; decimal survives round-trip with full precision (store as string with a type marker, or double if precision loss is acceptable — **decide: store decimal as string `"d:123.45"`? No — keep it simple: serialize decimal as JSON number, deserialize via the target property type; assert round-trip through the typed path is exact**); DateTime round-trips as ISO-8601 string.
- [ ] **Step 13.2: Verify fail. Step 13.3: Implement. Step 13.4: Verify pass. Commit** — `feat(embedded): POCO document mapper`

---

### Task 14: ExpressionTranslator

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/Typed/ExpressionTranslator.cs`
- Test: `AdvGenNoSqlServer.Embedded.Tests/ExpressionTranslatorTests.cs`

`QueryFilter? TryTranslate<T>(Expression<Func<T, bool>> predicate)` — returns `null` when any node falls outside the supported subset (caller falls back to in-memory). Supported: `x.Prop == c`, `!=`, `<`, `<=`, `>`, `>=` (constant on either side; captured closure variables evaluated via `Expression.Lambda(...).Compile()()`), `&&` → `QueryFilter.And`, `||` → `QueryFilter.Or`, `!x.Bool` / bare `x.Bool` → equality with false/true, `x.Prop.Contains/StartsWith/EndsWith(str)` → `$regex`-style condition **only if FilterEngine supports it — read `AdvGenNoSqlServer.Query/Filtering/FilterEngine.cs` first and map to the operators that actually exist; anything unsupported returns null**, `list.Contains(x.Prop)` → `QueryFilter.In`. Field naming must match the mapper's serialized casing.

- [ ] **Step 14.1: Write failing tests** — one test per construct asserting the produced `Conditions` dictionary shape; closure capture (`var min = 5; x => x.Age > min`); mixed and/or nesting; unsupported constructs (`x => x.Name.ToLower() == "a"`, method calls, arithmetic) → returns null.
- [ ] **Step 14.2: Verify fail. Step 14.3: Implement (ExpressionVisitor-style recursive translate). Step 14.4: Verify pass. Commit** — `feat(embedded): LINQ predicate to QueryFilter translator`

---

### Task 15: EmbeddedCollection\<T\>

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/Typed/IEmbeddedCollection.cs` (interface exactly as in spec AD-5)
- Create: `AdvGenNoSqlServer.Embedded/Typed/EmbeddedCollection.cs`
- Modify: `AdvGenNoSqlServer.Embedded/AdvGenDatabase.cs` (`GetCollection<T>`)
- Test: `AdvGenNoSqlServer.Embedded.Tests/TypedCollectionTests.cs`

Composition: mapper + translator + untyped `EmbeddedCollection`. `Find(predicate)`: translate → filtered query; null → stream `FindAsync(filter: null)` and filter with `predicate.Compile()`; increment `db.Diagnostics.FallbackQueryCount` on fallback. `EnsureIndex(x => x.Prop)` extracts the member name (throw on non-member expressions). Sync methods wrap async (`GetAwaiter().GetResult()`), matching LiteDB's sync ergonomics; async variants (`InsertAsync`, `FindAsync`, ...) delegate directly. `Upsert` = exists-check then insert-or-update under the collection write lock.

- [ ] **Step 15.1: Write failing tests** — CRUD round-trip with a realistic POCO (`Item { Id, Name, Brand, Barcode, Price, IsActive }`); `Insert` assigns and returns id, sets it on the entity; `Upsert` inserts then updates; `FindById/FindOne/Find/FindAll/Count` correct; indexed `Find(x => x.Barcode == "X")` equals unindexed result; fallback path (`x => x.Name.EndsWith("z".ToUpper() + "Z")` — untranslatable) returns correct results and bumps the fallback counter; unique index violation surfaces `DuplicateKeyException`.
- [ ] **Step 15.2: Verify fail. Step 15.3: Implement. Step 15.4: Verify pass. Commit** — `feat(embedded): typed LiteDB-style collection API`

---

### Task 16: Fluent Query()

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/Typed/EmbeddedQueryable.cs`
- Test: `AdvGenNoSqlServer.Embedded.Tests/EmbeddedQueryableTests.cs`

`col.Query().Where(pred).Where(pred2).OrderBy(x => x.Price).OrderByDescending(...).Skip(n).Limit(n).ToList() / .ToListAsync() / .First() / .FirstOrDefault() / .Count()`. Builder accumulates predicates (ANDed) + sort fields + paging; executes through the same translate-or-fallback path. When **any** predicate is untranslatable, all filtering happens in memory but sort/skip/limit still apply afterward (correctness first).

- [ ] **Step 16.1: Write failing tests** — chained wheres AND correctly; multi-key sort matches LINQ `OrderBy/ThenByDescending`; skip/limit paging; mixed translatable + untranslatable predicates still correct.
- [ ] **Step 16.2: Verify fail. Step 16.3: Implement. Step 16.4: Verify pass. Commit** — `feat(embedded): fluent query builder`

---

## Phase 4 — Hardening, migration proof, benchmarks, packaging

### Task 17: Compaction

**Files:**
- Create: `AdvGenNoSqlServer.Embedded/Storage/Compactor.cs`
- Modify: `AdvGenNoSqlServer.Embedded/AdvGenDatabase.cs` (`CompactAsync`)
- Test: `AdvGenNoSqlServer.Embedded.Tests/CompactorTests.cs`

Strategy (safe + simple): checkpoint, then write a brand-new file `<path>.compact` by enumerating live documents through the normal insert path, fsync, atomically swap (`File.Replace`), reopen page store, rebuild indexes. Takes the global write lock for the duration (documented blocking operation).

- [ ] **Step 17.1: Write failing tests** — insert 1,000, delete 900, `CompactAsync` → file shrinks (assert ≥ 50% smaller), all 100 survivors readable, indexes still work; crash-safety: if compaction is interrupted before swap (test hook), original file untouched and valid.
- [ ] **Step 17.2–17.4: fail → implement → pass. Commit** — `feat(embedded): online compaction`

---

### Task 18: Randomized property tests (oracle)

**Files:**
- Test: `AdvGenNoSqlServer.Embedded.Tests/PropertyTests.cs`

- [ ] **Step 18.1: Implement the oracle harness** — seeded `Random` (seed logged in the failure message and settable via env var `AGDB_TEST_SEED` for reproduction); 2,000 random ops (weighted: 40% insert, 25% update, 15% delete, 15% find-by-predicate, 5% reopen-database) mirrored against `Dictionary<string, Item>`; after every op the harness asserts store-vs-oracle equality on a random probe, and full equality after each reopen. Run the whole sequence twice: file-backed and `:memory:`.
- [ ] **Step 18.2: Run it** (`--filter FullyQualifiedName~PropertyTests`), fix whatever it finds (expect it to find something — that's its job), re-run until stable across 5 different seeds.
- [ ] **Step 18.3: Commit** — `test(embedded): randomized property tests against dictionary oracle`

---

### Task 19: Migration smoke test (AdvGenPriceComparer shape)

**Files:**
- Test: `AdvGenNoSqlServer.Embedded.Tests/MigrationSmokeTests.cs`

- [ ] **Step 19.1: Port a real repository shape** — copy the structure of `AdvGenPriceComparer.Data.LiteDB/Repositories/AlertRepository.cs` + the `DatabaseService` index bootstrap (`EnsureIndex` per field) into a test double using `AdvGenDatabase` instead of `LiteDatabase`, keeping method bodies as close to mechanical translation as possible. Exercise every repository method.
- [ ] **Step 19.2: Write a side-by-side equivalence test** — run the same operation script against LiteDB (package ref from Task 1) and AdvGenDatabase; assert identical observable results. Document any call-site change required in a `## Migration notes` section appended to the spec.
- [ ] **Step 19.3: Commit** — `test(embedded): LiteDB migration smoke + equivalence tests`

---

### Task 20: Benchmarks

**Files:**
- Create: `AdvGenNoSqlServer.Benchmarks/EmbeddedBenchmarks.cs` (follow the existing benchmark class pattern in that project; add LiteDB package ref to the Benchmarks project)

- [ ] **Step 20.1: Implement benchmarks** — Insert100k (bulk), PointLookupById, IndexedRangeQuery (1k of 100k), UnindexedScanQuery, ColdOpen100k (measures index rebuild — validates AD-3's assumption). Each with an LiteDB twin where the API allows.
- [ ] **Step 20.2: Run** `dotnet run --project AdvGenNoSqlServer.Benchmarks -c Release -- --filter *Embedded*`, paste the results table into `docs/superpowers/specs/2026-07-15-embedded-nosql-design.md` under a new `## Measured performance` section. If ColdOpen100k exceeds ~2 s, flag it in the results note (triggers the persisted-index future work earlier).
- [ ] **Step 20.3: Commit** — `perf(embedded): benchmarks vs LiteDB`

---

### Task 21: Packaging + docs

**Files:**
- Modify: `AdvGenNoSqlServer.Embedded/AdvGenNoSqlServer.Embedded.csproj` (NuGet metadata: PackageId `AdvGenNoSqlServer.Embedded`, version 0.1.0, description, license, `GeneratePackageOnBuild=false`)
- Create: `AdvGenNoSqlServer.Embedded/README.md` (quick start mirroring the spec's opening sample, supported query subset table, limitations: single process, in-memory indexes, no multi-op transactions)
- Modify: root `README.md` (short "Embedded mode" section linking to the project README)

- [ ] **Step 21.1: Write README + metadata.**
- [ ] **Step 21.2: Verify pack** — `dotnet pack AdvGenNoSqlServer.Embedded -c Release -o nupkg` succeeds; inspect the nupkg lists Core/Storage/Query as dependencies.
- [ ] **Step 21.3: Full solution gate** — `dotnet build AdvGenNoSqlServer.sln -c Release` and `dotnet test AdvGenNoSqlServer.Embedded.Tests -c Release` plus the existing `AdvGenNoSqlServer.Tests` suite (prove no regression in reused projects: Storage/Query untouched or compatibly extended).
- [ ] **Step 21.4: Commit** — `feat(embedded): NuGet packaging and documentation`

---

## Verification checklist (definition of done)

- [ ] All Embedded.Tests green (contract, crash-recovery, index, query, typed, property, migration) — file **and** memory backends.
- [ ] Existing `AdvGenNoSqlServer.Tests` still green (reused projects unbroken).
- [ ] Crash-recovery tests cover: torn WAL tail, corrupt WAL frame, corrupt data page, interrupted compaction.
- [ ] Benchmark results recorded in the spec; cold-open assumption validated.
- [ ] `dotnet pack` produces a clean package.
- [ ] Migration notes for AdvGenPriceComparer written.
