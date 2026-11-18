# Embedded SQL Server Lite — Implementation Plan

**Runtime:** C# / .NET 8+  
**Deployment:** Single-file, zero-install, embedded in desktop applications  
**Goal:** A self-contained T-SQL–compatible query engine that desktop apps can ship without requiring SQL Server, LocalDB, or any external installer.

---

## 1. What This Is (and Isn't)

**Is:** A lightweight, in-process database engine with a T-SQL–flavored API, stored in a single `.dll` or NuGet package. No server process, no registry entries, no installer.

**Is not:** A full SQL Server replacement. Advanced features (CLR, full-text search, replication, linked servers, `sp_*` system procs) are out of scope.

---

## 2. Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    Desktop Application                   │
│                                                         │
│   var db = new EmbeddedSqlDb("myapp.db");               │
│   db.Execute("SELECT * FROM Orders WHERE Id = @id");    │
└───────────────────┬─────────────────────────────────────┘
                    │ ADO.NET-compatible API
┌───────────────────▼─────────────────────────────────────┐
│                  Public API Layer                        │
│  EmbeddedSqlConnection / EmbeddedSqlCommand /           │
│  EmbeddedSqlDataReader  (ADO.NET interfaces)            │
└───────────────────┬─────────────────────────────────────┘
                    │
┌───────────────────▼─────────────────────────────────────┐
│                  Query Pipeline                         │
│                                                         │
│   ┌──────────┐  ┌──────────┐  ┌──────────────────────┐ │
│   │  Lexer / │→ │ Semantic │→ │  Query Optimizer      │ │
│   │  Parser  │  │ Analyzer │  │  (rule-based, simple) │ │
│   └──────────┘  └──────────┘  └──────────┬───────────┘ │
│                                           │             │
│                               ┌───────────▼───────────┐ │
│                               │  Execution Engine      │ │
│                               │  (Volcano / iterator)  │ │
│                               └───────────┬───────────┘ │
└───────────────────────────────────────────┼─────────────┘
                                            │
┌───────────────────────────────────────────▼─────────────┐
│                  Storage Layer                          │
│                                                         │
│   ┌────────────┐  ┌───────────┐  ┌────────────────────┐│
│   │ Page Mgr   │  │ Index Mgr │  │  Transaction / WAL  ││
│   │ (B+ pages) │  │ (B-Tree)  │  │  (Write-Ahead Log) ││
│   └────────────┘  └───────────┘  └────────────────────┘│
│                   Single .db file on disk               │
└─────────────────────────────────────────────────────────┘
```

---

## 3. Component Breakdown

### 3.1 Public API Layer
Implement the standard ADO.NET interfaces so existing code using `SqlConnection`/`SqlCommand` can swap in the embedded engine with minimal friction.

| Class | Interface |
|---|---|
| `EmbeddedSqlConnection` | `IDbConnection` |
| `EmbeddedSqlCommand` | `IDbCommand` |
| `EmbeddedSqlDataReader` | `IDataReader` |
| `EmbeddedSqlTransaction` | `IDbTransaction` |
| `EmbeddedSqlParameter` | `IDbDataParameter` |

Connection string format: `Data Source=myapp.db;Mode=ReadWrite` (SQLite-style, familiar to .NET devs).

---

### 3.2 T-SQL Parser
Parse a strict subset of T-SQL into an AST (Abstract Syntax Tree).

**Approach:** Hand-written recursive-descent parser (no ANTLR dependency — keeps the single-file story clean). ANTLR adds a ~2 MB runtime; a hand-written parser for a limited grammar stays well under 100 KB.

**Supported statements (Phase 1):**

```sql
-- DDL
CREATE TABLE t (col type [NOT NULL] [DEFAULT val] [PRIMARY KEY])
DROP TABLE t
ALTER TABLE t ADD COLUMN / DROP COLUMN
CREATE INDEX idx ON t(col)
DROP INDEX idx

-- DML
SELECT [TOP n] cols FROM t [JOIN...] [WHERE] [GROUP BY] [ORDER BY] [OFFSET/FETCH]
INSERT INTO t (cols) VALUES (...) | SELECT ...
UPDATE t SET col = val [WHERE]
DELETE FROM t [WHERE]

-- Transactions
BEGIN TRANSACTION / COMMIT / ROLLBACK

-- Parameters
@paramName (named parameters, no positional ?)
```

**Deferred (Phase 2+):** CTEs (`WITH`), window functions, `MERGE`, stored procedures, triggers, views.

---

### 3.3 Semantic Analyzer
After parsing, validate the AST against the catalog:
- Table and column existence checks
- Type compatibility (can't assign `NVARCHAR` to `INT` column)
- Resolve `*` expansions
- Bind parameter types

---

### 3.4 Query Optimizer (Rule-Based)
Keep it simple — no statistics, no cost model in v1. Apply deterministic rewrite rules:

1. **Predicate pushdown** — move WHERE filters as close to the scan as possible
2. **Index selection** — use a B-Tree index if the WHERE clause references an indexed column with `=`, `<`, `>`, `BETWEEN`
3. **Join order** — for 2-table joins, put the smaller (estimated row count) table on the build side of a hash join
4. **Constant folding** — evaluate `WHERE 1 + 1 = 2` at plan time

---

### 3.5 Execution Engine (Volcano / Pull Model)
Each operator is an iterator that calls `MoveNext()` on its child and produces one row at a time. This is the classic Volcano model — simple to implement and compose.

| Operator | Description |
|---|---|
| `TableScanOp` | Full scan of a heap table |
| `IndexScanOp` | B-Tree range or point scan |
| `FilterOp` | Evaluate a predicate per row |
| `ProjectOp` | Select and rename columns |
| `SortOp` | External merge sort (spills to temp pages) |
| `HashJoinOp` | Build/probe hash join |
| `NestedLoopJoinOp` | Fallback for small tables |
| `AggregateOp` | GROUP BY + aggregate functions (SUM, COUNT, MIN, MAX, AVG) |
| `TopOp` | TOP n / OFFSET-FETCH |
| `InsertOp` / `UpdateOp` / `DeleteOp` | DML execution |

---

### 3.6 Storage Layer

#### File Format (single `.db` file)
Inspired by SQLite's format. Fixed-size pages (default 4096 bytes).

```
Offset 0       : File header (100 bytes)
                 - Magic: "ESQL1.0\0" (8 bytes)
                 - Page size, page count, freelist head, schema root page
Page 1         : Schema catalog (tables, columns, indexes)
Page 2..N      : Data pages (heap rows or B-Tree nodes)
End of file    : Write-Ahead Log (WAL) segment (optional, appended)
```

#### Heap Storage
Rows are stored as variable-length records on 4 KB pages. Each page has a slot array at the end pointing to row offsets within the page. Overflow pages handle rows larger than ~2 KB.

#### B-Tree Index
Standard B+-Tree where leaf nodes hold `(key, rowId)` pairs and internal nodes hold separator keys. Pages are the same 4 KB size as heap pages.

#### Type System
Support the most-used SQL Server types, mapped to .NET types:

| T-SQL Type | .NET Type | Storage |
|---|---|---|
| `INT` | `int` | 4 bytes |
| `BIGINT` | `long` | 8 bytes |
| `BIT` | `bool` | 1 byte |
| `FLOAT` | `double` | 8 bytes |
| `DECIMAL(p,s)` | `decimal` | 16 bytes |
| `NVARCHAR(n)` | `string` | 2n bytes (UTF-16) |
| `VARCHAR(n)` | `string` | n bytes (UTF-8) |
| `DATETIME2` | `DateTime` | 8 bytes |
| `UNIQUEIDENTIFIER` | `Guid` | 16 bytes |
| `VARBINARY(n)` | `byte[]` | n bytes |
| `NULL` | `null` | 1-byte null bitmap |

---

### 3.7 Transaction Manager & WAL
Use a Write-Ahead Log (WAL) for crash recovery.

**Isolation level:** Read Committed (default). Serializable available via explicit lock escalation.

**Concurrency model:**
- Single writer at a time (write lock on the db file)
- Multiple concurrent readers allowed (snapshot from last committed WAL checkpoint)
- Lock granularity: table-level in Phase 1, row-level in Phase 2

**WAL flow:**
1. On write, append change records to the WAL file (`myapp.db-wal`)
2. On `COMMIT`, fsync the WAL
3. On `ROLLBACK`, discard WAL records since last `BEGIN`
4. Checkpoint: merge WAL into main db file when WAL exceeds threshold (e.g., 1000 pages)

---

## 4. Single-File Packaging

The engine ships as a single NuGet package. At publish time, `dotnet publish --self-contained` or ILMerge/ILRepack bundles it into the application executable. No separate installer, no GAC registration.

**Target:** `net8.0` (LTS). Optionally multi-target `net6.0` for older desktop apps.

**Binary size target:** < 500 KB for the engine assembly (before app bundling).

**No native dependencies.** Pure managed C# — no P/Invoke to OS-specific SQL libraries, so it works on Windows, macOS, and Linux desktops.

---

## 5. Phased Delivery Plan

### Phase 1 — Core Engine (Weeks 1–6)
Goal: Can run basic CRUD with persistence and survive a crash.

- [ ] File format + page manager (read/write pages from `.db` file)
- [ ] Schema catalog (store table/column/index metadata in page 1)
- [ ] Heap storage (insert, scan, delete rows on data pages)
- [ ] T-SQL parser: `CREATE TABLE`, `INSERT`, `SELECT`, `UPDATE`, `DELETE`, `DROP TABLE`
- [ ] Execution engine: `TableScanOp`, `FilterOp`, `ProjectOp`, `InsertOp`, `UpdateOp`, `DeleteOp`
- [ ] Named parameters (`@param`)
- [ ] ADO.NET API: `Connection`, `Command`, `DataReader`, `Parameter`
- [ ] WAL + `BEGIN`/`COMMIT`/`ROLLBACK`
- [ ] Basic expression evaluator (`=`, `<>`, `<`, `>`, `AND`, `OR`, `NOT`, `IS NULL`, `LIKE`)

**Done when:** A desktop app can open a `.db` file, create tables, insert rows, query them with WHERE filters, and reopen the file after a crash with data intact.

---

### Phase 2 — Indexes & Joins (Weeks 7–10)
Goal: Fast queries on large tables; multi-table queries.

- [ ] B+-Tree index (create, insert, point lookup, range scan)
- [ ] `CREATE INDEX` / `DROP INDEX`
- [ ] Index selection in optimizer
- [ ] `JOIN` support: `INNER JOIN`, `LEFT JOIN` (hash join + nested loop fallback)
- [ ] `ORDER BY` + `SortOp`
- [ ] `GROUP BY` + aggregate functions (`COUNT`, `SUM`, `MIN`, `MAX`, `AVG`)
- [ ] `TOP n` / `OFFSET x FETCH NEXT y ROWS`
- [ ] `ALTER TABLE ADD/DROP COLUMN`
- [ ] Subqueries in `WHERE` (`IN (SELECT ...)`, `EXISTS`)

**Done when:** Queries with joins over 100K rows complete in < 500 ms without a full scan when an index is available.

---

### Phase 3 — Advanced T-SQL & Polish (Weeks 11–14)
Goal: Cover the T-SQL patterns most desktop apps actually use.

- [ ] CTEs (`WITH cte AS (...)`)
- [ ] `CASE WHEN ... THEN ... END`
- [ ] String functions: `LEN`, `SUBSTRING`, `CHARINDEX`, `UPPER`, `LOWER`, `LTRIM`, `RTRIM`, `REPLACE`
- [ ] Date functions: `GETDATE`, `DATEADD`, `DATEDIFF`, `YEAR`, `MONTH`, `DAY`
- [ ] Math functions: `ABS`, `ROUND`, `CEILING`, `FLOOR`, `ISNULL`, `COALESCE`
- [ ] `INSERT ... SELECT ...`
- [ ] `TRUNCATE TABLE`
- [ ] `@@ROWCOUNT`, `SCOPE_IDENTITY()`
- [ ] Row-level locking
- [ ] `EXPLAIN` / query plan output (for debugging)
- [ ] In-memory mode (`Data Source=:memory:`) for unit test usage
- [ ] NuGet packaging + CI pipeline

---

### Phase 4 — Views & Perf (Weeks 15–18, optional)
- [ ] `CREATE VIEW` / `DROP VIEW`
- [ ] Statistics-based optimizer (collect row counts, histogram)
- [ ] Parallel scan (use `Task`/`Parallel.ForEach` for large table scans)
- [ ] Connection pooling
- [ ] Encryption at rest (AES-256 on the `.db` file, key from connection string)

---

## 6. Key Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Parser | Hand-written recursive descent | No ANTLR runtime dependency; keeps binary small |
| Execution model | Volcano (pull) iterator | Simple to compose, easy to add new operators |
| Storage page size | 4096 bytes | Matches OS page size; good for SSD alignment |
| Isolation level | Read Committed default | Covers 95% of desktop app use cases |
| WAL strategy | SQLite-style WAL | Proven crash-safe; simple to implement |
| Concurrency | Single writer / multi-reader | Desktop apps rarely need write parallelism |
| .NET target | net8.0 | LTS, AOT-friendly, best perf |
| Packaging | NuGet + `dotnet publish` bundling | Zero install, single `.exe` story |

---

## 7. Testing Strategy

- **Unit tests** — Parser, expression evaluator, individual operators
- **Integration tests** — Round-trip: create schema → insert → query → reopen file → verify
- **Crash-recovery tests** — Kill the process mid-write, reopen, verify WAL rolled back correctly
- **Compatibility tests** — Run identical queries against SQL Server LocalDB and the embedded engine, assert identical result sets
- **Benchmark suite** — 100K row insert, point lookup via index, 3-table join; track regression per PR

---

## 8. File & Project Structure

```
EmbeddedSqlLite/
├── src/
│   ├── EmbeddedSqlLite/                  ← main library
│   │   ├── Api/                          ← ADO.NET wrappers
│   │   ├── Parser/                       ← Lexer, Parser, AST nodes
│   │   ├── Analyzer/                     ← Semantic analysis
│   │   ├── Optimizer/                    ← Rewrite rules
│   │   ├── Execution/                    ← Operators (Volcano)
│   │   ├── Storage/                      ← PageManager, Heap, BTree
│   │   ├── Transactions/                 ← WAL, LockManager
│   │   ├── Types/                        ← Type system, value encoding
│   │   └── EmbeddedSqlLite.csproj
│   └── EmbeddedSqlLite.Benchmarks/
├── tests/
│   ├── EmbeddedSqlLite.UnitTests/
│   ├── EmbeddedSqlLite.IntegrationTests/
│   └── EmbeddedSqlLite.CompatibilityTests/
├── samples/
│   └── DesktopApp.WpfSample/
├── EmbeddedSqlLite.sln
└── README.md
```

---

## 9. Success Criteria

| Metric | Target |
|---|---|
| Binary size | < 500 KB |
| Point lookup (indexed, 1M rows) | < 5 ms |
| Full table scan (100K rows) | < 200 ms |
| Bulk insert (10K rows, transaction) | < 1 s |
| File open / connection time | < 50 ms |
| Crash recovery | Zero data loss on committed transactions |
| T-SQL compatibility | Identical results to SQL Server on Phase 1–3 feature set |
