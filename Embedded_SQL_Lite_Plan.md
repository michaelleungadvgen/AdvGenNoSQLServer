# Embedded SQL Lite — Implementation Plan

**Project:** AdvGen Embedded SQL Lite (`AdvGenSqlLite`)
**Target:** Lightweight, pure managed C# embedded SQL engine
**Timeline:** 18 weeks (Phases 1–3 core; Phase 4 optional)
**Status:** Planning

---

## Table of Contents

1. [Overview](#overview)
2. [Goals](#goals)
3. [Architecture Decisions](#architecture-decisions)
4. [Phase Breakdown](#phase-breakdown)
   - [Phase 1 — Core Engine (Wks 1–6)](#phase-1--core-engine-wks-16)
   - [Phase 2 — Indexes, Joins & Advanced Queries (Wks 7–10)](#phase-2--indexes-joins--advanced-queries-wks-710)
   - [Phase 3 — Completeness, In-Memory Mode & Packaging (Wks 11–14)](#phase-3--completeness-in-memory-mode--packaging-wks-1114)
   - [Phase 4 — Optional Enhancements (Wks 15–18)](#phase-4--optional-enhancements-wks-1518)
5. [Integration with AdvGenNoSQLServer](#integration-with-advgennosqlserver)
6. [Next Steps](#next-steps)

---

## Overview

**AdvGen Embedded SQL Lite** (`AdvGenSqlLite`) is a self-contained, zero-dependency embedded relational database engine written entirely in pure managed C#. It exposes a standard ADO.NET API (`IDbConnection`, `IDbCommand`, `IDataReader`) so that existing .NET tooling — ORMs, Dapper, raw ADO.NET — works without modification.

The engine is distributed as a **single NuGet DLL** with no native binaries, no P/Invoke, and no external dependencies. It runs identically on Windows, macOS, and Linux from one build artifact.

The primary use case within the AdvGenNoSQLServer ecosystem is:

- Providing an embedded relational layer alongside the existing document/NoSQL store.
- Powering structured metadata queries, reporting, and analytics without requiring an external SQL Server or SQLite dependency.
- Serving as a lightweight test harness database for integration tests (via in-memory mode).

---

## Goals

### Functional Goals

| # | Goal |
|---|------|
| G1 | Parse and execute a meaningful subset of T-SQL (DDL + DML) |
| G2 | Support CRUD: `CREATE TABLE`, `INSERT`, `SELECT`, `UPDATE`, `DELETE`, `DROP TABLE` |
| G3 | Support `JOIN` (INNER, LEFT), `WHERE`, `ORDER BY`, `GROUP BY`, aggregates, subqueries |
| G4 | Support CTEs (`WITH` expressions) and common built-in functions |
| G5 | Expose standard ADO.NET API (`IDbConnection` / `IDbCommand` / `IDataReader`) |
| G6 | Provide crash-safe durability via Write-Ahead Logging (WAL) |
| G7 | Provide an in-memory mode for unit and integration tests |
| G8 | Ship as a single `.dll` NuGet package, no native binaries |

### Non-Goals

- Full T-SQL or ANSI SQL compliance (stored procedures, triggers, CLR types, XML columns)
- Multi-process concurrent access (single-process embedded only)
- Distributed transactions or replication
- OLAP / columnar workloads

### Quality Targets

| Metric | Target |
|--------|--------|
| Binary size (NuGet DLL) | < 500 KB |
| Simple point-query latency | < 1 ms |
| Sequential scan (1M rows) | < 5 s |
| Test coverage | ≥ 80% line coverage |
| Zero unmanaged dependencies | Required |

---

## Architecture Decisions

### AD-1: Pure Managed C#, No P/Invoke

**Decision:** All code — storage, parsing, execution — is written in C# targeting .NET 8+ with no unsafe blocks and no P/Invoke calls.

**Rationale:** Enables a single portable DLL that runs on any .NET 8+ platform. Eliminates the multi-RID NuGet packaging complexity that SQLite.Interop.dll requires. Acceptable performance trade-off given the embedded/moderate-scale use case.

**Trade-off:** Slightly lower raw I/O throughput vs. native engines. Mitigated by page-level buffering and WAL batching.

---

### AD-2: Hand-Written Recursive-Descent Parser (No ANTLR)

**Decision:** The SQL parser is hand-written as a recursive-descent parser directly in C#. No parser-generator (ANTLR, Irony, Pidgin) is used.

**Rationale:**
- Keeps the binary small — parser-generator runtimes add 200–600 KB.
- Full control over error messages and recovery.
- T-SQL subset is well-understood; a generated parser is overkill.
- No additional build toolchain dependency (no Java runtime for ANTLR).

**Lexer → Parser pipeline:**

```
SQL text
   ↓
Lexer         — tokenises into Token[] (keyword, identifier, literal, op, punct)
   ↓
Parser        — recursive-descent, produces AST nodes
   ↓
Binder        — resolves table/column references, type-checks expressions
   ↓
Planner       — converts bound AST into a physical operator tree
   ↓
Executor      — Volcano pull-model evaluation
```

---

### AD-3: Volcano Pull-Model Execution Engine

**Decision:** Query execution uses the iterator (Volcano/pull) model: each operator exposes `Open()`, `Next() → Row?`, and `Close()`. The root operator is pulled by the client one row at a time.

**Rationale:** Simple to implement, compose, and test. Each operator is an independent class. Pipelines naturally without materialising intermediate results for most operators.

**Core operators:**

| Operator | Purpose |
|----------|---------|
| `SeqScanOperator` | Full heap scan |
| `IndexScanOperator` | B+-Tree index range/point lookup |
| `FilterOperator` | Applies a predicate expression |
| `ProjectOperator` | Column projection / computed expressions |
| `NestedLoopJoinOperator` | INNER and LEFT JOIN |
| `HashJoinOperator` | INNER JOIN (large relations) |
| `SortOperator` | ORDER BY (external sort for large result sets) |
| `HashAggregateOperator` | GROUP BY + aggregates |
| `LimitOperator` | TOP N / FETCH FIRST |
| `UnionOperator` | UNION / UNION ALL |
| `SubqueryOperator` | Correlated and uncorrelated subqueries |

---

### AD-4: Custom Binary File Format (Page-Based)

**Decision:** Data is stored in a single binary file using a fixed 8 KB page layout, similar to SQL Server's page architecture.

**Page layout:**

```
[Page Header  — 64 bytes ]
  magic:    uint32  (0xADDB0001)
  pageType: byte    (data=1, index=2, overflow=3, wal=4, free=5)
  pageId:   uint32
  lsn:      uint64  (log sequence number)
  freeSpace: uint16
  slotCount: uint16
  checksum: uint32

[Slot Array   — 4 bytes × slotCount, grows downward from end of header ]
  offset: uint16
  length: uint16

[Row Data     — variable, grows upward from end of slot array ]
```

**Heap storage:** Tables are stored as heap files (unordered row pages). Each row is serialised as a compact binary record (type-prefixed fields, nullable bitmap). `RowId` is a `(pageId, slotIndex)` pair — stable for indexed access.

---

### AD-5: SQLite-Style Write-Ahead Logging (WAL)

**Decision:** Durability and crash safety are implemented via a WAL file (`.wal` alongside the main `.db` file), modelled on SQLite's WAL mode.

**WAL mechanics:**

1. All writes go to the WAL file first, never directly to the database file during a transaction.
2. Each WAL frame contains: page number, LSN, before/after page image.
3. On `COMMIT`: WAL frames are fsynced; the transaction is durable.
4. On `ROLLBACK`: WAL frames for the aborted transaction are discarded in-memory; no wal sync needed.
5. **Checkpointing:** A background checkpoint pass periodically copies committed WAL frames back to the main database file and truncates the WAL.
6. **Crash recovery:** On open, the engine replays any committed-but-not-checkpointed WAL frames and discards frames for incomplete transactions.

**Isolation:** Serialisable isolation via exclusive write lock + snapshot read (readers see a consistent snapshot as of their transaction start LSN).

---

### AD-6: ADO.NET API Surface

```csharp
// Connection string examples
"Data Source=mydb.advdb"                     // persistent file
"Data Source=:memory:"                        // in-memory (test mode)
"Data Source=mydb.advdb;Password=secret"     // encrypted at rest (Phase 4)

// Usage
using var conn = new AdvGenConnection("Data Source=mydb.advdb");
conn.Open();

using var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT Id, Name FROM Products WHERE Price > @price";
cmd.Parameters.AddWithValue("@price", 10.00m);

using var reader = cmd.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine($"{reader["Id"]} — {reader["Name"]}");
}
```

All ADO.NET types implement their standard interfaces:

| AdvGen Type | Implements |
|---|---|
| `AdvGenConnection` | `IDbConnection` |
| `AdvGenCommand` | `IDbCommand` |
| `AdvGenDataReader` | `IDataReader` |
| `AdvGenTransaction` | `IDbTransaction` |
| `AdvGenParameter` | `IDbDataParameter` |

---

## Phase Breakdown

---

### Phase 1 — Core Engine (Wks 1–6)

**Goal:** A working end-to-end pipeline capable of basic single-table CRUD with durable WAL transactions.

#### Week-by-Week Breakdown

| Week | Focus |
|------|-------|
| 1 | Project skeleton, token types, lexer, parser for `CREATE TABLE` / `DROP TABLE` |
| 2 | AST node hierarchy, binder stubs, page allocator, heap page read/write |
| 3 | Row serialisation/deserialisation, `SeqScanOperator`, `ProjectOperator`, `FilterOperator` |
| 4 | `INSERT` execution, `DELETE` execution, `UPDATE` execution |
| 5 | WAL file format, `BEGIN`/`COMMIT`/`ROLLBACK`, crash recovery on `Open()` |
| 6 | ADO.NET wrapper classes, parameterised queries, Phase 1 integration tests |

#### Deliverables

- `AdvGenSqlLite` C# class library project (net8.0)
- Lexer + recursive-descent parser covering: `CREATE TABLE`, `DROP TABLE`, `INSERT INTO`, `SELECT … FROM … WHERE`, `UPDATE … SET … WHERE`, `DELETE FROM … WHERE`
- Page manager: allocate, read, write, free 8 KB pages
- Heap storage engine: insert row, seq scan, delete by RowId, update by RowId
- WAL implementation: write frames on modify, fsync on commit, replay on open
- ADO.NET wrappers: `AdvGenConnection`, `AdvGenCommand`, `AdvGenDataReader`, `AdvGenTransaction`, `AdvGenParameter`
- 200+ unit and integration tests

#### Acceptance Criteria

- [ ] `CREATE TABLE t (Id INT, Name NVARCHAR(100))` executes without error
- [ ] `INSERT INTO t VALUES (1, 'Alice')` persists to disk
- [ ] `SELECT * FROM t WHERE Id = 1` returns correct row via `IDataReader`
- [ ] `UPDATE t SET Name = 'Bob' WHERE Id = 1` modifies the row correctly
- [ ] `DELETE FROM t WHERE Id = 1` removes the row
- [ ] Crash simulation (process kill after WAL write, before checkpoint) followed by re-open returns consistent data
- [ ] All Phase 1 tests pass on Windows, macOS, Linux (CI matrix)

---

### Phase 2 — Indexes, Joins & Advanced Queries (Wks 7–10)

**Goal:** Move beyond single-table scans to multi-table queries, index-accelerated lookups, and result ordering/aggregation.

#### Week-by-Week Breakdown

| Week | Focus |
|------|-------|
| 7 | B+-Tree implementation: node structure, split/merge, point lookup, range scan |
| 8 | `CREATE INDEX` / `DROP INDEX` DDL, index maintenance on INSERT/UPDATE/DELETE, `IndexScanOperator` |
| 9 | `JOIN` parsing and planning: `NestedLoopJoinOperator` (INNER, LEFT); `HashJoinOperator` for larger inputs |
| 10 | `ORDER BY` (`SortOperator`), `GROUP BY` + aggregates (`COUNT`, `SUM`, `MIN`, `MAX`, `AVG`), `HAVING`, subqueries |

#### Deliverables

- B+-Tree index: order-100 internal nodes, leaf nodes with `(key → RowId[])` entries, leaf-level linked list for range scans
- `CREATE INDEX ix_name ON t(col)` / `CREATE UNIQUE INDEX` DDL
- Automatic index selection in the query planner (cost comparison: seq scan vs index scan based on row count estimate)
- `NestedLoopJoinOperator` and `HashJoinOperator`
- External merge sort for `ORDER BY` on large result sets
- `HashAggregateOperator` covering `COUNT`, `SUM`, `MIN`, `MAX`, `AVG`
- Correlated and uncorrelated subqueries in `WHERE` / `SELECT`
- Expanded test suite: join correctness, aggregate correctness, index correctness (including crash during index write)

#### Acceptance Criteria

- [ ] `SELECT * FROM t WHERE Id = 42` uses index scan (not seq scan) when an index exists on `Id`; confirmed via `EXPLAIN` output
- [ ] `SELECT a.Name, b.Amount FROM Customers a JOIN Orders b ON a.Id = b.CustomerId` returns correct rows
- [ ] `SELECT CustomerId, SUM(Amount) FROM Orders GROUP BY CustomerId HAVING SUM(Amount) > 100` returns correct aggregates
- [ ] `SELECT * FROM Orders ORDER BY Amount DESC` returns correctly sorted rows
- [ ] Subquery `SELECT * FROM Orders WHERE CustomerId IN (SELECT Id FROM Customers WHERE Region = 'EU')` executes correctly
- [ ] B+-Tree survives 1M sequential inserts and 500K random point lookups with correct results
- [ ] All Phase 2 tests pass on CI matrix

---

### Phase 3 — Completeness, In-Memory Mode & Packaging (Wks 11–14)

**Goal:** Reach a production-usable feature set, add the in-memory mode for test isolation, and ship a NuGet package.

#### Week-by-Week Breakdown

| Week | Focus |
|------|-------|
| 11 | CTEs (`WITH` expressions), multi-statement CTE chains, recursive CTEs (limited depth) |
| 12 | Built-in scalar functions: string (`LEN`, `LTRIM`, `RTRIM`, `SUBSTRING`, `UPPER`, `LOWER`, `REPLACE`, `CHARINDEX`), date (`GETDATE`, `DATEADD`, `DATEDIFF`, `YEAR`, `MONTH`, `DAY`), math (`ABS`, `CEILING`, `FLOOR`, `ROUND`, `POWER`, `SQRT`) |
| 13 | In-memory storage backend (`Data Source=:memory:`): all pages stored in `Dictionary<uint, byte[]>` — same page API, no disk I/O; WAL disabled; useful for test isolation and CI speed |
| 14 | NuGet packaging: single-DLL output, `<PackAsTool>false</PackAsTool>`, trimmer-safe annotations, XML doc comments, `README.md`, CI publish pipeline |

#### Deliverables

- CTE parser and planner (non-recursive and recursive with depth limit configurable via connection string)
- Complete built-in function library (string, date, math categories)
- `MemoryStorageBackend` implementing the same `IStorageBackend` interface as the file backend
- In-memory connection string (`Data Source=:memory:`) wired through `AdvGenConnection`
- `AdvGenSqlLite` NuGet package: `netstandard2.1` + `net8.0` multi-target, single `.dll`, trimmer annotations, IntelliSense XML docs
- Performance benchmarks (BenchmarkDotNet): point query, seq scan, join, aggregate
- Dapper compatibility test suite (confirms ORMs work against ADO.NET surface)

#### Acceptance Criteria

- [ ] `WITH cte AS (SELECT ...) SELECT * FROM cte` executes correctly
- [ ] `SELECT UPPER(Name), LEN(Name) FROM t` returns correct function results
- [ ] `SELECT DATEDIFF(DAY, StartDate, EndDate) FROM t` returns correct date differences
- [ ] In-memory mode: connection opens, full CRUD works, data does not persist after connection close
- [ ] NuGet package installs cleanly into a new .NET 8 project with `dotnet add package AdvGenSqlLite`
- [ ] Dapper `Query<T>()` and `Execute()` work correctly against `AdvGenConnection`
- [ ] NuGet DLL size ≤ 500 KB (Release, trimmed)
- [ ] All Phase 3 tests pass on CI matrix

---

### Phase 4 — Optional Enhancements (Wks 15–18)

**Goal:** Add enterprise-facing capabilities: views, a statistics-driven query optimiser, and encryption at rest. All items in this phase are optional and depend on project priority.

#### Week-by-Week Breakdown

| Week | Focus |
|------|-------|
| 15 | `CREATE VIEW` / `DROP VIEW` DDL; view expansion during binding (views are macro-expanded, not materialised) |
| 16 | Column and table statistics: histogram collection via `UPDATE STATISTICS`; row-count and selectivity estimates stored in system catalogue |
| 17 | Cost-based query optimiser: replace rule-based plan selection with cost estimates using statistics; join order selection for 2- and 3-table queries |
| 18 | Encryption at rest: AES-256-GCM page-level encryption; `Password=` connection string parameter; key derivation via PBKDF2 |

#### Deliverables

- `CREATE VIEW v AS SELECT ...` with correct resolution through the binder
- System catalogue tables (`sys.tables`, `sys.columns`, `sys.indexes`, `sys.views`) queryable via SQL
- Statistics histogram: per-column bucket array of `(min, max, rowCount, distinctCount)`
- Cost-based join order optimiser (dynamic programming for ≤ 5 tables; greedy heuristic beyond)
- AES-256-GCM page encryption: encrypt on write, decrypt on read, no plaintext pages written to disk
- `Password=` connection string option wired through to key derivation

#### Acceptance Criteria

- [ ] `CREATE VIEW v AS SELECT Id, Name FROM t WHERE Active = 1` then `SELECT * FROM v WHERE Id > 10` returns correct results
- [ ] `sys.tables` and `sys.columns` return correct metadata
- [ ] After `UPDATE STATISTICS`, the query plan for a highly selective predicate switches from seq scan to index scan
- [ ] Opening a database file with a correct password reads data; incorrect password throws `AdvGenAuthException`
- [ ] Hex dump of `.db` file shows no plaintext strings when encryption is enabled

---

## Integration with AdvGenNoSQLServer

`AdvGenSqlLite` is designed to integrate into the existing `AdvGenNoSQLServer` solution as a first-class module.

### Proposed Integration Points

#### 1. Structured Query Over Document Collections

The existing Core engine stores documents as JSON/BSON blobs. A thin bridge layer can expose document fields as virtual SQL columns, enabling:

```sql
SELECT doc->>'$.CustomerId', SUM(doc->>'$.Amount')
FROM orders_collection
GROUP BY doc->>'$.CustomerId'
```

This allows SQL analytics over NoSQL collections without schema migration.

#### 2. Metadata & Catalogue Storage

Internal server metadata (collection definitions, user accounts, index configurations, replication topology) can be stored in an embedded `AdvGenSqlLite` database instead of ad-hoc JSON files, giving ACID guarantees to catalogue mutations.

#### 3. Test Infrastructure

The in-memory mode (`Data Source=:memory:`) provides a fast, isolated database for the existing `AdvGenNoSqlServer.Tests` project — no external database dependency in CI.

#### 4. NuGet Package Reference

```xml
<!-- AdvGenNoSqlServer.Core.csproj -->
<PackageReference Include="AdvGenSqlLite" Version="1.0.0" />
```

Or, during active development, a direct project reference:

```xml
<ProjectReference Include="..\AdvGenSqlLite\AdvGenSqlLite.csproj" />
```

### Proposed Project Structure Addition

```
AdvGenNoSQLServer/
├── AdvGenSqlLite/                      ← NEW — SQL engine library
│   ├── Parser/
│   │   ├── Lexer.cs
│   │   ├── Parser.cs
│   │   └── Ast/
│   ├── Storage/
│   │   ├── PageManager.cs
│   │   ├── HeapFile.cs
│   │   ├── WalWriter.cs
│   │   └── BTree/
│   ├── Execution/
│   │   ├── Operators/
│   │   └── ExpressionEvaluator.cs
│   ├── Catalogue/
│   │   └── SystemCatalogue.cs
│   ├── AdoNet/
│   │   ├── AdvGenConnection.cs
│   │   ├── AdvGenCommand.cs
│   │   └── AdvGenDataReader.cs
│   └── AdvGenSqlLite.csproj
├── AdvGenSqlLite.Tests/                ← NEW — SQL engine tests
│   └── AdvGenSqlLite.Tests.csproj
```

---

## Next Steps

### Immediate Actions (Before Week 1)

- [ ] **Create solution structure:** Add `AdvGenSqlLite` and `AdvGenSqlLite.Tests` projects to the solution
- [ ] **Agree token grammar:** Draft the full token type enumeration covering T-SQL keywords in scope
- [ ] **Define `IStorageBackend` interface:** Establish the abstraction boundary between the page manager and the in-memory/file backends early
- [ ] **Set up CI pipeline:** Ensure build + test matrix runs on Windows, Ubuntu, macOS for both projects
- [ ] **Define `EXPLAIN` output format:** Agree on the plan-display format needed for acceptance testing of the optimiser

### Open Questions

| # | Question | Owner |
|---|----------|-------|
| OQ-1 | Should `AdvGenSqlLite` target `netstandard2.1` (broader compatibility) or `net8.0` only (better performance primitives)? | Arch |
| OQ-2 | Is recursive CTE support required in Phase 3, or can it move to Phase 4? | PM |
| OQ-3 | What is the expected maximum database file size? (Informs page cache sizing defaults.) | Arch |
| OQ-4 | Should the WAL checkpoint be triggered by a background thread, or only on connection close? | Arch |
| OQ-5 | Is `HashJoinOperator` in scope for Phase 2, or is `NestedLoopJoinOperator` sufficient for the initial cut? | Eng |

### Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| B+-Tree implementation bugs under concurrent insert/delete | Medium | High | Extensive randomised property-based tests; compare output vs. in-memory `SortedDictionary` oracle |
| WAL replay edge cases causing data corruption | Low | Critical | Chaos testing: kill process at random points during WAL write; replay and verify checksums |
| Parser ambiguity in T-SQL subset | Medium | Medium | Build a comprehensive grammar doc before writing the parser; add regression tests for each ambiguity found |
| Binary size exceeds 500 KB target | Low | Low | Enable IL trimming and `<Optimize>true</Optimize>`; profile with `dotnet-size` |
| Phase 4 encryption adds unacceptable latency | Low | Medium | Benchmark AES-GCM with `System.Security.Cryptography` hardware acceleration; if needed, encrypt only sensitive pages |

---

*Document version: 1.0 — Created 2026-06-05*
*Owner: AdvGen Engineering*
