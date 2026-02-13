# NoSQL Server in C# - Development Plan

**License**: MIT License  
**Copyright**: [Your Organization]  
**Status**: Open Source  

This project is released under the MIT License. All dependencies must be compatible with the MIT License (permissive licenses only).

## 1. Project Overview

Build a **lightweight, high-performance NoSQL server** in C# with .NET featuring:
- TCP-based network communication
- Robust security mechanisms
- Advanced transaction management
- Minimal resource footprint
- High throughput and low latency

---

## 2. Architecture Overview

### 2.1 Layered Architecture

```
┌─────────────────────────────────────┐
│      Client Application Layer       │
├─────────────────────────────────────┤
│      Network/Protocol Layer (TCP)   │
├─────────────────────────────────────┤
│    Security & Authentication Layer  │
├─────────────────────────────────────┤
│   Query Processing & Command Layer  │
├─────────────────────────────────────┤
│   Transaction Management Layer      │
├─────────────────────────────────────┤
│     Storage Engine Layer            │
├─────────────────────────────────────┤
│   Caching & Memory Management       │
├─────────────────────────────────────┤
│   Persistence & File I/O Layer      │
└─────────────────────────────────────┘
```

---

## 3. Core Components

### 3.1 Network Layer (AdvGenNoSqlServer.Network)
**Responsibility:** Handle TCP connections and protocol communication

#### Features:
- **Async TCP Server** using `TcpListener` and async/await
- **Connection pooling** for efficient resource utilization
- **Message framing** (length-prefixed or delimiter-based)
- **Bulk operations** support for batch processing
- **Keep-alive mechanism** to detect dead connections
- **Graceful shutdown** handling

#### Implementation Details:
```csharp
- TcpServer class (main server)
- ConnectionHandler class (per-connection logic)
- MessageProtocol class (message serialization/deserialization)
- ConnectionPool class (manage active connections)
```

**Performance Optimization:**
- Use `Socket` directly instead of `StreamReader/StreamWriter` for lower overhead
- Implement buffer pooling using `ArrayPool<byte>`
- Use `Pipelines` for efficient I/O

---

### 3.2 Security Layer (AdvGenNoSqlServer.Authentication)
**Responsibility:** Authentication, authorization, and encryption

#### Features:
- **User authentication** (username/password with hashing)
- **Token-based authorization** (JWT or custom tokens)
- **Role-based access control (RBAC)**
- **SSL/TLS support** for encrypted connections
- **Per-command permission checks**
- **Audit logging** of access and modifications

#### Implementation Details:
```csharp
- AuthenticationManager class
- JwtTokenProvider class
- EncryptionService class (AES encryption for sensitive data)
- PermissionValidator class
- AuditLogger class
```

**Security Best Practices:**
- Hash passwords using PBKDF2, bcrypt, or Argon2
- Use strong TLS 1.2+ for transport security
- Implement rate limiting to prevent brute force
- Validate all inputs to prevent injection attacks
- Use constant-time comparison for sensitive data

---

### 3.3 Transaction Management Layer (AdvGenNoSqlServer.Core/Transactions)
**Responsibility:** ACID compliance and multi-document transactions

#### Features:
- **ACID guarantees** (Atomicity, Consistency, Isolation, Durability)
- **Multi-document transactions** with rollback support
- **Isolation levels** (Read Uncommitted, Read Committed, Repeatable Read, Serializable)
- **Optimistic locking** for concurrent updates
- **Write-ahead logging (WAL)** for durability
- **Transaction timeout** management

#### Implementation Details:
```csharp
- TransactionManager class (coordinate transactions)
- TransactionContext class (transaction state)
- LockManager class (handle document locks)
- WriteAheadLog class (persist transaction logs)
- IsolationLevelStrategy (different isolation implementations)
```

**Key Algorithms:**
- **Two-Phase Commit (2PC)** for distributed transactions
- **MVCC (Multi-Version Concurrency Control)** for snapshot isolation
- **Write-ahead logging** for durability

---

### 3.4 Storage Engine (AdvGenNoSqlServer.Storage)
**Responsibility:** Data persistence and retrieval

#### Features:
- **Document-based storage** (JSON-like format)
- **Flexible schema** support
- **Efficient indexing** (B-tree, hash indexes)
- **Data compression** options (optional)
- **File-based persistence** with binary format
- **Garbage collection** for deleted documents
- **TTL Indexes**: Auto-expiration of documents at specified time
- **Capped Collections**: Fixed-size collections with automatic oldest document removal
- **Document Attachments**: Binary data attached to documents (planned)

#### Implementation Details:
```csharp
- StorageEngine class (core storage logic)
- DocumentStore class (document CRUD)
- IndexManager class (manage indexes)
- FileManager class (handle file I/O)
- MemoryAllocator class (optimize memory allocation)
```

**Storage Format:**
- Use binary serialization for efficiency
- Implement document versioning for conflict resolution
- Use file segmentation for large collections

---

### 3.5 Query Engine (AdvGenNoSqlServer.Query)
**Responsibility:** Parse and execute queries

#### Features:
- **Custom query language** or **MongoDB-like query syntax**
- **Query parsing** and validation
- **Query optimization** (index selection, plan generation)
- **Aggregation pipeline** support
- **Filtering, sorting, pagination**
- **Projections**: Return only specified fields
- **Cursor-based iteration**: Efficient large result set handling
- **EXPLAIN support**: Query plan analysis and debugging

#### Implementation Details:
```csharp
- QueryParser class (parse query strings)
- QueryExecutor class (execute optimized query plans)
- FilterEngine class (apply filters)
- AggregationPipeline class (pipeline stages)
- QueryOptimizer class (generate efficient plans)
```

---

### 3.6 Caching Layer (AdvGenNoSqlServer.Core/Caching)
**Responsibility:** In-memory caching for performance

#### Features:
- **LRU cache** eviction policy
- **TTL (Time-To-Live)** support
- **Cache invalidation** strategies
- **Memory limits** and monitoring
- **Thread-safe operations**

#### Implementation Details:
```csharp
- MemoryCacheManager class (basic cache)
- AdvancedMemoryCacheManager class (LRU with TTL)
- CacheInvalidationStrategy (invalidation logic)
```

---

### 3.7 Configuration Management (AdvGenNoSqlServer.Core/Configuration)
**Responsibility:** Server configuration and management using JSON

#### Features:
- **JSON-based configuration** (appsettings.json)
- **Environment-specific configs** (appsettings.Development.json, appsettings.Production.json)
- **Runtime configuration** changes without restart
- **Performance tuning** parameters
- **Logging configuration**
- **Security settings**
- **JSON schema validation**
- ✓ **Configuration hot-reload** (Agent-28 - FileSystemWatcher with debouncing)

#### Implementation Details:
```csharp
- ConfigurationManager class
- ServerConfiguration class (POCO with JSON mapping)
- ConfigurationValidator class
- ConfigurationChangeListener class (for hot-reload)
```

#### JSON Configuration Schema

**appsettings.json**:
```json
{
  "Server": {
    "Host": "0.0.0.0",
    "Port": 9090,
    "MaxConnections": 10000,
    "ConnectionTimeout": 30000,
    "KeepAliveInterval": 60000,
    "ReceiveBufferSize": 65536,
    "SendBufferSize": 65536
  },
  "Security": {
    "EnableSSL": false,
    "CertificatePath": "",
    "CertificatePassword": "",
    "RequireAuthentication": true,
    "AllowedAuthMethods": ["JWT", "Token"],
    "SessionTimeout": 3600000,
    "MaxFailedAttempts": 5,
    "LockoutDuration": 900000
  },
  "Storage": {
    "DataPath": "./data",
    "MaxFileSize": 1073741824,
    "CompressionEnabled": false,
    "CompressionLevel": "optimal",
    "PersistenceMode": "fsync",
    "IndexCacheSize": 536870912
  },
  "Transaction": {
    "DefaultIsolationLevel": "ReadCommitted",
    "DefaultTimeout": 30000,
    "MaxConcurrentTransactions": 1000,
    "DeadlockDetection": true,
    "EnableWAL": true,
    "WALFlushInterval": 1000
  },
  "Cache": {
    "Enabled": true,
    "MaxSize": 268435456,
    "EvictionPolicy": "LRU",
    "DefaultTTL": 3600000,
    "EnableDistributed": false
  },
  "Logging": {
    "Level": "Information",
    "EnableFileLogging": true,
    "LogPath": "./logs",
    "MaxLogFileSize": 104857600,
    "MaxLogFiles": 10,
    "EnableConsoleLogging": true,
    "EnableAuditLogging": true
  },
  "Performance": {
    "MaxQueryTimeout": 60000,
    "EnableQueryOptimization": true,
    "BatchSize": 1000,
    "ThreadPoolMinThreads": 16,
    "ThreadPoolMaxThreads": 256,
    "GCMode": "Server"
  }
}
```

#### Environment-Specific Configuration

**appsettings.Development.json**:
```json
{
  "Server": {
    "Port": 9090,
    "MaxConnections": 100
  },
  "Logging": {
    "Level": "Debug",
    "EnableConsoleLogging": true
  }
}
```

**appsettings.Production.json**:
```json
{
  "Server": {
    "Port": 9090,
    "MaxConnections": 10000
  },
  "Security": {
    "EnableSSL": true,
    "CertificatePath": "/etc/nosql-server/cert.pfx"
  },
  "Logging": {
    "Level": "Warning",
    "EnableConsoleLogging": false,
    "EnableFileLogging": true
  }
}
```

---

## 4. Performance Optimization Strategies

### 4.1 Memory Management
- **Object pooling** for frequently created objects
- **ArrayPool** for byte buffers
- **Struct usage** for small value types
- **Memory disposal** best practices
- **GC tuning** (consider Server GC mode)

### 4.2 Concurrency
- **Async/await** throughout the stack
- **Lock-free data structures** where possible
- **Reader-writer locks** for shared resources
- **Thread pool tuning**
- **CPU core affinity** for thread allocation

### 4.3 I/O Optimization
- **Buffered I/O** with pooled buffers
- **Batch operations** to reduce round trips
- **Index-based lookups** instead of full scans
- **Data compression** for network transfer
- **Memory-mapped files** for large datasets

### 4.4 Network Optimization
- **TCP_NODELAY** to disable Nagle's algorithm
- **Keepalive** to detect dead connections
- **Connection pooling** on client side
- **Protocol efficiency** (binary vs text)
- **Compression** of large payloads

---

## 5. Security Implementation

### 5.1 Authentication Flow
```
1. Client connects (establish TCP connection)
2. TLS handshake (if SSL enabled)
3. Client sends credentials (username/password)
4. Server validates and issues token (JWT/custom)
5. Token included in subsequent requests
6. Server validates token on each request
```

### 5.2 Authorization Checks
- **Command-level** permissions (GET, SET, DELETE, etc.)
- **Document-level** permissions (can access specific collections)
- **Field-level** permissions (can read/write specific fields)
- **Role hierarchy** (Admin > Power User > User > Guest)

### 5.3 Encryption
- **Transport**: TLS 1.2+ for all network communication
- **At-rest**: AES-256 for sensitive document fields
- **Credentials**: Never store plaintext passwords

### 5.4 Audit Trail
- **Log all access** to sensitive data
- **Track modifications** with timestamp and user
- **Failed authentication** attempts
- **Permission violations**

---

## 6. Transaction Management Details

### 6.1 Transaction Lifecycle
```
1. BEGIN - Start transaction, allocate ID
2. READ/WRITE - Document modifications (isolated)
3. VALIDATE - Check consistency
4. COMMIT/ROLLBACK - Apply or discard changes
5. CLEANUP - Release locks and resources
```

### 6.2 Isolation Levels

| Level | Dirty Read | Non-repeatable Read | Phantom Read | Performance |
|-------|-----------|-------------------|--------------|-------------|
| Read Uncommitted | ✓ | ✓ | ✓ | Fastest |
| Read Committed | ✗ | ✓ | ✓ | Good |
| Repeatable Read | ✗ | ✗ | ✓ | Better |
| Serializable | ✗ | ✗ | ✗ | Slowest |

### 6.3 Locking Strategy
- **Optimistic locks** (version-based) for low contention
- **Pessimistic locks** (actual locks) for high contention
- **Lock timeout** to prevent deadlocks
- **Lock escalation** (row → page → table)
- **Deadlock detection and resolution**

---

## 7. TCP Protocol Specification

### 7.1 Connection Format
```
[Protocol Header (4 bytes: "NOSQ")]
[Protocol Version (2 bytes)]
[Message Type (1 byte)]
[Flags (1 byte)]
[Message Length (4 bytes)]
[Message Body (variable)]
[CRC32 Checksum (4 bytes)]
```

### 7.2 Message Types
- `0x01`: Handshake
- `0x02`: Authentication
- `0x03`: Query/Command
- `0x04`: Response
- `0x05`: Error
- `0x06`: Ping/Keepalive
- `0x07`: Transaction Control

### 7.3 Example Command Format
```json
{
  "command": "SET",
  "collection": "users",
  "document": {
    "_id": "user123",
    "name": "John",
    "email": "john@example.com"
  },
  "options": {
    "ttl": 3600,
    "transaction_id": "txn_abc123"
  }
}
```

---

## 8. Development Phases

### Phase 1: Foundation (Weeks 1-2)
- ✓ Project structure setup (already done)
- Core model definitions (`Document` class)
- Basic file-based storage (`FileStorageManager`)
- Simple configuration management

### Phase 2: Network & Communication (Weeks 3-4) ✓ COMPLETE
- ✓ TCP server implementation (TcpServer with async/await)
- ✓ Connection handling and pooling (ConnectionPool, ConnectionHandler)
- ✓ Message protocol implementation (MessageProtocol with binary framing)
- ✓ Client library development (AdvGenNoSqlClient with TCP support)

### Phase 3: Authentication & Security (Weeks 5-6) ✓ COMPLETE
- ✓ User authentication system (AuthenticationManager, AuthenticationService)
- ✓ JWT token provider (JwtTokenProvider with HMAC-SHA256, 46 tests)
- ✓ Encryption/decryption services (EncryptionService with AES-256-GCM, 51 tests)
- ✓ Authorization and permission checks (RoleManager, RBAC, 59 tests)
- ✓ Audit logging (AuditLogger with file-based logging, 44 tests)
- ✓ SSL/TLS support (TlsStreamHelper, certificate management, server/client SSL, 13 tests)

### Phase 4: Storage Engine (Weeks 7-8) ✓ COMPLETE
- ✓ Document store implementation (DocumentStore with CRUD, 37 tests)
- ✓ File-based persistence (PersistentDocumentStore, 33 tests)
- ✓ Index management (BTreeIndex, IndexManager, 77 tests)
- ✓ Garbage collection for deleted documents (Tombstone, GarbageCollector, GarbageCollectedDocumentStore, 35 tests)

### Phase 5: Query Engine (Weeks 9-10) ✓ COMPLETE
- ✓ Query parser (QueryParser with MongoDB-like syntax)
- ✓ Query executor (QueryExecutor with filtering, sorting, pagination, 48 tests)
- ✓ Filter engine (FilterEngine with $eq, $ne, $gt, $gte, $lt, $lte, $in, $nin, $and, $or, $exists)
- ✓ Aggregation pipeline ($match, $group, $project, $sort, $limit, $skip, 49 tests)
- ○ Query plan optimization (planned for future)

### Phase 6: Transaction Management (Weeks 11-12) ✓ COMPLETE
- ✓ Transaction coordinator (TransactionCoordinator with 2PC, 41 tests)
- ✓ Lock manager (LockManager with deadlock detection, 38 tests)
- ✓ Write-ahead logging (WriteAheadLog with binary format, 27 tests)
- ✓ Isolation level implementations (ReadUncommitted, ReadCommitted, RepeatableRead, Serializable)

### Phase 7: Caching & Performance (Weeks 13-14) 🟢 COMPLETE
- ✓ Advanced memory caching (LruCache with TTL, 44 tests)
- ✓ Object pooling (ObjectPool, BufferPool, StringBuilderPool, 61 tests)
- ✓ Performance benchmarks (BenchmarkDotNet, 5 suites, 50+ methods)
- ○ Performance profiling and optimization (planned for future)
- ○ Stress testing (planned for future)

### Phase 8: Testing & Hardening (Weeks 15-16) 🟢 COMPLETE
- ✓ Comprehensive unit tests (828+ tests passing)
- ✓ Integration tests (all tests passing - fixed by Agent-22)
- ✓ Stress tests (implemented by Agent-23 - 4 stress scenarios + smoke test)
- ✓ Load tests (implemented by Agent-26 - 5 load scenarios + smoke test)
- ✓ Security penetration testing (31 tests - Agent-24)
- ✓ API Documentation (Agent-31 - API.md, UserGuide.md, DeveloperGuide.md, PerformanceTuning.md)
- ✓ SSL/TLS support (13 tests - Agent-27)
- ✓ Configuration hot-reload (17 tests - Agent-28)
- ✓ Batch operations (32 tests - Agent-30)

---

## 9. Project Dependencies

### .NET Framework
- `.NET 6.0+` or `.NET 7.0+` (latest stable)
- MIT License (Microsoft open-source)

### NuGet Packages (All MIT or MIT-Compatible)

#### Core Dependencies (MIT Licensed)
- **Security**: `System.Security.Cryptography` (Microsoft, MIT)
- **Serialization**: `System.Text.Json` (Microsoft, MIT)
- **Async**: `System.Threading.Tasks.Dataflow` (Microsoft, MIT)
- **Logging**: `Serilog` (Serilog Project, Apache 2.0 - MIT compatible)
- **Configuration**: `Microsoft.Extensions.Configuration` (Microsoft, MIT)
- **Dependency Injection**: `Microsoft.Extensions.DependencyInjection` (Microsoft, MIT)

#### Testing Dependencies (Permissive Licenses)
- **Unit Testing**: `xUnit` (Apache 2.0 - MIT compatible)
- **Mocking**: `Moq` (BSD 3-Clause - MIT compatible)
- **Assertions**: `FluentAssertions` (Apache 2.0 - MIT compatible)

#### Performance & Benchmarking (MIT Licensed)
- **Benchmarking**: `BenchmarkDotNet` (MIT)
- **Profiling**: Built-in .NET profiling tools (Microsoft, MIT)

### License Compatibility Matrix

| Dependency | License | MIT Compatible | Reason |
|-----------|---------|------------------|--------|
| .NET 6.0+ | MIT | ✓ | Microsoft open-source |
| System.* namespaces | MIT | ✓ | Microsoft standard library |
| Serilog | Apache 2.0 | ✓ | Permissive license |
| xUnit | Apache 2.0 | ✓ | Permissive license |
| Moq | BSD 3-Clause | ✓ | Permissive license |
| BenchmarkDotNet | MIT | ✓ | MIT licensed |
| FluentAssertions | Apache 2.0 | ✓ | Permissive license |

### Excluded Dependencies (License Incompatibility)
The following are **NOT** used due to license incompatibility:
- ❌ Entity Framework Core (if using GPL extensions)
- ❌ GPL-licensed libraries (copyleft)
- ❌ AGPL-licensed software (server-side copyleft)
- ❌ Proprietary libraries (non-open-source)
- ❌ SSPL-licensed databases/tools

### Custom Implementation Priority
Where third-party libraries have restrictive licenses, we implement custom solutions:
- ✓ Custom document serialization (instead of GPL-licensed alternatives)
- ✓ Custom query engine (instead of GPL-licensed query processors)
- ✓ Custom transaction management (custom implementation)
- ✓ Custom encryption utilities (using MIT-licensed crypto primitives)

---

## 10. Testing Strategy

### 10.1 Unit Tests
- Individual component testing
- Edge case validation
- Error handling verification

### 10.2 Integration Tests
- End-to-end workflows
- Multi-component interactions
- Database consistency checks

### 10.3 Performance Tests
- Throughput benchmarks
- Latency measurements
- Memory profiling
- Connection scalability

### 10.4 Security Tests
- Authentication/authorization flows
- Encryption validation
- Injection attack prevention
- Privilege escalation checks

### 10.5 Stress Tests
- High concurrent connections
- Large document handling
- Sustained load performance
- Failure recovery

---

## 11. Implementation Checklist

### Core Infrastructure
- [x] TCP Server with async connection handling
- [x] Connection pooling and management
- [x] Binary protocol implementation
- [x] Client library with connection pooling

### Security
- [x] User authentication (username/password)
- [x] Token generation and validation (JWT)
- [x] Role-based access control
- [x] TLS/SSL support (TlsStreamHelper, certificate management, 13 tests)
- [x] Encryption for sensitive data
- [x] Audit logging (IAuditLogger, AuditLogger with file-based logging, 44 tests)

### Storage
- [x] Document store with CRUD operations
- [x] File-based persistence
- [x] B-tree indexing (basic implementation, 77 tests passing)
- [x] Query optimization with index selection

### Query Engine
- [x] Query model classes (Query, QueryFilter, SortField, QueryOptions)
- [x] Query parser with MongoDB-like syntax support
- [x] Query executor with filtering, sorting, pagination
- [x] Filter engine with comparison and logical operators
- [x] Index-based query optimization
- [x] Aggregation pipeline ($match, $group, $project, $sort, $limit, $skip)
- [ ] Query plan optimization

### Transactions
- [x] Transaction coordinator (Two-Phase Commit, 41 tests)
- [x] Lock manager with deadlock detection
- [x] Write-ahead logging (WAL)
- [x] Rollback mechanism (via WAL)
- [x] Multiple isolation levels (ReadUncommitted, ReadCommitted, RepeatableRead, Serializable)

### Performance
- [x] Object pooling (buffers, objects)
- [x] LRU caching with TTL (LruCache<T> with O(1) operations, 44 tests)
- [ ] Query plan optimization
- [x] Batch operation support (Agent-30 - 32 tests passing)
- [ ] Memory profiling and tuning

### Examples in C# Console
- [x] Examples with multi db and authentication (Agent-41 - MultiDatabaseAndRbacExamples.cs)
- [x] Examples role based (Agent-41 - RBAC permission enforcement, role assignment) 

### Advanced Features 
- [ ] Change Streams/Subscriptions
- [ ] Full-Text Search indexes
- [ ] Geospatial indexes and queries
- [x] TTL indexes for document expiration (Agent-43 - TtlIndexService with background cleanup, 33 tests)
- [x] Unique indexes (Agent-42 - Unique constraint enforcement on single-field indexes)
- [x] Compound/Composite indexes (Agent-42 - Multi-field B-tree indexes with unique support)
- [ ] Partial/Sparse indexes
- [ ] Cursor-based pagination
- [ ] Projections
- [ ] Slow query logging
- [ ] EXPLAIN/Query plan analysis
- [ ] Import/Export tools
- [ ] Sessions (Unit of Work pattern)
- [ ] Optimistic concurrency (ETags)
- [ ] Field-level encryption
- [ ] Atomic update operations (increment, push, pull)
- [ ] Upsert operations
- [ ] Write Concern configuration
- [ ] Read Preference (for replication)
- [ ] P2P, That is allow to connect with other AdvGenNoSqlServer please reference Section 47 ,47. Peer-to-Peer (P2P) Cluster Architecture to acccording the plan in there
- [ ] Blazor Web Admin App

### Testing
- [x] Unit tests for all components (960+ tests passing)
- [x] Client integration tests (25/25 tests pass - Agent-22 fixed server-side message handling)
- [x] Performance benchmarks (BenchmarkDotNet, 5 benchmark suites, 50+ methods)
- [x] Stress tests (4 stress scenarios + smoke test - Agent-23)
- [x] Security penetration tests (31 tests - Agent-24)
- [x] Load tests (5 load scenarios + smoke test - Agent-26)

---

## 12. Success Metrics

### Performance
- **Throughput**: > 10,000 requests/second
- **Latency**: < 100ms for typical operations
- **Memory**: < 500MB baseline for server
- **CPU**: Efficient multi-core utilization

### Reliability
- **Uptime**: 99.9%+
- **Data Durability**: Zero data loss with fsync
- **Error Recovery**: Automatic crash recovery

### Security
- **Authentication**: 100% of connections authenticated
- **Encryption**: All network traffic encrypted
- **Audit**: Complete audit trail maintained
- **Vulnerability**: Zero known security issues

### Scalability
- **Connections**: Support 10,000+ concurrent clients
- **Documents**: Support billions of documents
- **Storage**: Efficient for TB+ datasets

---

## 13. Future Enhancements

### 13.1 High Priority (Production-Critical)
- **Replication**: Master-slave or multi-master replication with automatic failover
- **Sharding**: Horizontal scaling with data distribution and shard key routing
- **Clustering**: Multi-node coordination with leader election (Raft/Paxos)
- **Change Streams/Subscriptions**: Real-time data change notifications (like MongoDB Change Streams, RavenDB Subscriptions)
- **Full-Text Search**: Text indexes with stemming, analyzers, and relevance scoring

### 13.2 Medium Priority (Feature Completeness)
- **Geospatial Queries**: 2D and 2DSphere indexes for location-based queries
- **Document Attachments**: Binary attachments on documents (RavenDB-style)
- **Document Revisions**: Track document history with configurable retention
- **Map-Reduce**: Classic aggregation pattern for complex analytics
- **Server-side Patches/Scripts**: Atomic document modifications with server-side logic

### 13.3 Lower Priority (Nice to Have)
- **GraphQL**: GraphQL query support
- **Plugins**: Extensibility framework
- **Analytics**: Built-in analytics engine
- **Time-series**: Specialized time-series support
- **Wire Protocol Compatibility**: Optional MongoDB wire protocol support

---

## 14. Risks & Mitigation

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Performance degradation under load | Medium | High | Load testing, profiling, optimization |
| Data corruption | Low | Critical | WAL, checksums, backup strategies |
| Security vulnerabilities | Medium | Critical | Security audit, penetration testing |
| Concurrent access issues | Medium | High | Proper locking, comprehensive testing |
| Memory leaks | Medium | High | Profiling, disposal patterns, testing |

---

## 16. JSON Configuration Deep Dive

### 16.1 Configuration Hierarchy
1. **Default values** (hardcoded in code)
2. **appsettings.json** (base configuration)
3. **appsettings.{Environment}.json** (environment overrides)
4. **Environment variables** (runtime overrides)
5. **Runtime changes** (via API calls)

### 16.2 Configuration Features

#### Hot-Reload
```csharp
ConfigurationManager.OnConfigurationChanged += (oldConfig, newConfig) => 
{
    // React to configuration changes
    UpdateServerSettings(newConfig);
};
```

#### Validation
```csharp
var isValid = ConfigurationManager.ValidateConfiguration(config);
// Validates:
// - Port ranges (1-65535)
// - File paths exist/writable
// - Memory limits are reasonable
// - Security settings are strong
```

#### Typed Access
```csharp
var serverConfig = configuration.GetSection("Server").Get<ServerOptions>();
var securityConfig = configuration.GetSection("Security").Get<SecurityOptions>();
var storageConfig = configuration.GetSection("Storage").Get<StorageOptions>();
```

### 16.3 Configuration Schema Validation

**ConfigSchema.json** (JSON Schema):
```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "NoSQL Server Configuration",
  "type": "object",
  "properties": {
    "Server": {
      "type": "object",
      "properties": {
        "Host": { "type": "string", "format": "ipv4" },
        "Port": { "type": "integer", "minimum": 1, "maximum": 65535 },
        "MaxConnections": { "type": "integer", "minimum": 1, "maximum": 1000000 }
      },
      "required": ["Host", "Port"]
    },
    "Security": {
      "type": "object",
      "properties": {
        "EnableSSL": { "type": "boolean" },
        "RequireAuthentication": { "type": "boolean" }
      }
    }
  },
  "required": ["Server"]
}
```

### 16.4 Configuration Examples

#### Minimal Configuration
```json
{
  "Server": {
    "Host": "0.0.0.0",
    "Port": 9090
  }
}
```

#### High-Performance Configuration
```json
{
  "Server": {
    "Host": "0.0.0.0",
    "Port": 9090,
    "MaxConnections": 50000,
    "ReceiveBufferSize": 262144,
    "SendBufferSize": 262144
  },
  "Cache": {
    "Enabled": true,
    "MaxSize": 1073741824
  },
  "Performance": {
    "ThreadPoolMaxThreads": 512,
    "GCMode": "Server"
  }
}
```

#### Secure Configuration
```json
{
  "Server": {
    "Host": "127.0.0.1",
    "Port": 9090,
    "ConnectionTimeout": 10000
  },
  "Security": {
    "EnableSSL": true,
    "CertificatePath": "/etc/nosql-server/cert.pfx",
    "RequireAuthentication": true,
    "MaxFailedAttempts": 3,
    "LockoutDuration": 3600000
  },
  "Logging": {
    "EnableAuditLogging": true,
    "Level": "Information"
  }
}
```

### 16.5 Configuration Management API

```csharp
// Load configuration
var config = ConfigurationManager.LoadConfiguration(configPath);

// Get specific setting
var port = config.GetValue<int>("Server:Port");

// Update setting at runtime
ConfigurationManager.UpdateSetting("Cache:MaxSize", 536870912);

// Get configuration section as object
var serverOptions = config.GetSection("Server").Get<ServerOptions>();

// Validate configuration
var validation = ConfigurationManager.ValidateConfiguration(config);
if (!validation.IsValid)
{
    foreach (var error in validation.Errors)
    {
        Console.WriteLine($"Configuration error: {error}");
    }
}

// Save current configuration
ConfigurationManager.SaveConfiguration(config, outputPath);
```

---

## 17. License & Open Source Compliance

### MIT License Compliance
This project is released under the **MIT License**, which permits:
- ✓ Commercial use
- ✓ Modification
- ✓ Distribution
- ✓ Private use
- ✓ Patent use

### Requirements
- ⚠ Include MIT license text in distribution
- ⚠ Include copyright notice
- ⚠ State changes made to the code

### Dependency Audit
All third-party dependencies must pass:
1. **License Check** - Only MIT, Apache 2.0, BSD, or equivalent
2. **Source Availability** - Open source code must be available
3. **No Copyleft** - No GPL, AGPL, or SSPL licenses
4. **No Proprietary** - No closed-source or commercial-only software

### Adding New Dependencies
Before adding any NuGet package, verify:
```
1. Check license at: https://licenses.nuget.org/
2. Verify license compatibility with MIT
3. Review source code repository
4. Get approval if license is ambiguous
5. Document in DEPENDENCIES.md
```

### Third-Party Code Usage
- ✓ Permitted: Code snippets with MIT/Apache/BSD license
- ❌ Not Permitted: GPL/AGPL code snippets or code samples
- ❌ Not Permitted: Proprietary algorithms without license

---

## 15. Getting Started

1. **Clone repository** and review existing structure
2. **Setup development environment** (.NET SDK, IDE)
3. **Review LICENSE.txt** - MIT License terms
4. **Review DEPENDENCIES.md** - License compliance matrix
5. **Review appsettings.json** - Configuration structure
6. **Start with Phase 1** - complete foundation work
7. **Build incrementally** following the phase breakdown
8. **Test continuously** at each phase completion
9. **Profile and optimize** based on benchmarks
10. **Security review** before production release
11. **Audit dependencies** before each release

---

## 16. JSON Configuration Deep Dive

### 16.1 Configuration Hierarchy
1. **Default values** (hardcoded in code)
2. **appsettings.json** (base configuration)
3. **appsettings.{Environment}.json** (environment overrides)
4. **Environment variables** (runtime overrides)
5. **Runtime changes** (via API calls)

### 16.2 Configuration Features

#### Hot-Reload
```csharp
ConfigurationManager.OnConfigurationChanged += (oldConfig, newConfig) => 
{
    // React to configuration changes
    UpdateServerSettings(newConfig);
};
```

#### Validation
```csharp
var isValid = ConfigurationManager.ValidateConfiguration(config);
// Validates:
// - Port ranges (1-65535)
// - File paths exist/writable
// - Memory limits are reasonable
// - Security settings are strong
```

#### Typed Access
```csharp
var serverConfig = configuration.GetSection("Server").Get<ServerOptions>();
var securityConfig = configuration.GetSection("Security").Get<SecurityOptions>();
var storageConfig = configuration.GetSection("Storage").Get<StorageOptions>();
```

### 16.3 Configuration Schema Validation

**ConfigSchema.json** (JSON Schema):
```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "NoSQL Server Configuration",
  "type": "object",
  "properties": {
    "Server": {
      "type": "object",
      "properties": {
        "Host": { "type": "string", "format": "ipv4" },
        "Port": { "type": "integer", "minimum": 1, "maximum": 65535 },
        "MaxConnections": { "type": "integer", "minimum": 1, "maximum": 1000000 }
      },
      "required": ["Host", "Port"]
    },
    "Security": {
      "type": "object",
      "properties": {
        "EnableSSL": { "type": "boolean" },
        "RequireAuthentication": { "type": "boolean" }
      }
    }
  },
  "required": ["Server"]
}
```

### 16.4 Configuration Examples

#### Minimal Configuration
```json
{
  "Server": {
    "Host": "0.0.0.0",
    "Port": 9090
  }
}
```

#### High-Performance Configuration
```json
{
  "Server": {
    "Host": "0.0.0.0",
    "Port": 9090,
    "MaxConnections": 50000,
    "ReceiveBufferSize": 262144,
    "SendBufferSize": 262144
  },
  "Cache": {
    "Enabled": true,
    "MaxSize": 1073741824
  },
  "Performance": {
    "ThreadPoolMaxThreads": 512,
    "GCMode": "Server"
  }
}
```

#### Secure Configuration
```json
{
  "Server": {
    "Host": "127.0.0.1",
    "Port": 9090,
    "ConnectionTimeout": 10000
  },
  "Security": {
    "EnableSSL": true,
    "CertificatePath": "/etc/nosql-server/cert.pfx",
    "RequireAuthentication": true,
    "MaxFailedAttempts": 3,
    "LockoutDuration": 3600000
  },
  "Logging": {
    "EnableAuditLogging": true,
    "Level": "Information"
  }
}
```

### 16.5 Configuration Management API

```csharp
// Load configuration
var config = ConfigurationManager.LoadConfiguration(configPath);

// Get specific setting
var port = config.GetValue<int>("Server:Port");

// Update setting at runtime
ConfigurationManager.UpdateSetting("Cache:MaxSize", 536870912);

// Get configuration section as object
var serverOptions = config.GetSection("Server").Get<ServerOptions>();

// Validate configuration
var validation = ConfigurationManager.ValidateConfiguration(config);
if (!validation.IsValid)
{
    foreach (var error in validation.Errors)
    {
        Console.WriteLine($"Configuration error: {error}");
    }
}

// Save current configuration
ConfigurationManager.SaveConfiguration(config, outputPath);
```

---

## 17. Required Project Files

### LICENSE.txt
```
MIT License

Copyright (c) 2026 [Your Organization]

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### DEPENDENCIES.md
Create a file documenting all dependencies with:
- Package name
- Version
- License type
- License URL
- Compatibility status

### File Headers
All source files must include:
```csharp
// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.
```

---

## 18. JSON Configuration Files - Project Structure

Create the following JSON configuration files in the project root:

### appsettings.json
```json
{
  "Server": {
    "Host": "0.0.0.0",
    "Port": 9090,
    "MaxConnections": 10000,
    "ConnectionTimeout": 30000,
    "KeepAliveInterval": 60000,
    "ReceiveBufferSize": 65536,
    "SendBufferSize": 65536
  },
  "Security": {
    "EnableSSL": false,
    "CertificatePath": "",
    "CertificatePassword": "",
    "RequireAuthentication": true,
    "AllowedAuthMethods": ["JWT", "Token"],
    "SessionTimeout": 3600000,
    "MaxFailedAttempts": 5,
    "LockoutDuration": 900000
  },
  "Storage": {
    "DataPath": "./data",
    "MaxFileSize": 1073741824,
    "CompressionEnabled": false,
    "CompressionLevel": "optimal",
    "PersistenceMode": "fsync",
    "IndexCacheSize": 536870912
  },
  "Transaction": {
    "DefaultIsolationLevel": "ReadCommitted",
    "DefaultTimeout": 30000,
    "MaxConcurrentTransactions": 1000,
    "DeadlockDetection": true,
    "EnableWAL": true,
    "WALFlushInterval": 1000
  },
  "Cache": {
    "Enabled": true,
    "MaxSize": 268435456,
    "EvictionPolicy": "LRU",
    "DefaultTTL": 3600000,
    "EnableDistributed": false
  },
  "Logging": {
    "Level": "Information",
    "EnableFileLogging": true,
    "LogPath": "./logs",
    "MaxLogFileSize": 104857600,
    "MaxLogFiles": 10,
    "EnableConsoleLogging": true,
    "EnableAuditLogging": true
  },
  "Performance": {
    "MaxQueryTimeout": 60000,
    "EnableQueryOptimization": true,
    "BatchSize": 1000,
    "ThreadPoolMinThreads": 16,
    "ThreadPoolMaxThreads": 256,
    "GCMode": "Server"
  }
}
```

### appsettings.Development.json
```json
{
  "Server": {
    "Port": 9090,
    "MaxConnections": 100
  },
  "Logging": {
    "Level": "Debug",
    "EnableConsoleLogging": true
  },
  "Security": {
    "EnableSSL": false
  }
}
```

### appsettings.Production.json
```json
{
  "Server": {
    "Port": 9090,
    "MaxConnections": 10000
  },
  "Security": {
    "EnableSSL": true,
    "CertificatePath": "/etc/nosql-server/cert.pfx",
    "RequireAuthentication": true,
    "MaxFailedAttempts": 3
  },
  "Logging": {
    "Level": "Warning",
    "EnableConsoleLogging": false,
    "EnableFileLogging": true
  }
}
```

---

## 19. Core Interface Contracts (SOLID Foundation)

### 19.1 Dependency Injection Architecture
```
┌─────────────────────────────────────────────────────────────────┐
│                      IServiceCollection                          │
├─────────────────────────────────────────────────────────────────┤
│  Singleton: IConfigurationManager, IStorageEngine               │
│  Scoped: ITransactionContext, IQueryExecutor                    │
│  Transient: ICommandHandler, IDocumentSerializer                │
└─────────────────────────────────────────────────────────────────┘
```

### 19.2 Core Interfaces

```csharp
// AdvGenNoSqlServer.Core/Abstractions/IStorageEngine.cs
public interface IStorageEngine
{
    Task<Document?> GetAsync(string collection, string id, CancellationToken ct = default);
    Task<IEnumerable<Document>> GetManyAsync(string collection, IEnumerable<string> ids, CancellationToken ct = default);
    Task<bool> SetAsync(string collection, Document document, CancellationToken ct = default);
    Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default);
    Task<bool> ExistsAsync(string collection, string id, CancellationToken ct = default);
    Task<long> CountAsync(string collection, CancellationToken ct = default);
    IAsyncEnumerable<Document> ScanAsync(string collection, CancellationToken ct = default);
}

// AdvGenNoSqlServer.Core/Abstractions/IQueryEngine.cs
public interface IQueryEngine
{
    Task<QueryResult> ExecuteAsync(Query query, CancellationToken ct = default);
    QueryPlan Explain(Query query);
    bool ValidateQuery(Query query, out IReadOnlyList<string> errors);
}

// AdvGenNoSqlServer.Core/Abstractions/IIndexManager.cs
public interface IIndexManager
{
    Task CreateIndexAsync(string collection, IndexDefinition definition, CancellationToken ct = default);
    Task DropIndexAsync(string collection, string indexName, CancellationToken ct = default);
    Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(string collection, CancellationToken ct = default);
    Task RebuildIndexAsync(string collection, string indexName, CancellationToken ct = default);
    Task CreateIndexInBackgroundAsync(string collection, IndexDefinition definition, CancellationToken ct = default);
}

// Index types supported
public enum IndexType
{
    SingleField,      // Index on single field
    Compound,         // Multi-field composite index
    Unique,           // Unique constraint index
    Sparse,           // Only indexes documents containing the field
    Partial,          // Indexes subset of documents matching filter
    TTL,              // Time-to-live index for auto-expiration
    Text,             // Full-text search index (future)
    Geospatial        // 2D/2DSphere index (future)
}

public class IndexDefinition
{
    public required string Name { get; set; }
    public required List<IndexField> Fields { get; set; }
    public IndexType Type { get; set; } = IndexType.SingleField;
    public bool Unique { get; set; } = false;
    public bool Sparse { get; set; } = false;
    public JsonElement? PartialFilterExpression { get; set; }
    public TimeSpan? ExpireAfter { get; set; } // For TTL indexes
    public bool Background { get; set; } = false;
}

public class IndexField
{
    public required string FieldPath { get; set; }
    public SortDirection Direction { get; set; } = SortDirection.Ascending;
}

// AdvGenNoSqlServer.Core/Abstractions/IConnectionManager.cs
public interface IConnectionManager
{
    int ActiveConnections { get; }
    int MaxConnections { get; }
    Task<IClientConnection> AcceptAsync(CancellationToken ct = default);
    Task DisconnectAsync(string connectionId, DisconnectReason reason);
    IReadOnlyList<ConnectionInfo> GetActiveConnections();
}

// AdvGenNoSqlServer.Core/Abstractions/IAuthenticationProvider.cs
public interface IAuthenticationProvider
{
    Task<AuthResult> AuthenticateAsync(Credentials credentials, CancellationToken ct = default);
    Task<bool> ValidateTokenAsync(string token, CancellationToken ct = default);
    Task RevokeTokenAsync(string token, CancellationToken ct = default);
    Task<TokenInfo> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);
}

// AdvGenNoSqlServer.Core/Abstractions/ITransactionCoordinator.cs
public interface ITransactionCoordinator
{
    Task<ITransactionContext> BeginAsync(IsolationLevel isolation, CancellationToken ct = default);
    Task CommitAsync(string transactionId, CancellationToken ct = default);
    Task RollbackAsync(string transactionId, CancellationToken ct = default);
    TransactionStatus GetStatus(string transactionId);
}
```

---

## 20. Error Handling & Exception Strategy

### 20.1 Exception Hierarchy
```csharp
NoSqlServerException (base)
├── ConnectionException
│   ├── ConnectionTimeoutException
│   ├── ConnectionRefusedException
│   └── ConnectionLostException
├── AuthenticationException
│   ├── InvalidCredentialsException
│   ├── TokenExpiredException
│   └── AccountLockedException
├── AuthorizationException
│   ├── AccessDeniedException
│   └── InsufficientPrivilegesException
├── StorageException
│   ├── DocumentNotFoundException
│   ├── CollectionNotFoundException
│   ├── DuplicateKeyException
│   └── StorageCorruptionException
├── TransactionException
│   ├── TransactionTimeoutException
│   ├── DeadlockException
│   ├── OptimisticLockException
│   └── TransactionAbortedException
├── QueryException
│   ├── QuerySyntaxException
│   ├── QueryTimeoutException
│   └── InvalidQueryException
└── ConfigurationException
    ├── InvalidConfigurationException
    └── MissingConfigurationException
```

### 20.2 Error Response Format
```json
{
  "error": {
    "code": "DOCUMENT_NOT_FOUND",
    "message": "Document with id 'user123' not found in collection 'users'",
    "details": {
      "collection": "users",
      "documentId": "user123"
    },
    "timestamp": "2026-02-07T10:30:00Z",
    "requestId": "req_abc123",
    "retryable": false
  }
}
```

### 20.3 Error Codes
| Code | HTTP Equiv | Description |
|------|------------|-------------|
| `AUTH_FAILED` | 401 | Authentication failed |
| `ACCESS_DENIED` | 403 | Insufficient permissions |
| `NOT_FOUND` | 404 | Resource not found |
| `DUPLICATE_KEY` | 409 | Document already exists |
| `VALIDATION_ERROR` | 400 | Invalid request/document |
| `TRANSACTION_CONFLICT` | 409 | Optimistic lock failure |
| `DEADLOCK` | 409 | Deadlock detected |
| `TIMEOUT` | 408 | Operation timed out |
| `RATE_LIMITED` | 429 | Too many requests |
| `INTERNAL_ERROR` | 500 | Server internal error |
| `UNAVAILABLE` | 503 | Service unavailable |

---

## 21. Monitoring & Observability

### 21.1 Metrics (Prometheus-Compatible)

```csharp
// AdvGenNoSqlServer.Core/Metrics/IMetricsCollector.cs
public interface IMetricsCollector
{
    void IncrementCounter(string name, params KeyValuePair<string, string>[] labels);
    void RecordHistogram(string name, double value, params KeyValuePair<string, string>[] labels);
    void SetGauge(string name, double value, params KeyValuePair<string, string>[] labels);
}
```

#### Key Metrics
| Metric | Type | Description |
|--------|------|-------------|
| `nosql_connections_active` | Gauge | Current active connections |
| `nosql_connections_total` | Counter | Total connections established |
| `nosql_requests_total` | Counter | Total requests by command type |
| `nosql_request_duration_seconds` | Histogram | Request latency distribution |
| `nosql_documents_total` | Gauge | Total documents per collection |
| `nosql_storage_bytes` | Gauge | Storage usage in bytes |
| `nosql_cache_hits_total` | Counter | Cache hit count |
| `nosql_cache_misses_total` | Counter | Cache miss count |
| `nosql_transactions_active` | Gauge | Active transactions |
| `nosql_transactions_committed` | Counter | Committed transactions |
| `nosql_transactions_rolled_back` | Counter | Rolled back transactions |
| `nosql_deadlocks_total` | Counter | Deadlock occurrences |
| `nosql_query_duration_seconds` | Histogram | Query execution time |

### 21.2 Distributed Tracing
```csharp
// OpenTelemetry integration
public interface ITraceContext
{
    string TraceId { get; }
    string SpanId { get; }
    ISpan StartSpan(string operationName);
    void AddEvent(string name, IDictionary<string, object>? attributes = null);
    void SetAttribute(string key, object value);
}
```

### 21.3 Health Checks

```csharp
// AdvGenNoSqlServer.Core/Health/IHealthCheck.cs
public interface IHealthCheck
{
    string Name { get; }
    Task<HealthCheckResult> CheckAsync(CancellationToken ct = default);
}

public record HealthCheckResult(
    HealthStatus Status,
    string? Description = null,
    IReadOnlyDictionary<string, object>? Data = null);

public enum HealthStatus { Healthy, Degraded, Unhealthy }
```

#### Health Endpoints
| Endpoint | Purpose |
|----------|---------|
| `/health/live` | Liveness probe (is process running?) |
| `/health/ready` | Readiness probe (can accept traffic?) |
| `/health/startup` | Startup probe (initialization complete?) |
| `/health/detailed` | Full health report (admin only) |

### 21.4 Structured Logging Standards

```csharp
// Standard log context properties
public static class LogContext
{
    public const string RequestId = "RequestId";
    public const string ConnectionId = "ConnectionId";
    public const string TransactionId = "TransactionId";
    public const string Collection = "Collection";
    public const string DocumentId = "DocumentId";
    public const string UserId = "UserId";
    public const string Duration = "DurationMs";
    public const string Command = "Command";
}

// Log levels usage:
// - Trace: Detailed debugging (disabled in prod)
// - Debug: Development diagnostics
// - Information: Normal operations (connection, queries)
// - Warning: Recoverable issues (retries, degraded)
// - Error: Failed operations
// - Critical: System failures requiring immediate attention
```

---

## 22. Backup & Recovery Strategy

### 22.1 Backup Types
| Type | Description | RPO | RTO |
|------|-------------|-----|-----|
| **Full Backup** | Complete data snapshot | 24h | 4h |
| **Incremental** | Changes since last backup | 1h | 1h |
| **Continuous (WAL)** | Real-time WAL archival | <1s | 15m |
| **Point-in-Time** | Restore to specific moment | <1s | 30m |

### 22.2 Backup Interface
```csharp
// AdvGenNoSqlServer.Core/Backup/IBackupManager.cs
public interface IBackupManager
{
    Task<BackupInfo> CreateBackupAsync(BackupOptions options, CancellationToken ct = default);
    Task RestoreAsync(string backupId, RestoreOptions options, CancellationToken ct = default);
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken ct = default);
    Task DeleteBackupAsync(string backupId, CancellationToken ct = default);
    Task<BackupVerificationResult> VerifyBackupAsync(string backupId, CancellationToken ct = default);
}
```

### 22.3 Recovery Procedures
1. **Crash Recovery**: Auto-replay WAL on startup
2. **Point-in-Time Recovery**: Restore base + replay WAL to timestamp
3. **Corruption Recovery**: Checksum validation + segment repair
4. **Disaster Recovery**: Restore from offsite backup

---

## 23. Connection State Machine

```
                    ┌──────────────────┐
                    │   DISCONNECTED   │
                    └────────┬─────────┘
                             │ connect()
                             ▼
                    ┌──────────────────┐
                    │   CONNECTING     │───── timeout ──────┐
                    └────────┬─────────┘                    │
                             │ connected                    │
                             ▼                              │
                    ┌──────────────────┐                    │
                    │  AUTHENTICATING  │───── auth_fail ───┤
                    └────────┬─────────┘                    │
                             │ authenticated                │
                             ▼                              │
           ┌─────── ┌──────────────────┐ ───────┐          │
           │        │     READY        │        │          │
           │        └────────┬─────────┘        │          │
    idle_timeout             │ command          error       │
           │                 ▼                  │          │
           │        ┌──────────────────┐        │          │
           └──────► │   PROCESSING     │ ◄──────┘          │
                    └────────┬─────────┘                    │
                             │ done                         │
                             ▼                              │
                    ┌──────────────────┐                    │
                    │     READY        │                    │
                    └────────┬─────────┘                    │
                             │ close()                      │
                             ▼                              │
                    ┌──────────────────┐ ◄─────────────────┘
                    │   DISCONNECTING  │
                    └────────┬─────────┘
                             │ closed
                             ▼
                    ┌──────────────────┐
                    │   DISCONNECTED   │
                    └──────────────────┘
```

---

## 24. Protocol Versioning Strategy

### 24.1 Version Header
```
[Protocol Header: "NOSQ" (4 bytes)]
[Major Version (1 byte)]
[Minor Version (1 byte)]
[Capabilities Bitmap (2 bytes)]
```

### 24.2 Version Negotiation
1. Client sends supported version range
2. Server responds with selected version
3. Both parties use negotiated capabilities

### 24.3 Breaking vs Non-Breaking Changes
| Change Type | Version Bump | Compatibility |
|-------------|--------------|---------------|
| New optional field | Minor | Backward compatible |
| New command | Minor | Backward compatible |
| Field type change | Major | Breaking |
| Removed field | Major | Breaking |
| Protocol format change | Major | Breaking |

### 24.4 Deprecation Policy
- Minor version: 6 months notice
- Major version: 12 months notice
- Deprecated features logged as warnings

---

## 25. Collection Management

### 25.1 Collection Operations
```csharp
// AdvGenNoSqlServer.Core/Abstractions/ICollectionManager.cs
public interface ICollectionManager
{
    Task<bool> CreateCollectionAsync(string name, CollectionOptions options, CancellationToken ct = default);
    Task<bool> DropCollectionAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken ct = default);
    Task<CollectionStats> GetStatsAsync(string name, CancellationToken ct = default);
    Task<bool> RenameCollectionAsync(string oldName, string newName, CancellationToken ct = default);
    Task CompactAsync(string name, CancellationToken ct = default);
}

public record CollectionOptions(
    long? MaxDocuments = null,
    long? MaxSizeBytes = null,
    TimeSpan? DefaultTTL = null,
    bool? EnableCompression = null);

public record CollectionStats(
    string Name,
    long DocumentCount,
    long StorageSizeBytes,
    long IndexSizeBytes,
    DateTime CreatedAt,
    DateTime LastModifiedAt);
```

---

## 26. Document ID Generation

### 26.1 ID Strategies
| Strategy | Format | Use Case |
|----------|--------|----------|
| **UUID v7** | `018e1e4d-9abc-7def-8012-3456789abcde` | Time-ordered, globally unique |
| **ObjectId** | `65c5a3b4e1f2c3d4e5f6a7b8` | MongoDB-compatible, compact |
| **ULID** | `01ARZ3NDEKTSV4RRFFQ69G5FAV` | Sortable, URL-safe |
| **Custom** | User-provided | Application-specific |

### 26.2 Implementation
```csharp
// AdvGenNoSqlServer.Core/Abstractions/IIdGenerator.cs
public interface IIdGenerator
{
    string Generate();
    bool IsValid(string id);
    DateTime? ExtractTimestamp(string id);
}

// Default: UUID v7 (time-ordered)
public class UuidV7Generator : IIdGenerator
{
    public string Generate()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var random = RandomNumberGenerator.GetBytes(10);
        // UUID v7 construction...
    }
}
```

---

## 27. Rate Limiting & Throttling

### 27.1 Rate Limit Strategies
```csharp
public interface IRateLimiter
{
    Task<RateLimitResult> CheckAsync(string key, CancellationToken ct = default);
    Task<RateLimitResult> AcquireAsync(string key, int permits = 1, CancellationToken ct = default);
}

public record RateLimitResult(
    bool Allowed,
    int RemainingTokens,
    TimeSpan RetryAfter);
```

### 27.2 Rate Limit Configuration
```json
{
  "RateLimiting": {
    "Enabled": true,
    "Strategies": {
      "PerConnection": {
        "RequestsPerSecond": 1000,
        "BurstSize": 100
      },
      "PerUser": {
        "RequestsPerMinute": 10000,
        "BurstSize": 500
      },
      "Global": {
        "RequestsPerSecond": 50000,
        "BurstSize": 5000
      }
    },
    "ExemptRoles": ["Admin", "System"]
  }
}
```

---

## 28. Circuit Breaker & Resilience

### 28.1 Circuit Breaker States
```
        ┌──────────────────────────────────────────────┐
        │                                              │
        ▼                                              │
┌──────────────┐   failure threshold   ┌─────────────────┐
│    CLOSED    │ ───────────────────►  │     OPEN        │
│  (normal)    │                       │  (fail fast)    │
└──────────────┘                       └────────┬────────┘
        ▲                                       │
        │         success              timeout  │
        │                                       ▼
        │                              ┌─────────────────┐
        └───────────────────────────── │   HALF-OPEN     │
                                       │  (testing)      │
                                       └─────────────────┘
```

### 28.2 Resilience Policies
```csharp
// AdvGenNoSqlServer.Core/Resilience/IResiliencePolicy.cs
public interface IResiliencePolicy<T>
{
    Task<T> ExecuteAsync(Func<CancellationToken, Task<T>> action, CancellationToken ct = default);
}
```

### 28.3 Resilience Configuration
```json
{
  "Resilience": {
    "CircuitBreaker": {
      "FailureThreshold": 5,
      "SamplingDuration": 30000,
      "BreakDuration": 60000,
      "MinimumThroughput": 10
    },
    "Retry": {
      "MaxRetries": 3,
      "BackoffType": "Exponential",
      "BaseDelay": 100,
      "MaxDelay": 5000
    },
    "Timeout": {
      "DefaultTimeout": 30000,
      "MaxTimeout": 300000
    }
  }
}
```

---

## 29. Document Validation

### 29.1 Schema Validation
```csharp
// AdvGenNoSqlServer.Core/Validation/IDocumentValidator.cs
public interface IDocumentValidator
{
    ValidationResult Validate(Document document, JsonSchema? schema = null);
    Task<ValidationResult> ValidateAsync(Document document, string schemaName, CancellationToken ct = default);
}

public record ValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationError> Errors);

public record ValidationError(
    string Path,
    string ErrorCode,
    string Message);
```

### 29.2 Collection Schema Configuration
```json
{
  "collection": "users",
  "schema": {
    "type": "object",
    "required": ["email", "name"],
    "properties": {
      "email": { "type": "string", "format": "email" },
      "name": { "type": "string", "minLength": 1, "maxLength": 100 },
      "age": { "type": "integer", "minimum": 0 },
      "roles": { "type": "array", "items": { "type": "string" } }
    },
    "additionalProperties": true
  },
  "validationLevel": "strict",
  "validationAction": "error"
}
```

---

## 30. Client SDK Specification

### 30.1 Client Architecture
```
┌────────────────────────────────────────────────────────┐
│                    NoSqlClient                          │
├────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐ │
│  │ Connection   │  │    Query     │  │ Transaction  │ │
│  │    Pool      │  │   Builder    │  │   Context    │ │
│  └──────────────┘  └──────────────┘  └──────────────┘ │
├────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────┐ │
│  │              Protocol Handler                     │ │
│  │  (serialization, framing, compression)           │ │
│  └──────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────┐ │
│  │              Transport Layer (TCP/TLS)            │ │
│  └──────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────┘
```

### 30.2 Client API
```csharp
// AdvGenNoSqlServer.Client/INoSqlClient.cs
public interface INoSqlClient : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    Task<bool> PingAsync(CancellationToken ct = default);

    // Collection operations
    ICollection<T> GetCollection<T>(string name) where T : class;

    // Transaction support
    Task<IClientTransaction> BeginTransactionAsync(TransactionOptions? options = null, CancellationToken ct = default);

    // Bulk operations
    Task<BulkWriteResult> BulkWriteAsync(string collection, IEnumerable<WriteOperation> operations, CancellationToken ct = default);
}

public interface ICollection<T> where T : class
{
    Task<T?> GetAsync(string id, CancellationToken ct = default);
    Task<string> InsertAsync(T document, CancellationToken ct = default);
    Task<bool> UpdateAsync(string id, T document, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    IQueryable<T> AsQueryable();
    Task<IAsyncEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
}
```

### 30.3 Client Configuration
```csharp
var client = new NoSqlClientBuilder()
    .WithEndpoint("localhost", 9090)
    .WithCredentials("user", "password")
    .WithTls(options => options.ValidateCertificates = true)
    .WithConnectionPool(pool => {
        pool.MinConnections = 5;
        pool.MaxConnections = 100;
        pool.IdleTimeout = TimeSpan.FromMinutes(5);
    })
    .WithRetryPolicy(retry => {
        retry.MaxRetries = 3;
        retry.BackoffMultiplier = 2.0;
    })
    .WithTimeout(TimeSpan.FromSeconds(30))
    .Build();
```

---

## 31. Deployment Architecture

### 31.1 Container Configuration
```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 9090
EXPOSE 9091

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER nonroot:nonroot
ENTRYPOINT ["dotnet", "AdvGenNoSqlServer.Host.dll"]
```

### 31.2 Kubernetes Deployment
```yaml
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: nosql-server
spec:
  serviceName: nosql-server
  replicas: 3
  template:
    spec:
      containers:
      - name: nosql-server
        image: nosql-server:latest
        ports:
        - containerPort: 9090
          name: tcp
        - containerPort: 9091
          name: metrics
        resources:
          requests:
            memory: "512Mi"
            cpu: "500m"
          limits:
            memory: "2Gi"
            cpu: "2000m"
        livenessProbe:
          tcpSocket:
            port: 9090
          initialDelaySeconds: 10
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 9091
          initialDelaySeconds: 5
        volumeMounts:
        - name: data
          mountPath: /data
  volumeClaimTemplates:
  - metadata:
      name: data
    spec:
      accessModes: ["ReadWriteOnce"]
      resources:
        requests:
          storage: 100Gi
```

---

## 32. Project Folder Structure

```
AdvGenNoSqlServer/
├── src/
│   ├── AdvGenNoSqlServer.Core/
│   │   ├── Abstractions/          # Interfaces (IStorageEngine, IQueryEngine, etc.)
│   │   ├── Models/                # Document, Query, Result types
│   │   ├── Caching/               # ICacheManager implementations
│   │   ├── Configuration/         # ServerConfiguration, options
│   │   ├── Transactions/          # Transaction management
│   │   ├── Validation/            # Document validation
│   │   ├── Exceptions/            # Exception hierarchy
│   │   ├── Metrics/               # Metrics interfaces
│   │   ├── Health/                # Health check interfaces
│   │   └── Extensions/            # Extension methods
│   │
│   ├── AdvGenNoSqlServer.Storage/
│   │   ├── Engine/                # StorageEngine implementation
│   │   ├── Indexing/              # B-tree, hash index implementations
│   │   ├── FileIO/                # File management, memory-mapped files
│   │   ├── WAL/                   # Write-ahead log
│   │   └── Compression/           # Data compression
│   │
│   ├── AdvGenNoSqlServer.Query/
│   │   ├── Parser/                # Query parser
│   │   ├── Optimizer/             # Query optimizer
│   │   ├── Executor/              # Query execution
│   │   └── Aggregation/           # Aggregation pipeline
│   │
│   ├── AdvGenNoSqlServer.Network/
│   │   ├── Server/                # TcpServer, ConnectionHandler
│   │   ├── Protocol/              # Message framing, serialization
│   │   ├── Security/              # TLS, authentication
│   │   └── Pooling/               # Connection pooling
│   │
│   ├── AdvGenNoSqlServer.Server/
│   │   ├── Commands/              # Command handlers
│   │   ├── Pipeline/              # Request pipeline
│   │   └── Middleware/            # Logging, metrics, auth middleware
│   │
│   ├── AdvGenNoSqlServer.Host/
│   │   ├── Program.cs             # Entry point
│   │   └── appsettings*.json      # Configuration files
│   │
│   └── AdvGenNoSqlServer.Client/
│       ├── NoSqlClient.cs         # Main client class
│       ├── Connection/            # Connection management
│       ├── Builders/              # Query builders
│       └── Serialization/         # Client-side serialization
│
├── tests/
│   ├── AdvGenNoSqlServer.Tests/           # Unit tests
│   ├── AdvGenNoSqlServer.IntegrationTests/# Integration tests
│   └── AdvGenNoSqlServer.Benchmarks/      # Performance benchmarks
│
├── docs/
│   ├── architecture/             # Architecture documentation
│   ├── api/                      # API documentation
│   └── operations/               # Operational guides
│
├── scripts/
│   ├── build.ps1                 # Build scripts
│   └── docker/                   # Docker scripts
│
├── plan.md                       # This file
├── LICENSE.txt                   # MIT License
├── DEPENDENCIES.md               # License compliance
└── README.md                     # Project overview
```

---

## 33. Code Quality Standards

### 33.1 Static Analysis
```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  <AnalysisLevel>latest-all</AnalysisLevel>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="StyleCop.Analyzers" Version="1.*" PrivateAssets="all" />
  <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="8.*" PrivateAssets="all" />
  <PackageReference Include="SonarAnalyzer.CSharp" Version="9.*" PrivateAssets="all" />
</ItemGroup>
```

### 33.2 Code Coverage Requirements
| Component | Minimum Coverage |
|-----------|-----------------|
| Core | 90% |
| Storage | 85% |
| Query | 85% |
| Network | 80% |
| Client | 85% |

### 33.3 Performance Baselines
```csharp
// Must meet these benchmarks on standard hardware (4 core, 16GB RAM)
[Benchmark]
public void SingleDocumentRead() => // < 1ms p99

[Benchmark]
public void SingleDocumentWrite() => // < 5ms p99

[Benchmark]
public void BulkWrite1000Docs() => // < 100ms p99

[Benchmark]
public void SimpleQuery() => // < 10ms p99

[Benchmark]
public void IndexedQuery() => // < 5ms p99
```

---

## 34. Implementation Priority Matrix

### Phase 1: Critical Path (Must Have)
| Component | Priority | Dependency |
|-----------|----------|------------|
| Core Abstractions (Interfaces) | P0 | None |
| Document Model | P0 | None |
| File Storage Engine | P0 | Document Model |
| TCP Server | P0 | Core Abstractions |
| Basic Authentication | P0 | TCP Server |
| Configuration Manager | P0 | None |

### Phase 2: Essential (Should Have)
| Component | Priority | Dependency |
|-----------|----------|------------|
| Transaction Manager | P1 | Storage Engine |
| Write-Ahead Log | P1 | Transaction Manager |
| Query Parser | P1 | Document Model |
| B-Tree Index | P1 | Storage Engine |
| Memory Cache | P1 | Core Abstractions |
| Client SDK | P1 | TCP Server |

### Phase 3: Important (Nice to Have)
| Component | Priority | Dependency |
|-----------|----------|------------|
| Query Optimizer | P2 | Query Parser |
| Aggregation Pipeline | P2 | Query Executor |
| Rate Limiting | P2 | TCP Server |
| Metrics/Monitoring | P2 | All Components |
| Backup Manager | P2 | Storage Engine |
| Schema Validation | P2 | Document Model |

---

## 35. Command Reference

### 35.1 Document Commands
| Command | Description | Syntax |
|---------|-------------|--------|
| `GET` | Retrieve document by ID | `GET collection document_id` |
| `SET` | Create/update document | `SET collection document_id {data}` |
| `DELETE` | Delete document | `DELETE collection document_id` |
| `EXISTS` | Check document exists | `EXISTS collection document_id` |
| `MGET` | Get multiple documents | `MGET collection [ids...]` |
| `MSET` | Set multiple documents | `MSET collection [{docs...}]` |
| `INSERT` | Insert new document (fails if exists) | `INSERT collection document_id {data}` |
| `REPLACE` | Replace document (fails if not exists) | `REPLACE collection document_id {data}` |
| `TOUCH` | Update document timestamp only | `TOUCH collection document_id` |

### 35.2 Collection Commands
| Command | Description | Syntax |
|---------|-------------|--------|
| `CREATE_COLLECTION` | Create new collection | `CREATE_COLLECTION name {options}` |
| `DROP_COLLECTION` | Drop collection | `DROP_COLLECTION name` |
| `LIST_COLLECTIONS` | List all collections | `LIST_COLLECTIONS` |
| `COLLECTION_STATS` | Get collection stats | `COLLECTION_STATS name` |

### 35.3 Transaction Commands
| Command | Description | Syntax |
|---------|-------------|--------|
| `BEGIN` | Start transaction | `BEGIN [isolation_level]` |
| `COMMIT` | Commit transaction | `COMMIT` |
| `ROLLBACK` | Rollback transaction | `ROLLBACK` |

### 35.4 Query Commands
| Command | Description | Syntax |
|---------|-------------|--------|
| `FIND` | Query documents | `FIND collection {filter} {options}` |
| `FIND_ONE` | Find single document | `FIND_ONE collection {filter}` |
| `COUNT` | Count documents | `COUNT collection {filter}` |
| `AGGREGATE` | Run aggregation | `AGGREGATE collection [{pipeline}]` |
| `DISTINCT` | Get distinct field values | `DISTINCT collection field {filter}` |
| `EXPLAIN` | Explain query plan | `EXPLAIN collection {filter}` |

### 35.4.1 Index Commands
| Command | Description | Syntax |
|---------|-------------|--------|
| `CREATE_INDEX` | Create index | `CREATE_INDEX collection {definition}` |
| `DROP_INDEX` | Drop index | `DROP_INDEX collection index_name` |
| `LIST_INDEXES` | List all indexes | `LIST_INDEXES collection` |
| `REINDEX` | Rebuild index | `REINDEX collection index_name` |
| `INDEX_STATS` | Get index statistics | `INDEX_STATS collection index_name` |

### 35.5 Admin Commands
| Command | Description | Syntax |
|---------|-------------|--------|
| `PING` | Health check | `PING` |
| `INFO` | Server info | `INFO [section]` |
| `CONFIG` | Get/set config | `CONFIG GET/SET key [value]` |
| `SHUTDOWN` | Graceful shutdown | `SHUTDOWN [NOSAVE]` |

### 35.6 Atomic Update Commands
| Command | Description | Syntax |
|---------|-------------|--------|
| `UPSERT` | Insert or update document | `UPSERT collection doc_id {data}` |
| `FIND_AND_MODIFY` | Atomic find and update | `FIND_AND_MODIFY collection {filter} {update} {options}` |
| `INCREMENT` | Atomic increment field | `INCREMENT collection doc_id field amount` |
| `PUSH` | Push to array field | `PUSH collection doc_id field value` |
| `PULL` | Remove from array field | `PULL collection doc_id field value` |
| `ADD_TO_SET` | Add unique to array | `ADD_TO_SET collection doc_id field value` |
| `PATCH` | Partial document update | `PATCH collection doc_id {partial_data}` |

---

## 37. Change Streams & Real-Time Subscriptions (Planned)

### 37.1 Overview
Change streams allow applications to subscribe to real-time data changes without polling. This is critical for:
- Event-driven architectures
- Real-time dashboards
- Cache invalidation
- Data synchronization

### 37.2 Change Stream Interface
```csharp
// AdvGenNoSqlServer.Core/Abstractions/IChangeStreamProvider.cs
public interface IChangeStreamProvider
{
    IAsyncEnumerable<ChangeEvent> WatchCollectionAsync(
        string collection,
        ChangeStreamOptions? options = null,
        CancellationToken ct = default);

    IAsyncEnumerable<ChangeEvent> WatchDatabaseAsync(
        string database,
        ChangeStreamOptions? options = null,
        CancellationToken ct = default);

    IAsyncEnumerable<ChangeEvent> WatchDocumentAsync(
        string collection,
        string documentId,
        CancellationToken ct = default);
}

public record ChangeEvent(
    string OperationType,      // insert, update, delete, replace
    string Collection,
    string DocumentId,
    Document? FullDocument,
    Document? UpdateDescription,  // For updates: { updatedFields, removedFields }
    DateTime Timestamp,
    string ResumeToken);       // For resuming after disconnect

public class ChangeStreamOptions
{
    public string? ResumeToken { get; set; }
    public DateTime? StartAtOperationTime { get; set; }
    public bool FullDocument { get; set; } = false;
    public bool FullDocumentBeforeChange { get; set; } = false;
    public JsonElement? Pipeline { get; set; }  // Filter/transform changes
}
```

### 37.3 Subscription Commands
| Command | Description | Syntax |
|---------|-------------|--------|
| `SUBSCRIBE` | Subscribe to changes | `SUBSCRIBE collection {options}` |
| `UNSUBSCRIBE` | Cancel subscription | `UNSUBSCRIBE subscription_id` |
| `RESUME` | Resume from token | `RESUME subscription_id resume_token` |

### 37.4 Implementation Approach
- Store changes in WAL with sequence numbers
- Maintain per-connection subscription state
- Push changes over existing TCP connection
- Support resume tokens for reconnection
- Optional filtering via aggregation pipeline

---

## 38. Write Concern & Read Preference (Planned)

### 38.1 Write Concern
Controls acknowledgement level for write operations:

```csharp
public class WriteConcern
{
    public static WriteConcern Unacknowledged => new() { W = 0 };
    public static WriteConcern Acknowledged => new() { W = 1 };
    public static WriteConcern Journaled => new() { W = 1, Journal = true };
    public static WriteConcern Majority => new() { W = "majority" };

    public object W { get; set; } = 1;        // 0, 1, "majority", or number
    public bool Journal { get; set; } = false; // Wait for journal write
    public TimeSpan? WTimeout { get; set; }    // Timeout for acknowledgement
}
```

### 38.2 Read Preference (for future replication)
Controls which nodes can serve read operations:

```csharp
public enum ReadPreference
{
    Primary,           // Only primary node
    PrimaryPreferred,  // Primary, fallback to secondary
    Secondary,         // Only secondary nodes
    SecondaryPreferred,// Secondary, fallback to primary
    Nearest            // Lowest latency node
}
```

---

## 39. Cursor-Based Pagination

### 39.1 Overview
For efficient pagination of large result sets without OFFSET performance degradation:

```csharp
public interface ICursor<T> : IAsyncDisposable
{
    string CursorId { get; }
    Task<IReadOnlyList<T>> GetNextBatchAsync(int batchSize, CancellationToken ct = default);
    Task<bool> HasMoreAsync(CancellationToken ct = default);
    long? TotalCount { get; }
}

// Usage
var cursor = await collection.FindAsync(filter, new FindOptions { BatchSize = 100 });
while (await cursor.HasMoreAsync())
{
    var batch = await cursor.GetNextBatchAsync(100);
    // Process batch
}
```

### 39.2 Cursor Commands
| Command | Description | Syntax |
|---------|-------------|--------|
| `GET_MORE` | Get next batch from cursor | `GET_MORE cursor_id batch_size` |
| `KILL_CURSOR` | Close cursor | `KILL_CURSOR cursor_id` |
| `LIST_CURSORS` | List active cursors | `LIST_CURSORS` |

### 39.3 Cursor Configuration
```json
{
  "Cursor": {
    "DefaultBatchSize": 101,
    "MaxBatchSize": 10000,
    "TimeoutMinutes": 10,
    "MaxActiveCursors": 10000
  }
}
```

---

## 40. Slow Query Logging & Profiling

### 40.1 Slow Query Configuration
```json
{
  "Profiling": {
    "Enabled": true,
    "SlowQueryThresholdMs": 100,
    "LogQueryPlan": true,
    "SampleRate": 1.0,
    "MaxLoggedQueries": 10000
  }
}
```

### 40.2 Profiler Interface
```csharp
public interface IQueryProfiler
{
    void RecordQuery(QueryProfile profile);
    Task<IReadOnlyList<QueryProfile>> GetSlowQueriesAsync(int limit = 100);
    Task ClearProfileDataAsync();
}

public record QueryProfile(
    string QueryId,
    string Collection,
    JsonElement Query,
    QueryPlan? Plan,
    long DurationMs,
    long DocumentsExamined,
    long DocumentsReturned,
    bool UsedIndex,
    string? IndexUsed,
    DateTime Timestamp,
    string? User);
```

### 40.3 EXPLAIN Command
```csharp
// Detailed query plan analysis
var plan = await client.ExplainAsync(query, ExplainVerbosity.ExecutionStats);

public enum ExplainVerbosity
{
    QueryPlanner,      // Just the plan
    ExecutionStats,    // Plan + execution statistics
    AllPlansExecution  // All candidate plans compared
}
```

---

## 41. Import/Export Tools

### 41.1 Export Formats
- **JSON Lines** (.jsonl) - One document per line, streaming-friendly
- **BSON** - Binary JSON, compact and type-preserving
- **CSV** - For tabular data export

### 41.2 Export Interface
```csharp
public interface IDataExporter
{
    Task ExportCollectionAsync(string collection, string outputPath, ExportOptions options, CancellationToken ct = default);
    Task ExportDatabaseAsync(string database, string outputPath, ExportOptions options, CancellationToken ct = default);
    Task ExportQueryAsync(Query query, string outputPath, ExportOptions options, CancellationToken ct = default);
}

public class ExportOptions
{
    public ExportFormat Format { get; set; } = ExportFormat.JsonLines;
    public bool IncludeIndexes { get; set; } = true;
    public bool Compress { get; set; } = false;
    public JsonElement? Query { get; set; }  // Filter documents
    public List<string>? Fields { get; set; } // Project specific fields
}
```

### 41.3 Import Interface
```csharp
public interface IDataImporter
{
    Task<ImportResult> ImportAsync(string inputPath, string collection, ImportOptions options, CancellationToken ct = default);
}

public class ImportOptions
{
    public ImportMode Mode { get; set; } = ImportMode.Insert;
    public bool DropCollection { get; set; } = false;
    public bool StopOnError { get; set; } = false;
    public int BatchSize { get; set; } = 1000;
}

public enum ImportMode
{
    Insert,    // Fail on duplicates
    Upsert,    // Insert or update
    Merge      // Update only existing
}
```

---

## 42. Sessions & Unit of Work

### 42.1 Session Interface
Sessions provide causally consistent operations and are required for transactions:

```csharp
public interface ISession : IAsyncDisposable
{
    string SessionId { get; }
    SessionOptions Options { get; }

    // All operations within session are causally consistent
    Task<Document?> GetAsync(string collection, string id, CancellationToken ct = default);
    Task SetAsync(string collection, Document document, CancellationToken ct = default);

    // Transaction support
    Task StartTransactionAsync(TransactionOptions? options = null, CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task AbortTransactionAsync(CancellationToken ct = default);
}

public class SessionOptions
{
    public bool CausalConsistency { get; set; } = true;
    public TimeSpan DefaultTransactionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public ReadPreference ReadPreference { get; set; } = ReadPreference.Primary;
    public WriteConcern WriteConcern { get; set; } = WriteConcern.Acknowledged;
}
```

### 42.2 Unit of Work Pattern (RavenDB-style)
```csharp
public interface IDocumentSession : IAsyncDisposable
{
    // Track changes
    T Load<T>(string id) where T : class;
    void Store<T>(T entity) where T : class;
    void Delete<T>(T entity) where T : class;
    void Delete(string id);

    // Persist all tracked changes
    Task SaveChangesAsync(CancellationToken ct = default);

    // Query with change tracking
    IQueryable<T> Query<T>(string? collection = null) where T : class;

    // Advanced
    IAdvancedSessionOperations Advanced { get; }
}

// Usage
await using var session = store.OpenSession();
var user = await session.LoadAsync<User>("users/1");
user.Name = "Updated Name";
await session.SaveChangesAsync();  // Tracks and saves only changed documents
```

---

## 43. Optimistic Concurrency (ETags)

### 43.1 Overview
Prevent lost updates using document version/ETag:

```csharp
public class Document
{
    public required string Id { get; set; }
    public required JsonElement Data { get; set; }
    public string? ETag { get; set; }        // Version for concurrency
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// Update with concurrency check
var result = await client.UpdateAsync(collection, document, new UpdateOptions
{
    ExpectedETag = "abc123"  // Fails if document changed since read
});

if (!result.Success && result.Error == ErrorCode.ConcurrencyConflict)
{
    // Handle conflict: reload and retry
}
```

### 43.2 Concurrency Modes
```csharp
public enum ConcurrencyMode
{
    None,           // Last write wins
    Optimistic,     // Check ETag
    Pessimistic     // Acquire lock
}
```

---

## 44. Projections

### 44.1 Overview
Return only specific fields to reduce network overhead and memory usage:

```csharp
// Project specific fields
var users = await collection.FindAsync(
    filter: { "status": "active" },
    projection: { "name": 1, "email": 1, "_id": 1 }  // Include only these
);

// Exclude fields
var users = await collection.FindAsync(
    filter: { },
    projection: { "password": 0, "internalNotes": 0 }  // Exclude these
);
```

### 44.2 Projection Operations
```csharp
public class Projection
{
    public List<string> Include { get; set; } = new();   // Fields to include
    public List<string> Exclude { get; set; } = new();   // Fields to exclude
    public Dictionary<string, string> Rename { get; set; } = new();  // Rename fields
    public Dictionary<string, Expression> Computed { get; set; } = new();  // Computed fields
}

// Advanced projections (MongoDB-style)
var projection = new {
    fullName = new { $concat = ["$firstName", " ", "$lastName"] },
    yearOfBirth = new { $year = "$dateOfBirth" },
    isAdult = new { $gte = ["$age", 18] }
};
```

---

## 45. Field-Level Encryption (Planned)

### 45.1 Overview
Encrypt sensitive fields before storage, decrypt on read:

```csharp
// Collection-level encryption config
{
    "collection": "users",
    "encryptedFields": {
        "ssn": { "algorithm": "AEAD_AES_256_CBC_HMAC_SHA_512" },
        "creditCard": { "algorithm": "AEAD_AES_256_CBC_HMAC_SHA_512" }
    }
}

public interface IFieldEncryptor
{
    byte[] Encrypt(string fieldPath, byte[] plaintext, EncryptionContext context);
    byte[] Decrypt(string fieldPath, byte[] ciphertext, EncryptionContext context);
}
```

### 45.2 Key Management
```csharp
public interface IKeyVault
{
    Task<DataKey> CreateKeyAsync(string keyAltName, KeyOptions options);
    Task<DataKey> GetKeyAsync(string keyId);
    Task<DataKey> GetKeyByAltNameAsync(string keyAltName);
    Task RotateKeyAsync(string keyId);
}
```

---

## 46. Multi-Database Architecture (Planned)

### 36.1 Current State Analysis

**Existing Architecture**:
- Flat structure: Server → Collections → Documents
- Global authentication: Single `RequireAuthentication` flag with master password
- Universal cache: `ICacheManager` stores documents by key (`collection:documentId`)
- No database isolation: All collections share the same namespace

**Problems with Current Design**:
1. No logical separation between different applications/tenants
2. No per-database access control
3. Collection name conflicts possible across different use cases
4. Cannot backup/restore individual databases

### 36.2 Target Architecture

```
Server
  └── Database (e.g., "myapp", "analytics", "admin")
        ├── Security
        │     ├── Admins (full access)
        │     ├── Members (read/write documents)
        │     └── Readers (read-only)
        ├── Collections
        │     └── Documents
        ├── Indexes (per-collection)
        └── Configuration (limits, settings)
```

### 36.3 Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Database model | CouchDB-style per-database security | Simple, fits document model well |
| Storage layout | `data/{database}/{collection}/` | Clear isolation, easy backup |
| Default database | `default` | Backwards compatibility |
| Auth database | `_system` | Store users, roles, system config |

### 36.4 Implementation Phases

#### Phase 1: Database Model (Foundation)

**Goal**: Introduce `Database` class with isolated storage

**New Files**:
- `AdvGenNoSqlServer.Core/Models/Database.cs`
- `AdvGenNoSqlServer.Core/Models/DatabaseSecurity.cs`
- `AdvGenNoSqlServer.Storage/DatabaseManager.cs`

**Database.cs**:
```csharp
public class Database
{
    public required string Name { get; set; }
    public DatabaseSecurity Security { get; set; } = new();
    public DatabaseConfiguration Configuration { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DatabaseSecurity
{
    public List<string> Admins { get; set; } = new();
    public List<string> Members { get; set; } = new();
    public List<string> Readers { get; set; } = new();
}

public class DatabaseConfiguration
{
    public int MaxCollections { get; set; } = 100;
    public long MaxSizeBytes { get; set; } = 10_737_418_240; // 10GB
    public bool AllowAnonymousRead { get; set; } = false;
}
```

**Storage Layout**:
```
data/
  _system/                    # System database
    users/                    # User documents
    databases/                # Database metadata
  default/                    # Default database
    collection1/
    collection2/
  myapp/                      # User database
    users/
    products/
```

**Tasks**:
- [ ] Create `Database` and `DatabaseSecurity` models
- [ ] Create `DatabaseManager` class
- [ ] Update `PersistentDocumentStore` to accept database parameter
- [ ] Create `_system` and `default` databases on initialization
- [ ] Add database path isolation

#### Phase 2: Protocol & Commands

**Goal**: Add database context to all operations

**New Commands**:
| Command | Description | Example |
|---------|-------------|---------|
| `USE` | Switch database context | `USE myapp` |
| `CREATE DATABASE` | Create new database | `CREATE DATABASE myapp` |
| `DROP DATABASE` | Delete database | `DROP DATABASE myapp` |
| `LIST DATABASES` | List all databases | `LIST DATABASES` |
| `SHOW DATABASE` | Show current database info | `SHOW DATABASE` |

**MessageType Additions**:
```csharp
public enum MessageType
{
    // ... existing types ...
    DatabaseOperation = 10,  // CREATE, DROP, LIST, USE
}
```

**Protocol Changes**:
- Add `database` field to command messages
- Track current database per connection in `ConnectionState`

**Tasks**:
- [ ] Add `DatabaseOperation` message type
- [ ] Implement `USE` command in server
- [ ] Implement `CREATE DATABASE` command
- [ ] Implement `DROP DATABASE` command
- [ ] Implement `LIST DATABASES` command
- [ ] Add `CurrentDatabase` to connection state
- [ ] Update all existing commands to use database context

#### Phase 3: Per-Database Security

**Goal**: Implement database-level access control

**User Model**:
```csharp
public class User
{
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string Salt { get; set; }
    public List<string> Roles { get; set; } = new();
    public Dictionary<string, DatabaseRole> DatabaseAccess { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum DatabaseRole
{
    Admin,      // Full access including security changes
    Member,     // Read/write documents and indexes
    Reader      // Read-only access
}
```

**Authorization Flow**:
```
1. User authenticates → receives token
2. User sends USE database_name
3. Server checks: user.DatabaseAccess[database_name] or database.Security
4. If authorized → set connection database context
5. All subsequent operations checked against role
```

**Tasks**:
- [ ] Create `User` model with database access map
- [ ] Update `AuthenticationService` to return user with roles
- [ ] Create `AuthorizationService` for per-database checks
- [ ] Implement security check middleware for all commands
- [ ] Add `GRANT` and `REVOKE` commands for database access

#### Phase 4: Client Updates

**Goal**: Update client library to support database selection

**Connection String Format**:
```
advgen://user:password@localhost:9091/database_name
```

**Client API Changes**:
```csharp
// New constructor with database
var client = new AdvGenNoSqlClient("localhost:9091", "myapp", options);

// Or switch database after connection
await client.UseDatabaseAsync("myapp");

// Database operations
await client.CreateDatabaseAsync("newdb");
await client.DropDatabaseAsync("olddb");
var databases = await client.ListDatabasesAsync();
```

**Tasks**:
- [ ] Update `AdvGenNoSqlClient` constructor to accept database name
- [ ] Add `UseDatabaseAsync()` method
- [ ] Add `CreateDatabaseAsync()` method
- [ ] Add `DropDatabaseAsync()` method
- [ ] Add `ListDatabasesAsync()` method
- [ ] Update connection string parser

#### Phase 5: Migration & Compatibility

**Goal**: Migrate existing data and maintain backwards compatibility

**Migration Strategy**:
1. Existing data moved to `default` database
2. Connections without database specification use `default`
3. Old protocol messages without database field use current context

**Tasks**:
- [ ] Create migration utility for existing data
- [ ] Add fallback to `default` database
- [ ] Update documentation
- [ ] Add migration tests

### 36.5 Testing Strategy

**Unit Tests**:
- Database creation/deletion
- Security object management
- Authorization checks for each role
- Database isolation verification

**Integration Tests**:
- Multi-database operations
- Cross-database queries (should fail)
- User switching databases
- Concurrent database access

**Security Tests**:
- Unauthorized database access attempts
- Role escalation attempts
- SQL/NoSQL injection in database names

### 36.6 File Changes Summary

| File | Change Type | Description |
|------|-------------|-------------|
| `Core/Models/Database.cs` | New | Database model |
| `Core/Models/DatabaseSecurity.cs` | New | Security model |
| `Core/Models/User.cs` | Modified | Add database access |
| `Storage/DatabaseManager.cs` | New | Database operations |
| `Storage/PersistentDocumentStore.cs` | Modified | Add database parameter |
| `Server/NoSqlServer.cs` | Modified | Database commands |
| `Network/MessageProtocol.cs` | Modified | New message types |
| `Client/Client.cs` | Modified | Database methods |

### 36.7 References

- [MongoDB Multi-Tenant Architecture](https://www.mongodb.com/docs/atlas/build-multi-tenant-arch/)
- [CouchDB Security Model](https://docs.couchdb.org/en/stable/intro/security.html)
- [CouchDB Per-Database Security](https://docs.couchdb.org/en/stable/api/database/security.html)
- [Redis Multi-Tenancy](https://redis.io/blog/multi-tenancy-redis-enterprise/)
- [Managing Multitenancy in Redis](https://reintech.io/blog/managing-multitenancy-redis)

---

## 47. Peer-to-Peer (P2P) Cluster Architecture

### 47.1 Overview

Enable multiple AdvGenNoSqlServer instances to connect, synchronize, and operate as a distributed cluster. This provides:
- **High Availability**: Automatic failover when nodes go down
- **Scalability**: Distribute load across multiple nodes
- **Data Redundancy**: Replicate data for durability
- **Geographic Distribution**: Deploy nodes in different regions

### 47.2 Cluster Topology

```
┌─────────────────────────────────────────────────────────────────┐
│                        Cluster: "production"                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│    ┌──────────────┐     ┌──────────────┐     ┌──────────────┐   │
│    │   Node A     │◄───►│   Node B     │◄───►│   Node C     │   │
│    │  (Leader)    │     │  (Follower)  │     │  (Follower)  │   │
│    │  10.0.0.1    │     │  10.0.0.2    │     │  10.0.0.3    │   │
│    └──────────────┘     └──────────────┘     └──────────────┘   │
│           │                    │                    │            │
│           └────────────────────┼────────────────────┘            │
│                                │                                  │
│                    ┌───────────▼───────────┐                     │
│                    │   Gossip Protocol     │                     │
│                    │   (Node Discovery)    │                     │
│                    └───────────────────────┘                     │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

### 47.3 Cluster Modes

| Mode | Description | Use Case |
|------|-------------|----------|
| **Leader-Follower** | One leader handles writes, followers replicate | Strong consistency, simple |
| **Multi-Leader** | Multiple nodes accept writes | High write throughput, eventual consistency |
| **Leaderless** | Any node handles any operation | Maximum availability, conflict resolution needed |

### 47.4 Core Components

#### 47.4.1 Node Identity
```csharp
public class NodeIdentity
{
    public required string NodeId { get; set; }           // Unique GUID
    public required string ClusterId { get; set; }        // Cluster membership
    public required string Host { get; set; }             // IP or hostname
    public required int Port { get; set; }                // TCP port
    public required int P2PPort { get; set; }             // Inter-node port (separate from client port)
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();  // For node authentication
    public string[] Tags { get; set; } = Array.Empty<string>();   // e.g., "primary", "analytics", "region-us"
    public DateTime JoinedAt { get; set; }
    public NodeState State { get; set; } = NodeState.Joining;
}

public enum NodeState
{
    Joining,      // Node is joining cluster
    Syncing,      // Node is catching up on data
    Active,       // Node is fully operational
    Leaving,      // Node is gracefully departing
    Dead          // Node is unreachable
}
```

#### 47.4.2 Cluster Manager Interface
```csharp
// AdvGenNoSqlServer.Core/Abstractions/IClusterManager.cs
public interface IClusterManager
{
    // Cluster membership
    Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default);
    Task JoinClusterAsync(string seedNode, JoinOptions options, CancellationToken ct = default);
    Task LeaveClusterAsync(LeaveOptions options, CancellationToken ct = default);

    // Node management
    Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken ct = default);
    Task<NodeInfo?> GetNodeAsync(string nodeId, CancellationToken ct = default);
    Task RemoveNodeAsync(string nodeId, CancellationToken ct = default);

    // Leader election
    Task<NodeInfo> GetLeaderAsync(CancellationToken ct = default);
    Task RequestLeaderElectionAsync(CancellationToken ct = default);

    // Events
    event EventHandler<NodeJoinedEventArgs> NodeJoined;
    event EventHandler<NodeLeftEventArgs> NodeLeft;
    event EventHandler<LeaderChangedEventArgs> LeaderChanged;
}

public class ClusterInfo
{
    public required string ClusterId { get; set; }
    public required string ClusterName { get; set; }
    public required NodeInfo Leader { get; set; }
    public required IReadOnlyList<NodeInfo> Nodes { get; set; }
    public ClusterHealth Health { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### 47.5 Node Discovery

#### 47.5.1 Discovery Methods

| Method | Description | Configuration |
|--------|-------------|---------------|
| **Static Seeds** | Predefined list of seed nodes | `Seeds: ["10.0.0.1:9092", "10.0.0.2:9092"]` |
| **DNS** | DNS SRV records for node discovery | `DnsName: "nodes.nosql.local"` |
| **Multicast** | UDP multicast for LAN discovery | `MulticastGroup: "239.255.0.1:9093"` |
| **Kubernetes** | K8s headless service discovery | `K8sNamespace: "nosql", K8sService: "nosql-headless"` |
| **Consul/etcd** | Service registry integration | `ConsulAddress: "http://consul:8500"` |

#### 47.5.2 Gossip Protocol
```csharp
// Gossip message for node state propagation
public class GossipMessage
{
    public required string SenderId { get; set; }
    public required long Generation { get; set; }       // Monotonic counter
    public required long Version { get; set; }          // State version
    public required Dictionary<string, NodeState> NodeStates { get; set; }
    public required Dictionary<string, long> Heartbeats { get; set; }
    public byte[] Signature { get; set; } = Array.Empty<byte>();  // Signed by sender
}

public interface IGossipProtocol
{
    Task BroadcastAsync(GossipMessage message, CancellationToken ct = default);
    Task<GossipMessage> ReceiveAsync(CancellationToken ct = default);
    Task SyncWithPeerAsync(string nodeId, CancellationToken ct = default);
}
```

### 47.6 Data Replication

#### 47.6.1 Replication Strategies

| Strategy | Description | Consistency |
|----------|-------------|-------------|
| **Synchronous** | Wait for all replicas before acknowledging | Strong |
| **Semi-Synchronous** | Wait for majority (quorum) | Strong |
| **Asynchronous** | Acknowledge immediately, replicate in background | Eventual |

#### 47.6.2 Replication Interface
```csharp
public interface IReplicationManager
{
    // Configure replication
    Task SetReplicationFactorAsync(string collection, int factor, CancellationToken ct = default);

    // Replicate operations
    Task ReplicateWriteAsync(ReplicationEvent evt, CancellationToken ct = default);
    Task<ReplicationAck> WaitForAcksAsync(string operationId, int requiredAcks, TimeSpan timeout, CancellationToken ct = default);

    // Sync
    Task<SyncStatus> GetSyncStatusAsync(string nodeId, CancellationToken ct = default);
    Task RequestFullSyncAsync(string nodeId, CancellationToken ct = default);
}

public class ReplicationEvent
{
    public required string OperationId { get; set; }
    public required string SourceNodeId { get; set; }
    public required OperationType Type { get; set; }    // Insert, Update, Delete
    public required string Collection { get; set; }
    public required string DocumentId { get; set; }
    public Document? Document { get; set; }
    public required long SequenceNumber { get; set; }   // From WAL
    public required DateTime Timestamp { get; set; }
    public required byte[] Checksum { get; set; }
}
```

#### 47.6.3 Conflict Resolution
```csharp
public enum ConflictResolutionStrategy
{
    LastWriteWins,        // Use timestamp (default)
    FirstWriteWins,       // Keep original
    HighestVersion,       // Use ETag/version
    MergeFields,          // Merge non-conflicting fields
    Custom                // User-defined resolver
}

public interface IConflictResolver
{
    Document Resolve(Document local, Document remote, ConflictContext context);
}

// Example: Last-Write-Wins resolver
public class LastWriteWinsResolver : IConflictResolver
{
    public Document Resolve(Document local, Document remote, ConflictContext context)
    {
        return local.UpdatedAt >= remote.UpdatedAt ? local : remote;
    }
}
```

### 47.7 Security (Inter-Node Communication)

#### 47.7.1 Security Architecture
```
┌─────────────────────────────────────────────────────────────────┐
│                    P2P Security Layers                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  Layer 4: Application-Level Authentication               │   │
│   │  - Cluster secret/token validation                       │   │
│   │  - Node identity verification                             │   │
│   └─────────────────────────────────────────────────────────┘   │
│                           │                                       │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  Layer 3: Message Signing                                 │   │
│   │  - HMAC-SHA256 or Ed25519 signatures                     │   │
│   │  - Replay attack prevention (nonce/timestamp)            │   │
│   └─────────────────────────────────────────────────────────┘   │
│                           │                                       │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  Layer 2: Transport Encryption (mTLS)                    │   │
│   │  - Mutual TLS with client certificates                   │   │
│   │  - TLS 1.3 only                                          │   │
│   └─────────────────────────────────────────────────────────┘   │
│                           │                                       │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  Layer 1: Network Isolation                               │   │
│   │  - Dedicated P2P port (separate from client port)        │   │
│   │  - Firewall rules / Network policies                     │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

#### 47.7.2 Mutual TLS (mTLS)
```csharp
public class P2PSecurityOptions
{
    // mTLS Configuration
    public bool RequireMutualTls { get; set; } = true;
    public string CertificatePath { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public string CaCertificatePath { get; set; } = "";          // CA that signed all node certs
    public bool ValidateCertificateChain { get; set; } = true;
    public string[] AllowedCertificateThumbprints { get; set; } = Array.Empty<string>();

    // Cluster Secret (additional layer)
    public string ClusterSecret { get; set; } = "";              // Shared secret for cluster membership
    public string ClusterSecretHashAlgorithm { get; set; } = "SHA256";

    // Message Security
    public bool SignMessages { get; set; } = true;
    public string SignatureAlgorithm { get; set; } = "Ed25519"; // or "HMAC-SHA256"
    public TimeSpan MessageMaxAge { get; set; } = TimeSpan.FromMinutes(5);  // Anti-replay
}
```

#### 47.7.3 Node Authentication Flow
```
┌──────────┐                                      ┌──────────┐
│  Node A  │                                      │  Node B  │
└────┬─────┘                                      └────┬─────┘
     │                                                  │
     │  1. TCP Connect to P2P Port                     │
     │ ─────────────────────────────────────────────► │
     │                                                  │
     │  2. TLS Handshake (present client cert)         │
     │ ◄───────────────────────────────────────────── │
     │                                                  │
     │  3. Verify cert signed by cluster CA            │
     │  4. Check cert thumbprint in allowlist          │
     │                                                  │
     │  5. Send: JoinRequest + ClusterSecretHash       │
     │ ─────────────────────────────────────────────► │
     │                                                  │
     │  6. Verify cluster secret hash                  │
     │  7. Verify node not already in cluster          │
     │                                                  │
     │  8. Send: JoinAccepted + ClusterState           │
     │ ◄───────────────────────────────────────────── │
     │                                                  │
     │  9. Gossip: Announce new node to cluster        │
     │ ◄───────────────────────────────────────────── │
     │                                                  │
```

#### 47.7.4 Security Interfaces
```csharp
// AdvGenNoSqlServer.Core/Abstractions/IP2PAuthenticator.cs
public interface IP2PAuthenticator
{
    Task<AuthResult> AuthenticateNodeAsync(X509Certificate2 certificate, string clusterSecretHash, CancellationToken ct = default);
    Task<bool> IsNodeAuthorizedAsync(string nodeId, P2POperation operation, CancellationToken ct = default);
    byte[] SignMessage(byte[] message);
    bool VerifySignature(byte[] message, byte[] signature, string nodeId);
}

public enum P2POperation
{
    Join,
    Leave,
    Replicate,
    Query,
    Admin
}

// AdvGenNoSqlServer.Core/Abstractions/IP2PEncryption.cs
public interface IP2PEncryption
{
    byte[] Encrypt(byte[] plaintext, string targetNodeId);
    byte[] Decrypt(byte[] ciphertext, string sourceNodeId);
    Task RotateKeysAsync(CancellationToken ct = default);
}
```

### 47.8 Consensus Protocol (Raft)

#### 47.8.1 Raft Implementation
```csharp
public interface IRaftConsensus
{
    NodeRole CurrentRole { get; }
    string? CurrentLeader { get; }
    long CurrentTerm { get; }

    Task<bool> ProposeAsync(LogEntry entry, CancellationToken ct = default);
    Task<VoteResult> RequestVoteAsync(VoteRequest request, CancellationToken ct = default);
    Task<AppendResult> AppendEntriesAsync(AppendRequest request, CancellationToken ct = default);
}

public enum NodeRole
{
    Follower,
    Candidate,
    Leader
}

public class RaftConfiguration
{
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMilliseconds(150);
    public TimeSpan ElectionTimeoutMin { get; set; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan ElectionTimeoutMax { get; set; } = TimeSpan.FromMilliseconds(500);
    public int MaxEntriesPerAppend { get; set; } = 100;
}
```

### 47.9 Configuration

#### 47.9.1 P2P Configuration Schema
```json
{
  "Cluster": {
    "Enabled": true,
    "ClusterId": "production-cluster",
    "ClusterName": "Production NoSQL Cluster",
    "NodeId": "node-001",
    "Mode": "LeaderFollower",

    "Network": {
      "P2PPort": 9092,
      "BindAddress": "0.0.0.0",
      "AdvertiseAddress": "10.0.0.1",
      "ConnectionTimeout": 5000,
      "HeartbeatInterval": 1000,
      "DeadNodeTimeout": 30000
    },

    "Discovery": {
      "Method": "StaticSeeds",
      "Seeds": ["10.0.0.2:9092", "10.0.0.3:9092"],
      "DnsName": "",
      "MulticastGroup": "",
      "RefreshInterval": 30000
    },

    "Replication": {
      "Strategy": "SemiSynchronous",
      "ReplicationFactor": 3,
      "WriteQuorum": 2,
      "ReadQuorum": 1,
      "SyncTimeout": 5000
    },

    "Security": {
      "RequireMutualTls": true,
      "CertificatePath": "/etc/nosql/certs/node.crt",
      "PrivateKeyPath": "/etc/nosql/certs/node.key",
      "CaCertificatePath": "/etc/nosql/certs/ca.crt",
      "ClusterSecret": "${CLUSTER_SECRET}",
      "SignMessages": true,
      "SignatureAlgorithm": "Ed25519"
    },

    "ConflictResolution": {
      "Strategy": "LastWriteWins",
      "CustomResolverClass": ""
    }
  }
}
```

### 47.10 P2P Commands

| Command | Description | Syntax |
|---------|-------------|--------|
| `CLUSTER INFO` | Get cluster information | `CLUSTER INFO` |
| `CLUSTER NODES` | List all nodes | `CLUSTER NODES` |
| `CLUSTER JOIN` | Join a cluster | `CLUSTER JOIN seed_node` |
| `CLUSTER LEAVE` | Leave cluster gracefully | `CLUSTER LEAVE` |
| `CLUSTER FAILOVER` | Force leader election | `CLUSTER FAILOVER` |
| `CLUSTER REPLICATE` | Force replication sync | `CLUSTER REPLICATE node_id` |
| `CLUSTER FORGET` | Remove dead node | `CLUSTER FORGET node_id` |

### 47.11 Implementation Phases

#### Phase 1: Foundation (P3 Priority)
- [ ] Create `NodeIdentity` and `ClusterInfo` models
- [ ] Implement P2P TCP listener (separate port)
- [ ] Implement mTLS for inter-node communication
- [ ] Implement cluster secret validation
- [ ] Basic node registration

**Effort: ~20 hours**

#### Phase 2: Discovery & Gossip
- [ ] Implement static seed discovery
- [ ] Implement gossip protocol for state propagation
- [ ] Implement failure detection (heartbeats)
- [ ] Handle node join/leave events

**Effort: ~16 hours**

#### Phase 3: Leader Election
- [ ] Implement Raft consensus protocol
- [ ] Leader election and term management
- [ ] Log replication infrastructure
- [ ] Leader failover handling

**Effort: ~24 hours**

#### Phase 4: Data Replication
- [ ] Implement `IReplicationManager`
- [ ] WAL-based change streaming to followers
- [ ] Write quorum acknowledgement
- [ ] Conflict detection and resolution

**Effort: ~20 hours**

#### Phase 5: Operations
- [ ] CLUSTER commands implementation
- [ ] Admin UI for cluster monitoring
- [ ] Metrics and alerting
- [ ] Documentation and runbooks

**Effort: ~16 hours**

**Total P2P Effort: ~96 hours**

### 47.12 Security Checklist

- [ ] Separate P2P port from client port
- [ ] mTLS with cluster CA
- [ ] Certificate thumbprint validation
- [ ] Cluster secret hashing (not plaintext)
- [ ] Message signing (Ed25519/HMAC)
- [ ] Replay attack prevention (nonce + timestamp)
- [ ] Rate limiting on join requests
- [ ] Audit logging for all P2P operations
- [ ] Secure cluster secret rotation
- [ ] Network segmentation (P2P on private network)

### 47.13 References

- [Raft Consensus Algorithm](https://raft.github.io/)
- [CockroachDB Distributed Architecture](https://www.cockroachlabs.com/docs/stable/architecture/overview.html)
- [MongoDB Replica Set](https://www.mongodb.com/docs/manual/replication/)
- [RavenDB Clustering](https://ravendb.net/docs/article-page/5.4/csharp/server/clustering/overview)
- [etcd Raft Implementation](https://etcd.io/docs/v3.5/learning/design-learner/)
- [HashiCorp Serf (Gossip)](https://www.serf.io/docs/internals/gossip.html)

---

**Last Updated**: February 13, 2026
**License**: MIT License
**Status**: Planning Phase - Architecture Complete
**Next Step**: Begin Phase 1 - Core Abstractions & Foundation

---

## Summary of Added Features

This document was reviewed and enhanced with the following NoSQL best practices from RavenDB and MongoDB:

### Data Operations
- INSERT, REPLACE, TOUCH commands
- Atomic update operations (UPSERT, FIND_AND_MODIFY, INCREMENT, PUSH, PULL, ADD_TO_SET, PATCH)
- FIND_ONE, DISTINCT, EXPLAIN commands

### Index Types
- Unique indexes
- Compound/Composite indexes
- Sparse indexes (only index docs with field)
- Partial indexes (filter-based)
- TTL indexes (auto-expiration)
- Full-text search indexes (future)
- Geospatial indexes (future)
- Background index building

### Advanced Features
- Change Streams/Subscriptions for real-time updates
- Write Concern & Read Preference
- Cursor-based pagination
- Slow query logging & profiling
- Import/Export tools
- Sessions & Unit of Work pattern
- Optimistic concurrency (ETags)
- Projections
- Field-level encryption
- Capped collections
- Document attachments (future)
- Document revisions (future)

---

## 47. Prioritized Implementation Todo List

### P0 - Critical (Core Operations)
Essential for basic functionality. Must be implemented first.

| # | Task | Description | Est. Effort | Dependencies |
|---|------|-------------|-------------|--------------|
| 1 | INSERT command | Insert that fails if document exists | 2h | None |
| 2 | REPLACE command | Replace that fails if not exists | 2h | None |
| 3 | UPSERT command | Insert or update operation | 2h | INSERT, SET |
| 4 | FIND_ONE command | Return single matching document | 2h | FIND |
| 5 | Unique Index | Enforce unique constraints on fields | 4h | IndexManager |
| 6 | Projections | Return only specified fields | 4h | QueryExecutor |

**Total P0 Effort: ~16 hours**

---

### P1 - High Priority (Production Ready)
Required for production deployment.

| # | Task | Description | Est. Effort | Dependencies |
|---|------|-------------|-------------|--------------|
| 7 | PATCH command | Partial document updates | 3h | None |
| 8 | FIND_AND_MODIFY | Atomic find and update | 4h | LockManager |
| 9 | INCREMENT operation | Atomic numeric field increment | 2h | PATCH |
| 10 | Compound Index | Multi-field composite indexes | 6h | IndexManager |
| 11 | EXPLAIN command | Query plan analysis | 4h | QueryOptimizer |
| 12 | ETag/Optimistic Concurrency | Version-based conflict detection | 6h | Document model |
| 13 | Cursor-based Pagination | Efficient large result set handling | 6h | QueryExecutor |
| 14 | TTL Index | Auto-expiration of documents | 4h | IndexManager, Background worker |

**Total P1 Effort: ~35 hours**

---

### P2 - Medium Priority (Feature Complete)
Important for feature completeness.

| # | Task | Description | Est. Effort | Dependencies |
|---|------|-------------|-------------|--------------|
| 15 | Array Operations | PUSH, PULL, ADD_TO_SET | 4h | PATCH |
| 16 | DISTINCT command | Get unique field values | 3h | QueryExecutor |
| 17 | Sparse Index | Index only docs with field | 3h | IndexManager |
| 18 | Partial Index | Filter-based indexes | 4h | IndexManager |
| 19 | Background Index Build | Non-blocking index creation | 6h | IndexManager |
| 20 | INDEX_STATS command | Index usage statistics | 3h | IndexManager |
| 21 | Slow Query Logging | Query performance monitoring | 4h | QueryExecutor |
| 22 | Sessions/Unit of Work | Change tracking pattern | 8h | TransactionManager |
| 23 | Write Concern | Acknowledgement configuration | 4h | StorageEngine |
| 24 | Capped Collections | Fixed-size auto-rotating collections | 6h | StorageEngine |

**Total P2 Effort: ~45 hours**

---

### P3 - Lower Priority (Advanced Features)
Nice-to-have, can be deferred.

| # | Task | Description | Est. Effort | Dependencies |
|---|------|-------------|-------------|--------------|
| 25 | Change Streams | Real-time data subscriptions | 12h | WAL, ConnectionManager |
| 26 | Import/Export Tools | JSON Lines, BSON migration | 8h | StorageEngine |
| 27 | Full-Text Search | Text indexes with analyzers | 16h | IndexManager |
| 28 | Geospatial Indexes | 2D/2DSphere location queries | 16h | IndexManager |
| 29 | Field-Level Encryption | Client-side field encryption | 12h | EncryptionService |
| 30 | Document Revisions | History tracking and versioning | 10h | StorageEngine |

**Total P3 Effort: ~74 hours**

---

### Implementation Schedule

#### Phase A: Core Operations (P0) - Week 1
```
Day 1-2: INSERT, REPLACE, UPSERT commands
Day 3:   FIND_ONE command
Day 4:   Unique Index implementation
Day 5:   Projections support
```

#### Phase B: Production Ready (P1) - Weeks 2-3
```
Week 2:
  - PATCH, INCREMENT commands
  - FIND_AND_MODIFY atomic operation
  - Compound Index support

Week 3:
  - EXPLAIN command
  - ETag/Optimistic Concurrency
  - Cursor-based Pagination
  - TTL Index
```

#### Phase C: Feature Complete (P2) - Weeks 4-5
```
Week 4:
  - Array operations (PUSH, PULL, ADD_TO_SET)
  - DISTINCT command
  - Sparse & Partial indexes
  - Background index building

Week 5:
  - INDEX_STATS command
  - Slow Query Logging
  - Sessions/Unit of Work
  - Write Concern & Capped Collections
```

#### Phase D: Advanced Features (P3) - Weeks 6-8
```
Week 6:
  - Change Streams/Subscriptions
  - Import/Export tools

Week 7-8:
  - Full-Text Search
  - Geospatial indexes
  - Field-Level Encryption
  - Document Revisions
```

---

### Quick Reference Checklist

#### P0 - Critical (16h total)
- [ ] 1. INSERT command
- [ ] 2. REPLACE command
- [ ] 3. UPSERT command
- [ ] 4. FIND_ONE command
- [ ] 5. Unique Index
- [ ] 6. Projections

#### P1 - High (35h total)
- [ ] 7. PATCH command
- [ ] 8. FIND_AND_MODIFY
- [ ] 9. INCREMENT operation
- [ ] 10. Compound Index
- [ ] 11. EXPLAIN command
- [ ] 12. ETag/Optimistic Concurrency
- [ ] 13. Cursor-based Pagination
- [ ] 14. TTL Index

#### P2 - Medium (45h total)
- [ ] 15. Array Operations
- [ ] 16. DISTINCT command
- [ ] 17. Sparse Index
- [ ] 18. Partial Index
- [ ] 19. Background Index Build
- [ ] 20. INDEX_STATS command
- [ ] 21. Slow Query Logging
- [ ] 22. Sessions/Unit of Work
- [ ] 23. Write Concern
- [ ] 24. Capped Collections

#### P3 - Lower (74h total)
- [ ] 25. Change Streams
- [ ] 26. Import/Export Tools
- [ ] 27. Full-Text Search
- [ ] 28. Geospatial Indexes
- [ ] 29. Field-Level Encryption
- [ ] 30. Document Revisions

#### P3 - P2P Cluster (96h total)
- [ ] 31. P2P Foundation (mTLS, cluster secret, node identity)
- [ ] 32. Node Discovery (static seeds, gossip protocol)
- [ ] 33. Leader Election (Raft consensus)
- [ ] 34. Data Replication (WAL streaming, quorum)
- [ ] 35. Conflict Resolution (LWW, merge strategies)
- [ ] 36. CLUSTER commands (INFO, NODES, JOIN, LEAVE)

**Grand Total: ~266 hours (6-7 weeks full-time)**
