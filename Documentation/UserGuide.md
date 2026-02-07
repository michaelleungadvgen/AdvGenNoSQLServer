# AdvGenNoSQL Server - User Guide

**Version**: 1.0.0  
**Last Updated**: February 7, 2026

---

## Table of Contents

1. [Introduction](#introduction)
2. [Installation](#installation)
3. [Configuration](#configuration)
4. [Getting Started](#getting-started)
5. [Authentication](#authentication)
6. [Working with Documents](#working-with-documents)
7. [Querying Data](#querying-data)
8. [Batch Operations](#batch-operations)
9. [Transactions](#transactions)
10. [Monitoring and Logging](#monitoring-and-logging)
11. [Troubleshooting](#troubleshooting)

---

## Introduction

AdvGenNoSQL Server is a lightweight, high-performance NoSQL database server designed for applications requiring fast document storage with ACID transaction support. This guide will walk you through using the server from an end-user perspective.

### Key Features

- **Document Storage**: JSON-like document storage with flexible schema
- **ACID Transactions**: Full transaction support with multiple isolation levels
- **Authentication**: JWT-based authentication with role-based access control
- **Indexing**: B-tree indexes for efficient querying
- **Caching**: LRU cache with TTL support
- **Query Engine**: MongoDB-like query syntax
- **Aggregation Pipeline**: Complex data aggregation and transformation
- **SSL/TLS Support**: Encrypted connections

---

## Installation

### Prerequisites

- .NET 9.0 SDK or runtime
- Windows, Linux, or macOS

### Installing from Source

1. Clone the repository:
```bash
git clone https://github.com/yourorg/advgen-nosql-server.git
cd advgen-nosql-server
```

2. Build the solution:
```bash
dotnet build -c Release
```

3. Run the server:
```bash
cd AdvGenNoSqlServer.Server
dotnet run -c Release
```

### Docker Installation (Future)

```bash
docker pull advgen/nosql-server:latest
docker run -p 9090:9090 advgen/nosql-server:latest
```

---

## Configuration

### Configuration Files

The server uses JSON configuration files:

- `appsettings.json` - Base configuration
- `appsettings.Development.json` - Development overrides
- `appsettings.Production.json` - Production settings
- `appsettings.Testing.json` - Test configuration

### Basic Configuration

Create an `appsettings.json` file:

```json
{
  "Server": {
    "Host": "0.0.0.0",
    "Port": 9090,
    "MaxConnections": 10000
  },
  "Security": {
    "RequireAuthentication": true,
    "JwtSecretKey": "your-secret-key-min-32-chars-long",
    "JwtIssuer": "AdvGenNoSQL",
    "JwtAudience": "NoSQLClients"
  },
  "Storage": {
    "DataPath": "./data",
    "MaxFileSize": 1073741824
  },
  "Cache": {
    "Enabled": true,
    "MaxSize": 268435456,
    "EvictionPolicy": "LRU"
  },
  "Logging": {
    "Level": "Information",
    "EnableFileLogging": true,
    "LogPath": "./logs"
  }
}
```

### Environment Variables

You can override configuration using environment variables:

```bash
# Linux/macOS
export Server__Port=9091
export Security__RequireAuthentication=true

# Windows PowerShell
$env:Server__Port=9091
$env:Security__RequireAuthentication="true"
```

---

## Getting Started

### Starting the Server

```bash
dotnet run --project AdvGenNoSqlServer.Server
```

The server will start and listen on the configured port (default: 9090).

### Connecting with the Client

```csharp
using AdvGenNoSqlServer.Client;

// Create client
var client = new AdvGenNoSqlClient("localhost:9090");

// Connect
await client.ConnectAsync();

// Authenticate
var token = await client.AuthenticateAsync("admin", "password");

// Use the client...

// Disconnect when done
await client.DisconnectAsync();
```

### Connection Options

```csharp
var options = new AdvGenNoSqlClientOptions
{
    ConnectionTimeout = TimeSpan.FromSeconds(30),
    MaxRetryAttempts = 3,
    EnableAutoReconnect = true,
    UseSsl = false
};

var client = new AdvGenNoSqlClient("localhost:9090", options);
```

---

## Authentication

### Default Users

The server comes with a default admin user that should be changed immediately:

- **Username**: `admin`
- **Password**: `admin`

### Creating Users (Programmatic)

```csharp
// Server-side code
var authManager = new AuthenticationManager(configuration);
authManager.RegisterUser("newuser", "securepassword");
```

### Role-Based Access Control

The server provides predefined roles:

| Role | Permissions |
|------|-------------|
| Admin | All permissions |
| PowerUser | Most permissions except user management |
| User | CRUD operations, queries |
| ReadOnly | Read-only access |
| Guest | Limited access |

### Assigning Roles

```csharp
var roleManager = new RoleManager();
roleManager.AssignRoleToUser("john", "User");
```

---

## Working with Documents

### Document Structure

Documents are JSON-like objects with the following structure:

```json
{
  "_id": "unique-document-id",
  "field1": "value1",
  "field2": 123,
  "nested": {
    "field": "value"
  },
  "_createdAt": "2026-02-07T10:00:00Z",
  "_updatedAt": "2026-02-07T10:00:00Z",
  "_version": 1
}
```

### Inserting Documents

```csharp
// Insert with auto-generated ID
var id = await client.InsertAsync("users", new
{
    name = "John Doe",
    email = "john@example.com",
    age = 30
});

// Insert with specific ID
var doc = new Document
{
    Id = "user123",
    Data = new Dictionary<string, object?>
    {
        ["name"] = "Jane Doe",
        ["email"] = "jane@example.com"
    }
};
```

### Retrieving Documents

```csharp
// Get by ID
var doc = await client.GetAsync("users", "user123");

// Check if exists
if (doc != null)
{
    Console.WriteLine($"Name: {doc.Data["name"]}");
}
```

### Updating Documents

```csharp
// Update specific fields
await client.UpdateAsync("users", "user123", new Dictionary<string, object>
{
    ["email"] = "new.email@example.com",
    ["age"] = 31
});

// Document versioning is automatic
var updated = await client.GetAsync("users", "user123");
Console.WriteLine($"Version: {updated.Version}"); // Will be 2
```

### Deleting Documents

```csharp
var deleted = await client.DeleteAsync("users", "user123");
if (deleted)
{
    Console.WriteLine("Document deleted successfully");
}
```

### Managing Collections

```csharp
// Create collection
await client.CreateCollectionAsync("products");

// List collections
var collections = await client.GetCollectionsAsync();

// Drop collection
await client.DropCollectionAsync("old_data");
```

---

## Querying Data

### Basic Queries

```csharp
// Get all documents
var allUsers = await client.GetAllAsync("users");

// Simple filter
var filter = new QueryFilter
{
    { "status", new Dictionary<string, object> { ["$eq"] = "active" } }
};
var activeUsers = await client.QueryAsync("users", filter);
```

### Comparison Operators

```csharp
// Greater than
var adults = await client.QueryAsync("users", new QueryFilter
{
    { "age", new Dictionary<string, object> { ["$gte"] = 18 } }
});

// Range query
var middleAged = await client.QueryAsync("users", new QueryFilter
{
    { "age", new Dictionary<string, object> { ["$gte"] = 30, ["$lte"] = 50 } }
});

// In array
var statuses = new[] { "active", "pending" };
var filtered = await client.QueryAsync("users", new QueryFilter
{
    { "status", new Dictionary<string, object> { ["$in"] = statuses } }
});
```

### Logical Operators

```csharp
// AND
var result = await client.QueryAsync("users", new QueryFilter
{
    { "$and", new[]
        {
            new Dictionary<string, object> { ["age"] = new Dictionary<string, object> { ["$gte"] = 18 } },
            new Dictionary<string, object> { ["status"] = new Dictionary<string, object> { ["$eq"] = "active" } }
        }
    }
});

// OR
var result = await client.QueryAsync("users", new QueryFilter
{
    { "$or", new[]
        {
            new Dictionary<string, object> { ["role"] = new Dictionary<string, object> { ["$eq"] = "admin" } },
            new Dictionary<string, object> { ["role"] = new Dictionary<string, object> { ["$eq"] = "moderator" } }
        }
    }
});
```

### Sorting and Pagination

```csharp
var query = new Query
{
    Collection = "users",
    Filter = new QueryFilter { { "status", new { "$eq" = "active" } } },
    SortFields = new List<SortField>
    {
        new SortField { Field = "createdAt", Direction = SortDirection.Descending }
    },
    Options = new QueryOptions
    {
        Skip = 0,
        Limit = 20
    }
};

var result = await queryExecutor.ExecuteAsync(query, documentStore);
```

---

## Batch Operations

### Batch Insert

```csharp
var users = new List<object>
{
    new { name = "User 1", email = "user1@example.com" },
    new { name = "User 2", email = "user2@example.com" },
    new { name = "User 3", email = "user3@example.com" }
};

var result = await client.BatchInsertAsync("users", users);
Console.WriteLine($"Inserted: {result.InsertedCount}, Failed: {result.Results.Count(r => !r.Success)}");
```

### Batch Update

```csharp
var updates = new List<(string id, Dictionary<string, object> fields)>
{
    ("user1", new Dictionary<string, object> { ["status"] = "active" }),
    ("user2", new Dictionary<string, object> { ["status"] = "inactive" })
};

var result = await client.BatchUpdateAsync("users", updates);
```

### Bulk Operations with Progress

```csharp
var largeDataset = Enumerable.Range(1, 10000)
    .Select(i => new { _id = $"user{i}", name = $"User {i}" })
    .ToList();

await client.BulkInsertAsync(
    "users",
    largeDataset,
    batchSize: 1000,
    progressCallback: (processed, total) =>
    {
        Console.WriteLine($"Progress: {processed}/{total} ({100.0 * processed / total:F1}%)");
    });
```

---

## Transactions

### Using Transactions (Server-Side)

```csharp
using AdvGenNoSqlServer.Core.Transactions;

var coordinator = serviceProvider.GetRequiredService<ITransactionCoordinator>();

// Begin transaction
var transaction = await coordinator.BeginAsync(IsolationLevel.ReadCommitted);

try
{
    // Perform operations within transaction
    await documentStore.InsertAsync("accounts", doc1, transaction.TransactionId);
    await documentStore.UpdateAsync("accounts", doc2, transaction.TransactionId);
    await documentStore.DeleteAsync("accounts", "id3", transaction.TransactionId);

    // Commit
    await coordinator.CommitAsync(transaction.TransactionId);
    Console.WriteLine("Transaction committed successfully");
}
catch (Exception ex)
{
    // Rollback on error
    await coordinator.RollbackAsync(transaction.TransactionId);
    Console.WriteLine($"Transaction rolled back: {ex.Message}");
}
```

### Isolation Levels

| Level | Description | Use Case |
|-------|-------------|----------|
| ReadUncommitted | May see uncommitted changes | High performance, eventual consistency |
| ReadCommitted | Only committed changes | Default, good balance |
| RepeatableRead | Consistent reads | Reporting, analytics |
| Serializable | Full isolation | Financial transactions |

### Savepoints

```csharp
var tx = await coordinator.BeginAsync(IsolationLevel.ReadCommitted);

// Create savepoint
await coordinator.CreateSavepointAsync(tx.TransactionId, "after_insert");

// Do more work...

// Rollback to savepoint if needed
await coordinator.RollbackToSavepointAsync(tx.TransactionId, "after_insert");
```

---

## Monitoring and Logging

### Audit Logging

The server logs security events:

- Authentication success/failure
- Authorization checks
- Data access operations
- User/role management
- Server events

### Viewing Audit Logs

```csharp
var auditLogger = serviceProvider.GetRequiredService<IAuditLogger>();

// Get recent events
var recentEvents = auditLogger.GetRecentEvents(100);

// Get events for specific user
var userEvents = auditLogger.GetEventsByUser("john", 50);
```

### Performance Metrics

```csharp
// Cache statistics
var cacheStats = cache.GetStatistics();
Console.WriteLine($"Cache Hit Ratio: {cacheStats.HitRatio:P}");
Console.WriteLine($"Items: {cacheStats.ItemCount}");

// Connection statistics
var connStats = tcpServer.GetStatistics();
Console.WriteLine($"Active Connections: {connStats.ActiveConnections}");
```

---

## Troubleshooting

### Common Issues

#### Connection Refused

**Problem**: Cannot connect to server

**Solutions**:
- Verify server is running: `dotnet run --project AdvGenNoSqlServer.Server`
- Check firewall settings
- Verify port is not in use: `netstat -an | findstr 9090`
- Check configuration: Verify `Host` and `Port` settings

#### Authentication Failed

**Problem**: Cannot authenticate

**Solutions**:
- Verify username and password
- Check JWT secret key configuration
- Ensure authentication is enabled: `RequireAuthentication: true`
- Check token expiration

#### Slow Queries

**Problem**: Queries are slow

**Solutions**:
- Add indexes on frequently queried fields
- Use query filters to reduce result set
- Check cache hit ratio
- Review query execution plan

### Log Files

Log files are stored in the configured log directory (default: `./logs`):

- `nosql-server-YYYY-MM-DD.log` - General logs
- `audit-YYYY-MM-DD.log` - Audit logs

### Getting Help

1. Check the [API Documentation](API.md) for detailed method signatures
2. Review the [Developer Guide](DeveloperGuide.md) for internals
3. Check log files for error details
4. Run diagnostics: `dotnet test` to verify installation

---

## Best Practices

### Security

1. **Change default credentials** immediately after installation
2. **Use SSL/TLS** in production
3. **Rotate JWT secret keys** regularly
4. **Enable audit logging** for compliance
5. **Use least-privilege** roles for users

### Performance

1. **Create indexes** on frequently queried fields
2. **Use batch operations** for bulk data loading
3. **Enable caching** for read-heavy workloads
4. **Use appropriate isolation levels** - don't use Serializable unless necessary
5. **Monitor cache hit ratio** and adjust cache size

### Data Management

1. **Regular backups** of the data directory
2. **Archive old data** to separate collections
3. **Use transactions** for multi-document updates
4. **Implement retry logic** for transient failures

---

## Quick Reference

### Default Ports

| Service | Port | Description |
|---------|------|-------------|
| TCP Server | 9090 | Main client connections |
| SSL/TLS | 9090 | Encrypted connections (same port) |

### Default Paths

| Path | Description |
|------|-------------|
| `./data` | Data storage |
| `./logs` | Log files |
| `./config` | Configuration files |

### Command Summary

```bash
# Build
dotnet build -c Release

# Run server
dotnet run --project AdvGenNoSqlServer.Server

# Run tests
dotnet test

# Run benchmarks
dotnet run --project AdvGenNoSqlServer.Benchmarks -- all
```
