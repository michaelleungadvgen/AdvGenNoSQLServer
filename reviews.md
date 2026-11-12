# AdvGenNoSQL Server - Reviewer's Plan

**Document Version**: 1.0
**Last Updated**: 2026-02-16
**Author**: Expert Reviewer

---

## Table of Contents

1. [Review Overview](#1-review-overview)
2. [Project Structure Review](#2-project-structure-review)
3. [Code Quality Review by Project](#3-code-quality-review-by-project)
4. [Architecture Review](#4-architecture-review)
5. [Security Review](#5-security-review)
6. [Performance Review](#6-performance-review)
7. [Testing Coverage Review](#7-testing-coverage-review)
8. [Documentation Review](#8-documentation-review)
9. [Configuration Review](#9-configuration-review)
10. [Best Practices Compliance](#10-best-practices-compliance)
11. [Review Checklist](#11-review-checklist)
12. [Priority Review Items](#12-priority-review-items)

---

## 1. Review Overview

### 1.1 Purpose
This document outlines the comprehensive review plan for the AdvGenNoSQL Server project. The review aims to ensure code quality, security, performance, and alignment with the architectural design documented in `plan.md`.

### 1.2 Scope
- **14 Projects** to review
- **~70+ Source Files** (excluding generated code)
- **~25 Test Files**
- **5 Benchmark Files**
- **Multiple Documentation Files**

### 1.3 Review Methodology
1. Static code analysis
2. Manual code review
3. Design pattern verification
4. Security audit
5. Performance profiling
6. Test coverage analysis
7. Documentation completeness check

---

## 2. Project Structure Review

### 2.1 Projects to Review

| Project | Type | Files | Priority |
|---------|------|-------|----------|
| AdvGenNoSqlServer.Core | Class Library | ~25 | High |
| AdvGenNoSqlServer.Storage | Class Library | ~19 | High |
| AdvGenNoSqlServer.Query | Class Library | ~17 | High |
| AdvGenNoSqlServer.Network | Class Library | ~5 | High |
| AdvGenNoSqlServer.Client | Class Library | ~4 | High |
| AdvGenNoSqlServer.Server | Application | ~2 | High |
| AdvGenNoSqlServer.Host | Application | ~2 | Medium |
| AdvGenNoSqlServer.Tests | Test Project | ~25 | High |
| AdvGenNoSqlServer.Benchmarks | Benchmark | ~5 | Medium |
| AdvGenNoSqlServer.Examples | Examples | ~3 | Low |

### 2.2 Structure Review Checklist
- [x] Verify project dependencies are correctly layered **[OK: Core ← Storage ← Query; Network ← Core only; Client ← Core+Network; Host/Server ← all. Correct DAG.]**
- [x] Check for circular dependencies **[OK: No circular dependencies found.]**
- [x] Validate namespace conventions **[OK: Consistent AdvGenNoSqlServer.* hierarchy across all projects.]**
- [x] Review project file configurations (.csproj) **[MINOR: Storage and Query .csproj files missing NuGet package metadata (PackageId, Version, Authors) — not publish-ready unlike Core/Network/Client.]**
- [x] Verify target framework consistency (net9.0) **[OK: All projects target net9.0. Nullable and ImplicitUsings enabled throughout.]**
- [x] Check NuGet package versions and compatibility **[MINOR: Microsoft.Extensions.Hosting version divergence — Server uses 9.0.0, Host uses 9.0.1. Align to same patch version.]**

---

## 3. Code Quality Review by Project

### 3.1 AdvGenNoSqlServer.Core

#### 3.1.1 Authentication Module
Files to review:
- [x] `Authentication/AuthenticationManager.cs` - User authentication logic **[REVIEWED - 5 ISSUES FOUND: SEC-001 to SEC-005]**
- [x] `Authentication/AuthenticationService.cs` - Authentication service interface **[REVIEWED - 5 ISSUES: BUG-003 (High - GetUsernameFromToken returns null), SEC-034 (High - Authorize bypasses permission check), AUDIT-001, CONC-007 (Medium), CODE-011 (Low)]**
- [x] `Authentication/JwtTokenProvider.cs` - JWT token generation/validation **[REVIEWED - GOOD: Uses HS256 + FixedTimeEquals. 3 MINOR ISSUES: SEC-006 to SEC-008]**
- [x] `Authentication/RoleManager.cs` - RBAC implementation **[REVIEWED - 4 ISSUES: SEC-011 (High), SEC-012-014 (Medium/Low). Thread safety + authorization needed]**
- [x] `Authentication/AuditLogger.cs` - Audit logging **[REVIEWED - GOOD: ConcurrentQueue, SemaphoreSlim, file rotation, critical event flush. 3 LOW: SEC-015, PERF-001, OPS-001]**
- [x] `Authentication/EncryptionService.cs` - Data encryption **[REVIEWED - EXCELLENT: AES-256-GCM, proper nonce, PBKDF2-100k, ZeroMemory cleanup. 2 LOW: SEC-009, SEC-010]**
- [x] `Authentication/IAuditLogger.cs` - Interface definitions **[REVIEWED - GOOD: Comprehensive audit coverage, correlation ID, UTC timestamps. 2 LOW: API-001/002 - async consistency]**
- [x] `Authentication/IJwtTokenProvider.cs` - Interface definitions **[REVIEWED - GOOD: Clean JWT interface, TokenValidationResult with factory methods, JwtTokenException. No issues.]**
- [x] `Authentication/IEncryptionService.cs` - Interface definitions **[REVIEWED - GOOD: AES-256-GCM interface, key derivation, key rotation, format validation. No issues.]**

**Review Focus:**
- Password hashing algorithm (PBKDF2, bcrypt, Argon2)
- Token expiration and refresh logic
- Role permission mappings
- Audit log completeness
- Encryption key management

#### 3.1.2 Caching Module
Files to review:
- [x] `Caching/ICacheManager.cs` - Cache interface **[REVIEWED - OK: Simple cache interface. 2 LOW: API-003 (missing license), API-004 (no async)]**
- [x] `Caching/MemoryCacheManager.cs` - Basic memory cache **[REVIEWED - 1 ISSUE: BUG-004 (Medium - Clear() throws NotImplementedException, breaks interface contract)]**
- [x] `Caching/AdvancedMemoryCacheManager.cs` - Advanced caching **[REVIEWED - EXCELLENT: LRU eviction, TTL, size limits, statistics, events, IDisposable. No issues.]**
- [x] `Caching/LruCache.cs` - LRU eviction implementation **[REVIEWED - GOOD: O(1) ops, RWLS, high-precision timer, background cleanup. 2 LOW: CONC-008 (lock during full enumeration), MEM-006 (entry class design)]**

**Review Focus:**
- Cache eviction policies
- Thread safety
- Memory bounds
- Cache invalidation strategies

#### 3.1.3 Transactions Module
Files to review:
- [x] `Transactions/ITransactionManager.cs` - Transaction interface **[REVIEWED - OK: Basic transaction interface. 1 LOW: API-005 (missing license), string OperationType not enum]**
- [x] `Transactions/TransactionManager.cs` - Basic transaction management **[REVIEWED - STUB: Doesn't actually commit/rollback! 4 ISSUES: DATA-010 (High - no-op), MEM-004, CONC-006 (Medium), DATA-011 (Low)]**
- [x] `Transactions/AdvancedTransactionManager.cs` - Advanced transactions **[REVIEWED - STUB: Also doesn't commit/rollback! Has timeout/cleanup but same DATA-012 no-op issue. MEM-005 - completed txns never removed]**
- [x] `Transactions/ITransactionCoordinator.cs` - Coordinator interface **[REVIEWED - EXCELLENT: Isolation levels, savepoints, 2PC states, async with cancellation, events. No issues.]**
- [x] `Transactions/TransactionCoordinator.cs` - Distributed transactions **[REVIEWED - GOOD: Full 2PC coord, WAL integration, deadlock handling, cleanup. 3 ISSUES: ASYNC-002, PERF-012, SEC-035 (Medium/Low)]**
- [x] `Transactions/TransactionContext.cs` - Transaction context **[REVIEWED - GOOD: Full context, savepoints, read/write sets, WAL. 3 ISSUES: BUG-005 (Medium - incomplete savepoint rollback), ASYNC-003, CONC-009 (Low)]**
- [x] `Transactions/ILockManager.cs` - Lock interface **[REVIEWED - EXCELLENT: Shared/Exclusive locks, deadlock event, upgrade support, full lock info. No issues.]**
- [x] `Transactions/LockManager.cs` - Lock implementation **[REVIEWED - GOOD: Wait-for graph deadlock detection, RWLS, victim selection. 4 ISSUES: PERF-003 (High), CONC-001/002, DATA-003]**
- [x] `Transactions/IWriteAheadLog.cs` - WAL interface **[REVIEWED - EXCELLENT: Full WAL interface, checkpoints, recovery, IAsyncEnumerable replay, statistics, events. No issues.]**
- [x] `Transactions/WriteAheadLog.cs` - WAL implementation **[REVIEWED - EXCELLENT: CRC32, before/after images, checkpoints, recovery. 4 ISSUES: PERF-002 (High), SEC-016, DATA-001/002]**

**Review Focus:**
- ACID compliance
- Isolation levels
- Deadlock detection/prevention
- WAL durability guarantees
- Lock granularity

#### 3.1.4 Pooling Module
Files to review:
- [x] `Pooling/IObjectPool.cs` - Pool interface **[REVIEWED - GOOD: Clean Rent/Return pattern, PoolStatistics with Interlocked. No issues.]**
- [x] `Pooling/ObjectPool.cs` - Generic object pool **[REVIEWED - GOOD: ConcurrentBag, factory/reset actions, pre-populate. 1 NOTE: CONC-010 (race in max check, acceptable for pool)]**
- [x] `Pooling/BufferPool.cs` - Buffer pooling **[REVIEWED - GOOD: ArrayPool<byte> backing, PooledMemory struct, AsSpan/AsMemory. No issues.]**
- [x] `Pooling/PooledObject.cs` - Pooled object wrapper **[REVIEWED - EXCELLENT: Readonly struct, implicit conversion, RentAndExecute extensions, async variants. No issues.]**
- [x] `Pooling/StringBuilderPool.cs` - StringBuilder pooling **[REVIEWED - EXCELLENT: Capacity management, PooledStringBuilder fluent API, implicit string conversion. No issues.]**
- [x] `Pooling/ObjectPoolManager.cs` - Pool management **[REVIEWED - GOOD: Named pool registry, GetOrCreatePool, disposal handling. 1 LOW: CODE-012 (reflection in GetAllStatistics)]**

**Review Focus:**
- Pool sizing strategies
- Object lifecycle management
- Memory leak prevention
- Thread safety

#### 3.1.5 Configuration Module
Files to review:
- [x] `Configuration/IConfigurationManager.cs` - Config interface **[REVIEWED - GOOD: Hot-reload support, ConfigurationChanged event. No issues.]**
- [x] `Configuration/ConfigurationManager.cs` - Configuration management **[REVIEWED - GOOD: Hot-reload with FSW, env var override, debouncing. 1 ISSUE: CONC-011 (Medium - mutable config exposed)]**
- [x] `Configuration/ServerConfiguration.cs` - Server config model **[REVIEWED - GOOD: Comprehensive config, organized regions, sensible defaults. No issues.]**

**Review Focus:**
- Configuration validation
- Hot-reload capabilities
- Sensitive data handling

#### 3.1.6 Models
Files to review:
- [x] `Models/Document.cs` - Document model **[REVIEWED - No issues. Simple, clean model with required Id and version tracking]**
- [x] `Models/BatchOperation.cs` - Batch operation model **[REVIEWED - No issues. Well-designed request/response models with sensible defaults]**

**Review Focus:**
- Model completeness
- Serialization attributes
- Validation annotations

---

### 3.2 AdvGenNoSqlServer.Storage

#### 3.2.1 Document Stores
Files to review:
- [x] `IDocumentStore.cs` - Store interface **[REVIEWED - No issues. Well-designed async CRUD interface with typed exception hierarchy]**
- [x] `DocumentStore.cs` - Basic document store **[REVIEWED - 1 ISSUE: CONC-012 (Low - unused ReaderWriterLockSlim, missing IDisposable)]**
- [x] `IPersistentDocumentStore.cs` - Persistent store interface **[REVIEWED - No issues. Clean interface extending IDocumentStore with persistence methods]**
- [x] `PersistentDocumentStore.cs` - Persistent implementation **[REVIEWED - 4 ISSUES: DATA-006 (High), DATA-007, CODE-001, CONC-003. Non-atomic writes + reflection usage]**
- [x] `InMemoryDocumentCollection.cs` - In-memory collection **[REVIEWED - 1 ISSUE: CONC-013 (Low - non-atomic Clear() can cause count desync)]**
- [x] `GarbageCollectedDocumentStore.cs` - GC-enabled store **[REVIEWED - 1 ISSUE: CONC-014 (Low - race condition in DeleteAsync, stale version)]**
- [x] `GarbageCollector.cs` - Document garbage collection **[REVIEWED - 1 ISSUE: DATA-016 (Medium - tombstones keyed by docId only, not collection:docId)]**
- [x] `HybridDocumentStore.cs` - Hybrid storage **[REVIEWED - 4 ISSUES: DATA-013 (High - silent exceptions), DATA-014 (Medium - non-atomic writes), DATA-015 (Medium - race condition), SEC-033 (Low - silent write failures)]**
- [x] `TtlDocumentStore.cs` - TTL-enabled store **[REVIEWED - 2 ISSUES: DATA-017 (Medium - TTL registration before operation), DATA-018 (Low - hardcoded field in ClearCollection)]**
- [x] `AtomicUpdateDocumentStore.cs` - Atomic update support **[REVIEWED - 1 ISSUE: MEM-001 (Medium - document locks never cleaned up, memory leak)]**
- [x] `IAtomicUpdateOperations.cs` - Atomic operations interface **[REVIEWED - No issues. Clean interface with atomic operations and proper exception hierarchy]**

**Review Focus:**
- CRUD operation correctness
- Concurrency handling
- Data consistency
- Memory management
- TTL cleanup logic

#### 3.2.2 Indexing
Files to review:
- [x] `Indexing/IBTreeIndex.cs` - B-tree interface **[REVIEWED - No issues. Clean generic interface with range queries and proper constraints]**
- [x] `Indexing/BTreeIndex.cs` - B-tree implementation **[REVIEWED - GOOD: Proper B-tree ops, generic, thread-safe. 3 ISSUES: PERF-004 (Medium), DATA-004/005 (Low)]**
- [x] `Indexing/BTreeNode.cs` - B-tree node structure **[REVIEWED - No issues. Solid B-tree node with split/merge/borrow operations]**
- [x] `Indexing/IndexManager.cs` - Index management **[REVIEWED - 1 ISSUE: DATA-019 (Low - null check missing for Document.Data in sparse index)]**
- [x] `Indexing/CompoundIndexKey.cs` - Compound key support **[REVIEWED - No issues. Excellent readonly struct with lexicographical comparison]**
- [x] `Indexing/ITtlIndexService.cs` - TTL index interface **[REVIEWED - No issues. Clean interface with background service pattern]**
- [x] `Indexing/TtlIndexService.cs` - TTL index implementation **[REVIEWED - 1 ISSUE: CONC-015 (Medium - PriorityQueue race condition in RegisterDocument)]**
- [x] `Indexing/PartialSparseIndex.cs` - Partial/Sparse index **[REVIEWED - 1 ISSUE: DATA-020 (Low - nullable Data.ContainsKey() in ShouldIncludeDocument)]**

**Review Focus:**
- B-tree correctness (insert, delete, rebalance)
- Index performance characteristics
- Compound key ordering
- TTL cleanup efficiency
- Sparse index optimization

#### 3.2.3 Storage Managers
Files to review:
- [x] `Storage/IStorageManager.cs` - Storage interface **[REVIEWED - No issues. Clean minimal storage interface]**
- [x] `Storage/FileStorageManager.cs` - File-based storage **[REVIEWED - 2 ISSUES: DATA-021 (Medium - non-atomic write), CODE-014 (Low - fake async with Task.Run)]**
- [x] `Storage/AdvancedFileStorageManager.cs` - Advanced file storage **[REVIEWED - 2 ISSUES: CONC-016 (Medium - stale cache from unprotected read), DATA-022 (Medium - non-atomic write)]**

**Review Focus:**
- File I/O efficiency
- Crash recovery
- Data corruption handling
- Compaction strategies

---

### 3.3 AdvGenNoSqlServer.Query

#### 3.3.1 Parsing
Files to review:
- [x] `Parsing/IQueryParser.cs` - Parser interface **[REVIEWED - No issues. Clean interface with TryParse pattern and proper exception type]**
- [x] `Parsing/QueryParser.cs` - Query parsing logic **[REVIEWED - CLEAN: Uses System.Text.Json safely, TryParse pattern, proper exceptions. 2 MINOR: DOS-002, CODE-008 (Low/Info)]**

**Review Focus:**
- Query syntax validation
- Injection prevention
- Error handling

#### 3.3.2 Filtering
Files to review:
- [x] `Filtering/IFilterEngine.cs` - Filter interface **[REVIEWED - No issues. Clean minimal interface with nullable filter pass-through]**
- [x] `Filtering/FilterEngine.cs` - Filter implementation **[REVIEWED - GOOD: MongoDB-like ops, logical operators, nested paths. 3 ISSUES: SEC-031 (Medium - ReDoS risk), PERF-008, CODE-009 (Low)]**

**Review Focus:**
- Operator support completeness
- Performance optimization
- Edge case handling

#### 3.3.3 Execution
Files to review:
- [x] `Execution/IQueryExecutor.cs` - Executor interface **[REVIEWED - No issues. Clean interface with CancellationToken and ExplainAsync]**
- [x] `Execution/QueryExecutor.cs` - Query execution **[REVIEWED - GOOD: Index-aware, explain, sorting, projection. 4 ISSUES: CODE-010, PERF-009 (Medium - mutates docs!), SEC-032, PERF-010 (Low)]**

**Review Focus:**
- Execution plan optimization
- Index utilization
- Resource limits

#### 3.3.4 Aggregation
Files to review:
- [x] `Aggregation/IAggregationStage.cs` - Stage interface **[REVIEWED - No issues. Clean interface with sync/async Execute and contextual exceptions]**
- [x] `Aggregation/AggregationPipeline.cs` - Pipeline implementation **[REVIEWED - 1 ISSUE: CODE-015 (Low - AddStages skips null check on individual stages)]**
- [x] `Aggregation/AggregationPipelineBuilder.cs` - Pipeline builder **[REVIEWED - No issues. Excellent fluent API with rich static factory helpers]**
- [x] `Aggregation/AggregationResult.cs` - Result model **[REVIEWED - 1 ISSUE: CODE-016 (Low - mutable public setters; use `init`; redundant Documents init in FailureResult)]**

**Review Focus:**
- Stage ordering
- Memory usage for large aggregations
- Streaming vs. buffering

#### 3.3.5 Cursors
Files to review:
- [x] `Cursors/Cursor.cs` - Cursor model **[REVIEWED - 2 ISSUES: SEC-036 (Medium - unsigned resume token), CODE-017 (Low - mutable Sort list in interface)]**
- [x] `Cursors/CursorImpl.cs` - Cursor implementation **[REVIEWED - 5 ISSUES: PERF-013 (High - full scan per batch), DATA-023, BUG-007 (Medium - ExtendExpiration no-op), BUG-008, CONC-017 (Low)]**
- [x] `Cursors/CursorManager.cs` - Cursor lifecycle management **[REVIEWED - 4 ISSUES: ASYNC-004, BUG-009 (Medium - double scan per operation), CODE-018, MEM-007 (Low)]**
- [x] `Cursors/CursorQueryExecutor.cs` - Cursor-based execution **[REVIEWED - No issues. Clean interface extending IQueryExecutor with cursor-specific pagination methods]**
- [x] `Cursors/CursorEnabledQueryExecutor.cs` - Cursor-enabled executor **[REVIEWED - 3 ISSUES: CODE-019 (Medium - cursor leak in limit-only mode), BUG-010 (Medium - incorrect TotalCount), CODE-020 (Low - hard-coupled constructor)]**

**Review Focus:**
- Cursor timeout handling
- Memory management
- Pagination correctness

#### 3.3.6 Models
Files to review:
- [x] `Models/Query.cs` - Query model **[REVIEWED - No issues. Rich builder pattern (Eq/Gt/In/And/Or), correct JsonElement conversion. Clean.]**
- [x] `Models/QueryResult.cs` - Result model **[REVIEWED - No issues. Elegant `HasMore` computed property, consistent factory methods.]**

---

### 3.4 AdvGenNoSqlServer.Network

Files to review:
- [x] `TcpServer.cs` - TCP server implementation **[REVIEWED - GOOD: ConcurrentDictionary, IAsyncDisposable, ConnectionPool, graceful shutdown. 5 ISSUES: NET-001 (Medium), SEC-022/023, RES-001, PERF-005]**
- [x] `ConnectionHandler.cs` - Connection handling **[REVIEWED - GOOD: SemaphoreSlim write lock, ArrayPool, checksum validation, IAsyncEnumerable. 7 ISSUES: BUG-001 (High), SEC-024, NET-002, PERF-006 (Medium), SEC-025, CODE-002, MEM-001 (Low)]**
- [x] `ConnectionPool.cs` - Connection pooling **[REVIEWED - GOOD: SemaphoreSlim, Interlocked counters, statistics tracking. Clean implementation. 3 MINOR ISSUES: RES-003 (Medium), CONC-004, CODE-003 (Low)]**
- [x] `MessageProtocol.cs` - Message framing **[REVIEWED - GOOD: ArrayPool, BinaryPrimitives, CRC32, Span<T>, magic/version validation, size limit. 5 ISSUES: SEC-026, DOS-001 (Medium), MEM-002, CODE-004, DATA-009 (Low)]**
- [x] `TlsStreamHelper.cs` - TLS support **[REVIEWED - GOOD: TLS 1.2/1.3, mTLS, cert revocation. 5 ISSUES: SEC-017/018 (High), SEC-019-021 (Medium/Low)]**

**Review Focus:**
- Async/await patterns
- Connection lifecycle
- Buffer management
- TLS configuration
- DoS protection

---

### 3.5 AdvGenNoSqlServer.Client

Files to review:
- [x] `Client.cs` - Main client class **[REVIEWED - GOOD: SemaphoreSlim locks, IAsyncDisposable, TLS support, keep-alive, batch ops, events. 6 ISSUES: SEC-027 (Critical), PERF-007 (Medium), SEC-028, CODE-005, MEM-003, NET-003 (Low)]**
- [x] `AdvGenNoSqlClient.Commands.cs` - Command implementations **[REVIEWED - CLEAN: Uses JsonSerializer properly, good input validation, proper exception handling. No issues found.]**
- [x] `ClientFactory.cs` - Client factory **[REVIEWED - BUG-002 (Medium): CreateClient ignores all options except ServerAddress!]**
- [x] `ClientOptions.cs` - Client configuration **[REVIEWED - CLEAN: Good defaults, SSL/mTLS support, keep-alive, retry options. 1 MINOR: CODE-006 (Low) - no value validation]**

**Review Focus:**
- Connection retry logic
- Command serialization
- Error handling
- Resource cleanup

---

### 3.6 AdvGenNoSqlServer.Server

Files to review:
- [x] `Program.cs` - Server entry point **[REVIEWED - CLEAN: Standard hosting, DI, config-driven. No issues.]**
- [x] `NoSqlServer.cs` - Server implementation **[REVIEWED - GOOD: IHostedService, IAsyncDisposable, graceful shutdown, proper logging. 5 ISSUES: NET-004, SEC-029 (Medium), ASYNC-001, SEC-030, CONC-005 (Low)]**

**Review Focus:**
- Startup/shutdown logic
- Dependency injection
- Configuration loading
- Graceful shutdown

---

### 3.7 AdvGenNoSqlServer.Host

Files to review:
- [x] `Program.cs` - Host entry point **[REVIEWED - 4 ISSUES: SEC-037 (High - no per-connection auth enforcement), ASYNC-005, BUG-011 (Medium - bulk stub), CONC-019 (Low)]**

**Review Focus:**
- Host builder configuration
- Service registration
- Environment handling

---

## 4. Architecture Review

### 4.1 Layered Architecture Compliance
Review against `plan.md` Section 2.1:

- [x] Client Application Layer isolation **[REVIEWED 2026-11-13 - DIRECTION OK: Client references only Core + Network; no Storage/Query/Server/Host reference and no server project references Client. 3 ISSUES: ARCH-001 (Medium - client transitively ships server-only code), ARCH-002 (Low - no client interface), ARCH-003 (Low - package description says "embedded")]**
- [x] Network/Protocol Layer (TCP) correctness **[REVIEWED 2026-11-13 - FRAMING OK: exact-length reads, big-endian header, magic/version/type validation, per-connection pre-auth payload limit checked before allocation, serialized writes. 5 ISSUES: NET-005 (High - no effective read timeout), NET-006 (Medium - rejection reason sent as empty payload), NET-007 (Medium - flags never honoured or rejected), NET-008 (Low - CRC excludes header), NET-009 (Low - protocol errors close silently)]**
- [x] Security & Authentication Layer integration **[REVIEWED 2026-11-13 - TCP gate in Host is sound when auth is required (per-connection identity, fail-closed `CommandAuthorizer`, real role in JWT). 7 ISSUES: SEC-038 (Critical - Server project HTTP API has no authentication), SEC-039 (High - Host HTTP writes skip role check), SEC-040 (High - anonymous role never enforced on TCP), SEC-041 (Medium - role changes do not reach live connections), SEC-042 (Medium - database select mutates shared state), ARCH-004 (Medium - parallel unused RBAC stack), AUDIT-002 (Low - denials not audited)]**
- [x] Query Processing & Command Layer design **[REVIEWED 2026-11-13 - NOT INTEGRATED: the Query project (parser, filter engine, executor, aggregation, cursors) is used only by the Embedded library; neither server reaches it. 4 ISSUES: QRY-001 (High - query engine not wired into Host/Server), QRY-002 (Medium - duplicated, divergent command dispatch), QRY-003 (Medium - listdocuments full scan + sort per page), QRY-004 (Low - Transaction message type has no handler)]**
- [x] Transaction Management Layer implementation **[REVIEWED 2026-11-13 - NOT INTEGRATED: components exist (WAL, lock manager, 2PC coordinator, MVCC) but no server write path goes through them. 5 ISSUES: TXN-001 (High - layer registered in DI but never used; WAL recovery never invoked), TXN-002 (High - Session writes bypass the transaction, rollback undoes nothing), TXN-003 (High - MVCC visibility/conflict defects in the pending P0-1 change), TXN-004 (Medium - four competing transaction implementations), TXN-005 (Medium - pending WAL change truncates on any CRC mismatch)]**
- [x] Storage Engine Layer architecture **[REVIEWED 2026-11-13 - Clean `IDocumentStore` abstraction with a decorator family, but the server runs on `HybridDocumentStore` only. 5 ISSUES: STOR-001 (Critical - production store acknowledges writes before they are durable and writes non-atomically), STOR-002 (High - drop/clear races the write queue; dropped collections can reappear), STOR-003 (Medium - full dataset in memory, file per document, unbounded queue), STOR-004 (Medium - decorators and indexes not composed into the server), STOR-005 (Low - FlushAsync reports success after failed writes)]**
- [x] Caching & Memory Management integration **[REVIEWED 2026-11-13 - NOT INTEGRATED: `ICacheManager` is registered in both hosts and resolved by nothing; the pooling module has no production caller. 4 ISSUES: CACHE-001 (Medium - cache registered but unused), CACHE-002 (Medium - cache/pooling configuration keys have no effect), CACHE-003 (Medium - no overall memory budget), CACHE-004 (Low - three cache implementations and an unused pooling module)]**
- [x] Persistence & File I/O Layer reliability **[REVIEWED 2026-11-13 - The new `AtomicFileWriter` (temp + fsync + rename, pending P0-5 change) is the right pattern but is used by only two classes. 5 ISSUES: IO-001 (High - durable-write pattern not applied to most persistent files), IO-002 (Medium - `AtomicFileWriter` gaps: no directory fsync, orphan temp files, internal to Storage), IO-003 (Medium - corrupt user file silently becomes an empty user list), IO-004 (Medium - no data-directory lock; non-atomic collection drop), IO-005 (Low - no I/O failure policy)]**

### 4.2 Design Pattern Usage
- [x] Repository pattern in Storage layer **[REVIEWED 2026-11-13 - PRESENT: `IDocumentStore` (Core/Abstractions) is a proper repository abstraction, implementations and decorators depend on the interface, and a reusable `DocumentStoreContractTests` suite exists. 5 ISSUES: REPO-001 (Medium - CRUD-only interface forces full scans), REPO-002 (Medium - stores hand out and keep live mutable instances), REPO-003 (Medium - no decorator base; capability downcast to concrete store), REPO-004 (Low - cancellation tokens accepted but ignored), REPO-005 (Low - duplicate `IDatabaseManager`/`DatabaseManager` names; contract tests cover 3 of ~15 stores)]**
- [x] Factory pattern in Client creation **[REVIEWED 2026-11-13 - NOMINAL ONLY: `AdvGenNoSqlClientFactory` is a static two-method wrapper used by two unit tests; every real caller uses `new AdvGenNoSqlClient(...)`. 3 ISSUES: FACT-001 (High - factory drops `UseSsl`, so a TLS request becomes a plaintext connection; raises BUG-002), FACT-002 (Low - factory adds no value: no DI, no interface, no validation), FACT-003 (Low - server address has two sources and a lenient parser)]**
- [x] Command pattern in Query processing **[REVIEWED 2026-11-13 - PARTIAL: aggregation stages are proper command objects (`IAggregationStage.Execute`, six stage classes, fluent builder) and `Query` is a self-contained request object handed to `IQueryExecutor`. Filter operators and server commands are not. 3 ISSUES: CMD-001 (Medium - filter operators are a hard-coded switch re-interpreted per document, validated only at evaluation time), CMD-002 (Medium - no wire-format factory for aggregation stages, so pipelines cannot be sent by a client), CMD-003 (Low - server command dispatch is a string switch; see QRY-002)]**
- [ ] Observer pattern in Event handling
- [ ] Strategy pattern in Caching policies
- [ ] Builder pattern in Aggregation pipelines

### 4.3 SOLID Principles
- [ ] **S**ingle Responsibility - Each class has one reason to change
- [ ] **O**pen/Closed - Open for extension, closed for modification
- [ ] **L**iskov Substitution - Interfaces can be substituted
- [ ] **I**nterface Segregation - No forced interface implementation
- [ ] **D**ependency Inversion - Depend on abstractions

### 4.4 Dependency Injection
- [ ] All dependencies injected via constructors
- [ ] No service locator anti-pattern
- [ ] Proper lifetime management (Singleton, Scoped, Transient)

---

## 5. Security Review

### 5.1 Authentication & Authorization
- [ ] Password hashing strength (minimum PBKDF2 with 100k iterations)
- [ ] JWT token security (algorithm, expiration, claims)
- [ ] RBAC permission model completeness
- [ ] Session management
- [ ] Brute force protection (rate limiting)

### 5.2 Transport Security
- [ ] TLS 1.2+ enforcement
- [ ] Certificate validation
- [ ] Cipher suite configuration
- [ ] Perfect forward secrecy support
- [ ] Certificate pinning (if implemented)

### 5.3 Data Security
- [ ] Input validation (injection prevention)
- [ ] Output encoding
- [ ] Sensitive data masking in logs
- [ ] Encryption key management
- [ ] Data at rest encryption readiness

### 5.4 OWASP Top 10 Review
- [ ] A01:2021 - Broken Access Control
- [ ] A02:2021 - Cryptographic Failures
- [ ] A03:2021 - Injection
- [ ] A04:2021 - Insecure Design
- [ ] A05:2021 - Security Misconfiguration
- [ ] A06:2021 - Vulnerable Components
- [ ] A07:2021 - Authentication Failures
- [ ] A08:2021 - Data Integrity Failures
- [ ] A09:2021 - Logging & Monitoring Failures
- [ ] A10:2021 - Server-Side Request Forgery

### 5.5 Security Test Files
- [x] `SecurityPenetrationTests.cs` - Penetration test coverage **[REVIEWED - GOOD: JWT attacks, brute force, RBAC, encryption, input validation. 2 LOW: TEST-001 (double-invoke in assertion), TEST-002 (vacuous audit assertion)]**

---

## 6. Performance Review

### 6.1 Benchmark Analysis
Review benchmark results in `AdvGenNoSqlServer.Benchmarks/`:
- [x] `DocumentStoreBenchmarks.cs` - Document operation performance **[REVIEWED - BENCH-001 (Low - Guid.NewGuid() inside DeleteDocument benchmark contaminates measurement)]**
- [x] `BTreeIndexBenchmarks.cs` - Index performance **[REVIEWED - BENCH-002 (Low - DeleteIntKey creates BTreeIndex inside benchmark body)]**
- [x] `CacheBenchmarks.cs` - Cache performance **[REVIEWED - BENCH-003 (Low - Remove/Eviction create LruCache inside measurement)]**
- [x] `SerializationBenchmarks.cs` - Serialization overhead **[REVIEWED - BENCH-004 (Low - deserialization to object/JsonElement; DeserializeMessage includes serialization cost)]**
- [x] `QueryEngineBenchmarks.cs` - Query execution performance **[REVIEWED - BENCH-005 (Low - FilterEngineComplexMatch constructs filter inside benchmark body)]**

### 6.2 Performance Patterns
- [ ] Async/await usage (no sync-over-async)
- [ ] Object pooling utilization
- [ ] Buffer pooling (ArrayPool<byte>)
- [ ] Span<T> and Memory<T> usage
- [ ] Lock contention analysis
- [ ] Memory allocation patterns

### 6.3 Scalability Review
- [ ] Connection handling under load
- [ ] Document count scalability
- [ ] Index performance at scale
- [ ] Cache hit ratios
- [ ] Query complexity handling

### 6.4 Load Test Review
- [ ] `LoadTests.cs` - Load test scenarios
- [ ] `StressTests.cs` - Stress test coverage

---

## 7. Testing Coverage Review

### 7.1 Unit Test Files
| Test File | Target Component | Priority |
|-----------|------------------|----------|
| `TransactionManagerTests.cs` | Transaction Manager | High |
| `TransactionCoordinatorTests.cs` | Transaction Coordinator | High |
| `WriteAheadLogTests.cs` | WAL | High |
| `LockManagerTests.cs` | Lock Manager | High |
| `FileStorageManagerTests.cs` | File Storage | High |
| `AdvancedFileStorageManagerTests.cs` | Advanced Storage | High |
| `PersistentDocumentStoreTests.cs` | Persistent Store | High |
| `DocumentStoreTests.cs` | Document Store | High |
| `GarbageCollectedDocumentStoreTests.cs` | GC Store | Medium |
| `GarbageCollectorTests.cs` | Garbage Collector | Medium |
| `HybridDocumentStoreTests.cs` | Hybrid Store | Medium |
| `AtomicUpdateOperationsTests.cs` | Atomic Updates | High |
| `BTreeIndexTests.cs` | B-tree Index | High |
| `IndexManagerTests.cs` | Index Manager | High |
| `CompoundAndUniqueIndexTests.cs` | Compound/Unique Index | High |
| `TtlIndexTests.cs` | TTL Index | Medium |
| `PartialSparseIndexTests.cs` | Partial/Sparse Index | Medium |
| `CursorTests.cs` | Cursors | Medium |
| `AuthenticationServiceTests.cs` | Authentication | High |
| `JwtTokenProviderTests.cs` | JWT | High |
| `RoleManagerTests.cs` | RBAC | High |
| `AuditLoggerTests.cs` | Audit Logging | Medium |
| `EncryptionServiceTests.cs` | Encryption | High |
| `CacheManagerTests.cs` | Cache | Medium |
| `ObjectPoolTests.cs` | Object Pool | Low |
| `QueryEngineTests.cs` | Query Engine | High |
| `AggregationPipelineTests.cs` | Aggregation | Medium |
| `NetworkTests.cs` | Network Layer | High |
| `NoSqlClientTests.cs` | Client | High |
| `SslTlsTests.cs` | TLS | High |
| `ConfigurationManagerTests.cs` | Configuration | Medium |
| `ConfigurationHotReloadTests.cs` | Hot Reload | Low |
| `BatchOperationTests.cs` | Batch Operations | Medium |

### 7.2 Test Quality Review
- [ ] Test naming conventions
- [ ] Arrange-Act-Assert pattern
- [ ] Test isolation (no shared state)
- [ ] Mock usage appropriateness
- [ ] Edge case coverage
- [ ] Negative testing (error paths)
- [ ] Integration test presence

### 7.3 Code Coverage Goals
- [ ] Core: Minimum 80% coverage
- [ ] Storage: Minimum 80% coverage
- [ ] Query: Minimum 75% coverage
- [ ] Network: Minimum 70% coverage
- [ ] Client: Minimum 70% coverage

---

## 8. Documentation Review

### 8.1 Documentation Files
- [ ] `README.md` - Project overview
- [ ] `LICENSE.md` - MIT License
- [ ] `plan.md` - Development plan
- [ ] `PROJECT_STATUS.md` - Current status
- [ ] `Documentation/API.md` - API documentation
- [ ] `Documentation/UserGuide.md` - User guide
- [ ] `Documentation/DeveloperGuide.md` - Developer guide
- [ ] `Documentation/PerformanceTuning.md` - Performance guide
- [ ] `Documentation/README.md` - Documentation index

### 8.2 Code Documentation
- [ ] XML documentation on public APIs
- [ ] Interface documentation completeness
- [ ] Complex algorithm explanations
- [ ] Configuration option documentation

### 8.3 Example Completeness
- [ ] `AdvGenNoSqlServer.Examples/Program.cs` - Main examples
- [ ] `AdvGenNoSqlServer.Examples/ClientServerExamples.cs` - Client examples
- [ ] `AdvGenNoSqlServer.Examples/TodoList/` - TodoList sample app

---

## 9. Configuration Review

### 9.1 Configuration Files
- [ ] `appsettings.json` - Base configuration
- [ ] `appsettings.Development.json` - Development settings
- [ ] `appsettings.Production.json` - Production settings
- [ ] `appsettings.Testing.json` - Test settings

### 9.2 Configuration Review Points
- [ ] Sensitive data handling (no hardcoded secrets)
- [ ] Environment-specific overrides
- [ ] Default value appropriateness
- [ ] Validation of configuration values
- [ ] Hot-reload support

---

## 10. Best Practices Compliance

### 10.1 C# Best Practices
- [ ] Nullable reference types enabled
- [ ] Async naming conventions (Async suffix)
- [ ] IDisposable implementation correctness
- [ ] Exception handling patterns
- [ ] LINQ usage efficiency
- [ ] String handling (StringBuilder, interpolation)

### 10.2 .NET Best Practices
- [ ] Dependency injection usage
- [ ] Configuration binding
- [ ] Logging framework usage
- [ ] Health check implementation
- [ ] Graceful shutdown handling

### 10.3 NoSQL Best Practices
- [ ] Document ID generation
- [ ] Index selection guidance
- [ ] Query optimization patterns
- [ ] Bulk operation support
- [ ] TTL implementation

---

## 11. Review Checklist

### 11.1 Pre-Review Checklist
- [ ] Clone latest code from repository
- [ ] Verify build succeeds
- [ ] Run all tests
- [ ] Generate code coverage report
- [ ] Run static analysis tools

### 11.2 Review Execution Checklist
- [ ] Complete Section 3 (Code Quality Review)
- [ ] Complete Section 4 (Architecture Review)
- [ ] Complete Section 5 (Security Review)
- [ ] Complete Section 6 (Performance Review)
- [ ] Complete Section 7 (Testing Review)
- [ ] Complete Section 8 (Documentation Review)
- [ ] Complete Section 9 (Configuration Review)
- [ ] Complete Section 10 (Best Practices)

### 11.3 Post-Review Checklist
- [ ] Document all findings
- [ ] Categorize issues by severity
- [ ] Create GitHub issues for findings
- [ ] Prioritize fixes
- [ ] Schedule follow-up review

---

## 12. Priority Review Items

### 12.1 Critical (Must Review First)
1. **Security Layer** - Authentication, encryption, TLS
2. **Transaction Management** - ACID compliance, WAL
3. **Storage Engine** - Data integrity, persistence
4. **Network Layer** - Connection handling, protocol security

### 12.2 High Priority
1. **Query Engine** - Injection prevention, performance
2. **Index Management** - Correctness, performance
3. **Client Library** - Error handling, reliability
4. **Test Coverage** - Critical path coverage

### 12.3 Medium Priority
1. **Caching Layer** - Eviction policies, memory management
2. **Aggregation Pipeline** - Correctness
3. **Configuration** - Validation, hot-reload
4. **Documentation** - API completeness

### 12.4 Lower Priority
1. **Examples** - Correctness, best practices
2. **Benchmarks** - Methodology validation
3. **Object Pooling** - Optimization opportunities

---

## Review Notes

### Findings Log

| ID | File | Line | Severity | Description | Status |
|----|------|------|----------|-------------|--------|
| SEC-001 | AuthenticationManager.cs | 143-155 | Critical | Password hashing uses SHA256 instead of PBKDF2/bcrypt/Argon2. SHA256 is too fast and vulnerable to brute-force attacks. Must use key derivation function with minimum 100k iterations. | Open |
| SEC-002 | AuthenticationManager.cs | 58 | High | Password hash comparison uses `!=` operator which is vulnerable to timing attacks. Must use constant-time comparison `CryptographicOperations.FixedTimeEquals()`. | Open |
| SEC-003 | AuthenticationManager.cs | 13-14 | High | `_users` and `_activeSessions` Dictionary objects are not thread-safe. Concurrent access from multiple connections will cause race conditions. Use `ConcurrentDictionary<>`. | Open |
| SEC-004 | AuthenticationManager.cs | 52-71 | Medium | No rate limiting for authentication attempts. Vulnerable to brute-force attacks. Implement failed attempt tracking and lockout. | Open |
| SEC-005 | AuthenticationManager.cs | 30-50 | Medium | No password complexity validation. Should enforce minimum length (12+), complexity requirements, and check against common password lists. | Open |
| SEC-006 | JwtTokenProvider.cs | 19 | Medium | Secret key stored as plain string in memory. Sensitive to memory dump attacks. Consider using SecureString or protected memory. | Open |
| SEC-007 | JwtTokenProvider.cs | - | Low | No token revocation/blacklist mechanism. Tokens remain valid until expiration even after logout. Consider implementing JWT blacklist. | Open |
| SEC-008 | JwtTokenProvider.cs | 208-228, 231-254 | Low | ExtractUsername and GetExpirationTime methods do not validate signature before returning data. Could expose claims from tampered tokens. | Open |
| SEC-009 | EncryptionService.cs | 51-53 | Low | Auto-generated key has only TODO for logging. Could lead to data loss if key is not persisted. Should log warning or throw if no key store configured. | Open |
| SEC-010 | EncryptionService.cs | 265-270 | Low | DeriveKeyFromPassword returns salt+key combined. Consider returning a struct with named properties for clarity. | Open |
| SEC-011 | RoleManager.cs | 12-14 | High | `_roles`, `_userRoles`, and `PermissionRegistry._validPermissions` use non-thread-safe collections. Race conditions under concurrent access. Use `ConcurrentDictionary<>`. | Open |
| SEC-012 | RoleManager.cs | - | Medium | No authorization check on role management methods. Any caller can elevate privileges. Should require `RoleManage` permission. | Open |
| SEC-013 | RoleManager.cs | 263-321 | Medium | Default roles created in-memory only. Role/permission changes are lost on restart. Need persistence layer. | Open |
| SEC-014 | RoleManager.cs | 425-428 | Low | `RegisterCustomPermission` not thread-safe. HashSet modification during concurrent reads causes exceptions. | Open |
| SEC-015 | AuditLogger.cs | 512-517 | Low | JSON serialization could expose sensitive data. Consider filtering/masking sensitive fields before logging. | Open |
| PERF-001 | AuditLogger.cs | 545 | Low | `GetAwaiter().GetResult()` in Dispose blocks thread. Consider implementing `IAsyncDisposable`. | Open |
| OPS-001 | AuditLogger.cs | - | Low | No log retention/archival policy. Old log files accumulate indefinitely. Add configurable cleanup. | Open |
| PERF-002 | WriteAheadLog.cs | 60 | High | `GetAwaiter().GetResult()` in constructor blocks thread. Can cause deadlocks. Use factory pattern or async initialization. | Open |
| SEC-016 | WriteAheadLog.cs | - | Medium | WAL data is unencrypted. Sensitive document data stored in plaintext. Add optional encryption layer. | Open |
| DATA-001 | WriteAheadLog.cs | 696-700 | Medium | CRC mismatch throws exception, stopping recovery. Consider option to skip corrupted entries for partial recovery. | Open |
| DATA-002 | WriteAheadLog.cs | 99-101 | Low | Silent exception swallowing when loading checkpoint hides corruption. Should log warning. | Open |
| PERF-003 | LockManager.cs | 112 | High | Sync `AcquireLock` uses `GetAwaiter().GetResult()` blocking thread. Remove sync method or use sync-specific implementation. | Open |
| CONC-001 | LockManager.cs | 16,19,22 | Medium | `List<>` and `HashSet<>` inside `ConcurrentDictionary` are not thread-safe for mutations. Use `ConcurrentBag<>` or explicit locking. | Open |
| CONC-002 | LockManager.cs | 451-456 | Medium | `WaitForUpgradeAsync` releases then reacquires lock, creating race condition window. Implement true atomic upgrade. | Open |
| DATA-003 | LockManager.cs | 678 | Low | Silent exception swallowing in deadlock detection could hide bugs. Should log warning. | Open |
| SEC-017 | TlsStreamHelper.cs | 254-262 | High | Allows localhost certificates with name mismatch - production security risk. Add configuration flag. | Open |
| SEC-018 | TlsStreamHelper.cs | 135-137, 217 | High | Uses `X509KeyStorageFlags.Exportable` - private keys should not be exportable in production. | Open |
| SEC-019 | TlsStreamHelper.cs | 252, 280 | Medium | Uses `Console.WriteLine` for security logging. Should use ILogger for proper audit trail. | Open |
| SEC-020 | TlsStreamHelper.cs | - | Medium | No certificate pinning support. Documented in plan.md but not implemented. | Open |
| SEC-021 | TlsStreamHelper.cs | 90 | Low | No TLS 1.3-only option for high-security environments. Consider adding SslProtocols.Tls13 only mode. | Open |
| PERF-004 | BTreeIndex.cs | 19 | Medium | Uses coarse-grained locking (single lock). Consider `ReaderWriterLockSlim` for read-heavy workloads. | Open |
| DATA-004 | BTreeIndex.cs | 268-271 | Low | Count management in delete uses loop - could be simplified with values.Count decrement. | Open |
| DATA-005 | BTreeIndex.cs | - | Low | No index persistence - in-memory only. Rebuilds on startup from documents. | Info |
| DATA-006 | PersistentDocumentStore.cs | 373-377 | High | Silent exception swallowing when loading documents. Corrupted data is skipped without logging - could hide data loss. | Open |
| DATA-007 | PersistentDocumentStore.cs | 450 | Medium | `File.WriteAllTextAsync` is not atomic. Crash during write corrupts file. Write to temp file then rename. | Open |
| CODE-001 | PersistentDocumentStore.cs | 462-483 | Medium | Uses reflection to access private fields - fragile and breaks if InMemoryDocumentCollection changes. | Open |
| CONC-003 | PersistentDocumentStore.cs | 478-480 | Low | Reflection-based count update with `Interlocked.Increment` then SetValue - potential race condition. | Open |
| NET-001 | TcpServer.cs | 176, 216, 286 | Medium | Uses `Console.Error.WriteLine` for error logging. Should use ILogger for structured logging and proper audit trail. | Open |
| SEC-022 | TcpServer.cs | 301 | Medium | JSON payload construction uses string interpolation `$"{{\"error\":\"{reason}\"}}"`. If reason contains quotes/special chars, JSON could be malformed or injected. Use JsonSerializer. | Open |
| SEC-023 | TcpServer.cs | 304-305, 318-319 | Low | Silent exception swallowing in `SendConnectionRejectedAsync` and `SendSslErrorAsync`. Should log exceptions for debugging. | Open |
| RES-001 | TcpServer.cs | 163 | Low | Fire-and-forget task `_ = HandleConnectionAsync(...)` without exception observation. Exceptions silently lost. Add `.ContinueWith()` for logging. | Open |
| PERF-005 | TcpServer.cs | 353 | Low | `Dispose()` calls `GetAwaiter().GetResult()` on async DisposeAsync. Common pattern but can deadlock if called from sync context. | Open |
| BUG-001 | ConnectionHandler.cs | 115-134 | High | `EnableSslAsync` creates new SslStream but discards it - never assigns to `_stream`. Method is non-functional. The SslStream is lost and connection remains plaintext. | Open |
| SEC-024 | ConnectionHandler.cs | 309-310 | Medium | JSON construction uses string interpolation for error messages. Same injection vulnerability as SEC-022. Use JsonSerializer. | Open |
| NET-002 | ConnectionHandler.cs | 132 | Medium | Uses `Console.Error.WriteLine` for SSL error logging. Should use ILogger for structured logging. | Open |
| PERF-006 | ConnectionHandler.cs | 338 | Medium | Uses blocking `.Wait()` on async `ShutdownAsync()`. Should use `await` with timeout or `CancellationTokenSource`. | Open |
| SEC-025 | ConnectionHandler.cs | 340, 345 | Low | Silent exception swallowing in `CloseAsync`. Should log for debugging. | Open |
| CODE-002 | ConnectionHandler.cs | 347 | Low | `await Task.CompletedTask` is unnecessary. Method should return `ValueTask.CompletedTask` or be non-async. | Open |
| MEM-001 | ConnectionHandler.cs | 29-30 | Low | `PipeReader` and `PipeWriter` created but never used. Code uses `_stream.ReadAsync/WriteAsync` directly. Dead code. | Open |
| RES-003 | ConnectionPool.cs | 16 | Medium | Class doesn't implement `IDisposable`. The `SemaphoreSlim` should be disposed when pool is no longer needed. | Open |
| CONC-004 | ConnectionPool.cs | 110-112 | Low | Release() decrements counters before `_semaphore.Release()`. If Release throws (called too many times), counters become incorrect. | Open |
| CODE-003 | ConnectionPool.cs | - | Low | No async versions of `Acquire`. Add `AcquireAsync()` using `_semaphore.WaitAsync()` for non-blocking async usage. | Open |
| SEC-026 | MessageProtocol.cs | 167-168, 186 | Medium | JSON construction uses string interpolation in `CreateCommand` and `CreateError`. Injection risk if command/collection contains special chars. Use JsonSerializer. | Open |
| DOS-001 | MessageProtocol.cs | 340 | Medium | Max payload size of 100MB is too large. Could enable memory exhaustion DoS. Reduce to 10MB or make configurable. | Open |
| MEM-002 | MessageProtocol.cs | 296 | Low | `Serialize` returns rented buffer that caller must return. Error-prone API. Consider IDisposable wrapper struct. | Open |
| CODE-004 | MessageProtocol.cs | 406 | Low | `buffer.AsSpan(offset).ToArray()` creates unnecessary copy. ParseHeader could accept ReadOnlySpan<byte>. | Open |
| DATA-009 | MessageProtocol.cs | 374-376 | Low | Returns checksum 0 for empty data. Valid but means empty payload with checksum=0 always validates. Document this behavior. | Open |
| SEC-027 | Client.cs | 325 | Critical | Auth payload uses string interpolation `$"{{\"username\":\"{username}\",\"password\":\"{password}\"}}"`. Injection risk if credentials contain special chars. Use JsonSerializer. | Open |
| PERF-007 | Client.cs | 843 | Medium | `Dispose()` uses `GetAwaiter().GetResult()` - sync-over-async pattern. Can cause deadlocks. | Open |
| SEC-028 | Client.cs | 237 | Low | Silent exception swallowing in `DisconnectAsync`. Should log for debugging. | Open |
| CODE-005 | Client.cs | 267 | Low | `PingAsync` catch block swallows all exceptions silently. Should distinguish between connection errors and others. | Open |
| MEM-003 | Client.cs | 409, 426-427 | Low | `ReceiveMessageAsync` creates new byte arrays instead of using ArrayPool for header and payload buffers. | Open |
| NET-003 | Client.cs | 717-718 | Info | Redundant encoding: `GetBytes(json)` then `GetByteCount(json)`. Just use `bytes.Length`. | Open |
| CODE-006 | ClientOptions.cs | - | Low | No validation of option values. Negative timeouts, retry delays, or max retry attempts would cause issues at runtime. | Open |
| BUG-002 | ClientFactory.cs | 14-15 | Medium | `CreateClient(AdvGenNoSqlClientOptions)` ignores all options except ServerAddress. All SSL, keepalive, retry settings are lost. | Open |
| NET-004 | NoSqlServer.cs | 71 | Medium | `_tcpServer.StartAsync(cancellationToken)` is NOT AWAITED! Should be `await _tcpServer.StartAsync(...)` to propagate exceptions. | Open |
| SEC-029 | NoSqlServer.cs | 227-230 | Medium | Simple auth compares plaintext master password without rate limiting. Token is just random GUID with no user association. | Open |
| ASYNC-001 | NoSqlServer.cs | 115 | Low | `async void` event handler. Unavoidable for events but could swallow exceptions. Try-catch mitigates this. | Open |
| SEC-030 | NoSqlServer.cs | 165 | Low | Silent exception swallowing when parsing handshake. Should at least log a warning. | Open |
| CONC-005 | NoSqlServer.cs | 352-363 | Low | Check-then-act pattern (ExistsAsync then InsertAsync/UpdateAsync) is not atomic. Race condition possible. | Open |
| DOS-002 | QueryParser.cs | - | Low | No limit on JSON depth. Deeply nested JSON could cause stack overflow. Set JsonSerializerOptions.MaxDepth. | Open |
| CODE-008 | QueryParser.cs | 86 | Info | Unknown properties silently treated as filter conditions. Could be confusing. Consider whitelist or documentation. | Open |
| SEC-031 | FilterEngine.cs | 252-256 | Medium | Regex.IsMatch without timeout. Complex patterns could cause ReDoS. Add RegexOptions with MatchTimeout. | Open |
| PERF-008 | FilterEngine.cs | 252-256 | Low | Regex compiled on every call. Cache compiled regex patterns for performance. | Open |
| CODE-009 | FilterEngine.cs | 250-259 | Info | Wildcard syntax (* and ?) mixed with regex. Potentially confusing. Document the behavior clearly. | Open |
| DATA-010 | TransactionManager.cs | 50-51, 72-73 | High | **STUB IMPLEMENTATION**: Commit/Rollback don't actually apply operations! Comments say "In a real implementation...". No ACID guarantees. | Open |
| MEM-004 | TransactionManager.cs | 13 | Medium | Transactions stored in ConcurrentDictionary indefinitely. No cleanup of completed transactions. Memory leak. | Open |
| CONC-006 | TransactionManager.cs | 43-56 | Medium | Single global `_lock` with ConcurrentDictionary is inconsistent. Race between TryGetValue and lock acquisition. | Open |
| DATA-011 | TransactionManager.cs | 115 | Low | `AsReadOnly()` returns snapshot but Operations could be modified concurrently if AddOperation called during iteration. | Open |
| DATA-012 | AdvancedTransactionManager.cs | 63-64, 85-86 | High | **STUB**: Same as DATA-010. Commit/Rollback don't apply operations. Has timeout but no actual functionality. | Open |
| MEM-005 | AdvancedTransactionManager.cs | 150-167 | Low | Cleanup only marks expired active txns as Failed. Completed txns never removed from dictionary. | Open |
| CODE-010 | QueryExecutor.cs | 312-346 | Medium | Uses reflection to invoke index methods. Fragile and slow. Should use common IIndex interface. | Open |
| PERF-009 | QueryExecutor.cs | 412-440 | Medium | ApplyProjection modifies original documents in-place. Mutates cached/stored docs causing side effects. Clone docs first. | Open |
| SEC-032 | QueryExecutor.cs | 343-345 | Low | Silent exception swallowing in QueryIndexAsync. Should log warning. | Open |
| PERF-010 | QueryExecutor.cs | 49-55 | Low | Sequential document fetching by ID in loop. Could be parallelized or batch-fetched. | Open |
| DATA-013 | HybridDocumentStore.cs | 98-101, 382-385 | High | Silent exception swallowing when loading documents from disk. Corrupted or invalid JSON data is silently skipped without logging - could hide data loss. | Open |
| DATA-014 | HybridDocumentStore.cs | 442-443 | Medium | `File.WriteAllTextAsync` is not atomic. Crash during write corrupts file. Write to temp file then atomic rename. | Open |
| DATA-015 | HybridDocumentStore.cs | 122-136 | Medium | Race condition in `InsertAsync` between `ContainsKey` check and `TryAdd`. Concurrent inserts could bypass duplicate detection. | Open |
| SEC-033 | HybridDocumentStore.cs | 411-414 | Low | Silent exception swallowing in write queue processing. Write failures are never reported, potentially losing data. | Open |
| BUG-003 | AuthenticationService.cs | 71-76 | High | `GetUsernameFromToken` always returns `null`. Method is non-functional - comments indicate it was never implemented. | Open |
| SEC-034 | AuthenticationService.cs | 182-200 | High | `Authorize` method bypasses permission check entirely. After token validation, it always returns `Success()` without checking user permissions against required permission. Major security flaw. | Open |
| AUDIT-001 | AuthenticationService.cs | 226 | Medium | Uses `Console.WriteLine` for audit logging instead of existing `IAuditLogger` interface. Audit logs are lost/inconsistent. | Open |
| CONC-007 | AuthenticationService.cs | 32-42 | Medium | Non-atomic `RegisterUser` - if auth registration succeeds but role assignment fails, user exists without a role. Should cleanup on failure. | Open |
| CODE-011 | AuthenticationService.cs | 36-42 | Low | Silent failure when initial role doesn't exist. Role assignment silently skipped. Should log warning or throw. | Open |
| API-001 | IAuditLogger.cs | 16-80 | Low | Sync/async inconsistency. Only `Log`/`LogAsync` pair exists. Specialized methods (LogAuthentication, LogAuthorizationFailure, etc.) only have sync versions. | Open |
| API-002 | IAuditLogger.cs | 83-103 | Low | Query methods (GetRecentEvents, GetEventsByUser, GetEventsByType) should have async variants for file/database access patterns. | Open |
| API-003 | ICacheManager.cs | 1 | Low | Missing file header/license comment that other files have. Inconsistent code style. | Open |
| API-004 | ICacheManager.cs | 7-31 | Low | No async variants of cache operations (GetAsync, SetAsync). Needed for distributed cache implementations. | Open |
| BUG-004 | MemoryCacheManager.cs | 33-38 | Medium | `Clear()` throws `NotImplementedException` - breaks ICacheManager interface contract. Callers expecting Clear() to work will crash. | Open |
| CONC-008 | LruCache.cs | 391 | Low | `_cache.Values.Where(...).ToList()` under write lock enumerates all values. For large caches, holds lock for long time. Consider batching. | Open |
| MEM-006 | LruCache.cs | 14-21 | Low | `LruCacheEntry<TValue>` has mutable properties with `default!` for Value. Could be null for reference types despite non-nullable annotation. | Open |
| API-005 | ITransactionManager.cs | 1 | Low | Missing file header/license comment. Also `TransactionOperation.OperationType` uses string instead of enum for type safety. | Open |
| ASYNC-002 | TransactionCoordinator.cs | 289, 301 | Medium | `async void` in event handlers and timer callbacks. Exceptions would crash process if not caught. Methods have try-catch but risky pattern. | Open |
| PERF-012 | TransactionCoordinator.cs | 359 | Medium | `AbortAsync(...).Wait()` sync-over-async in Dispose. Can cause deadlocks. | Open |
| SEC-035 | TransactionCoordinator.cs | 313-316 | Low | Silent exception swallowing in cleanup. Should log error for debugging. | Open |
| BUG-005 | TransactionContext.cs | 239-240 | Medium | `RollbackToSavepointAsync` resets operation count but doesn't undo operations. WriteSet is not restored - savepoint rollback is incomplete. | Open |
| ASYNC-003 | TransactionContext.cs | 378 | Low | Fire-and-forget `_ = RollbackAsync()` in Dispose. Exceptions lost, rollback may not complete. | Open |
| CONC-009 | TransactionContext.cs | 279 | Low | Lock type uses `Shared` for non-Serializable writes. Writes should always use `Exclusive` to prevent write-write conflicts. | Open |
| CONC-010 | ObjectPool.cs | 89-99 | Info | Non-atomic check `_count >= _maxCapacity` before add. Could exceed max capacity briefly under concurrent access. Acceptable for pool semantics. | Info |
| CODE-012 | ObjectPoolManager.cs | 141-153 | Low | Uses reflection to access Statistics property in `GetAllStatistics`. Fragile and slow. Consider adding Statistics to common interface. | Open |
| CONC-011 | ConfigurationManager.cs | 88 | Medium | `Configuration` property returns mutable reference directly. Callers can modify internal state. Return clone or readonly view. | Open |
| CONC-012 | DocumentStore.cs | 17 | Low | Unused `ReaderWriterLockSlim _collectionsLock` declared but never used. Dead code and missing disposal (IDisposable not implemented). | Open |
| CONC-013 | InMemoryDocumentCollection.cs | 188-192 | Low | Non-atomic `Clear()` - between `_documents.Clear()` and `Interlocked.Exchange`, concurrent Insert can cause Count to be out of sync with actual documents. | Open |
| CONC-014 | GarbageCollectedDocumentStore.cs | 50-67 | Low | Race condition in `DeleteAsync` - document version captured at line 53 may be stale by line 57 if another thread modifies document. | Open |
| DATA-016 | GarbageCollector.cs | 236 | Medium | Tombstones keyed by `documentId` alone, not `collection:documentId`. If different collections have same document ID, only one tombstone is tracked, causing incorrect GC. | Open |
| DATA-017 | TtlDocumentStore.cs | 67-75, 92-100 | Medium | TTL registration happens BEFORE InsertAsync/UpdateAsync. If operation fails, document is still registered for TTL tracking, causing inconsistent state. | Open |
| DATA-018 | TtlDocumentStore.cs | 161-165 | Low | `ClearCollectionAsync` recreates TTL index with hardcoded `"expireAt"` field, losing original configuration. | Open |
| MEM-001 | AtomicUpdateDocumentStore.cs | 16-23 | Medium | `_documentLocks` stores SemaphoreSlim indefinitely. Locks never cleaned up on document deletion, causing memory leak with high document churn. | Open |
| DATA-019 | IndexManager.cs | 662, 686 | Low | SparseIndexWrapper uses `document.Data.ContainsKey()` without null check. Document.Data is nullable, will throw NullReferenceException if null. | Open |
| CONC-015 | TtlIndexService.cs | 127-131 | Medium | `RegisterDocument` accesses PriorityQueue (not thread-safe) without collection lock, while `CleanupExpiredDocumentsAsync` uses the lock. Race condition. | Open |
| DATA-020 | PartialSparseIndex.cs | 82 | Low | `ShouldIncludeDocument` calls `document.Data.ContainsKey()` without null check. Document.Data is nullable, will throw NullReferenceException. | Open |
| DATA-021 | FileStorageManager.cs | 52 | Medium | `File.WriteAllText` is non-atomic. Process crash mid-write corrupts file. Use write-to-temp-file + rename pattern for crash safety. | Open |
| CODE-014 | FileStorageManager.cs | 41-54 | Low | Uses `Task.Run` wrapping synchronous I/O. Does not achieve true async I/O, just offloads to thread pool. Use `File.ReadAllTextAsync`/`WriteAllTextAsync`. | Open |
| CONC-016 | AdvancedFileStorageManager.cs | 86-99 | Medium | Cache read in `LoadDocumentAsync` happens outside semaphore. Concurrent delete can cause stale cached data to be returned. | Open |
| DATA-022 | AdvancedFileStorageManager.cs | 62 | Medium | `File.WriteAllTextAsync` is non-atomic. Process crash mid-write corrupts file. Use write-to-temp + rename pattern. | Open |
| CODE-015 | AggregationPipeline.cs | 58-62 | Low | `AddStages` does not null-check individual stages unlike `AddStage`. Null stage silently enters list and causes NullReferenceException at execution instead of ArgumentNullException at add time. | Open |
| CODE-016 | AggregationResult.cs | 17,27,37,42 | Low | Result type has public setters on all properties. Callers can mutate results post-construction. Replace `set` with `init` accessors to enforce immutability and signal intent. `FailureResult` redundantly sets `Documents = new List<Document>()` (property initializer already does this). | Open |
| SEC-036 | Cursor.cs | 119-138 | Medium | `ResumeToken` is Base64-encoded JSON with no HMAC/signature. Clients can tamper with `FilterJson`, `SortJson`, or `LastDocumentId` to manipulate pagination. Sign tokens with HMAC-SHA256 before returning to clients. | Open |
| CODE-017 | Cursor.cs | 33 | Low | `ICursor.Sort` is typed as `List<SortField>?` instead of `IReadOnlyList<SortField>?`. Callers can mutate the active cursor's sort specification mid-flight. | Open |
| PERF-013 | CursorImpl.cs | 189 | High | `FetchDocumentsAsync` calls `GetAllAsync` on the full collection on every invocation. Both `GetNextBatchAsync` and `HasMoreAsync` call it independently. Iterating N batches = O(C×N) full collection scans. Cache the full filtered+sorted result on first fetch. | Open |
| DATA-023 | CursorImpl.cs | 209 | Medium | Position-based pagination re-fetches live data each batch. Documents inserted/deleted between calls cause result skips or duplicates. Cursors should snapshot the result set at creation time. | Open |
| BUG-007 | CursorImpl.cs | 253-259 | Medium | `ExtendExpiration` is a complete no-op. It computes `originalTimeout` but never modifies `ExpiresAt`. Cursor expires on the original schedule even with constant active use. | Open |
| BUG-008 | CursorImpl.cs | 65 | Low | `_resumeAfterPosition` is hardcoded to `0` in the constructor. The `ResumeToken` parameter is accepted but never used. Resume tokens are silently ignored and cursors always start from position 0. | Open |
| CONC-017 | CursorImpl.cs | 173,181 | Low | `IsClosed` is written in `CloseAsync`/`DisposeAsync` without holding `_lock` and is not `volatile`. Racy reads in `GetNextBatchAsync`/`HasMoreAsync` are not guaranteed by C# memory model on all architectures. | Open |
| ASYNC-004 | CursorManager.cs | 408,440 | Medium | `CloseAsync().GetAwaiter().GetResult()` in timer callback and `KillAllCursorsAsync().GetResult()` in `Dispose` — sync-over-async. Can deadlock when thread pool is exhausted or `SemaphoreSlim.WaitAsync` in cursor requires a free thread. | Open |
| BUG-009 | CursorManager.cs | 203-204,284-285 | Medium | `CreateCursorAsync` and `GetMoreAsync` each call `GetNextBatchAsync` immediately followed by `HasMoreAsync`. `HasMoreAsync` pre-fetches a full batch to determine if more exist. Every cursor operation issues 2 full collection scans instead of 1, compounding PERF-013. | Open |
| CODE-018 | CursorManager.cs | 346 | Low | `AverageCursorLifetimeMs` always returns hardcoded 300,000ms. Formula `(UtcNow - UtcNow.AddMinutes(-10)).TotalMilliseconds / 2` is constant and meaningless. Real cursor lifetimes are never tracked. | Open |
| MEM-007 | CursorManager.cs | 436-441 | Low | Implements `IDisposable` but not `IAsyncDisposable`. `Dispose` blocks on async `KillAllCursorsAsync`. Should implement `IAsyncDisposable.DisposeAsync` and `await` the cleanup. | Open |
| CODE-019 | CursorEnabledQueryExecutor.cs | 55-64 | Medium | `ExecuteAsync` silently switches to cursor mode for limit-only queries. The created cursor is never killed in `ExecuteWithCursorInternalAsync` — leaks in `_cursorManager._cursors` for up to 10 minutes per call. Call `KillCursorAsync` after returning the first batch. | Open |
| BUG-010 | CursorEnabledQueryExecutor.cs | 139 | Medium | `TotalCount = cursorResult.TotalCount ?? cursorResult.Documents.Count` — when `IncludeTotalCount` is false (default), falls back to first-batch size, not the real total. Callers receive incorrect TotalCount values. | Open |
| CODE-020 | CursorEnabledQueryExecutor.cs | 26-33 | Low | Primary constructor hard-codes `new CursorManager(...)` — bypasses DI and couples to concrete implementation. Use the overloaded constructor accepting `ICursorManager` from DI instead. | Open |
| SEC-037 | Program.cs (Host) | 419-458 | High | No per-connection authentication state tracked. `HandleCommandAsync` executes document operations without verifying the connection authenticated first. Any client can skip `Authentication` message and send commands directly. Track auth state per `connectionId` and gate `HandleCommandAsync` on it. | Open |
| ASYNC-005 | Program.cs (Host) | 277 | Medium | `async void OnMessageReceivedAsync` — if `e.SendResponseAsync(errorResponse)` inside the `catch` block throws, the exception propagates unhandled on the thread pool and crashes the process. Wrap the entire handler body in try/catch or swallow/log the secondary failure. | Open |
| BUG-011 | Program.cs (Host) | 599-607 | Medium | `HandleBulkOperationAsync` is a stub — always returns `{ success:true, totalProcessed:0 }` regardless of payload. Bulk operations are silently no-oped. | Open |
| CONC-019 | Program.cs (Host) | 514-522 | Low | `HandleSetCommandAsync`: `ExistsAsync` → `InsertAsync`/`UpdateAsync` is a TOCTOU race. Two concurrent requests for the same ID can both pass `ExistsAsync=false` and both invoke `InsertAsync`. | Open |
| BENCH-001 | DocumentStoreBenchmarks.cs | 87 | Low | `Guid.NewGuid()` called inside `DeleteDocument` benchmark body — allocation overhead included in measurement. Move unique key generation to `[IterationSetup]`. | Open |
| BENCH-002 | BTreeIndexBenchmarks.cs | 95-99 | Low | `DeleteIntKey` creates a new `BTreeIndex` inside the benchmark — index construction cost is measured instead of pure delete performance. | Open |
| BENCH-003 | CacheBenchmarks.cs | 74-79,85-91 | Low | `CacheRemove`/`CacheEvictionUnderPressure` create and dispose `LruCache` inside the benchmark body — object allocation is part of measured time. | Open |
| BENCH-004 | SerializationBenchmarks.cs | 103-117 | Low | Deserialization benchmarks deserialize to `object` (boxed `JsonElement`), not a typed model. `DeserializeMessage` re-serializes inside a deserialization benchmark — both measurements are contaminated. | Open |
| BENCH-005 | QueryEngineBenchmarks.cs | 157 | Low | `FilterEngineComplexMatch` constructs a multi-part filter chain inside the benchmark body — filter construction overhead is included in match measurement. | Open |
| TEST-001 | SecurityPenetrationTests.cs | 388-390 | Low | `Encryption_EmptyCiphertext_ShouldFail` double-invokes `service.Decrypt("")` — `Assert.Throws` re-executes the delegate as a separate call after the left-side expression. Use try/catch instead. | Open |
| TEST-002 | SecurityPenetrationTests.cs | 606 | Low | `Assert.True(failedAuthEvents.Count >= 0, ...)` is vacuously true (any int ≥ 0). Provides zero coverage guarantee for audit logging of failed auth attempts. | Open |
| TEST-003 | LoadTests.cs | 443-459 | Low | `LoadTest_MixedWorkload` simulates Read/Write/Query with `Task.Delay` instead of real store operations. Throughput metrics for these operations are not meaningful for capacity planning. | Open |
| TEST-004 | LoadTests.cs | 53 | Low | `Dispose()` calls `_server?.StopAsync().Wait(TimeSpan.FromSeconds(5))` — sync-over-async in `IDisposable.Dispose`. Should be `DisposeAsync`. | Open |
| ARCH-001 | AdvGenNoSqlServer.Client.csproj | 31-34 | Medium | Client layer is not isolated from server internals. It references the whole `Core` and `Network` assemblies but uses only `Core.Models` (Document, batch models), `MessageProtocol`/`NoSqlMessage` and `TlsStreamHelper.CreateClientSslStreamAsync`. The published client package therefore carries `TcpServer`, `ConnectionHandler`, `P2PServer`, `CertificateReloader`, the authentication/transaction/caching code and the `Microsoft.Extensions.Caching.Memory` dependency, and any server-side change forces a client release. | Open |
| NET-005 | TcpServer.cs | 224-225, 247-265 | High | No effective read or idle timeout. `ReceiveTimeout`/`SendTimeout` only affect synchronous socket calls, and every read here is `_stream.ReadAsync` with only the server shutdown token. A client that sends part of a header and then stalls holds its connection-pool slot forever. The TLS handshake also runs before `_connectionPool.TryAcquire()`, so handshakes in progress are not counted against the connection limit. | Open |
| NET-006 | TcpServer.cs | 351-355 | Medium | `SendConnectionRejectedAsync` sets `Payload` but not `PayloadLength`. `MessageProtocol.Serialize` writes `message.PayloadLength` (0), so the client receives an empty Error frame and never sees "Server at maximum capacity". Root cause: `NoSqlMessage.Payload` and `PayloadLength` are independent settable properties that can disagree. | Open |
| NET-007 | MessageProtocol.cs | 72-98, 326-341 | Medium | `MessageFlags` (`Compressed`, `Encrypted`, `RequireAck`, `EndOfBatch`) are parsed and copied onto the message but no production code reads them, and `ValidateHeader` accepts any flag byte. A frame flagged `Compressed` or `Encrypted` is processed as plain JSON, and `RequireAck` is silently ignored. | Open |
| NET-008 | MessageProtocol.cs | 289-291, 398-422 | Low | CRC32 covers the payload only, so a corrupted type or flags byte that is still a valid value passes validation, and zero-length frames have no integrity check at all. `Deserialize` additionally skips the checksum when the payload is empty and allocates `new byte[payloadLength]` (up to 100MB) before checking that the buffer actually holds that many bytes. | Open |
| NET-009 | TcpServer.cs | 341-344 | Low | On a `ProtocolException` (bad magic, version mismatch, oversized frame, checksum failure) the loop logs and drops the connection without sending an Error frame; `ConnectionHandler.SendErrorAsync` exists but is unused. Version is checked with strict equality and there is no negotiation, so any future protocol version bump is a silent disconnect for old clients. | Open |
| SEC-038 | Program.cs (Server) | 84, 123-196 | Critical | The `AdvGenNoSqlServer.Server` HTTP API binds `http://0.0.0.0:{httpPort}` and maps every endpoint, including `POST`/`DELETE /api/databases/{name}` and `DELETE /api/collections/{name}`, with no `[Authorize]`, no `UseAuthentication` and no `RequireAuthorization`. Anyone who can reach the port can read and destroy data regardless of `RequireAuthentication`. | Open |
| SEC-039 | Program.cs (Host) | 401, 418, 301, 346 | High | HTTP document `POST` and `PUT` carry a bare `[Authorize]`, so a `ReadOnly` user can insert and overwrite documents over HTTP while the same user is refused `set` over TCP. The HTTP matrix also disagrees with `CommandAuthorizer` elsewhere: document delete and collection create are Admin-only over HTTP but `ReadWrite` over TCP. | Open |
| SEC-040 | Program.cs (Host) | 940-949, 1062-1070 | High | With `RequireAuthentication=false` the anonymous identity is stored with `AnonymousRole` (default ReadOnly), but the role check is inside `if (RequireAuthentication && ...)` and never runs. Anonymous TCP clients can run every command including `createuser`, `setrole` and `dropcollection`. The startup warning "anonymous connections get role X" describes behaviour the code does not implement. Same pattern in `NoSqlServer.cs` 278, 332. | Open |
| SEC-041 | Program.cs (Host) | 694, 996 | Medium | `_authConnections` caches `(Username, Role)` for the life of the connection and is only cleared on disconnect. `setrole`, `deleteuser` and `setpassword` do not update or evict it, so a demoted or deleted user keeps the old privileges on open connections indefinitely (and on HTTP until the JWT expires, see SEC-007). | Open |
| SEC-042 | Program.cs (Host) | 301-308 | Medium | `POST /api/databases/{name}/select` assigns `data.DocumentStore` on the singleton `ApiDataService`. Any authenticated user switches the active database for every other HTTP caller, and concurrent requests can read or write the wrong database. | Open |
| ARCH-004 | AuthenticationService.cs, RoleManager.cs | - | Medium | Two authorization stacks exist. Host and Server use `AuthenticationManager` + `UserRole` + `CommandAuthorizer`; `AuthenticationService` and the permission-based `RoleManager` are referenced only by tests and `Example.ConsoleApp`. Policy is therefore defined in three places (CommandAuthorizer map, HTTP attributes, RoleManager defaults) that already disagree, and the gate code is duplicated between `Host/Program.cs` and `Server/NoSqlServer.cs`. | Open |
| AUDIT-002 | Program.cs (Host) | 1064-1069 | Low | `AUTH_REQUIRED` and `FORBIDDEN` results are returned without an audit event, although `IAuditLogger` has an authorization-failure method. Privilege probing leaves no trail. | Open |
| QRY-001 | Program.cs (Host) | 346-363, 675 | High | The query engine is not reachable from either server. Host and Server reference `AdvGenNoSqlServer.Query` but contain no `using` of it; only `AdvGenNoSqlServer.Embedded` uses it. `POST /api/query` ignores `QueryRequest.Query`, returns the first 100 documents of the collection and reports `totalCount` as the page size. Host's TCP dispatch has no query, find or aggregate command; Server's `find_one` re-implements filtering in a private `MatchesFilter` (NoSqlServer.cs 1019) with a full scan instead of using `FilterEngine`/`QueryExecutor`. Indexes, cursors and aggregation are therefore unused over the network. | Open |
| QRY-002 | Program.cs (Host) | 1088-1110 | Medium | The command layer is a string `switch` inside a 1,586-line `Program.cs`, duplicated in the 1,896-line `NoSqlServer.cs`, and the two have drifted: Server dispatches `insert`, `replace`, `upsert`, `find_one`, `touch`, `cluster`; Host does not, although `CommandAuthorizer` maps them. Host rejects `BulkOperation` as `UNSUPPORTED`, so the client's `BatchInsertAsync`/`BatchUpdateAsync`/`BatchDeleteAsync`/`ExecuteBatchAsync`/`BulkInsertAsync` cannot succeed against Host. Authorization, auditing and metrics are woven into the switch by hand. | Open |
| QRY-003 | Program.cs (Host) | HandleListDocumentsCommandAsync | Medium | `listdocuments` loads the entire collection with `GetAllAsync`, materializes it, and sorts by Id on every page request. Paging a collection of N documents costs O(N log N) time and O(N) memory per page, on the request thread, with no upper bound. | Open |
| QRY-004 | Program.cs (Host) | 903-911 | Low | `MessageType.Transaction` (0x08) and `Notification` (0x0A) are part of the documented protocol but have no dispatch arm in Host or Server; they return `UNSUPPORTED_MESSAGE`. The transaction coordinator and MVCC code are not reachable by network clients. | Open |
| TXN-001 | Program.cs (Host) | 587-611 | High | `IWriteAheadLog`, `ILockManager` and `ITransactionCoordinator` are registered as singletons but nothing resolves them: command handlers and HTTP endpoints call `IDocumentStore` directly. `IWriteAheadLog.RecoverAsync` has no caller outside tests, so this WAL is never replayed after a crash. Server writes have no atomicity or isolation beyond what the document store itself provides. | Open |
| TXN-002 | Session.cs | 99-149, 317-350 | High | `Session.SaveChangesAsync` applies deletes, inserts and updates straight to `IDocumentStore`; `TransactionCoordinator` holds no store reference and only writes WAL records and releases locks. `RollbackAsync` after `SaveChangesAsync` therefore undoes nothing, a failure part-way through `SaveChangesAsync` leaves a partial write, and `CommitAsync` applies the data before the coordinator commit can fail. The unit-of-work API promises atomicity it does not deliver. | Open |
| TXN-003 | DocumentVersion.cs, MvccTransactionContext.cs, MvccDocumentStore.cs | 155, 241, 296, 91 | High | Found in the uncommitted P0-1 MVCC change (working tree, 2026-11-13). (a) `VersionChain.GetVisibleVersion` walks past a version whose deletion is visible and returns the older version, so a document that was updated then deleted reappears with stale content. (b) Callers still filter on the raw `IsDeleted` flag, so another transaction's uncommitted delete is observed. (c) `EnsureNoWriteConflictAsync` checks only the latest version's creator, not its deleter, and is a non-atomic check-then-write. (d) A failed commit leaves the transaction's versions in the chains with the record stuck Active, and `RollbackAsync` refuses a Failed transaction. | Open |
| TXN-004 | Core/Transactions/ | - | Medium | Four transaction implementations coexist: `TransactionManager` and `AdvancedTransactionManager` (stubs, DATA-010/012, still injected into `DocumentService` in `Class1.cs`), `TransactionCoordinator` + `TransactionContext` (lock-based, used by `Session`), and `MvccTransactionCoordinator` + `MvccDocumentStore` (referenced only by tests). Embedded has a fifth, separate WAL. None is the defined production path. | Open |
| TXN-005 | WriteAheadLog.cs (Core) | 552, 671, 744 | Medium | In the uncommitted P0-3 change, any CRC mismatch in the current log is treated as a torn tail: recovery stops, truncates the file at that point and reports success. A single corrupted entry in the middle of the log silently discards every later committed transaction. Supersedes the direction suggested in DATA-001. | Open |
| STOR-001 | HybridDocumentStore.cs | 51, 457-468, 508-525 | Critical | Both servers use `HybridDocumentStore` (`DatabaseManager.cs` 54/64/106, `NoSqlServer.cs` 83). It updates the in-memory map, enqueues the disk write on an unbounded channel and returns, so a client receives success before anything is on disk. The background writer uses `File.WriteAllTextAsync` (non-atomic, no fsync), and load-time parse failures are swallowed (lines 100, 451). A crash loses every acknowledged write still in the queue and can leave a torn file that is then silently dropped. The P0-5 atomic writer (`AtomicFileWriter`) was applied to `PersistentDocumentStore` and `FileStorageManager`, which no server uses. | Open |
| STOR-002 | HybridDocumentStore.cs | 336-391, 510-515 | High | `DropCollectionAsync` and `ClearCollectionAsync` delete from disk immediately without draining or invalidating queued writes for that collection. The background writer later recreates the directory (`Directory.CreateDirectory`) and writes the queued documents, so a dropped or cleared collection reappears with stale documents after restart. | Open |
| STOR-003 | HybridDocumentStore.cs | 23, 51, 87 | Medium | `_cache` is the whole database, not a cache: every document of every collection is loaded at `InitializeAsync` and never evicted, one JSON file per document. Memory grows with data size, startup time grows with file count, and the unbounded write channel grows without back-pressure under a write burst. No stated capacity limit. | Open |
| STOR-004 | DatabaseManager.cs | 54-65, 106 | Medium | `DatabaseManager` hard-codes `new HybridDocumentStore(...)` (with sync-over-async `InitializeAsync().GetAwaiter().GetResult()` in the constructor). The storage features built as `IDocumentStore` decorators (TTL, ETag, encryption, full-text, revisions, write concern, change streams, capped, sharding) and `IndexManager` are not composed into the server and no store maintains indexes on write, so those features exist only in tests and examples. | Open |
| STOR-005 | HybridDocumentStore.cs | 396-403, 480-491, 566-576 | Low | `FlushAsync` polls `_pendingWrites` with `Task.Delay(10)` and no timeout, and a failed write still decrements the counter, so flush (and graceful shutdown) completes "successfully" with data that was never persisted. On dispose, writes still queued after 30 seconds are cancelled without a log entry. | Open |
| CACHE-001 | Program.cs (Host) | 501-516 | Medium | `AddMemoryEngine` and `ICacheManager` (`AdvancedMemoryCacheManager`) are registered in Host and Server, but no handler, endpoint or store takes `ICacheManager`; the only consumer is the unused `DocumentService` in `Core/Class1.cs`. The caching layer in the architecture diagram is not on any read or write path. (The store is already fully in memory, see STOR-003, so a JSON-serializing cache in front of it would add cost, not remove it.) | Open |
| CACHE-002 | Program.cs (Host) | 501-508 | Medium | Host hard-codes `MemoryManagementConfiguration` (Managed, 512 MB, 75%, LRU, 1800 s) while Server binds the `MemoryManagement` section. `ServerConfiguration.MaxCacheItemCount`, `MaxCacheSizeInBytes`, `DefaultCacheTtlMilliseconds` are parsed and validated but never passed to the cache, and `EnableObjectPooling` is read by no code. These keys ship in `appsettings.json` and operators can change them with no effect. | Open |
| CACHE-003 | Program.cs (Host) | 501-508, 642 | Medium | There is no overall memory budget. The cache engine has its own 512 MB limit, the document store holds the full dataset with no limit, the write queue is unbounded, and per-connection payload limits are independent of all three. `AddMemoryHealthCheck(null, ...)` runs without a profiler, and `MemoryProfiler`/`MemoryTuner` are not registered in Host, so nothing reacts to memory pressure. | Open |
| CACHE-004 | Core/Caching/, Core/Pooling/ | - | Low | Three cache implementations exist (`MemoryCacheManager` whose `Clear()` throws, `LruCache`, `AdvancedMemoryCacheManager`), and the pooling module (`ObjectPoolManager`, `BufferPool`, `StringBuilderPool`, `ObjectPool<T>`) is referenced only by tests and benchmarks; the network layer rents from `ArrayPool<byte>.Shared` directly. `ICacheManager` keys are a bare string with no database/collection scope. | Open |
| IO-001 | (multiple) | see description | High | Persistent files are written four different ways, most of them not crash-safe. Direct overwrite, no fsync: `HybridDocumentStore.cs` 422/524 (the production store, STOR-001), `AdvancedFileStorageManager.cs` 62 (DATA-022), `Core/Database/DatabaseManager.cs` 356 (database metadata), `Core/Transactions/WriteAheadLog.cs` 469 (checkpoint file; a torn checkpoint is then swallowed, DATA-002). Temp + rename but no fsync: `FileUserStore.cs` 47-48 (user accounts and password hashes), `AttachmentStore.cs` 97-104 and 456-462 (content and metadata as two separate non-atomic steps, so a crash can leave one without the other). Append without fsync: `AuditLogger.cs` 280. Only `PersistentDocumentStore` and `FileStorageManager` use `AtomicFileWriter`. | Open |
| IO-002 | AtomicFileWriter.cs | 11-47 | Medium | (a) The parent directory is not fsynced after `File.Move`; on Linux (the Docker deployment target) the rename itself is not durable until the directory is synced, so a power loss can revert to the old file or lose a newly created one. (b) Temp files are named `<file>.<guid>.tmp` and a crash between write and rename leaves them behind permanently; nothing sweeps them at startup. (c) The class is `internal` to Storage, so Core (`FileUserStore`, WAL checkpoint, database metadata) cannot use it. (d) The async overload opens the stream without `FileOptions.Asynchronous` and calls synchronous `Flush(true)`, blocking a thread-pool thread per write. | Open |
| IO-003 | FileUserStore.cs | 24-38 | Medium | If the users file fails to parse, `Load` copies it aside and returns an empty list. The server then starts with no persisted users instead of refusing to start, so a torn or damaged file silently removes every account and role assignment (and the next `Save` overwrites the original). File permissions are also applied after the rename, leaving a brief window with default permissions on a file of password hashes. | Open |
| IO-004 | HybridDocumentStore.cs, DatabaseManager.cs | 347 | Medium | No lock file or exclusive handle guards the data directory, so two server processes pointed at the same path (an overlapping container restart, or Host and Server started together) both load and write the same files. `DropCollectionAsync` uses recursive `Directory.Delete`, which is not atomic: a crash part-way leaves a collection with a random subset of its documents that loads normally on restart. | Open |
| IO-005 | (storage layer) | - | Low | There is no defined behaviour for disk-full or I/O errors: background write failures are counted and printed to stderr (STOR-005) while the server keeps accepting writes, quarantine failures are swallowed (`PersistentDocumentStore.cs` 425), and no free-space check gates writes. | Open |
| REPO-001 | IDocumentStore.cs | 22-113 | Medium | The repository exposes only key lookups and `GetAllAsync`, which returns an entire collection. There is no filtered, paged or streaming read, so every consumer that needs a subset (`listdocuments`, `find_one`, `/api/query`, cursors PERF-013, the query executor) loads the whole collection and filters in the caller, and an index can never be pushed down into the store. | Open |
| REPO-002 | HybridDocumentStore.cs | 116-143, 172-186 | Medium | The store keeps the caller's `Document` instance on insert (it also mutates the caller's object to set timestamps and version) and returns the same live instance from reads. Any caller that modifies a returned document changes stored state without `UpdateAsync`, without a version bump and without a disk write; concurrent readers see half-applied edits. This is the root cause behind PERF-009. | Open |
| REPO-003 | WriteConcernDocumentStore.cs | 300-312 | Medium | There is no shared decorator base: each of about twelve decorators re-implements all twelve `IDocumentStore` members by hand, so any interface change touches every one. Capabilities are discovered by downcast (`_innerStore is HybridDocumentStore`), which silently stops working as soon as another decorator is placed in between; `HybridDocumentStore` has `FlushAsync` but does not implement `IPersistentDocumentStore`. `PersistentDocumentStore` reaches into `InMemoryDocumentCollection` by reflection (CODE-001). | Open |
| REPO-004 | HybridDocumentStore.cs | - | Low | Every `IDocumentStore` method takes a `CancellationToken`, but `HybridDocumentStore` never observes it (no `ThrowIfCancellationRequested`, not passed to the write queue or file reads). Request timeouts and client disconnects cannot stop a store operation. | Open |
| REPO-005 | Core/Abstractions/IDatabaseManager.cs, Core/Database/, Storage/DatabaseManager.cs | - | Low | Two different `IDatabaseManager` interfaces and two `DatabaseManager` classes share the same simple names in different namespaces, so which one a file gets depends on its `using` lines. `DocumentStoreContractTests` is run against `DocumentStore` and the two Embedded stores only; `HybridDocumentStore`, `PersistentDocumentStore` and the decorators are not held to the contract. | Open |
| FACT-001 | ClientFactory.cs | 13-16 | High | BUG-002 is still present and its impact is a security one: `CreateClient(options)` calls `new AdvGenNoSqlClient(options.ServerAddress)`, which substitutes default options where `UseSsl = false`. A caller that sets `UseSsl = true`, `ClientCertificate` or `SslTargetHost` and uses the factory gets an unencrypted connection with no error, and then sends credentials over it. The existing test `AdvGenNoSqlClientFactory_CreateClientWithOptions_ShouldCreateClient` passes because it only checks that a client is returned. | Open |
| FACT-002 | ClientFactory.cs | 6-27 | Low | The factory is a static class that forwards to a public constructor, so it centralizes nothing: no options validation (CODE-006), no interface return type (ARCH-002), no `IServiceCollection` registration, no named or pooled clients. No production or example code uses it; `AdminClient/Services/TcpAdminService.cs` 31 and all examples construct the client directly. The file also lacks the required copyright header. | Open |
| FACT-003 | Client.cs | 139-151; ClientOptions.cs 17 | Low | The server address exists twice: the constructor parameter and `AdvGenNoSqlClientOptions.ServerAddress`; the constructor ignores the one in options. Parsing uses `Split(':')`, so IPv6 literals such as `[::1]:9090` are mis-parsed, and an unparsable port (`host:abc`) silently becomes 9090 instead of failing. | Open |
| CMD-001 | FilterEngine.cs | 97-115, 182-219 | Medium | `QueryFilter.Conditions` is an untyped `Dictionary<string, object>`, and `FilterEngine` walks it and switches on operator-name strings (`$and`, `$eq`, `$regex`, ...) again for every document. Operators are not objects: adding one means editing the switch, and an unknown or misspelled operator is only reported as `FilterEvaluationException` when the first document is evaluated (and not at all on an empty collection). `QueryParser` has no validation step. Per-document string dispatch and boxing also put a ceiling on scan speed. | Open |
| CMD-002 | Aggregation/ | - | Medium | Stages can only be composed in C# through `AggregationPipelineBuilder`. Nothing maps a stage's `StageType` (`"$match"`, `"$group"`, ...) back to a stage instance from JSON: there is no stage factory or registry, and `QueryParser` does not parse pipelines. A network client therefore has no way to submit an aggregation even after QRY-001 wires the engine in. | Open |
| CMD-003 | Program.cs (Host), NoSqlServer.cs | 1088-1110, 350-377 | Low | At the server boundary the Command pattern is absent: commands are string literals in a `switch`, with authorization, auditing and metrics inlined. Tracked with a concrete design under QRY-002; listed here so section 4.2 has a complete picture. | Open |
| ARCH-002 | Client.cs | 73 | Low | No `IAdvGenNoSqlClient` abstraction. `AdvGenNoSqlClient` is a concrete partial class and the factory returns the concrete type, so consumers (Admin, AdminClient, Examples) cannot substitute or mock the client without a live server. | Open |
| ARCH-003 | AdvGenNoSqlServer.Client.csproj | 14, 20 | Low | Package `Description` and `PackageTags` call the TCP client an "embedded NoSQL database", which now collides with the separate `AdvGenNoSqlServer.Embedded` library and misleads package consumers. | Open |

### Severity Levels
- **Critical**: Security vulnerability, data loss risk, crash
- **High**: Significant bug, performance issue, security concern
- **Medium**: Code quality issue, minor bug, missing documentation
- **Low**: Style issue, optimization opportunity, nice-to-have

---

## Recommendations & Required Actions

### SEC-001: Replace SHA256 with PBKDF2 (Critical)
**File:** `AuthenticationManager.cs` lines 143-155
**Current:** Uses `SHA256.Create()` for password hashing
**Required Action:**
```csharp
// Replace HashPassword method with:
private static string HashPassword(string password, string salt)
{
    var saltBytes = Convert.FromBase64String(salt);
    var passwordBytes = Encoding.UTF8.GetBytes(password);

    // Use PBKDF2 with 100,000+ iterations
    using var pbkdf2 = new Rfc2898DeriveBytes(
        passwordBytes,
        saltBytes,
        iterations: 100000,
        HashAlgorithmName.SHA256);

    var hash = pbkdf2.GetBytes(32);
    return Convert.ToBase64String(hash);
}
```

### SEC-002: Use Constant-Time Comparison (High)
**File:** `AuthenticationManager.cs` line 58
**Current:** `if (hashedPassword != credentials.PasswordHash)`
**Required Action:**
```csharp
// Replace with:
var expected = Convert.FromBase64String(credentials.PasswordHash);
var actual = Convert.FromBase64String(hashedPassword);
if (!CryptographicOperations.FixedTimeEquals(expected, actual))
    return null;
```

### SEC-003: Use Thread-Safe Collections (High)
**File:** `AuthenticationManager.cs` lines 13-14
**Current:** `Dictionary<string, UserCredentials>` and `Dictionary<string, AuthToken>`
**Required Action:**
```csharp
private readonly ConcurrentDictionary<string, UserCredentials> _users = new();
private readonly ConcurrentDictionary<string, AuthToken> _activeSessions = new();
```

### SEC-004: Implement Rate Limiting (Medium)
**File:** `AuthenticationManager.cs`
**Required Action:** Add failed login attempt tracking:
```csharp
private readonly ConcurrentDictionary<string, (int attempts, DateTime lastAttempt)> _failedAttempts = new();
private const int MaxFailedAttempts = 5;
private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

// In Authenticate method, check and track failed attempts
```

### SEC-005: Add Password Complexity Validation (Medium)
**File:** `AuthenticationManager.cs`
**Required Action:** Add password validation in RegisterUser/ChangePassword:
```csharp
private static bool ValidatePasswordComplexity(string password)
{
    if (password.Length < 12) return false;
    if (!password.Any(char.IsUpper)) return false;
    if (!password.Any(char.IsLower)) return false;
    if (!password.Any(char.IsDigit)) return false;
    if (!password.Any(c => !char.IsLetterOrDigit(c))) return false;
    return true;
}
```

### SEC-006: Protect Secret Key in Memory (Medium)
**File:** `JwtTokenProvider.cs` line 19
**Current:** `private readonly string _secretKey;`
**Recommendation:** Consider using `System.Security.SecureString` or clearing the key bytes after use. For production, load keys from secure key vault rather than configuration.

### SEC-007: Implement Token Blacklist (Low)
**File:** `JwtTokenProvider.cs`
**Recommendation:** Add a distributed cache (Redis) or in-memory blacklist to track revoked tokens:
```csharp
private readonly ConcurrentDictionary<string, DateTime> _blacklistedTokens = new();

public void RevokeToken(string jti)
{
    _blacklistedTokens[jti] = DateTime.UtcNow.Add(_defaultExpiration);
}

// In ValidateToken, check: if (_blacklistedTokens.ContainsKey(payload.Jti)) return Failed("Token revoked");
```

### SEC-008: Validate Signature Before Extracting Claims (Low)
**File:** `JwtTokenProvider.cs` lines 208-254
**Recommendation:** ExtractUsername and GetExpirationTime should either:
1. Validate signature first (call ValidateToken), or
2. Be marked internal/private, or
3. Document clearly that they return UNVERIFIED claims

### SEC-009: Log Warning for Auto-Generated Keys (Low)
**File:** `EncryptionService.cs` lines 51-53
**Current:** `// TODO: Log warning that a new key was generated`
**Recommendation:** Either:
1. Log a warning using ILogger, or
2. Throw exception if no key configured and no key store provided, or
3. Auto-persist to key store if available
```csharp
if (_keyStore != null)
{
    _keyStore.StoreKeyAsync(_keyId, _masterKey).GetAwaiter().GetResult();
}
else
{
    // Log warning: "Auto-generated encryption key will be lost on restart. Configure EncryptionKey or provide IKeyStore."
}
```

### SEC-010: Use Structured Return for Derived Key (Low)
**File:** `EncryptionService.cs` lines 265-270
**Recommendation:** Instead of concatenating salt+key, return a named struct:
```csharp
public readonly record struct DerivedKeyResult(byte[] Salt, byte[] Key);

public DerivedKeyResult DeriveKeyFromPassword(string password, ...)
{
    // ...
    return new DerivedKeyResult(salt, key);
}
```

### SEC-011: Use Thread-Safe Collections in RoleManager (High)
**File:** `RoleManager.cs` lines 12-14
**Required Action:**
```csharp
private readonly ConcurrentDictionary<string, Role> _roles = new();
private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _userRoles = new();
// PermissionRegistry should also use ConcurrentDictionary or lock
```

### SEC-012: Add Authorization to Role Management (Medium)
**File:** `RoleManager.cs`
**Required Action:** Role management methods should require authorization:
```csharp
public bool CreateRole(string roleName, string callerUsername, ...)
{
    if (!UserHasPermission(callerUsername, Permissions.RoleManage))
        throw new UnauthorizedAccessException("Role management requires RoleManage permission");
    // ...
}
```

### SEC-013: Persist Role Configuration (Medium)
**File:** `RoleManager.cs`
**Required Action:** Inject `IRoleStore` for persistence:
```csharp
public interface IRoleStore
{
    Task SaveRolesAsync(IEnumerable<Role> roles);
    Task<IEnumerable<Role>> LoadRolesAsync();
    Task SaveUserRolesAsync(string username, IEnumerable<string> roles);
    Task<IEnumerable<string>> LoadUserRolesAsync(string username);
}
```

### SEC-014: Thread-Safe Permission Registration (Low)
**File:** `RoleManager.cs` lines 425-428
**Required Action:** Use lock or ConcurrentDictionary:
```csharp
private readonly ConcurrentDictionary<string, byte> _validPermissions = new();
// Or use ReaderWriterLockSlim for the HashSet
```

### SEC-015: Mask Sensitive Data in Audit Logs (Low)
**File:** `AuditLogger.cs` lines 512-517
**Recommendation:** Add sensitive field filtering before serialization:
```csharp
private static string FormatEventForFile(AuditEvent evt)
{
    // Clone and mask sensitive fields
    var sanitized = new AuditEvent { ... };
    if (!string.IsNullOrEmpty(sanitized.Details))
        sanitized.Details = MaskSensitiveData(sanitized.Details);
    // ...
}
```

### PERF-001: Use Async Disposal Pattern (Low)
**File:** `AuditLogger.cs` line 545
**Recommendation:** Implement `IAsyncDisposable`:
```csharp
public class AuditLogger : IAuditLogger, IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _flushTimer?.Dispose();
        await FlushAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }
}
```

### OPS-001: Add Log Retention Policy (Low)
**File:** `AuditLogger.cs`
**Recommendation:** Add configurable log retention:
```csharp
private readonly int _retentionDays = 90;

private void CleanupOldLogs()
{
    var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
    foreach (var file in Directory.GetFiles(_logDirectory, "audit-*.log"))
    {
        if (File.GetCreationTimeUtc(file) < cutoff)
            File.Delete(file);
    }
}
```

### PERF-002: Use Factory Pattern for Async Initialization (High)
**File:** `WriteAheadLog.cs` line 60
**Current:** `InitializeAsync().GetAwaiter().GetResult();` in constructor
**Required Action:** Use factory pattern:
```csharp
public static async Task<WriteAheadLog> CreateAsync(WalOptions? options = null)
{
    var wal = new WriteAheadLog(options, skipInit: true);
    await wal.InitializeAsync();
    return wal;
}
```

### SEC-016: Add WAL Encryption Option (Medium)
**File:** `WriteAheadLog.cs`
**Recommendation:** Add optional encryption using EncryptionService:
```csharp
private readonly IEncryptionService? _encryption;

private byte[] SerializeEntry(WalLogEntry entry)
{
    var data = SerializeEntryInternal(entry);
    return _encryption?.Encrypt(data) ?? data;
}
```

### DATA-001: Optional Skip-on-Corruption for Recovery (Medium)
**File:** `WriteAheadLog.cs` lines 696-700
**Recommendation:** Add recovery option to skip corrupted entries:
```csharp
public class WalOptions
{
    public bool SkipCorruptedEntries { get; set; } = false;
}
// In ReplayFileAsync:
if (calculatedChecksum != checksum)
{
    if (_options.SkipCorruptedEntries)
    {
        // Log warning and continue
        continue;
    }
    throw new InvalidDataException(...);
}
```

### DATA-002: Log Checkpoint Load Errors (Low)
**File:** `WriteAheadLog.cs` lines 99-101
**Recommendation:** Add logging for checkpoint load failures:
```csharp
catch (Exception ex)
{
    _logger?.LogWarning(ex, "Failed to load checkpoint file, starting fresh");
    LastCheckpoint = null;
}
```

### PERF-003: Remove Sync-over-Async in LockManager (High)
**File:** `LockManager.cs` line 112
**Current:** `return AcquireLockAsync(...).GetAwaiter().GetResult();`
**Required Action:** Either:
1. Remove sync method entirely, or
2. Implement separate sync path without async calls

### CONC-001: Use Thread-Safe Collections Inside ConcurrentDictionary (Medium)
**File:** `LockManager.cs` lines 16, 19, 22
**Current:** `List<LockInfo>`, `HashSet<string>`, `Queue<LockRequest>` inside ConcurrentDictionary
**Required Action:** The inner collections are accessed under `ReaderWriterLockSlim`, which is correct but requires documentation. Alternatively, use immutable collections with atomic swap pattern.

### CONC-002: Implement Atomic Lock Upgrade (Medium)
**File:** `LockManager.cs` lines 451-456
**Current:** Releases lock then reacquires for upgrade
**Required Action:**
```csharp
private async Task<LockResult> WaitForUpgradeAsync(...)
{
    // Don't release - add to upgrade wait queue
    var request = new LockRequest { IsUpgrade = true, ... };
    // Wait until all other shared locks are released
    // Then atomically upgrade without releasing
}
```

### DATA-003: Log Deadlock Detection Errors (Low)
**File:** `LockManager.cs` line 678
**Recommendation:** Add logging for exceptions:
```csharp
catch (Exception ex)
{
    _logger?.LogWarning(ex, "Error during deadlock detection");
}
```

### SEC-017: Disable Development Mode Certificate Bypass in Production (High)
**File:** `TlsStreamHelper.cs` lines 254-262
**Required Action:** Add configuration flag:
```csharp
private static bool _allowDevCertificates = false; // Set via config

if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch)
{
    if (_allowDevCertificates && certificate?.Subject.Contains("localhost") == true)
        return true;
}
return false; // Always reject in production
```

### SEC-018: Remove Exportable Flag from Server Certificates (High)
**File:** `TlsStreamHelper.cs` lines 135-137, 217
**Required Action:** For production, don't use Exportable flag:
```csharp
var flags = isProduction
    ? X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet
    : X509KeyStorageFlags.Exportable; // Dev only
```

### SEC-019: Use ILogger for Security Logging (Medium)
**File:** `TlsStreamHelper.cs` lines 252, 280
**Required Action:** Replace Console.WriteLine with ILogger:
```csharp
private static ILogger? _logger;
public static void SetLogger(ILogger logger) => _logger = logger;
// Replace: Console.WriteLine -> _logger?.LogWarning
```

### SEC-020: Implement Certificate Pinning (Medium)
**File:** `TlsStreamHelper.cs`
**Required Action:** Add pinning support as per plan.md section on TLS:
```csharp
private static HashSet<string>? _pinnedCertificates;
public static void SetPinnedCertificates(IEnumerable<string> thumbprints)
    => _pinnedCertificates = new HashSet<string>(thumbprints, StringComparer.OrdinalIgnoreCase);
```

### SEC-021: Add TLS 1.3 Only Mode (Low)
**File:** `TlsStreamHelper.cs` line 90
**Recommendation:** Add configuration for TLS 1.3-only:
```csharp
var protocols = configuration.Tls13Only
    ? SslProtocols.Tls13
    : SslProtocols.Tls12 | SslProtocols.Tls13;
```

### DATA-006: Log Errors When Loading Documents (High)
**File:** `PersistentDocumentStore.cs` lines 373-377
**Required Action:** Add logging and optionally move corrupted files:
```csharp
catch (JsonException ex)
{
    _logger?.LogError(ex, "Failed to load document from {File}", file);
    // Optionally: Move corrupted file to a .corrupted suffix
    File.Move(file, file + ".corrupted");
}
```

### DATA-007: Use Atomic File Write (Medium)
**File:** `PersistentDocumentStore.cs` line 450
**Required Action:** Write to temp file then rename for atomicity:
```csharp
private async Task SaveDocumentToDiskAsync(string collectionName, Document document)
{
    var documentPath = GetDocumentPath(collectionName, document.Id);
    var tempPath = documentPath + ".tmp";
    var json = JsonSerializer.Serialize(document, _jsonOptions);
    await File.WriteAllTextAsync(tempPath, json);
    File.Move(tempPath, documentPath, overwrite: true); // Atomic on POSIX
}
```

### CODE-001: Avoid Reflection for Loading Documents (Medium)
**File:** `PersistentDocumentStore.cs` lines 462-483
**Required Action:** Add a public or internal method to InMemoryDocumentCollection:
```csharp
// In InMemoryDocumentCollection:
internal void LoadDocument(Document document)
{
    _documents.TryAdd(document.Id, document);
    Interlocked.Increment(ref _documentCount);
}
```

### NET-001: Use ILogger for Error Logging (Medium)
**File:** `TcpServer.cs` lines 176, 216, 286
**Current:** `Console.Error.WriteLine($"Error accepting connection: {ex.Message}")`
**Required Action:** Inject ILogger through constructor:
```csharp
private readonly ILogger<TcpServer>? _logger;

public TcpServer(ServerConfiguration configuration, ILogger<TcpServer>? logger = null)
{
    _logger = logger;
    // ...
}

// Replace Console.Error.WriteLine with:
_logger?.LogWarning(ex, "Error accepting connection");
_logger?.LogError(ex, "SSL handshake failed for connection {ConnectionId}", connectionId);
_logger?.LogError(ex, "Connection error on {ConnectionId}", handler.ConnectionId);
```

### SEC-022: Use Proper JSON Serialization (Medium)
**File:** `TcpServer.cs` line 301
**Current:** `$"{{\"error\":\"{reason}\"}}"`
**Required Action:** Use JsonSerializer for safe JSON construction:
```csharp
using System.Text.Json;

private async Task SendConnectionRejectedAsync(ConnectionHandler handler, string reason)
{
    try
    {
        var errorPayload = JsonSerializer.SerializeToUtf8Bytes(new { error = reason });
        var rejectionMessage = new NoSqlMessage
        {
            MessageType = MessageType.Error,
            Payload = errorPayload
        };
        await handler.SendAsync(rejectionMessage, CancellationToken.None);
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "Failed to send rejection message");
    }
}
```

### SEC-023: Log Exceptions in Error Handlers (Low)
**File:** `TcpServer.cs` lines 304-305, 318-319
**Current:** `catch { /* Best effort */ }`
**Required Action:** Add logging for debugging:
```csharp
catch (Exception ex)
{
    _logger?.LogDebug(ex, "Failed to send connection rejected message");
}
```

### RES-001: Observe Exceptions on Fire-and-Forget Tasks (Low)
**File:** `TcpServer.cs` line 163
**Current:** `_ = HandleConnectionAsync(client, cancellationToken);`
**Required Action:** Add exception observation:
```csharp
_ = HandleConnectionAsync(client, cancellationToken)
    .ContinueWith(t =>
    {
        if (t.IsFaulted)
            _logger?.LogError(t.Exception, "Unhandled exception in connection handler");
    }, TaskContinuationOptions.OnlyOnFaulted);
```

### BUG-001: Fix EnableSslAsync - Stream Not Assigned (High)
**File:** `ConnectionHandler.cs` lines 115-134
**Current:** Creates SslStream but doesn't assign it to `_stream`
**Required Action:** Either remove the method (SSL is handled at TcpServer level) or fix it:
```csharp
// Option 1: Remove the method - SSL is already handled in TcpServer.HandleConnectionAsync
// The ConnectionHandler constructor accepts the SSL stream directly

// Option 2: If method is needed, use field or make stream mutable (not recommended due to threading):
// Note: This class creates _reader/_writer from _stream in constructor, so late SSL upgrade is complex
// Better design: Always handle SSL at connection establishment time (current TcpServer approach)
```

### SEC-024: Use Proper JSON Serialization (Medium)
**File:** `ConnectionHandler.cs` lines 309-310
**Current:** `$"{{\"code\":\"{errorCode}\",\"message\":\"{errorMessage}\"}}"`
**Required Action:** Use JsonSerializer:
```csharp
using System.Text.Json;

public async ValueTask SendErrorAsync(string errorCode, string errorMessage, CancellationToken cancellationToken = default)
{
    var errorPayload = JsonSerializer.SerializeToUtf8Bytes(new { code = errorCode, message = errorMessage });
    var errorMsg = new NoSqlMessage
    {
        MessageType = MessageType.Error,
        Flags = 0,
        Payload = errorPayload,
        PayloadLength = errorPayload.Length
    };
    await SendAsync(errorMsg, cancellationToken);
}
```

### NET-002: Use ILogger (Medium)
**File:** `ConnectionHandler.cs` line 132
**Required Action:** Inject ILogger:
```csharp
private readonly ILogger<ConnectionHandler>? _logger;

public ConnectionHandler(..., ILogger<ConnectionHandler>? logger = null)
{
    _logger = logger;
}

// Replace: Console.Error.WriteLine($"SSL handshake failed: {ex.Message}")
_logger?.LogError(ex, "SSL handshake failed");
```

### PERF-006: Avoid Blocking Wait on Async Method (Medium)
**File:** `ConnectionHandler.cs` line 338
**Current:** `sslStream.ShutdownAsync().Wait(TimeSpan.FromSeconds(5))`
**Required Action:** Use async with timeout:
```csharp
public async ValueTask CloseAsync()
{
    if (_disposed) return;

    try
    {
        if (_stream is SslStream sslStream)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await sslStream.ShutdownAsync().WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug("SSL shutdown timed out");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "SSL shutdown error");
            }
        }
        _client.Close();
    }
    catch (Exception ex)
    {
        _logger?.LogDebug(ex, "Error closing connection");
    }
}
```

### SEC-025 / CODE-002 / MEM-001: Minor Cleanup (Low)
**File:** `ConnectionHandler.cs`
**Required Actions:**
1. SEC-025: Log exceptions in CloseAsync (see PERF-006 fix above)
2. CODE-002: Remove `await Task.CompletedTask` or make method synchronous
3. MEM-001: Remove unused `_reader` and `_writer` fields or use them:
```csharp
// Either remove these unused fields:
// private readonly PipeReader _reader;  // Not used
// private readonly PipeWriter _writer;  // Not used

// Or refactor ReadExactAsync to use Pipelines for better performance
```

### SEC-031: Add Regex Timeout to Prevent ReDoS (Medium)
**File:** `FilterEngine.cs` lines 252-256
**Current:** `Regex.IsMatch(fieldString, regexPattern, RegexOptions.IgnoreCase)` without timeout
**Required Action:** Add timeout to prevent catastrophic backtracking:
```csharp
private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

private bool EvaluateRegexOperator(object? fieldValue, object pattern)
{
    // ... existing validation ...

    try
    {
        return Regex.IsMatch(fieldString, regexPattern,
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);
    }
    catch (RegexMatchTimeoutException)
    {
        throw new FilterEvaluationException("Regex pattern evaluation timed out");
    }
}
```

### NET-004: Await TcpServer.StartAsync (Medium)
**File:** `NoSqlServer.cs` line 71
**Current:** `_tcpServer.StartAsync(cancellationToken);` (not awaited)
**Required Action:**
```csharp
await _tcpServer.StartAsync(cancellationToken);
```

### SEC-029: Improve Authentication (Medium)
**File:** `NoSqlServer.cs` lines 227-230
**Required Actions:**
1. Use the AuthenticationManager class instead of direct comparison
2. Add rate limiting for failed attempts
3. Generate proper tokens (use JwtTokenProvider)
```csharp
// Use existing authentication infrastructure:
private readonly AuthenticationManager _authManager;

// In HandleAuthenticationAsync:
var token = _authManager.Authenticate(username, password);
if (token == null)
    return NoSqlMessage.CreateError("AUTH_FAILED", "Invalid credentials");
return NoSqlMessage.CreateSuccess(new { authenticated = true, token = token.TokenId });
```

### CONC-005: Use Atomic Upsert (Low)
**File:** `NoSqlServer.cs` lines 352-363
**Recommendation:** The check-then-act pattern is race-prone. Either:
1. Use transaction/lock, or
2. Add an UpsertAsync method to document store that's atomic:
```csharp
// In document store:
await _documentStore.UpsertAsync(collection, document);
```

### BUG-002: Pass Full Options to Client Constructor (Medium)
**File:** `ClientFactory.cs` lines 14-15
**Current:** `return new AdvGenNoSqlClient(options.ServerAddress);`
**Required Action:** Pass the full options object:
```csharp
public static AdvGenNoSqlClient CreateClient(AdvGenNoSqlClientOptions options)
{
    return new AdvGenNoSqlClient(options.ServerAddress, options);
}
```

### CODE-006: Add Options Validation (Low)
**File:** `ClientOptions.cs`
**Recommendation:** Add validation method or use data annotations:
```csharp
public void Validate()
{
    if (ConnectionTimeout <= 0)
        throw new ArgumentException("ConnectionTimeout must be positive");
    if (MaxRetryAttempts < 0)
        throw new ArgumentException("MaxRetryAttempts cannot be negative");
    if (RetryDelayMs < 0)
        throw new ArgumentException("RetryDelayMs cannot be negative");
    if (KeepAliveInterval <= TimeSpan.Zero)
        throw new ArgumentException("KeepAliveInterval must be positive");
}
```

### SEC-027: Use JsonSerializer for Authentication (Critical)
**File:** `Client.cs` line 325
**Current:** `$"{{\"username\":\"{username}\",\"password\":\"{password}\"}}"`
**Required Action:** Use JsonSerializer to prevent injection:
```csharp
public async Task<bool> AuthenticateAsync(
    string username,
    string password,
    CancellationToken cancellationToken = default)
{
    EnsureConnected();

    var authPayload = System.Text.Json.JsonSerializer.Serialize(new { username, password });
    var message = NoSqlMessage.Create(MessageType.Authentication, authPayload);
    var response = await SendAndReceiveAsync(message, cancellationToken);

    if (response.MessageType == MessageType.Response)
    {
        var result = ParseResponse(response);
        return result.Success;
    }

    return false;
}
```

### PERF-007: Avoid Sync-over-Async in Dispose (Medium)
**File:** `Client.cs` line 843
**Current:** `DisposeAsync().AsTask().GetAwaiter().GetResult()`
**Recommendation:** Document that callers should prefer DisposeAsync, or implement IDisposable with sync-only cleanup:
```csharp
public void Dispose()
{
    // Sync-only cleanup - don't call async methods
    _keepAliveCts?.Cancel();
    _keepAliveCts?.Dispose();
    _sendLock.Dispose();
    _receiveLock.Dispose();
    CleanupConnection();
}
```

### MEM-003: Use ArrayPool in ReceiveMessageAsync (Low)
**File:** `Client.cs` lines 409, 426-427
**Required Action:** Use ArrayPool for buffers:
```csharp
private async Task<NoSqlMessage> ReceiveMessageAsync(CancellationToken cancellationToken)
{
    var headerBuffer = ArrayPool<byte>.Shared.Rent(MessageHeader.HeaderSize);
    try
    {
        await ReadExactAsync(headerBuffer, MessageHeader.HeaderSize, cancellationToken);
        // ... rest of implementation
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(headerBuffer);
    }
}
```

### SEC-026: Use JsonSerializer in MessageProtocol (Medium)
**File:** `MessageProtocol.cs` lines 167-168, 186
**Required Action:** Replace string interpolation with JsonSerializer:
```csharp
public static NoSqlMessage CreateCommand(string command, string collection, object? document = null)
{
    var payload = new
    {
        command = command,
        collection = collection,
        document = document
    };
    var json = System.Text.Json.JsonSerializer.Serialize(payload);
    return Create(MessageType.Command, json);
}

public static NoSqlMessage CreateError(string errorCode, string errorMessage)
{
    var payload = new { success = false, error = new { code = errorCode, message = errorMessage } };
    return Create(MessageType.Error, System.Text.Json.JsonSerializer.Serialize(payload));
}
```

### DOS-001: Reduce Maximum Payload Size (Medium)
**File:** `MessageProtocol.cs` line 340
**Current:** `header.PayloadLength > 100 * 1024 * 1024` (100MB)
**Required Action:** Make configurable with conservative default:
```csharp
public class MessageProtocol
{
    private readonly int _maxPayloadSize;

    public MessageProtocol(int maxPayloadSize = 10 * 1024 * 1024) // 10MB default
    {
        _maxPayloadSize = maxPayloadSize;
    }

    public bool ValidateHeader(MessageHeader header)
    {
        // ...
        if (header.PayloadLength < 0 || header.PayloadLength > _maxPayloadSize)
            return false;
        // ...
    }
}
```

### MEM-002: Safer Buffer Return Pattern (Low)
**File:** `MessageProtocol.cs` line 296
**Recommendation:** Consider returning a disposable wrapper:
```csharp
public readonly struct RentedBuffer : IDisposable
{
    public byte[] Buffer { get; }
    public int Length { get; }

    public RentedBuffer(byte[] buffer, int length)
    {
        Buffer = buffer;
        Length = length;
    }

    public void Dispose() => ArrayPool<byte>.Shared.Return(Buffer);
}

public RentedBuffer Serialize(NoSqlMessage message)
{
    // ... existing code ...
    return new RentedBuffer(buffer, totalLength);
}
```

### CODE-004: Use ReadOnlySpan in ParseHeader (Low)
**File:** `MessageProtocol.cs` line 406
**Required Action:** Avoid unnecessary allocation:
```csharp
public MessageHeader ParseHeader(ReadOnlySpan<byte> buffer)
{
    if (buffer.Length < MessageHeader.HeaderSize)
        throw new ArgumentException("Buffer too small for header", nameof(buffer));

    return new MessageHeader
    {
        Magic = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(0, 4)),
        Version = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(4, 2)),
        MessageType = (MessageType)buffer[6],
        Flags = (MessageFlags)buffer[7],
        PayloadLength = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(8, 4))
    };
}
```

### RES-003: Implement IDisposable in ConnectionPool (Medium)
**File:** `ConnectionPool.cs` line 16
**Required Action:** Implement IDisposable:
```csharp
public class ConnectionPool : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _semaphore.Dispose();
    }
}
```

### CONC-004: Fix Release Order (Low)
**File:** `ConnectionPool.cs` lines 110-112
**Required Action:** Release semaphore first or use try-catch:
```csharp
public void Release()
{
    // Release semaphore first - if it throws, counters stay consistent
    _semaphore.Release();
    Interlocked.Decrement(ref _activeConnections);
    Interlocked.Increment(ref _totalReleased);
}
```

### CODE-003: Add Async Acquire (Low)
**File:** `ConnectionPool.cs`
**Recommendation:** Add async variant for non-blocking usage:
```csharp
public async Task AcquireAsync(CancellationToken cancellationToken = default)
{
    await _semaphore.WaitAsync(cancellationToken);
    Interlocked.Increment(ref _activeConnections);
    Interlocked.Increment(ref _totalAcquired);
}

public async Task<bool> TryAcquireAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
{
    if (await _semaphore.WaitAsync(timeout, cancellationToken))
    {
        Interlocked.Increment(ref _activeConnections);
        Interlocked.Increment(ref _totalAcquired);
        return true;
    }
    return false;
}
```

### PERF-005: Async Dispose Pattern Note (Low)
**File:** `TcpServer.cs` line 353
**Current:** `DisposeAsync().AsTask().GetAwaiter().GetResult();`
**Note:** This is a common pattern when implementing both IDisposable and IAsyncDisposable. The risk of deadlock exists if Dispose() is called from a synchronous context with a captured SynchronizationContext. Consider:
1. Documenting that DisposeAsync should be preferred
2. Using `ConfigureAwait(false)` throughout the async chain
3. Adding analyzer warning suppression with comment explaining the pattern

### DATA-013: Log Errors When Loading Documents from Disk (High)
**File:** `HybridDocumentStore.cs` lines 98-101, 382-385
**Current:** Silent exception swallowing in document load operations
**Required Action:** Add logging and optionally move corrupted files:
```csharp
catch (JsonException ex)
{
    _logger?.LogError(ex, "Failed to deserialize document {Id} from disk cache", id);
    // Optionally: delete corrupted cache file
    try { File.Delete(filePath); } catch { }
}
catch (Exception ex)
{
    _logger?.LogError(ex, "Failed to load document {Id} from disk", id);
}
```

### DATA-014: Use Atomic File Write Pattern (Medium)
**File:** `HybridDocumentStore.cs` lines 442-443
**Current:** `File.WriteAllTextAsync(filePath, json)` - not crash-safe
**Required Action:** Write to temp file then atomic rename:
```csharp
private async Task SaveDocumentToDiskAsync(string collection, string id, BsonDocument document)
{
    var filePath = GetFilePath(collection, id);
    var tempPath = filePath + ".tmp";
    var json = document.ToJson();

    // Write to temp file first
    await File.WriteAllTextAsync(tempPath, json);

    // Atomic rename (overwrites existing)
    File.Move(tempPath, filePath, overwrite: true);
}
```

### DATA-015: Use Atomic Insert Pattern (Medium)
**File:** `HybridDocumentStore.cs` lines 122-136
**Current:** Check-then-act pattern with `ContainsKey` followed by `TryAdd`
**Required Action:** Use single atomic operation:
```csharp
public async Task<bool> InsertAsync(string collection, BsonDocument document)
{
    var id = document.GetId();
    var documents = GetOrCreateCollection(collection);

    // Single atomic operation - returns false if key exists
    if (!documents.TryAdd(id, document))
    {
        throw new DocumentExistsException($"Document with ID '{id}' already exists");
    }

    // Queue for disk persistence
    await _writeChannel.Writer.WriteAsync(new WriteOperation(collection, id, document));
    return true;
}
```

### SEC-033: Log Write Queue Failures (Low)
**File:** `HybridDocumentStore.cs` lines 411-414
**Current:** Silent exception swallowing in write queue processor
**Required Action:** Add logging for write failures:
```csharp
catch (Exception ex)
{
    _logger?.LogError(ex, "Failed to persist document {Collection}/{Id} to disk",
        operation.Collection, operation.DocumentId);
    // Optionally: implement retry with exponential backoff
}
```

### BUG-003: Implement GetUsernameFromToken (High)
**File:** `AuthenticationService.cs` lines 71-76
**Current:** `return null;` - method is non-functional
**Required Action:** Implement token-to-user mapping:
```csharp
public string? GetUsernameFromToken(string tokenId)
{
    // Get the token from AuthenticationManager
    var token = _authManager.GetToken(tokenId);
    return token?.Username;
}

// Add to AuthenticationManager:
public AuthToken? GetToken(string tokenId)
{
    _activeSessions.TryGetValue(tokenId, out var token);
    return token;
}
```

### SEC-034: Implement Actual Permission Check in Authorize (High)
**File:** `AuthenticationService.cs` lines 182-200
**Current:** Always returns `Success()` after token validation - no permission check
**Required Action:** Actually check permissions:
```csharp
public AuthorizationResult Authorize(string tokenId, string requiredPermission)
{
    // First validate the token
    if (!_authManager.ValidateToken(tokenId))
    {
        return AuthorizationResult.Failed("Invalid or expired token");
    }

    // Get username from token
    var username = GetUsernameFromToken(tokenId);
    if (string.IsNullOrEmpty(username))
    {
        return AuthorizationResult.Failed("Cannot identify user from token");
    }

    // If auth not required, skip permission check
    if (!_configuration.RequireAuthentication)
    {
        return AuthorizationResult.Success();
    }

    // Check if user has the required permission
    if (!_roleManager.UserHasPermission(username, requiredPermission))
    {
        return AuthorizationResult.Failed($"User lacks required permission: {requiredPermission}");
    }

    return AuthorizationResult.Success();
}
```

### AUDIT-001: Use IAuditLogger for Authentication Logging (Medium)
**File:** `AuthenticationService.cs` line 226
**Current:** `Console.WriteLine($"[AUTH] ...")`
**Required Action:** Inject and use IAuditLogger:
```csharp
private readonly IAuditLogger? _auditLogger;

public AuthenticationService(ServerConfiguration configuration, IAuditLogger? auditLogger = null)
{
    _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    _authManager = new AuthenticationManager(configuration);
    _roleManager = new RoleManager();
    _auditLogger = auditLogger;
}

public void LogAuthenticationAttempt(string username, bool success, string? clientIp = null)
{
    var eventType = success ? AuditEventType.AuthenticationSuccess : AuditEventType.AuthenticationFailure;
    _auditLogger?.LogAsync(eventType, username, new { clientIp });
}
```

### CONC-007: Make RegisterUser Atomic (Medium)
**File:** `AuthenticationService.cs` lines 32-42
**Current:** User registration and role assignment are separate operations
**Required Action:** Either use transaction or cleanup on failure:
```csharp
public bool RegisterUser(string username, string password, string? initialRole = null)
{
    if (!_authManager.RegisterUser(username, password))
        return false;

    // Assign role (default to User)
    var roleToAssign = !string.IsNullOrEmpty(initialRole) && _roleManager.RoleExists(initialRole)
        ? initialRole
        : RoleNames.User;

    if (!_roleManager.AssignRoleToUser(username, roleToAssign))
    {
        // Rollback: remove user since role assignment failed
        _authManager.RemoveUser(username);
        return false;
    }

    return true;
}
```

### CODE-011: Log Warning When Initial Role Not Found (Low)
**File:** `AuthenticationService.cs` lines 36-42
**Current:** Silent fallback to default role when specified role doesn't exist
**Recommendation:** Log a warning:
```csharp
if (!string.IsNullOrEmpty(initialRole))
{
    if (_roleManager.RoleExists(initialRole))
    {
        _roleManager.AssignRoleToUser(username, initialRole);
    }
    else
    {
        _logger?.LogWarning("Requested role '{Role}' does not exist for user '{User}', assigning default", initialRole, username);
        _roleManager.AssignRoleToUser(username, RoleNames.User);
    }
}
```

### API-001/API-002: Add Async Interface Consistency (Low)
**File:** `IAuditLogger.cs`
**Current:** Only `LogAsync` exists, specialized methods are sync-only. Query methods have no async variants.
**Recommendation:** For interface consistency, either:
1. Add async variants of specialized methods:
```csharp
Task LogAuthenticationAsync(string username, string? ipAddress = null, string? details = null, CancellationToken cancellationToken = default);
Task LogAuthenticationFailureAsync(string username, string? ipAddress = null, string? reason = null, CancellationToken cancellationToken = default);
// ... etc for other Log* methods

// Async query methods for I/O operations
Task<IReadOnlyList<AuditEvent>> GetRecentEventsAsync(int count = 100, CancellationToken cancellationToken = default);
Task<IReadOnlyList<AuditEvent>> GetEventsByUserAsync(string username, int count = 100, CancellationToken cancellationToken = default);
Task<IReadOnlyList<AuditEvent>> GetEventsByTypeAsync(AuditEventType eventType, int count = 100, CancellationToken cancellationToken = default);
```
2. Or document that specialized methods delegate to buffered async queue internally

### BUG-004: Implement Clear() or Change Interface (Medium)
**File:** `MemoryCacheManager.cs` lines 33-38
**Current:** `Clear()` throws `NotImplementedException`, breaking ICacheManager contract
**Required Action:** Either:
1. Track keys and implement Clear:
```csharp
private readonly ConcurrentDictionary<string, byte> _keys = new();

public void Set(string key, Document document, int expirationMinutes = 30)
{
    _keys.TryAdd(key, 0);
    var options = new MemoryCacheEntryOptions()
        .SetSlidingExpiration(TimeSpan.FromMinutes(expirationMinutes))
        .RegisterPostEvictionCallback((key, value, reason, state) =>
        {
            _keys.TryRemove((string)key, out _);
        });
    _cache.Set(key, document, options);
}

public void Clear()
{
    foreach (var key in _keys.Keys)
    {
        _cache.Remove(key);
    }
    _keys.Clear();
}
```
2. Or remove `Clear()` from `ICacheManager` interface if not needed

### ARCH-001: Isolate the Client Layer from Server Internals (Medium)
**Section:** 4.1 Client Application Layer isolation
**File:** `AdvGenNoSqlServer.Client/AdvGenNoSqlServer.Client.csproj` lines 31-34
**Current:** The dependency direction is correct (Client → Core + Network, nothing server-side → Client), but the client consumes only a thin slice of those assemblies: `Core.Models` (`Document`, `BatchOperation*`), `MessageProtocol`/`NoSqlMessage`/`MessageType`, and the client half of `TlsStreamHelper`. Everything else in Core and Network (TCP server, connection pool, P2P clustering, certificate reload, authentication, transactions, caching) ships to every client consumer.
**Recommendation:** Split the shared contract out of the server assemblies so the client depends on contracts only.
**Required Actions:**
1. Create `AdvGenNoSqlServer.Protocol` (no project references) containing `MessageProtocol`, `NoSqlMessage`, `MessageHeader`, `MessageType`, `MessageFlags`, `ProtocolException`, the client-side TLS helper and pinning types, and the wire models `Document` / `BatchOperation*`.
2. Reference `Protocol` from `Client`, `Network` and `Core`; remove the `Core` and `Network` project references from `Client`.
3. Keep the existing namespaces, or add `[TypeForwardedTo]` in Core/Network, so current consumers compile unchanged.
4. Add an architecture test that fails if the Client assembly references anything other than `Protocol`:
```csharp
[Fact]
public void ClientAssembly_ReferencesOnlyProtocol_NoServerAssemblies()
{
    var refs = typeof(AdvGenNoSqlClient).Assembly.GetReferencedAssemblies()
        .Select(a => a.Name!)
        .Where(n => n.StartsWith("AdvGenNoSqlServer."));
    Assert.Equal(new[] { "AdvGenNoSqlServer.Protocol" }, refs);
}
```

### ARCH-002: Add a Client Interface (Low)
**File:** `AdvGenNoSqlServer.Client/Client.cs` line 73, `ClientFactory.cs`
**Current:** `public partial class AdvGenNoSqlClient : IDisposable, IAsyncDisposable` with no interface; the factory returns the concrete class.
**Required Actions:**
1. Extract `IAdvGenNoSqlClient : IAsyncDisposable` covering connect/disconnect, authentication, CRUD, batch, user and attachment operations.
2. Have `AdvGenNoSqlClientFactory.CreateClient` return the interface, and inject the interface in Admin/AdminClient so their logic can be unit-tested without a server.

### ARCH-003: Correct Client Package Metadata (Low)
**File:** `AdvGenNoSqlServer.Client/AdvGenNoSqlServer.Client.csproj` lines 14, 20
**Required Action:** Describe the package as the TCP client, and drop the `embedded` tag:
```xml
<Description>TCP client library for AdvGen NoSQL Server</Description>
<PackageTags>nosql;database;client;tcp;document-database</PackageTags>
```

### SEC-038: Authenticate the Server Project's HTTP API (Critical)
**Section:** 4.1 Security & Authentication Layer integration
**File:** `AdvGenNoSqlServer.Server/Program.cs` lines 84, 123-196
**Current:** All endpoints are anonymous and the listener binds all interfaces.
**Required Actions:**
1. Decide whether `AdvGenNoSqlServer.Server` is still a supported entry point. If `Host` has replaced it, delete its HTTP API (or the project) rather than maintain a second, unprotected copy.
2. If it stays: register the same JWT bearer setup as Host, call `app.UseAuthentication(); app.UseAuthorization();`, and make authorization the default so a new endpoint cannot be added unprotected:
```csharp
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
```
3. Bind to `127.0.0.1` unless an explicit `HttpBindAddress` is configured.
4. Add an integration test that enumerates every mapped endpoint and asserts 401 without a token (health endpoints excepted).

### SEC-039: One Authorization Matrix for HTTP and TCP (High)
**File:** `AdvGenNoSqlServer.Host/Program.cs` lines 401, 418 (and 267-434 generally)
**Required Actions:**
1. Define named policies that mirror `CommandAccess` and apply them instead of ad-hoc attributes:
```csharp
services.AddAuthorization(o =>
{
    o.AddPolicy("Read",  p => p.RequireRole(UserRole.Admin, UserRole.ReadWrite, UserRole.ReadOnly));
    o.AddPolicy("Write", p => p.RequireRole(UserRole.Admin, UserRole.ReadWrite));
    o.AddPolicy("Admin", p => p.RequireRole(UserRole.Admin));
    o.FallbackPolicy = o.GetPolicy("Admin"); // unmapped endpoint = Admin only, same as CommandAuthorizer
});
```
2. Document `POST`/`PUT` → `Write`; align document delete and collection create with the TCP mapping (`Write`), or change `CommandAuthorizer` if Admin-only is the intended rule. Record the decision in one table in `Documentation/`.
3. Add a test per role that runs the same operation over HTTP and TCP and asserts the same allow/deny result.

### SEC-040: Enforce the Anonymous Role When Authentication Is Disabled (High)
**File:** `AdvGenNoSqlServer.Host/Program.cs` lines 940-949, 1062-1070; `AdvGenNoSqlServer.Server/NoSqlServer.cs` lines 278, 332
**Current:** Role check is skipped entirely when `RequireAuthentication` is false.
**Required Action:** Always resolve an identity and always authorize; only the source of the identity depends on the setting:
```csharp
if (!_authConnections.TryGetValue(connectionId, out var identity))
{
    if (config.RequireAuthentication)
        return NoSqlMessage.CreateError("AUTH_REQUIRED", "Authenticate before sending commands");
    identity = ("anonymous", UserRole.IsValid(config.AnonymousRole) ? config.AnonymousRole : UserRole.ReadOnly);
}
if (!CommandAuthorizer.IsAllowed(command, identity.Role))
    return NoSqlMessage.CreateError("FORBIDDEN", $"Role '{identity.Role}' may not run '{command}'");
```
Add tests: with `RequireAuthentication=false` and the default `AnonymousRole`, `get` succeeds and `set`, `createuser` and `dropcollection` return `FORBIDDEN`.

### SEC-041: Propagate Role and Account Changes to Live Sessions (Medium)
**File:** `AdvGenNoSqlServer.Host/Program.cs` lines 694, 996
**Required Actions:**
1. Cache only the username per connection and resolve the role from `AuthenticationManager` on each command (a dictionary lookup), or
2. Have `setrole`, `deleteuser` and `setpassword` evict matching entries from `_authConnections` and close those connections.
3. Pair with SEC-007 (token revocation) so the HTTP path has the same guarantee.

### SEC-042: Make Database Selection Per-Request (Medium)
**File:** `AdvGenNoSqlServer.Host/Program.cs` lines 301-308
**Required Action:** Remove the mutable `ApiDataService.DocumentStore` selection. Carry the database in the route (`/api/databases/{db}/collections/...`) or an `X-Database` header and resolve the store per request via `DatabaseManager.GetDatabase(db)`; keep the existing routes as aliases for the default database.

### ARCH-004: Consolidate the Authorization Stack (Medium)
**File:** `Core/Authentication/AuthenticationService.cs`, `RoleManager.cs`, `CommandAuthorizer.cs`
**Recommendation:** Keep one model. The role-based `UserRole` + `CommandAuthorizer` model is the one in production use.
**Required Actions:**
1. Either delete `AuthenticationService`/`RoleManager` (and close SEC-011..014, SEC-034, BUG-003, CONC-007 as obsolete), or wire `RoleManager` in as the single policy source and derive `CommandAuthorizer` and the HTTP policies from it. Do not keep both.
2. Extract the duplicated authenticate/authorize/audit code from `Host/Program.cs` and `Server/NoSqlServer.cs` into one `ConnectionAuthorizationGate` class in Core that both call.

### AUDIT-002: Audit Authorization Denials (Low)
**File:** `AdvGenNoSqlServer.Host/Program.cs` lines 1064-1069
**Required Action:** Call `_auditLogger.LogAuthorizationFailure(username, command, ...)` before returning `AUTH_REQUIRED` or `FORBIDDEN`, and add the equivalent `OnForbidden`/`OnChallenge` JWT bearer events for HTTP.

### QRY-001: Wire the Query Engine into the Server (High)
**Section:** 4.1 Query Processing & Command Layer design
**File:** `AdvGenNoSqlServer.Host/Program.cs` lines 346-363, 675; `AdvGenNoSqlServer.Server/NoSqlServer.cs` line 1019
**Current:** `/api/query` returns `GetAllAsync(...).Take(100)` and ignores the query; no TCP query command exists in Host.
**Required Actions:**
1. Register `IQueryParser`, `IFilterEngine`, `IQueryExecutor` (and `IndexManager`) in DI, the same way `AdvGenDatabase` composes them for Embedded.
2. Replace the `/api/query` body with a real execution, and return 400 on a parse error instead of an unfiltered result:
```csharp
app.MapPost("/api/query", async (QueryRequest req, IQueryParser parser, IQueryExecutor executor, CancellationToken ct) =>
{
    if (!parser.TryParse(req.Query, out var query, out var error))
        return Results.BadRequest(new { success = false, error });
    query.Collection ??= req.Collection;
    var result = await executor.ExecuteAsync(query, ct);
    return Results.Ok(new { success = true, documents = result.Documents, totalCount = result.TotalCount });
}).RequireAuthorization("Read");
```
3. Add TCP commands `find`, `find_one`, `count` (with filter) and `aggregate` that delegate to the same executor; map them in `CommandAuthorizer` as `Read`. Delete the private `MatchesFilter` in `NoSqlServer.cs`.
4. Apply server-side limits on every query: maximum result size, maximum execution time via `CancellationToken`, and the regex timeout from SEC-031.
5. Add integration tests over HTTP and TCP: a filter that matches a subset returns only that subset and the correct total.

### QRY-002: Replace the Command Switch with Registered Handlers (Medium)
**File:** `AdvGenNoSqlServer.Host/Program.cs` lines 1088-1110; `AdvGenNoSqlServer.Server/NoSqlServer.cs` lines 350-377
**Recommendation:** One handler per command, registered once, shared by both hosts. This is also the Command pattern item in section 4.2.
**Required Actions:**
```csharp
public interface ICommandHandler
{
    string Name { get; }               // "get", "set", ...
    CommandAccess RequiredAccess { get; }
    bool IsMutating { get; }           // drives auditing
    Task<NoSqlMessage> HandleAsync(CommandContext context, JsonElement command, CancellationToken ct);
}
```
1. Move each `Handle*Command` method into its own `ICommandHandler` in a shared assembly; build a `CommandDispatcher` that looks up the handler by name and applies authorization (`RequiredAccess`), auditing (`IsMutating`) and metrics in one place.
2. Derive `CommandAuthorizer`'s map from the registered handlers so a command cannot be authorized without existing, or exist without an access level.
3. Have both `Host` and `Server` use the dispatcher, then decide whether `Server` is still needed (see SEC-038).
4. Either implement `BulkOperation` in the dispatcher or remove the batch methods from the client; today they are a client API that always fails against Host.

### QRY-003: Page listdocuments Without Loading the Collection (Medium)
**File:** `AdvGenNoSqlServer.Host/Program.cs` `HandleListDocumentsCommandAsync`
**Required Actions:**
1. Add a paged, ordered read to `IDocumentStore` (for example `GetPageAsync(collection, afterId, take)`) backed by the primary-key order, and use `CountAsync` for `total`.
2. Prefer keyset paging (`afterId`) over `skip`; keep `skip` only as a compatibility parameter with a documented maximum.
3. Until then, cap the collection size for which `listdocuments` is allowed and return a clear error above it.

### QRY-004: Handle or Remove the Transaction Message Type (Low)
**File:** `AdvGenNoSqlServer.Host/Program.cs` lines 903-911
**Required Action:** Either implement `MessageType.Transaction` (begin/commit/rollback bound to the connection, rolled back on disconnect) once the MVCC issues are fixed, or mark 0x08 and 0x0A as reserved in `AGENTS.md` and the client documentation so users do not expect network transactions.

### TXN-001: Put Server Writes Behind the Transaction Layer (High)
**Section:** 4.1 Transaction Management Layer implementation
**File:** `AdvGenNoSqlServer.Host/Program.cs` lines 587-611
**Current:** WAL, lock manager and coordinator are constructed by DI and never used.
**Required Actions:**
1. Choose the single production transaction path first (TXN-004). Do not wire two.
2. Introduce a transactional store decorator registered as the server's `IDocumentStore`, so every existing handler becomes an auto-commit transaction without code changes:
```csharp
public sealed class TransactionalDocumentStore : IDocumentStore
{
    public async Task<Document> InsertAsync(string collection, Document document)
    {
        await using var tx = await _coordinator.BeginTransactionAsync();
        var result = await tx.InsertAsync(collection, document); // logs to WAL, takes locks
        await tx.CommitAsync();                                  // durable before returning
        return result;
    }
    // Update/Delete likewise; reads go through the snapshot
}
```
3. Call `RecoverAsync` during startup, before the TCP and HTTP listeners accept traffic, and fail readiness (`/health/ready`) until it completes. Log the number of redone and discarded transactions.
4. If the decision is instead that the server relies only on `PersistentDocumentStore`'s atomic file writes, remove the three unused registrations and state that guarantee in `Documentation/ProductionReadiness.md`.
5. Add a crash test: kill the process between WAL append and store apply, restart, and assert the committed write is present and an uncommitted one is absent.

### TXN-002: Make Session Writes Transactional (High)
**File:** `AdvGenNoSqlServer.Core/Sessions/Session.cs` lines 99-149, 317-350
**Required Actions:**
1. Route `SaveChangesAsync` through the transaction context instead of `_documentStore`, so changes are buffered (or versioned) until commit:
```csharp
var tx = _transactionCoordinator.GetTransaction(_currentTransactionId!);
foreach (var added in _changeTracker.Added)
    await tx.RecordInsertAsync(added.CollectionName, added.Entity);   // WAL + write set, not the store
// the coordinator applies the write set to the store only on commit
```
2. Give the coordinator the store (or an `IMvccStore`) so commit applies the write set and rollback discards it.
3. Until that is done, change the XML docs on `ISession.RollbackAsync` to state that saved changes are not undone, and make `TryCommitAsync` stop swallowing the exception type.
4. Add tests: insert + `SaveChangesAsync` + `RollbackAsync` leaves the document absent; a failure on the second of three saves leaves none applied.

### TXN-003: Fix MVCC Visibility and Conflict Handling Before Merging P0-1 (High)
**File:** `DocumentVersion.cs` line 155; `MvccDocumentStore.cs` lines 91, 226; `MvccTransactionContext.cs` lines 241, 296, 439
**Required Actions:**
1. Stop the chain walk at a version whose deletion is visible:
```csharp
while (current != null)
{
    if (current.IsCreationVisible(snapshot, resolve))
        return current.IsDeletionVisible(snapshot, resolve) ? null : current;
    current = current.PreviousVersion;
}
return null;
```
   (split `IsVisibleToSnapshot` into creation and deletion visibility so the two cases are distinguishable).
2. Remove every remaining `IsDeleted` filter from callers; visibility must come only from the snapshot check.
3. In the conflict check, treat an uncommitted or post-snapshot `DeletedByTransactionId` as a conflict, and perform check + version install under the chain lock (a `TryAddVersion(expectedLatest, newVersion)` compare-and-swap).
4. On commit failure run the undo log and mark the record Aborted; allow `RollbackAsync` from the Failed state; replace the fire-and-forget rollback in `Dispose` with `DisposeAsync`.
5. Add tests for: insert → update → delete → read returns null; reader does not see an uncommitted delete; delete vs. concurrent update raises a conflict; documents are writable again after a failed commit.

### TXN-004: Reduce to One Transaction Implementation (Medium)
**File:** `AdvGenNoSqlServer.Core/Transactions/`
**Required Actions:**
1. Delete `TransactionManager`, `AdvancedTransactionManager`, `ITransactionManager` and `DocumentService` in `Class1.cs` (closes DATA-010, DATA-011, DATA-012, MEM-004, MEM-005, CONC-006, API-005).
2. Decide between the lock-based coordinator and MVCC. Recommendation: MVCC once TXN-003 is fixed, with the lock manager kept only for write-write ordering; mark the other `[Obsolete]` and remove it in the following release.
3. Record the decision and the resulting isolation guarantees in `Documentation/`.

### TXN-005: Distinguish a Torn Tail from Mid-Log Corruption (Medium)
**File:** `AdvGenNoSqlServer.Core/Transactions/WriteAheadLog.cs` lines 552, 671, 744
**Required Actions:**
1. Treat a bad entry as a torn tail only if nothing valid follows it: after a CRC or length failure, scan forward for another entry with a valid header and CRC. If one exists, this is corruption, not a torn write.
2. On corruption, fail recovery with `InvalidDataException` by default; never truncate. Offer an explicit `WalOptions.CorruptionPolicy` (`Fail`, `SalvageAndQuarantine`) for operators, where salvage copies the damaged file aside before continuing.
3. Do not continue into the next archive file after stopping mid-file; that replays a log with a gap.
4. Add tests: flip one bit in a middle entry and assert recovery fails and the file length is unchanged; truncate the last entry and assert recovery succeeds and trims only that entry.

### STOR-001: Make the Production Store Durable (Critical)
**Section:** 4.1 Storage Engine Layer architecture
**File:** `AdvGenNoSqlServer.Storage/HybridDocumentStore.cs` lines 51, 100, 422, 451, 457-468, 508-525
**Current:** Write-behind with acknowledgement before persistence; non-atomic file writes; corrupted files silently ignored.
**Required Actions:**
1. Replace all three `File.WriteAllTextAsync` calls (lines 422, 524 and any in init) with `AtomicFileWriter.WriteAllTextAsync` (temp file + fsync + rename), as already done for `PersistentDocumentStore`.
2. Make the acknowledgement honest. Carry a completion source on each queued operation and await it before returning from `InsertAsync`/`UpdateAsync`/`DeleteAsync`:
```csharp
private sealed class WriteOperation
{
    public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // Type, CollectionName, Document ...
}

// writer loop
try { await ProcessWriteOperationAsync(op); op.Completed.SetResult(); }
catch (Exception ex) { op.Completed.SetException(ex); /* count + log */ }

// caller
await QueueWriteAsync(op);
if (_durability == Durability.Synchronous) await op.Completed.Task;
```
   Default to `Synchronous`; keep write-behind only as an explicit, documented `Durability.Relaxed` option. The writer may batch several operations per fsync to recover throughput.
3. On a failed synchronous write, roll the in-memory map back to the previous value and surface the exception to the client.
4. On load, quarantine unreadable files (`.corrupt` suffix) and log an error, as done in `PersistentDocumentStore` (closes DATA-013).
5. Alternatively, put the WAL in front of the store (TXN-001) and keep the file writes asynchronous; either way an acknowledged write must survive a crash.
6. Add a test that kills the store after `InsertAsync` returns (without dispose), reopens it, and asserts the document is present.

### STOR-002: Order Drop/Clear with the Write Queue (High)
**File:** `AdvGenNoSqlServer.Storage/HybridDocumentStore.cs` lines 336-391, 510-515
**Required Actions:**
1. Send drop and clear through the same queue as `WriteOperationType.DropCollection` / `ClearCollection`, so they are applied in order after earlier writes and before later ones, and await their completion.
2. Do not call `Directory.CreateDirectory` in the writer for a collection that no longer exists in `_cache`; skip the write instead.
3. Add a test: enqueue 1,000 inserts, drop the collection immediately, flush, reopen the store, and assert the collection is absent.

### STOR-003: State and Enforce the In-Memory Capacity Model (Medium)
**File:** `AdvGenNoSqlServer.Storage/HybridDocumentStore.cs` lines 23, 51, 87
**Required Actions:**
1. Rename `_cache` to reflect that it is the primary copy, and document the limit in `Documentation/Deployment.md`: dataset must fit in memory.
2. Use a bounded channel (`BoundedChannelOptions { FullMode = Wait }`) so writers get back-pressure instead of unbounded memory growth.
3. Add a configurable maximum document count/bytes with a clear error when exceeded, and expose current usage on `/metrics`.
4. Longer term, move the server onto the page-based `AdvGenNoSqlServer.Embedded` storage engine (WAL + pages + compaction) rather than maintaining two engines.

### STOR-004: Compose the Storage Pipeline in One Place (Medium)
**File:** `AdvGenNoSqlServer.Storage/DatabaseManager.cs` lines 54-65, 106
**Required Actions:**
1. Inject a store factory instead of constructing the store inline:
```csharp
public DatabaseManager(string basePath, Func<string, Task<IDocumentStore>> storeFactory)
```
   and build the decorator chain there from configuration (for example base store → write concern → TTL → ETag → change streams).
2. Move initialization out of the constructor into `InitializeAsync`, called from host startup (removes the sync-over-async calls).
3. Add index maintenance to the write path (a decorator that calls `IndexManager` on insert/update/delete) so the query engine wired in by QRY-001 has indexes to use.
4. For each decorator not included in the server pipeline, either add it behind a configuration switch or mark it as library-only in the documentation.

### STOR-005: Make Flush Truthful and Bounded (Low)
**File:** `AdvGenNoSqlServer.Storage/HybridDocumentStore.cs` lines 396-403, 480-491, 566-576
**Required Actions:**
1. Implement `FlushAsync(CancellationToken)` by enqueuing a marker operation and awaiting its completion instead of polling.
2. Throw (or return a result) from `FlushAsync` when `WriteFailureCount` increased since the previous flush, and fail `/health/ready` while it is non-zero.
3. Log the number of abandoned writes when dispose times out, and replace `Console.Error.WriteLine` with `ILogger`.

### CACHE-001: Decide the Cache's Role, Then Wire or Remove It (Medium)
**Section:** 4.1 Caching & Memory Management integration
**File:** `AdvGenNoSqlServer.Host/Program.cs` lines 501-516; `AdvGenNoSqlServer.Server/Program.cs` lines 215-224
**Recommendation:** With the current in-memory `HybridDocumentStore`, a document cache has nothing to accelerate. Remove the registrations now; reintroduce a cache only when the store becomes disk-backed (STOR-003 item 4).
**Required Actions:**
1. Remove `AddMemoryEngine` and the `ICacheManager` registration from both hosts, and delete `DocumentService` in `Core/Class1.cs`.
2. When a disk-backed store is adopted, add the cache as an `IDocumentStore` decorator so every path uses it and invalidation lives in one place:
```csharp
public sealed class CachedDocumentStore : IDocumentStore
{
    private static string Key(string db, string collection, string id) => $"{db}\u001f{collection}\u001f{id}";

    public async Task<Document?> GetAsync(string collection, string id, CancellationToken ct = default)
        => _cache.Get(Key(_db, collection, id)) ?? await LoadAndCacheAsync(collection, id, ct);

    public async Task<Document> UpdateAsync(string collection, Document doc, CancellationToken ct = default)
    {
        var result = await _inner.UpdateAsync(collection, doc, ct);
        _cache.Remove(Key(_db, collection, doc.Id));   // invalidate after the write succeeds
        return result;
    }
    // Delete, DropCollection and ClearCollection must invalidate as well
}
```
3. Add tests for read-after-update, read-after-delete and drop-collection through the cached store.

### CACHE-002: Remove or Honour Dead Configuration Keys (Medium)
**File:** `AdvGenNoSqlServer.Host/Program.cs` lines 501-508; `Core/Configuration/ServerConfiguration.cs` lines 50-60, 164
**Required Actions:**
1. If the cache is removed (CACHE-001), delete `MaxCacheItemCount`, `MaxCacheSizeInBytes`, `DefaultCacheTtlMilliseconds`, `CacheTimeoutMinutes` and `EnableObjectPooling` from `ServerConfiguration`, `ConfigurationManager`, `config-schema.json`, the `appsettings*.json` files and `Documentation/Deployment.md`.
2. If it is kept, bind one configuration source in both hosts (the `MemoryManagement` section) and map the legacy keys onto it, logging a deprecation warning when a legacy key is set.
3. Add a startup log line listing configuration keys that were supplied but are not recognized.

### CACHE-003: Introduce a Single Memory Budget (Medium)
**File:** `AdvGenNoSqlServer.Host/Program.cs` lines 501-508, 642
**Required Actions:**
1. Add `MaxMemoryMb` to `ServerConfiguration` (default derived from the container limit via `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`) and divide it explicitly between the document store, the write queue and in-flight request payloads.
2. Enforce it on the write path: reject inserts with a clear `OUT_OF_MEMORY_BUDGET` error when the store is at its share, instead of letting the process be OOM-killed.
3. Register `IMemoryProfiler` and pass it to `AddMemoryHealthCheck`, and report store bytes, queue depth and budget on `/metrics`.

### CACHE-004: Prune Unused Cache and Pool Implementations (Low)
**File:** `AdvGenNoSqlServer.Core/Caching/`, `AdvGenNoSqlServer.Core/Pooling/`
**Required Actions:**
1. Delete `MemoryCacheManager` (closes BUG-004) and keep at most one cache implementation.
2. Either adopt the pooling module where allocations are measurable (for example pooled `StringBuilder` in response building, pooled buffers in `ConnectionHandler.ReadExactAsync`, guided by the existing benchmarks) or move it out of Core into a separate optional package.
3. If `ICacheManager` is kept, change the key to a structured type (database, collection, id).

### IO-001: One Durable-Write Primitive for Every Persistent File (High)
**Section:** 4.1 Persistence & File I/O Layer reliability
**Files:** `HybridDocumentStore.cs` 422/524, `AdvancedFileStorageManager.cs` 62, `Core/Database/DatabaseManager.cs` 356, `Core/Transactions/WriteAheadLog.cs` 469, `FileUserStore.cs` 47-48, `AttachmentStore.cs` 97-104 and 456-462, `AuditLogger.cs` 280
**Required Actions:**
1. Move `AtomicFileWriter` to Core as a public type (IO-002) and replace every write listed above with it. Add `WriteAllBytesAsync` and a stream overload for attachments.
2. Attachments: write content first, then metadata, each atomically; treat metadata as the commit record, and on startup delete content files that have no metadata.
3. Audit log: keep append mode but open one `FileStream` and call `Flush(flushToDisk: true)` for critical events and on the existing periodic flush.
4. Add a build-time guard so the pattern does not regress, for example a test that scans the production assemblies' source for `File.WriteAll`:
```csharp
[Fact]
public void ProductionCode_DoesNotWritePersistentFilesDirectly()
{
    var offenders = Directory.EnumerateFiles(RepoRoot, "*.cs", SearchOption.AllDirectories)
        .Where(IsProductionSource)                       // exclude tests, examples, import/export
        .Where(f => File.ReadAllText(f).Contains("File.WriteAll") && !f.EndsWith("AtomicFileWriter.cs"));
    Assert.Empty(offenders);
}
```

### IO-002: Complete AtomicFileWriter (Medium)
**File:** `AdvGenNoSqlServer.Storage/AtomicFileWriter.cs`
**Required Actions:**
1. Fsync the parent directory after the rename on Unix:
```csharp
[DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
[DllImport("libc", SetLastError = true)] private static extern int fsync(int fd);
[DllImport("libc")] private static extern int close(int fd);

private static void FlushDirectory(string directory)
{
    if (OperatingSystem.IsWindows()) return;           // NTFS journals the rename
    int fd = open(directory, 0 /* O_RDONLY */);
    if (fd < 0) throw new IOException($"Cannot open directory '{directory}' for fsync");
    try { if (fsync(fd) != 0) throw new IOException($"fsync failed for '{directory}'"); }
    finally { close(fd); }
}
```
2. Add `CleanupTempFiles(string directory)` that deletes `*.tmp` and call it when a store opens a directory.
3. Open the stream with `FileOptions.Asynchronous | FileOptions.WriteThrough` in the async overload and accept a `CancellationToken`.
4. Accept an optional Unix file mode so secrets are created with restricted permissions before the rename (used by IO-003).
5. Tests: content is intact after a simulated failure between write and rename; no `.tmp` remains after cleanup.

### IO-003: Fail Closed on a Corrupt User Store (Medium)
**File:** `AdvGenNoSqlServer.Core/Authentication/FileUserStore.cs` lines 24-50
**Required Actions:**
1. On `JsonException`, throw a dedicated `UserStoreCorruptException` instead of returning an empty list; in Production the host must refuse to start and name the file and the `.corrupt-*` copy in the error.
2. Keep the previous good file as `users.json.bak` on every successful save and document the manual restore step in `Documentation/Deployment.md`.
3. Write through `AtomicFileWriter` with mode `0600` applied to the temp file before the rename.

### IO-004: Lock the Data Directory and Make Drop Atomic (Medium)
**File:** `AdvGenNoSqlServer.Storage/DatabaseManager.cs`, `HybridDocumentStore.cs` line 347
**Required Actions:**
1. On startup, open `<StoragePath>/.lock` with `FileShare.None` and hold it for the process lifetime; if it cannot be opened, exit with a clear "data directory is in use by another process" error.
2. Drop a collection by renaming its directory to `<name>.dropped-<guid>` (one atomic step) and then deleting the renamed directory; on startup, delete any leftover `*.dropped-*` directories and ignore them when listing collections.

### IO-005: Define an I/O Failure Policy (Low)
**Required Actions:**
1. After a persistent write fails, switch the store to read-only, return `STORAGE_UNAVAILABLE` for writes, and fail `/health/ready` until an operator intervenes or a retry succeeds.
2. Add a minimum-free-space setting checked by the existing disk health check and before large writes (attachments, export).
3. Log quarantine failures instead of swallowing them, and route all storage diagnostics through `ILogger`.

### REPO-001: Add Streaming and Paged Reads to the Repository (Medium)
**Section:** 4.2 Repository pattern in Storage layer
**File:** `AdvGenNoSqlServer.Core/Abstractions/IDocumentStore.cs`
**Required Actions:**
1. Add read methods that do not require materializing a collection. Use default interface implementations so existing stores keep compiling, then override them in the real stores:
```csharp
IAsyncEnumerable<Document> ScanAsync(string collectionName, CancellationToken cancellationToken = default);

Task<IReadOnlyList<Document>> GetPageAsync(
    string collectionName, string? afterId, int take, CancellationToken cancellationToken = default);
```
2. Change the query executor, cursors and `listdocuments` to use `ScanAsync`/`GetPageAsync`; mark `GetAllAsync` as intended for small collections in its XML doc.
3. Add both methods to `DocumentStoreContractTests` (ordering by Id, stable paging, empty collection).

### REPO-002: Do Not Share Mutable Document Instances Across the Repository Boundary (Medium)
**File:** `AdvGenNoSqlServer.Storage/HybridDocumentStore.cs` lines 116-143, 172-186 (same pattern in `DocumentStore`/`InMemoryDocumentCollection`)
**Required Actions:**
1. Copy on the way in and on the way out:
```csharp
var stored = document.DeepClone();        // keep our own copy
stored.CreatedAt = stored.UpdatedAt = DateTime.UtcNow;
stored.Version = 1;
collection.TryAdd(stored.Id, stored);
return stored.DeepClone();                // caller gets an independent copy
```
   Implement `Document.DeepClone()` once in Core (clone the `Data` dictionary recursively).
2. If cloning cost is a concern, store an immutable representation (serialized bytes or a frozen dictionary) and materialize on read.
3. Add a contract test: mutate a document returned by `GetAsync`, read again, and assert the stored value is unchanged; same for the instance passed to `InsertAsync`.

### REPO-003: Introduce a Decorator Base and Capability Interfaces (Medium)
**File:** all `*DocumentStore` decorators in `AdvGenNoSqlServer.Storage`; `WriteConcernDocumentStore.cs` lines 300-312
**Required Actions:**
1. Add a pass-through base class and have every decorator derive from it, overriding only what it changes:
```csharp
public abstract class DocumentStoreDecorator : IDocumentStore
{
    protected IDocumentStore Inner { get; }
    protected DocumentStoreDecorator(IDocumentStore inner) => Inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public virtual Task<Document> InsertAsync(string c, Document d, CancellationToken ct = default) => Inner.InsertAsync(c, d, ct);
    // ... remaining members forward to Inner

    /// <summary>Finds a capability anywhere in the decorator chain.</summary>
    public virtual T? GetCapability<T>() where T : class
        => this as T ?? (Inner as DocumentStoreDecorator)?.GetCapability<T>() ?? Inner as T;
}
```
2. Define `IFlushableStore { Task FlushAsync(CancellationToken ct); }`, implement it on `HybridDocumentStore` and `PersistentDocumentStore`, and replace the downcasts in `WriteConcernDocumentStore.FlushToJournalAsync` with `GetCapability<IFlushableStore>()`. Throw if a durable write concern is requested and no flushable store exists, instead of silently doing nothing.
3. Replace the reflection in `PersistentDocumentStore` with an internal load method (CODE-001).

### REPO-004: Honour Cancellation in Store Operations (Low)
**File:** `AdvGenNoSqlServer.Storage/HybridDocumentStore.cs`
**Required Action:** Call `cancellationToken.ThrowIfCancellationRequested()` on entry and inside scan loops, and pass the token to `_writeQueue.Writer.WriteAsync`, `File.ReadAllTextAsync` and awaited completions. Do not cancel after the in-memory state has been changed; from that point the operation must run to completion.

### REPO-005: Remove Duplicate Names and Widen the Contract Tests (Low)
**Required Actions:**
1. Keep one `IDatabaseManager` (in `Core/Abstractions`) and one implementation; rename or delete the `Core/Database` pair.
2. Move `DocumentStoreContractTests` to `AdvGenNoSqlServer.Tests` (or a shared test library) and add a subclass per store and per decorator wrapping a base store, so each is held to the same behaviour.

### FACT-001: Factory Must Not Downgrade TLS (High)
**Section:** 4.2 Factory pattern in Client creation
**File:** `AdvGenNoSqlServer.Client/ClientFactory.cs` lines 13-16
**Required Actions:**
1. Pass the options through (this also closes BUG-002):
```csharp
public static AdvGenNoSqlClient CreateClient(AdvGenNoSqlClientOptions options)
{
    ArgumentNullException.ThrowIfNull(options);
    options.Validate();                                   // CODE-006
    return new AdvGenNoSqlClient(options.ServerAddress, options);
}
```
2. Replace the existing factory test with ones that would have caught this: create a client through the factory with `UseSsl = true` against a TLS test server and assert the connection is secure; create one against a plaintext server and assert the connect fails.
3. Raise BUG-002 from Medium to High in the findings log when triaging.

### FACT-002: Make the Factory Earn Its Place (Low)
**File:** `AdvGenNoSqlServer.Client/ClientFactory.cs`
**Recommendation:** Either remove the static factory and document the constructor as the supported entry point, or turn it into a real, injectable factory.
**Required Actions (if kept):**
```csharp
public interface IAdvGenNoSqlClientFactory
{
    IAdvGenNoSqlClient CreateClient(string name = "default");
}

public static IServiceCollection AddAdvGenNoSqlClient(
    this IServiceCollection services, string name, Action<AdvGenNoSqlClientOptions> configure);
```
1. Validate options once at registration, return the interface from ARCH-002, and let `AdminClient` obtain its client from the factory instead of `new`.
2. Add the copyright header to `ClientFactory.cs`.

### FACT-003: Single, Strictly Parsed Server Address (Low)
**File:** `AdvGenNoSqlServer.Client/Client.cs` lines 139-151; `ClientOptions.cs` line 17
**Required Actions:**
1. Add a constructor that takes only `AdvGenNoSqlClientOptions` and reads `ServerAddress` from it; keep `(string, options)` as an overload that sets `options.ServerAddress` and is marked `[Obsolete]`.
2. Parse with a helper that supports IPv6 brackets and rejects bad input:
```csharp
if (!IPEndPoint.TryParse(address, out var ep) && !TryParseHostPort(address, out host, out port))
    throw new ArgumentException($"Invalid server address '{address}'. Expected host:port.", nameof(address));
```
3. Tests: `[::1]:9090` connects to port 9090 on `::1`; `host:abc` throws `ArgumentException`; `host` alone uses the documented default port.

### CMD-001: Compile Filters into Operator Objects (Medium)
**Section:** 4.2 Command pattern in Query processing
**File:** `AdvGenNoSqlServer.Query/Filtering/FilterEngine.cs` lines 97-115, 182-219; `Models/Query.cs` line 51
**Recommendation:** Parse the filter once into a tree of small predicate objects, then evaluate that tree per document.
**Required Actions:**
1. Introduce a predicate abstraction and an operator registry:
```csharp
public interface IFilterNode { bool Matches(Document document); }

public interface IFilterOperator
{
    string Name { get; }                                   // "$gt"
    IFilterNode Build(string fieldPath, object operand);   // validates the operand, throws QueryParseException
}

public sealed class FilterCompiler
{
    private readonly Dictionary<string, IFilterOperator> _operators;   // registered once
    public IFilterNode Compile(QueryFilter filter);                    // unknown operator => QueryParseException here
}
```
2. Move each existing `Evaluate*Operator` method into its own `IFilterOperator` class (`EqOperator`, `InOperator`, `RegexOperator` with the cached, time-limited `Regex` from SEC-031/PERF-008, and the logical `And`/`Or`/`Not`/`Nor` nodes).
3. Keep `IFilterEngine.Matches(document, filter)` as a thin wrapper that compiles and caches per `QueryFilter` instance, so existing callers and tests keep working; have `QueryExecutor` compile once per query.
4. Call the compiler from `QueryParser.Parse` so an invalid filter is rejected before execution.
5. Tests: unknown operator fails at parse time on an empty collection; each operator class has direct unit tests; the existing `QueryEngineTests` pass unchanged. Compare `FilterEngineComplexMatch` in the benchmarks before and after.

### CMD-002: Add a Stage Factory So Pipelines Can Be Deserialized (Medium)
**File:** `AdvGenNoSqlServer.Query/Aggregation/`
**Required Actions:**
1. Add a registry keyed by `StageType` and a parser entry point:
```csharp
public interface IAggregationStageFactory
{
    string StageType { get; }                                // "$match"
    IAggregationStage Create(JsonElement specification);     // validates and throws AggregationStageException
}

public AggregationPipeline ParsePipeline(JsonElement stagesArray);   // [ { "$match": {...} }, { "$group": {...} } ]
```
2. Provide one factory per existing stage (`$match`, `$group`, `$project`, `$sort`, `$limit`, `$skip`), with `$match` reusing the filter compiler from CMD-001.
3. Enforce limits while parsing: maximum number of stages and, during execution, a maximum number of documents held by blocking stages (`$group`, `$sort`).
4. Expose it through the `aggregate` command proposed in QRY-001, and add round-trip tests (a pipeline built with the builder and the same pipeline parsed from JSON produce identical results).

### CMD-003: Server Command Dispatch (Low)
**Required Action:** Implement the `ICommandHandler`/`CommandDispatcher` design recorded under QRY-002. No separate work item.

### NET-005: Enforce Read, Idle and Handshake Timeouts (High)
**Section:** 4.1 Network/Protocol Layer (TCP) correctness
**File:** `TcpServer.cs` lines 224-225, 247-265; `ConnectionHandler.cs` lines 179-292
**Current:** `client.ReceiveTimeout`/`SendTimeout` are set, but they are ignored by async I/O. `ReadExactAsync` waits indefinitely, and the pool slot is acquired only after the TLS handshake.
**Required Actions:**
1. Apply a per-frame read deadline once the first header byte has arrived, and a separate (longer) idle deadline while waiting for the next frame:
```csharp
private async Task<byte[]?> ReadExactAsync(int count, TimeSpan timeout, CancellationToken cancellationToken)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(timeout);
    try
    {
        // existing read loop, passing cts.Token to _stream.ReadAsync
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        throw new ProtocolException($"Read timed out after {timeout.TotalSeconds:F0}s");
    }
}
```
2. Add `ReadTimeout`, `IdleTimeout` and `HandshakeTimeout` to `ServerConfiguration` (validated as positive) and stop relying on the socket-level timeout properties.
3. Acquire the connection-pool slot before the TLS handshake and wrap the handshake in `HandshakeTimeout`, releasing the slot on failure.
4. Add tests: a client that sends 5 header bytes then stalls is disconnected within the read timeout; a client that connects and never completes TLS is disconnected and does not consume a slot afterwards.

### NET-006: Make Payload and PayloadLength Impossible to Desynchronise (Medium)
**File:** `TcpServer.cs` lines 351-355; `MessageProtocol.cs` lines 128-133
**Current:** `new NoSqlMessage { MessageType = MessageType.Error, Payload = bytes }` is serialized with `PayloadLength == 0`.
**Required Actions:**
1. Immediate fix: build the rejection with `NoSqlMessage.CreateError("SERVER_BUSY", reason)` (after SEC-026 makes `CreateError` use `JsonSerializer`).
2. Structural fix: derive the length from the payload so it cannot be forgotten:
```csharp
private byte[]? _payload;
public byte[]? Payload
{
    get => _payload;
    set { _payload = value; PayloadLength = value?.Length ?? 0; }
}
```
   and have `Serialize` throw `ArgumentException` when `PayloadLength > (Payload?.Length ?? 0)`.
3. Add a test that fills the pool, connects once more, and asserts the Error frame payload contains the capacity message.

### NET-007: Reject Unsupported Message Flags (Medium)
**File:** `MessageProtocol.cs` lines 72-98, 326-341
**Recommendation:** Until compression, payload encryption and acknowledgements are implemented, a frame carrying those flags must be refused instead of being misread.
**Required Actions:**
```csharp
private const MessageFlags SupportedFlags = MessageFlags.EndOfBatch; // extend as features land

public bool ValidateHeader(MessageHeader header)
{
    // ... existing checks ...
    if ((header.Flags & ~SupportedFlags) != 0)
        return false;
    return true;
}
```
Either implement `Compressed`/`Encrypted`/`RequireAck` end to end (client and server) or remove them from the public enum and from the protocol documentation in `AGENTS.md`.

### NET-008: Extend Checksum Coverage and Bounds-Check Deserialize (Low)
**File:** `MessageProtocol.cs` lines 289-291, 398-422
**Recommendation:** Covering the header in the CRC is a wire-format change, so schedule it with the next protocol version (see NET-009) rather than changing version 1.
**Required Actions:**
1. In protocol version 2, compute the CRC over header + payload.
2. In `Deserialize`, validate the length before allocating and always verify the checksum:
```csharp
long required = (long)MessageHeader.HeaderSize + header.PayloadLength + 4;
if (buffer.Length - offset < required)
    throw new ProtocolException("Buffer shorter than declared payload length");
```

### NET-009: Report Protocol Errors and Add Version Negotiation (Low)
**File:** `TcpServer.cs` lines 341-344; `MessageProtocol.cs` lines 331-332
**Required Actions:**
1. In `ProcessConnectionLoopAsync`, catch `ProtocolException` separately and make a best-effort `handler.SendErrorAsync("PROTOCOL_ERROR", ex.Message)` before closing (bounded by a short timeout so a dead peer cannot delay cleanup).
2. Return distinct failure reasons from `ValidateHeader` (bad magic, unsupported version, unknown type, bad length) so the error frame and the log say which check failed.
3. Accept a supported version range (`MinProtocolVersion..ProtocolVersion`) and reply with the server's supported range during Handshake, to satisfy the "ensure backward compatibility" rule in `AGENTS.md`.

---

*End of Reviewer's Plan*
