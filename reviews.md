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
- [ ] Verify project dependencies are correctly layered
- [ ] Check for circular dependencies
- [ ] Validate namespace conventions
- [ ] Review project file configurations (.csproj)
- [ ] Verify target framework consistency (net9.0)
- [ ] Check NuGet package versions and compatibility

---

## 3. Code Quality Review by Project

### 3.1 AdvGenNoSqlServer.Core

#### 3.1.1 Authentication Module
Files to review:
- [x] `Authentication/AuthenticationManager.cs` - User authentication logic **[REVIEWED - 5 ISSUES FOUND: SEC-001 to SEC-005]**
- [ ] `Authentication/AuthenticationService.cs` - Authentication service interface
- [x] `Authentication/JwtTokenProvider.cs` - JWT token generation/validation **[REVIEWED - GOOD: Uses HS256 + FixedTimeEquals. 3 MINOR ISSUES: SEC-006 to SEC-008]**
- [x] `Authentication/RoleManager.cs` - RBAC implementation **[REVIEWED - 4 ISSUES: SEC-011 (High), SEC-012-014 (Medium/Low). Thread safety + authorization needed]**
- [x] `Authentication/AuditLogger.cs` - Audit logging **[REVIEWED - GOOD: ConcurrentQueue, SemaphoreSlim, file rotation, critical event flush. 3 LOW: SEC-015, PERF-001, OPS-001]**
- [x] `Authentication/EncryptionService.cs` - Data encryption **[REVIEWED - EXCELLENT: AES-256-GCM, proper nonce, PBKDF2-100k, ZeroMemory cleanup. 2 LOW: SEC-009, SEC-010]**
- [ ] `Authentication/IAuditLogger.cs` - Interface definitions
- [ ] `Authentication/IJwtTokenProvider.cs` - Interface definitions
- [ ] `Authentication/IEncryptionService.cs` - Interface definitions

**Review Focus:**
- Password hashing algorithm (PBKDF2, bcrypt, Argon2)
- Token expiration and refresh logic
- Role permission mappings
- Audit log completeness
- Encryption key management

#### 3.1.2 Caching Module
Files to review:
- [ ] `Caching/ICacheManager.cs` - Cache interface
- [ ] `Caching/MemoryCacheManager.cs` - Basic memory cache
- [ ] `Caching/AdvancedMemoryCacheManager.cs` - Advanced caching
- [ ] `Caching/LruCache.cs` - LRU eviction implementation

**Review Focus:**
- Cache eviction policies
- Thread safety
- Memory bounds
- Cache invalidation strategies

#### 3.1.3 Transactions Module
Files to review:
- [ ] `Transactions/ITransactionManager.cs` - Transaction interface
- [x] `Transactions/TransactionManager.cs` - Basic transaction management **[REVIEWED - STUB: Doesn't actually commit/rollback! 4 ISSUES: DATA-010 (High - no-op), MEM-004, CONC-006 (Medium), DATA-011 (Low)]**
- [x] `Transactions/AdvancedTransactionManager.cs` - Advanced transactions **[REVIEWED - STUB: Also doesn't commit/rollback! Has timeout/cleanup but same DATA-012 no-op issue. MEM-005 - completed txns never removed]**
- [ ] `Transactions/ITransactionCoordinator.cs` - Coordinator interface
- [ ] `Transactions/TransactionCoordinator.cs` - Distributed transactions
- [ ] `Transactions/TransactionContext.cs` - Transaction context
- [ ] `Transactions/ILockManager.cs` - Lock interface
- [x] `Transactions/LockManager.cs` - Lock implementation **[REVIEWED - GOOD: Wait-for graph deadlock detection, RWLS, victim selection. 4 ISSUES: PERF-003 (High), CONC-001/002, DATA-003]**
- [ ] `Transactions/IWriteAheadLog.cs` - WAL interface
- [x] `Transactions/WriteAheadLog.cs` - WAL implementation **[REVIEWED - EXCELLENT: CRC32, before/after images, checkpoints, recovery. 4 ISSUES: PERF-002 (High), SEC-016, DATA-001/002]**

**Review Focus:**
- ACID compliance
- Isolation levels
- Deadlock detection/prevention
- WAL durability guarantees
- Lock granularity

#### 3.1.4 Pooling Module
Files to review:
- [ ] `Pooling/IObjectPool.cs` - Pool interface
- [ ] `Pooling/ObjectPool.cs` - Generic object pool
- [ ] `Pooling/BufferPool.cs` - Buffer pooling
- [ ] `Pooling/PooledObject.cs` - Pooled object wrapper
- [ ] `Pooling/StringBuilderPool.cs` - StringBuilder pooling
- [ ] `Pooling/ObjectPoolManager.cs` - Pool management

**Review Focus:**
- Pool sizing strategies
- Object lifecycle management
- Memory leak prevention
- Thread safety

#### 3.1.5 Configuration Module
Files to review:
- [ ] `Configuration/IConfigurationManager.cs` - Config interface
- [ ] `Configuration/ConfigurationManager.cs` - Configuration management
- [ ] `Configuration/ServerConfiguration.cs` - Server config model

**Review Focus:**
- Configuration validation
- Hot-reload capabilities
- Sensitive data handling

#### 3.1.6 Models
Files to review:
- [ ] `Models/Document.cs` - Document model
- [ ] `Models/BatchOperation.cs` - Batch operation model

**Review Focus:**
- Model completeness
- Serialization attributes
- Validation annotations

---

### 3.2 AdvGenNoSqlServer.Storage

#### 3.2.1 Document Stores
Files to review:
- [ ] `IDocumentStore.cs` - Store interface
- [ ] `DocumentStore.cs` - Basic document store
- [ ] `IPersistentDocumentStore.cs` - Persistent store interface
- [x] `PersistentDocumentStore.cs` - Persistent implementation **[REVIEWED - 4 ISSUES: DATA-006 (High), DATA-007, CODE-001, CONC-003. Non-atomic writes + reflection usage]**
- [ ] `InMemoryDocumentCollection.cs` - In-memory collection
- [ ] `GarbageCollectedDocumentStore.cs` - GC-enabled store
- [ ] `GarbageCollector.cs` - Document garbage collection
- [x] `HybridDocumentStore.cs` - Hybrid storage **[REVIEWED - 4 ISSUES: DATA-013 (High - silent exceptions), DATA-014 (Medium - non-atomic writes), DATA-015 (Medium - race condition), SEC-033 (Low - silent write failures)]**
- [ ] `TtlDocumentStore.cs` - TTL-enabled store
- [ ] `AtomicUpdateDocumentStore.cs` - Atomic update support
- [ ] `IAtomicUpdateOperations.cs` - Atomic operations interface

**Review Focus:**
- CRUD operation correctness
- Concurrency handling
- Data consistency
- Memory management
- TTL cleanup logic

#### 3.2.2 Indexing
Files to review:
- [ ] `Indexing/IBTreeIndex.cs` - B-tree interface
- [x] `Indexing/BTreeIndex.cs` - B-tree implementation **[REVIEWED - GOOD: Proper B-tree ops, generic, thread-safe. 3 ISSUES: PERF-004 (Medium), DATA-004/005 (Low)]**
- [ ] `Indexing/BTreeNode.cs` - B-tree node structure
- [ ] `Indexing/IndexManager.cs` - Index management
- [ ] `Indexing/CompoundIndexKey.cs` - Compound key support
- [ ] `Indexing/ITtlIndexService.cs` - TTL index interface
- [ ] `Indexing/TtlIndexService.cs` - TTL index implementation
- [ ] `Indexing/PartialSparseIndex.cs` - Partial/Sparse index

**Review Focus:**
- B-tree correctness (insert, delete, rebalance)
- Index performance characteristics
- Compound key ordering
- TTL cleanup efficiency
- Sparse index optimization

#### 3.2.3 Storage Managers
Files to review:
- [ ] `Storage/IStorageManager.cs` - Storage interface
- [ ] `Storage/FileStorageManager.cs` - File-based storage
- [ ] `Storage/AdvancedFileStorageManager.cs` - Advanced file storage

**Review Focus:**
- File I/O efficiency
- Crash recovery
- Data corruption handling
- Compaction strategies

---

### 3.3 AdvGenNoSqlServer.Query

#### 3.3.1 Parsing
Files to review:
- [ ] `Parsing/IQueryParser.cs` - Parser interface
- [x] `Parsing/QueryParser.cs` - Query parsing logic **[REVIEWED - CLEAN: Uses System.Text.Json safely, TryParse pattern, proper exceptions. 2 MINOR: DOS-002, CODE-008 (Low/Info)]**

**Review Focus:**
- Query syntax validation
- Injection prevention
- Error handling

#### 3.3.2 Filtering
Files to review:
- [ ] `Filtering/IFilterEngine.cs` - Filter interface
- [x] `Filtering/FilterEngine.cs` - Filter implementation **[REVIEWED - GOOD: MongoDB-like ops, logical operators, nested paths. 3 ISSUES: SEC-031 (Medium - ReDoS risk), PERF-008, CODE-009 (Low)]**

**Review Focus:**
- Operator support completeness
- Performance optimization
- Edge case handling

#### 3.3.3 Execution
Files to review:
- [ ] `Execution/IQueryExecutor.cs` - Executor interface
- [x] `Execution/QueryExecutor.cs` - Query execution **[REVIEWED - GOOD: Index-aware, explain, sorting, projection. 4 ISSUES: CODE-010, PERF-009 (Medium - mutates docs!), SEC-032, PERF-010 (Low)]**

**Review Focus:**
- Execution plan optimization
- Index utilization
- Resource limits

#### 3.3.4 Aggregation
Files to review:
- [ ] `Aggregation/IAggregationStage.cs` - Stage interface
- [ ] `Aggregation/AggregationPipeline.cs` - Pipeline implementation
- [ ] `Aggregation/AggregationPipelineBuilder.cs` - Pipeline builder
- [ ] `Aggregation/AggregationResult.cs` - Result model

**Review Focus:**
- Stage ordering
- Memory usage for large aggregations
- Streaming vs. buffering

#### 3.3.5 Cursors
Files to review:
- [ ] `Cursors/Cursor.cs` - Cursor model
- [ ] `Cursors/CursorImpl.cs` - Cursor implementation
- [ ] `Cursors/CursorManager.cs` - Cursor lifecycle management
- [ ] `Cursors/CursorQueryExecutor.cs` - Cursor-based execution
- [ ] `Cursors/CursorEnabledQueryExecutor.cs` - Cursor-enabled executor

**Review Focus:**
- Cursor timeout handling
- Memory management
- Pagination correctness

#### 3.3.6 Models
Files to review:
- [ ] `Models/Query.cs` - Query model
- [ ] `Models/QueryResult.cs` - Result model

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
- [ ] `Program.cs` - Host entry point

**Review Focus:**
- Host builder configuration
- Service registration
- Environment handling

---

## 4. Architecture Review

### 4.1 Layered Architecture Compliance
Review against `plan.md` Section 2.1:

- [ ] Client Application Layer isolation
- [ ] Network/Protocol Layer (TCP) correctness
- [ ] Security & Authentication Layer integration
- [ ] Query Processing & Command Layer design
- [ ] Transaction Management Layer implementation
- [ ] Storage Engine Layer architecture
- [ ] Caching & Memory Management integration
- [ ] Persistence & File I/O Layer reliability

### 4.2 Design Pattern Usage
- [ ] Repository pattern in Storage layer
- [ ] Factory pattern in Client creation
- [ ] Command pattern in Query processing
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
- [ ] `SecurityPenetrationTests.cs` - Penetration test coverage

---

## 6. Performance Review

### 6.1 Benchmark Analysis
Review benchmark results in `AdvGenNoSqlServer.Benchmarks/`:
- [ ] `DocumentStoreBenchmarks.cs` - Document operation performance
- [ ] `BTreeIndexBenchmarks.cs` - Index performance
- [ ] `CacheBenchmarks.cs` - Cache performance
- [ ] `SerializationBenchmarks.cs` - Serialization overhead
- [ ] `QueryEngineBenchmarks.cs` - Query execution performance

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

---

*End of Reviewer's Plan*
