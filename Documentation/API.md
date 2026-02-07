# AdvGenNoSQL Server API Documentation

**Version**: 1.0.0  
**Framework**: .NET 9.0  
**License**: MIT License

---

## Table of Contents

1. [Overview](#overview)
2. [Client Library](#client-library)
3. [Core Components](#core-components)
4. [Storage Engine](#storage-engine)
5. [Query Engine](#query-engine)
6. [Network Layer](#network-layer)
7. [Authentication](#authentication)
8. [Transactions](#transactions)
9. [Caching](#caching)
10. [Configuration](#configuration)

---

## Overview

AdvGenNoSQL Server is a lightweight, high-performance NoSQL database server built in C# with .NET 9.0. This API documentation covers all public interfaces and classes available for developers.

### Namespaces

| Namespace | Description |
|-----------|-------------|
| `AdvGenNoSqlServer.Client` | Client library for connecting to the NoSQL server |
| `AdvGenNoSqlServer.Core` | Core models, authentication, caching, configuration, and transactions |
| `AdvGenNoSqlServer.Network` | TCP server, connection handling, and message protocol |
| `AdvGenNoSqlServer.Storage` | Document storage, indexing, and persistence |
| `AdvGenNoSqlServer.Query` | Query parsing, execution, filtering, and aggregation |

---

## Client Library

### AdvGenNoSqlClient

The main client class for connecting to and interacting with the NoSQL server.

```csharp
public class AdvGenNoSqlClient : IDisposable
```

#### Constructors

| Constructor | Description |
|-------------|-------------|
| `AdvGenNoSqlClient(string connectionString)` | Creates a client with the specified connection string |
| `AdvGenNoSqlClient(string connectionString, AdvGenNoSqlClientOptions options)` | Creates a client with custom options |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsConnected` | `bool` | Indicates if the client is currently connected |
| `ConnectionId` | `string?` | The unique connection ID assigned by the server |
| `ServerVersion` | `string?` | The version of the connected server |

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `ConnectAsync()` | `Task` | Establishes connection to the server |
| `DisconnectAsync()` | `Task` | Closes the connection gracefully |
| `AuthenticateAsync(string username, string password)` | `Task<string>` | Authenticates and returns JWT token |
| `PingAsync()` | `Task<TimeSpan>` | Sends a ping and returns round-trip time |
| `InsertAsync(string collection, object document)` | `Task<string>` | Inserts a document, returns document ID |
| `GetAsync(string collection, string id)` | `Task<Document?>` | Retrieves a document by ID |
| `UpdateAsync(string collection, string id, object updates)` | `Task<bool>` | Updates a document |
| `DeleteAsync(string collection, string id)` | `Task<bool>` | Deletes a document |
| `QueryAsync(string collection, QueryFilter filter)` | `Task<IEnumerable<Document>>` | Queries documents matching filter |
| `BatchInsertAsync(string collection, IEnumerable<object> documents)` | `Task<BatchOperationResponse>` | Batch insert operation |
| `BatchUpdateAsync(string collection, IEnumerable<(string id, Dictionary<string, object> updates)> updates)` | `Task<BatchOperationResponse>` | Batch update operation |
| `BatchDeleteAsync(string collection, IEnumerable<string> ids)` | `Task<BatchOperationResponse>` | Batch delete operation |

### AdvGenNoSqlClientOptions

Configuration options for the client.

```csharp
public class AdvGenNoSqlClientOptions
```

#### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ConnectionTimeout` | `TimeSpan` | 30s | Timeout for establishing connection |
| `ReceiveTimeout` | `TimeSpan` | 30s | Timeout for receiving data |
| `SendTimeout` | `TimeSpan` | 30s | Timeout for sending data |
| `MaxRetryAttempts` | `int` | 3 | Maximum retry attempts for failed operations |
| `EnableAutoReconnect` | `bool` | true | Automatically reconnect on disconnect |
| `UseSsl` | `bool` | false | Enable SSL/TLS encryption |
| `SslTargetHost` | `string?` | null | Target hostname for SSL certificate validation |
| `ValidateServerCertificate` | `bool` | true | Validate server SSL certificate |

---

## Core Components

### Document

The core document model representing a stored document.

```csharp
public class Document
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `string` | Unique document identifier |
| `Data` | `Dictionary<string, object?>` | Document data as key-value pairs |
| `CreatedAt` | `DateTime` | Timestamp when document was created |
| `UpdatedAt` | `DateTime` | Timestamp when document was last updated |
| `Version` | `int` | Document version for optimistic concurrency |

#### Example

```csharp
var document = new Document
{
    Id = "user123",
    Data = new Dictionary<string, object?>
    {
        ["name"] = "John Doe",
        ["email"] = "john@example.com",
        ["age"] = 30
    }
};
```

### BatchOperationRequest

Request model for batch operations.

```csharp
public class BatchOperationRequest
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Collection` | `string` | Target collection name |
| `Operations` | `List<BatchOperationItem>` | List of operations to execute |
| `StopOnError` | `bool` | Stop processing on first error |
| `UseTransaction` | `bool` | Execute in a transaction |
| `TransactionId` | `string?` | Optional existing transaction ID |

### BatchOperationResponse

Response model for batch operations.

```csharp
public class BatchOperationResponse
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Success` | `bool` | True if all operations succeeded |
| `InsertedCount` | `int` | Number of documents inserted |
| `UpdatedCount` | `int` | Number of documents updated |
| `DeletedCount` | `int` | Number of documents deleted |
| `TotalProcessed` | `int` | Total operations processed |
| `ProcessingTimeMs` | `long` | Processing time in milliseconds |
| `Results` | `List<BatchOperationItemResult>` | Individual operation results |

---

## Storage Engine

### IDocumentStore

Interface for document storage operations.

```csharp
public interface IDocumentStore
```

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `GetAsync(string collection, string id)` | `Task<Document?>` | Get document by ID |
| `InsertAsync(string collection, Document document)` | `Task<bool>` | Insert a new document |
| `UpdateAsync(string collection, Document document)` | `Task<bool>` | Update existing document |
| `DeleteAsync(string collection, string id)` | `Task<bool>` | Delete document by ID |
| `GetAllAsync(string collection)` | `Task<IEnumerable<Document>>` | Get all documents in collection |
| `CountAsync(string collection)` | `Task<long>` | Count documents in collection |
| `CreateCollectionAsync(string name)` | `Task<bool>` | Create a new collection |
| `DropCollectionAsync(string name)` | `Task<bool>` | Delete a collection |
| `GetCollectionsAsync()` | `Task<IEnumerable<string>>` | List all collections |

### IPersistentDocumentStore

Extends `IDocumentStore` with persistence capabilities.

```csharp
public interface IPersistentDocumentStore : IDocumentStore
```

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `InitializeAsync()` | `Task` | Initialize and load existing data |
| `SaveChangesAsync()` | `Task` | Persist all changes to disk |
| `SaveCollectionAsync(string collection)` | `Task` | Persist specific collection |

### IBTreeIndex&lt;TKey, TValue&gt;

B-tree index interface for efficient lookups.

```csharp
public interface IBTreeIndex<TKey, TValue> where TKey : IComparable<TKey>
```

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Insert(TKey key, TValue value)` | `void` | Insert key-value pair |
| `Delete(TKey key)` | `bool` | Delete by key |
| `Search(TKey key)` | `TValue?` | Search for value by key |
| `RangeQuery(TKey start, TKey end)` | `IEnumerable<TValue>` | Query range of keys |
| `GetAll()` | `IEnumerable<TValue>` | Get all values |

### IndexManager

Manages multiple indexes per collection.

```csharp
public class IndexManager
```

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `CreateIndex(string collection, string field, bool unique = false)` | `void` | Create an index on a field |
| `DropIndex(string collection, string field)` | `void` | Remove an index |
| `GetIndex(string collection, string field)` | `IBTreeIndex<object, string>?` | Get index by field |
| `BuildIndex(string collection, string field, IEnumerable<Document> documents)` | `void` | Build index from documents |

---

## Query Engine

### Query

Query model for document retrieval.

```csharp
public class Query
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Collection` | `string` | Target collection |
| `Filter` | `QueryFilter?` | Filter criteria |
| `SortFields` | `List<SortField>` | Sort specifications |
| `Options` | `QueryOptions` | Query options (skip, limit, projection) |

### QueryFilter

Filter criteria for queries using MongoDB-like syntax.

```csharp
public class QueryFilter
```

#### Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `$eq` | Equal | `{ "age": { "$eq": 30 } }` |
| `$ne` | Not equal | `{ "status": { "$ne": "inactive" } }` |
| `$gt` | Greater than | `{ "age": { "$gt": 18 } }` |
| `$gte` | Greater than or equal | `{ "score": { "$gte": 100 } }` |
| `$lt` | Less than | `{ "age": { "$lt": 65 } }` |
| `$lte` | Less than or equal | `{ "price": { "$lte": 100.00 } }` |
| `$in` | In array | `{ "status": { "$in": ["active", "pending"] } }` |
| `$nin` | Not in array | `{ "role": { "$nin": ["admin", "superuser"] } }` |
| `$and` | Logical AND | `{ "$and": [{...}, {...}] }` |
| `$or` | Logical OR | `{ "$or": [{...}, {...}] }` |
| `$exists` | Field exists | `{ "email": { "$exists": true } }` |

#### Example

```csharp
var filter = new QueryFilter
{
    { "status", new Dictionary<string, object> { ["$eq"] = "active" } },
    { "age", new Dictionary<string, object> { ["$gte"] = 18 } }
};
```

### IQueryExecutor

Interface for query execution.

```csharp
public interface IQueryExecutor
```

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `ExecuteAsync(Query query, IDocumentStore store)` | `Task<QueryResult>` | Execute query and return results |
| `ExecuteAsync(Query query, IDocumentStore store, CancellationToken cancellationToken)` | `Task<QueryResult>` | Execute with cancellation support |

### QueryResult

Result of a query execution.

```csharp
public class QueryResult
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Documents` | `List<Document>` | Matching documents |
| `TotalCount` | `long` | Total matching documents (before skip/limit) |
| `Skipped` | `int` | Number of documents skipped |
| `Limit` | `int?` | Applied limit (if any) |
| `ExecutionTimeMs` | `double` | Query execution time |
| `Stats` | `QueryStats` | Detailed statistics |

### AggregationPipeline

Pipeline for complex data aggregation.

```csharp
public class AggregationPipeline
```

#### Stages

| Stage | Description |
|-------|-------------|
| `$match` | Filter documents |
| `$group` | Group documents and aggregate |
| `$project` | Reshape documents |
| `$sort` | Sort documents |
| `$limit` | Limit output count |
| `$skip` | Skip N documents |

#### Group Operators

| Operator | Description |
|----------|-------------|
| `$sum` | Sum of values |
| `$avg` | Average of values |
| `$min` | Minimum value |
| `$max` | Maximum value |
| `$count` | Count of documents |
| `$first` | First value |
| `$last` | Last value |
| `$push` | Push values to array |
| `$addToSet` | Add unique values to set |

#### Example

```csharp
var pipeline = new AggregationPipelineBuilder()
    .Match(new QueryFilter { { "status", new { "$eq" = "active" } } })
    .Group(new { _id = "$category", total = new { "$sum" = "$amount" } })
    .Sort(new { total = -1 })
    .Limit(10)
    .Build();
```

---

## Network Layer

### TcpServer

TCP server for handling client connections.

```csharp
public class TcpServer : IDisposable
```

#### Events

| Event | Description |
|-------|-------------|
| `ConnectionEstablished` | Fired when a new connection is established |
| `ConnectionClosed` | Fired when a connection is closed |
| `MessageReceived` | Fired when a message is received |
| `ErrorOccurred` | Fired when an error occurs |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsRunning` | `bool` | Server running state |
| `ActiveConnectionCount` | `int` | Number of active connections |
| `Configuration` | `ServerConfiguration` | Server configuration |

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `StartAsync(CancellationToken)` | `Task` | Start the server |
| `StopAsync()` | `Task` | Stop the server gracefully |
| `GetStatistics()` | `ConnectionPoolStatistics` | Get connection statistics |

### MessageProtocol

Binary message protocol for communication.

```csharp
public static class MessageProtocol
```

#### Message Types

| Type | Value | Description |
|------|-------|-------------|
| `Handshake` | 0x01 | Initial handshake |
| `Authentication` | 0x02 | Authentication request/response |
| `Command` | 0x03 | Database command |
| `Response` | 0x04 | Command response |
| `Error` | 0x05 | Error message |
| `Ping` | 0x06 | Keepalive ping |
| `Pong` | 0x07 | Keepalive pong |
| `Transaction` | 0x08 | Transaction control |
| `BulkOperation` | 0x09 | Batch operations |
| `Notification` | 0x0A | Server notification |

#### Message Format

```
[Magic (4 bytes): "NOSQ"]
[Version (2 bytes): 1]
[Message Type (1 byte)]
[Flags (1 byte)]
[Payload Length (4 bytes)]
[Payload (variable)]
[CRC32 Checksum (4 bytes)]
```

### ConnectionPool

Manages connection limits and statistics.

```csharp
public class ConnectionPool
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `MaxConnections` | `int` | Maximum allowed connections |
| `ActiveConnections` | `int` | Currently active connections |
| `AvailableSlots` | `int` | Available connection slots |

---

## Authentication

### IJwtTokenProvider

Interface for JWT token generation and validation.

```csharp
public interface IJwtTokenProvider
```

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `GenerateToken(string username, IEnumerable<string> roles, IEnumerable<string> permissions)` | `string` | Generate JWT token |
| `ValidateToken(string token)` | `bool` | Validate token signature and expiration |
| `GetUsernameFromToken(string token)` | `string?` | Extract username from token |
| `GetRolesFromToken(string token)` | `IEnumerable<string>` | Extract roles from token |
| `GetExpirationTime(string token)` | `DateTime?` | Get token expiration time |
| `RefreshToken(string token)` | `string?` | Refresh a valid token |

### IAuditLogger

Interface for security audit logging.

```csharp
public interface IAuditLogger
```

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `LogAuthenticationSuccess(string username, string? clientIp)` | `void` | Log successful authentication |
| `LogAuthenticationFailure(string username, string? clientIp, string reason)` | `void` | Log failed authentication |
| `LogAuthorizationCheck(string username, string permission, bool granted)` | `void` | Log permission check |
| `LogDataAccess(string username, string operation, string collection, string? documentId)` | `void` | Log data access |
| `LogUserManagement(string adminUsername, string action, string targetUser)` | `void` | Log user management |
| `LogServerEvent(string eventType, string details)` | `void` | Log server event |
| `GetRecentEvents(int count)` | `IEnumerable<AuditEvent>` | Get recent audit events |
| `GetEventsByUser(string username, int count)` | `IEnumerable<AuditEvent>` | Get events for user |
| `FlushAsync()` | `Task` | Flush logs to storage |

### RoleManager

Manages roles and permissions.

```csharp
public class RoleManager
```

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `CreateRole(string name, string? description)` | `Role` | Create a new role |
| `DeleteRole(string name)` | `bool` | Delete a role |
| `GetRole(string name)` | `Role?` | Get role by name |
| `GetAllRoles()` | `IEnumerable<Role>` | Get all roles |
| `AssignPermissionToRole(string roleName, string permission)` | `bool` | Add permission to role |
| `RemovePermissionFromRole(string roleName, string permission)` | `bool` | Remove permission from role |
| `GetRolePermissions(string roleName)` | `IEnumerable<string>` | Get role permissions |
| `AssignRoleToUser(string username, string roleName)` | `bool` | Assign role to user |
| `RemoveRoleFromUser(string username, string roleName)` | `bool` | Remove role from user |
| `GetUserRoles(string username)` | `IEnumerable<string>` | Get user roles |
| `GetUserPermissions(string username)` | `IEnumerable<string>` | Get all user permissions |

#### Default Roles

| Role | Description |
|------|-------------|
| `Admin` | Full server administration access |
| `PowerUser` | Extended user with most permissions |
| `User` | Standard user with CRUD operations |
| `ReadOnly` | Read-only access |
| `Guest` | Limited access |

---

## Transactions

### ITransactionCoordinator

Interface for transaction management with ACID properties.

```csharp
public interface ITransactionCoordinator
```

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `BeginAsync(IsolationLevel isolationLevel, TimeSpan? timeout)` | `Task<ITransactionContext>` | Begin a new transaction |
| `CommitAsync(string transactionId)` | `Task<TransactionResult>` | Commit transaction |
| `RollbackAsync(string transactionId)` | `Task<TransactionResult>` | Rollback transaction |
| `GetTransactionStatus(string transactionId)` | `TransactionStatus` | Get transaction status |
| `CreateSavepointAsync(string transactionId, string name)` | `Task<bool>` | Create a savepoint |
| `RollbackToSavepointAsync(string transactionId, string name)` | `Task<bool>` | Rollback to savepoint |
| `RecoverAsync()` | `Task` | Recover incomplete transactions |

### IsolationLevel

Transaction isolation levels.

```csharp
public enum IsolationLevel
{
    ReadUncommitted,  // May read uncommitted changes
    ReadCommitted,    // Only read committed changes
    RepeatableRead,   // Consistent reads within transaction
    Serializable      // Full isolation, no concurrency
}
```

### IWriteAheadLog

Interface for write-ahead logging for durability.

```csharp
public interface IWriteAheadLog
```

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `AppendAsync(LogEntry entry)` | `Task<long>` | Append entry to log |
| `ForceFlushAsync()` | `Task` | Force flush to disk |
| `GetEntriesAsync(long startLsn)` | `Task<IEnumerable<LogEntry>>` | Get entries from LSN |
| `TruncateAsync(long upToLsn)` | `Task` | Truncate log up to LSN |
| `RecoverAsync()` | `Task<IEnumerable<LogEntry>>` | Get all entries for recovery |

---

## Caching

### ICacheManager

Interface for caching operations.

```csharp
public interface ICacheManager
```

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Get(string key)` | `Document?` | Get cached document |
| `Set(string key, Document document, int ttlSeconds)` | `void` | Cache document with TTL |
| `Remove(string key)` | `void` | Remove from cache |
| `Clear()` | `void` | Clear all cached items |

### LruCache&lt;TKey, TValue&gt;

Thread-safe LRU cache with TTL support.

```csharp
public class LruCache<TKey, TValue> where TKey : notnull
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Count` | `int` | Number of items in cache |
| `CurrentSizeInBytes` | `long` | Current memory usage |
| `HitRatio` | `double` | Cache hit ratio (0-1) |

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Get(TKey key)` | `TValue?` | Get value by key |
| `Set(TKey key, TValue value, TimeSpan? ttl)` | `void` | Set value with optional TTL |
| `Remove(TKey key)` | `bool` | Remove item |
| `TryGet(TKey key, out TValue value)` | `bool` | Try get value |
| `Clear()` | `void` | Clear all items |
| `GetStatistics()` | `CacheStatistics` | Get cache statistics |

---

## Configuration

### ServerConfiguration

Main configuration class for the server.

```csharp
public class ServerConfiguration
```

#### Server Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Host` | `string` | "0.0.0.0" | Bind address |
| `Port` | `int` | 9090 | TCP port |
| `MaxConnections` | `int` | 10000 | Maximum concurrent connections |
| `ConnectionTimeout` | `int` | 30000 | Connection timeout (ms) |
| `KeepAliveInterval` | `int` | 60000 | Keepalive interval (ms) |

#### Security Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnableSsl` | `bool` | false | Enable SSL/TLS |
| `SslCertificatePath` | `string?` | null | Path to SSL certificate |
| `SslCertificatePassword` | `string?` | null | Certificate password |
| `RequireAuthentication` | `bool` | true | Require authentication |
| `JwtSecretKey` | `string?` | null | JWT signing key |
| `JwtIssuer` | `string?` | null | JWT issuer |
| `JwtAudience` | `string?` | null | JWT audience |

#### Storage Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DataPath` | `string` | "./data" | Data directory path |
| `MaxFileSize` | `long` | 1GB | Max file size |
| `CompressionEnabled` | `bool` | false | Enable compression |
| `PersistenceMode` | `string` | "fsync" | Persistence mode |

#### Cache Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxCacheItemCount` | `int` | 100000 | Max items in cache |
| `MaxCacheSizeInBytes` | `long` | 256MB | Max cache size |
| `DefaultCacheTtlMilliseconds` | `int` | 3600000 | Default TTL (ms) |

---

## Examples

### Basic Connection

```csharp
using var client = new AdvGenNoSqlClient("localhost:9090");
await client.ConnectAsync();
await client.AuthenticateAsync("username", "password");
```

### CRUD Operations

```csharp
// Insert
var id = await client.InsertAsync("users", new
{
    name = "John Doe",
    email = "john@example.com"
});

// Read
var doc = await client.GetAsync("users", id);

// Update
await client.UpdateAsync("users", id, new Dictionary<string, object>
{
    ["email"] = "john.doe@example.com"
});

// Delete
await client.DeleteAsync("users", id);
```

### Query with Filter

```csharp
var filter = new QueryFilter
{
    { "age", new Dictionary<string, object> { ["$gte"] = 18 } },
    { "status", new Dictionary<string, object> { ["$eq"] = "active" } }
};

var results = await client.QueryAsync("users", filter);
```

### Batch Operations

```csharp
var documents = new List<object>
{
    new { _id = "1", name = "User 1" },
    new { _id = "2", name = "User 2" },
    new { _id = "3", name = "User 3" }
};

var result = await client.BatchInsertAsync("users", documents);
Console.WriteLine($"Inserted: {result.InsertedCount}");
```

### Using Transactions

```csharp
// Server-side transaction usage
using var coordinator = serviceProvider.GetRequiredService<ITransactionCoordinator>();
var transaction = await coordinator.BeginAsync(IsolationLevel.ReadCommitted);

try
{
    // Perform operations...
    await coordinator.CommitAsync(transaction.TransactionId);
}
catch
{
    await coordinator.RollbackAsync(transaction.TransactionId);
    throw;
}
```

---

## See Also

- [User Guide](UserGuide.md) - End-user documentation
- [Developer Guide](DeveloperGuide.md) - Contributing and development
- [Performance Tuning](PerformanceTuning.md) - Optimization guidelines
