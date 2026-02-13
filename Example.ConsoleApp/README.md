# AdvGen NoSQL Server (C#) - Example Console Application

WARNING: This example and the surrounding codebase were written quickly as "vibe coding" for a supermarket price-comparison application. Security was NOT a focus for this project — treat this code as a prototype only. DO NOT store any sensitive information (passwords, API keys, personal data, payment details, etc.) in this repository or in runtime configuration.

Console application demonstrating how to use the NoSQL Server client library and core components.

## Features Demonstrated

### 1. Connection Management
- Connecting to NoSQL server with TCP
- Configuration of host, port, SSL, timeouts
- Reconnection logic and error handling

### 2. Authentication
- User credential-based authentication
- JWT token generation and validation
- Role-based access control (RBAC)

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

### 7. Multi-Database Operations ⭐ NEW
- Creating and managing multiple isolated databases
- Department-based database separation
- Database isolation verification
- Cross-database analytics

### 8. Role-Based Access Control (RBAC) ⭐ NEW
- Creating custom roles with specific permissions
- Assigning roles to users
- Permission checking and enforcement
- Multi-role user permission aggregation

### 9. Multi-Tenant Isolation ⭐ NEW
- Tenant-specific database creation
- Data isolation between tenants
- Access control per tenant
- Super admin cross-tenant access

## Running the Example

```bash
cd e:\Projects\AdvGenNoSQLServer\Example.ConsoleApp
dotnet run
```

Then select an option from the menu:
- **1**: Basic Examples (Simulated operations)
- **2**: Multi-Database & RBAC Examples (Real components)
- **3**: Run All Examples
- **4**: Exit

## Expected Output

### Multi-Database & RBAC Examples

```
╔════════════════════════════════════════════════════════════╗
║  Multi-Database & RBAC Examples                            ║
║  Demonstrating database isolation and access control       ║
╚════════════════════════════════════════════════════════════╝

╔ Example 1: Multi-Database Operations ╗

📁 Creating HR Database...
   ✓ Inserted: Alice Johnson (HR Department)

📁 Creating Sales Database...
   ✓ Inserted: Bob Smith (Sales Department)

📁 Creating Engineering Database...
   ✓ Inserted: Carol White (Engineering Department)

🔒 Database Isolation Verification:
   HR Database: 1 employees
   Sales Database: 1 salespeople
   Engineering Database: 1 engineers
   ✓ HR DB contains sales_001: False (should be False)
   ✓ Sales DB contains emp_001: False (should be False)

✅ Multi-database operations completed successfully!

╔ Example 2: Role-Based Access Control (RBAC) Setup ╗

👥 Creating Custom Roles...
   ✓ Created role: DepartmentAdmin (7 permissions)
   ✓ Created role: DataAnalyst (3 permissions)
   ✓ Created role: BackupOperator (2 permissions)

📋 Available Roles:
   - Admin (13 permissions)
   - PowerUser (7 permissions)
   - DepartmentAdmin (7 permissions)
   - DataAnalyst (3 permissions)
   - BackupOperator (2 permissions)

👤 Creating Users with Role Assignments...
   ✓ User: hr_admin (Role: DepartmentAdmin)
   ✓ User: sales_analyst (Role: DataAnalyst)
   ✓ User: backup_op (Role: BackupOperator)

✅ RBAC setup completed successfully!
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

### Create Multiple Databases (Multi-Tenant)
```csharp
// Create isolated databases for different tenants
var tenantAStore = new PersistentDocumentStore("./data/tenant_a");
var tenantBStore = new PersistentDocumentStore("./data/tenant_b");

await tenantAStore.InitializeAsync();
await tenantBStore.InitializeAsync();

// Insert data into Tenant A
await tenantAStore.InsertAsync("customers", new Document { 
    Id = "cust_001",
    Data = new Dictionary<string, object> {
        ["name"] = "Tenant A Customer",
        ["tenant_id"] = "tenant_a"
    }
});

// Tenant B cannot see Tenant A's data
var existsInB = await tenantBStore.ExistsAsync("customers", "cust_001");
// existsInB == false
```

### RBAC Role Creation and Permission Checking
```csharp
// Create RoleManager
var roleManager = new RoleManager();

// Create custom role with specific permissions
roleManager.CreateRole(
    "DepartmentAdmin",
    "Administrator for a specific department",
    new[] {
        Permissions.DocumentRead,
        Permissions.DocumentWrite,
        Permissions.DocumentDelete,
        Permissions.QueryExecute
    }
);

// Assign role to user
roleManager.AssignRoleToUser("hr_admin", "DepartmentAdmin");

// Check if user has permission
bool canWrite = roleManager.UserHasPermission("hr_admin", Permissions.DocumentWrite);
// canWrite == true
```

### Cross-Database Analytics
```csharp
// Analytics user with access to multiple databases
var hrStore = new PersistentDocumentStore("./data/hr");
var financeStore = new PersistentDocumentStore("./data/finance");

await hrStore.InitializeAsync();
await financeStore.InitializeAsync();

// Aggregate data from both databases
var employees = await hrStore.GetAllAsync("employees");
var expenses = await financeStore.GetAllAsync("expenses");

var totalPayroll = employees.Sum(e => (long)e.Data["salary"]);
var totalExpenses = expenses.Sum(e => (long)e.Data["amount"]);

Console.WriteLine($"Payroll/Expense ratio: {(double)totalPayroll / totalExpenses:F2}x");
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

### PersistentDocumentStore
File-based persistent document store
- **DataPath**: Base directory for storing collection files
- **InsertAsync**: Insert a document
- **GetAsync**: Retrieve a document by ID
- **GetAllAsync**: Get all documents in a collection
- **UpdateAsync**: Update an existing document
- **DeleteAsync**: Delete a document
- **ExistsAsync**: Check if a document exists
- **CountAsync**: Count documents in a collection
- **InitializeAsync**: Initialize the store and load existing data

### RoleManager
Manages roles and permissions for RBAC
- **CreateRole**: Create a new role with permissions
- **DeleteRole**: Delete a role
- **GetRole**: Get a role by name
- **GetAllRoles**: Get all defined roles
- **AssignRoleToUser**: Assign a role to a user
- **RemoveRoleFromUser**: Remove a role from a user
- **UserHasPermission**: Check if a user has a specific permission
- **GetUserPermissions**: Get all permissions for a user

### Permissions
Predefined permission constants
- **DocumentRead**: Read documents (`document:read`)
- **DocumentWrite**: Write documents (`document:write`)
- **DocumentDelete**: Delete documents (`document:delete`)
- **CollectionCreate**: Create collections (`collection:create`)
- **CollectionDelete**: Delete collections (`collection:delete`)
- **QueryExecute**: Execute queries (`query:execute`)
- **QueryAggregate**: Execute aggregations (`query:aggregate`)
- **TransactionExecute**: Execute transactions (`transaction:execute`)
- **UserManage**: Manage users (`user:manage`)
- **RoleManage**: Manage roles (`role:manage`)

## Project Structure

```
Example.ConsoleApp/
├── Program.cs                          # Main entry point with menu
├── MultiDatabaseAndRbacExamples.cs     # NEW: Multi-database & RBAC demos
├── Example.ConsoleApp.csproj           # Project file
└── README.md                           # This file
```

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
- **Multi-database**: Each database is isolated with separate file storage

## Security Considerations

- Always use SSL/TLS in production
- Use strong passwords and rotate regularly
- Implement rate limiting on authentication attempts
- Audit all database access
- Keep credentials out of code (use environment variables or config files)
- Follow RBAC principles - grant minimum necessary permissions
- Isolate tenant data in separate databases
- Regular backups for disaster recovery

---

**License**: MIT License  
**Created**: February 7, 2026  
**Updated**: February 13, 2026 (Added Multi-Database & RBAC examples)
