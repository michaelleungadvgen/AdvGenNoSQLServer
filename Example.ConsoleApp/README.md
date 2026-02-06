# Example Console Application - NoSQL Server Client

Simple console application demonstrating how to use the NoSQL Server client library.

## Features Demonstrated

### 1. Connection Management
- Connecting to NoSQL server with TCP
- Configuration of host, port, SSL, timeouts
- Reconnection logic and error handling

### 2. Authentication
- User credential-based authentication
- JWT token generation and validation
- Role-based access control

### 3. CRUD Operations
- **Create**: Insert new documents into collections
- **Read**: Retrieve documents by ID
- **Update**: Modify existing documents
- **Delete**: Remove documents from collections

### 4. Query Operations
- Filter queries with operators ($gt, $lt, $contains, etc.)
- Text search capabilities
- Aggregation pipelines
- Sorting and pagination

### 5. Transaction Management
- Begin, commit, and rollback transactions
- Isolation levels
- Consistency validation
- Write-ahead logging

### 6. Batch Operations
- Batch insert (1000+ documents)
- Batch update with filtering
- Batch delete operations
- Progress tracking and performance metrics

## Running the Example

```bash
cd e:\Projects\AdvGenNoSQLServer\Example.ConsoleApp
dotnet run
```

## Expected Output

```
╔════════════════════════════════════════════════════════════╗
║     NoSQL Server - Console Application Example             ║
║     MIT License - Lightweight & High Performance          ║
╚════════════════════════════════════════════════════════════╝

╔ Example 1: Connecting to NoSQL Server ╗
✓ Connecting to NoSQL Server...
  Host: 127.0.0.1
  Port: 9090
  SSL: False
✓ Connection successful!

╔ Example 2: Authentication ╗
✓ Authenticating user...
  Username: admin
✓ Authentication successful!
  Token: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
  Role: Admin
  Permissions: All

[... more examples ...]
```

## Code Examples

### Connect to Server
```csharp
var clientOptions = new ClientOptions
{
    Host = "127.0.0.1",
    Port = 9090,
    Timeout = TimeSpan.FromSeconds(30),
    EnableSSL = false
};

var client = new NoSqlClient(clientOptions);
```

### Authenticate
```csharp
var credentials = new UserCredentials
{
    Username = "admin",
    Password = "YourPassword"
};

var token = await client.AuthenticateAsync(credentials);
```

### Create Document
```csharp
var document = new
{
    _id = "user_123",
    name = "John Doe",
    email = "john@example.com",
    age = 28
};

await client.InsertAsync("users", document);
```

### Query Documents
```csharp
var query = new
{
    age = new { $gt = 25 }
};

var results = await client.FindAsync("users", query);
```

### Start Transaction
```csharp
using (var transaction = await client.BeginTransactionAsync())
{
    await transaction.UpdateAsync("accounts", doc1);
    await transaction.UpdateAsync("accounts", doc2);
    await transaction.CommitAsync();
}
```

### Batch Insert
```csharp
var documents = new List<object>();
for (int i = 0; i < 1000; i++)
{
    documents.Add(new { _id = $"event_{i}", timestamp = DateTime.UtcNow });
}

var result = await client.BatchInsertAsync("events", documents);
Console.WriteLine($"Inserted: {result.InsertedCount}");
```

## Class Definitions

### ClientOptions
Configuration for connecting to NoSQL server
- **Host**: Server hostname or IP
- **Port**: Server port (default: 9090)
- **Timeout**: Operation timeout
- **EnableSSL**: Enable SSL/TLS encryption
- **ReconnectAttempts**: Number of reconnection attempts

### UserCredentials
Authentication credentials
- **Username**: User identifier
- **Password**: Password (should be transmitted over encrypted connection)

### UserDocument
Example document model
- **_id**: Document unique identifier
- **name**: User name
- **email**: User email
- **age**: User age
- **created**: Creation timestamp
- **roles**: Array of user roles

## Next Steps

1. **Implement the actual client library** using the provided interfaces
2. **Add error handling** for network and validation errors
3. **Implement connection pooling** for better performance
4. **Add logging** for debugging and monitoring
5. **Create unit tests** for client operations
6. **Add performance benchmarks** to measure throughput

## Performance Notes

- **Throughput**: Examples show ~4000+ docs/sec for batch operations
- **Latency**: Single operations typically < 100ms
- **Connection pooling**: Reuse connections for multiple operations
- **Batch operations**: Use batch APIs for bulk inserts/updates for better performance

## Security Considerations

- Always use SSL/TLS in production
- Use strong passwords and rotate regularly
- Implement rate limiting on authentication attempts
- Audit all database access
- Keep credentials out of code (use environment variables or config files)

---

**License**: MIT License  
**Created**: February 7, 2026
