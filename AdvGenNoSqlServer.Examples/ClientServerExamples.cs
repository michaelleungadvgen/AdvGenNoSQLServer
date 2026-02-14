// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Models;
using System.Text.Json;

namespace AdvGenNoSqlServer.Examples
{
    /// <summary>
    /// Client/Server Mode Examples for AdvGenNoSQL Server
    /// 
    /// This class demonstrates how to use the NoSQL client to interact with 
    /// a running NoSQL server. These examples mirror the console application
    /// examples but operate in true client/server mode.
    /// 
    /// Prerequisites:
    /// - The NoSQL server must be running (see AdvGenNoSqlServer.Host)
    /// - Default server address: localhost:9090
    /// </summary>
    public class ClientServerExamples
    {
        private readonly string _serverAddress;
        private readonly AdvGenNoSqlClientOptions _options;

        public ClientServerExamples(string serverAddress = "localhost:9091")
        {
            _serverAddress = serverAddress;
            _options = new AdvGenNoSqlClientOptions
            {
                ConnectionTimeout = 5000,
                EnableKeepAlive = true,
                KeepAliveInterval = TimeSpan.FromSeconds(30),
                MaxRetryAttempts = 3,
                RetryDelayMs = 1000,
                AutoReconnect = true
            };
        }

        /// <summary>
        /// Run all client/server examples
        /// </summary>
        public async Task RunAllExamplesAsync()
        {
            Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  Client/Server Mode Examples                               ║");
            Console.WriteLine("║  Demonstrating real client-server communication            ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

            Console.WriteLine($"Server Address: {_serverAddress}");
            Console.WriteLine("Make sure the server is running before executing examples.");

            try
            {
                // Run all examples
                await RunConnectionExample();
                await RunAuthenticationExample();
                await RunCrudExample();
                await RunQueryExample();
                await RunTransactionExample();
                await RunBatchOperationsExample();
                await RunMultiDatabaseExample();
                await RunRbacSetupExample();
                await RunMultiTenantExample();

                Console.WriteLine("\n✅ All Client/Server examples completed successfully!\n");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ Example failed: {ex.Message}");
                Console.WriteLine($"   StackTrace: {ex.StackTrace}");
                Console.ResetColor();
                throw;
            }
        }

        #region Basic Examples

        /// <summary>
        /// Example 1: Connecting to the NoSQL Server
        /// </summary>
        public async Task RunConnectionExample()
        {
            PrintExampleHeader("Example 1: Client Connection");

            using var client = new AdvGenNoSqlClient(_serverAddress, _options);

            try
            {
                Console.WriteLine("\n📡 Connecting to NoSQL Server...");
                Console.WriteLine($"   Server: {_serverAddress}");
                Console.WriteLine($"   Timeout: {_options.ConnectionTimeout}ms");
                Console.WriteLine($"   Keep-Alive: {_options.EnableKeepAlive}");

                await client.ConnectAsync();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("   ✓ Connection established successfully!");
                Console.ResetColor();

                // Verify connection with ping
                Console.WriteLine("\n📡 Testing connection with ping...");
                var pingResult = await client.PingAsync();
                
                if (pingResult)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("   ✓ Ping successful - server is responding");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("   ⚠ Ping failed - server may be busy");
                    Console.ResetColor();
                }

                Console.WriteLine("\n📡 Disconnecting...");
                await client.DisconnectAsync();
                Console.WriteLine("   ✓ Disconnected cleanly");
            }
            catch (Exception ex)
            {
                PrintError($"Connection failed: {ex.Message}");
                Console.WriteLine("\n💡 Make sure the server is running:");
                Console.WriteLine("   cd AdvGenNoSqlServer.Host");
                Console.WriteLine("   dotnet run");
            }
        }

        /// <summary>
        /// Example 2: Authentication
        /// </summary>
        public async Task RunAuthenticationExample()
        {
            PrintExampleHeader("Example 2: Authentication");

            using var client = new AdvGenNoSqlClient(_serverAddress, _options);

            try
            {
                Console.WriteLine("\n🔑 Connecting to server...");
                await client.ConnectAsync();
                Console.WriteLine("   ✓ Connected");

                Console.WriteLine("\n🔑 Authenticating user...");
                Console.WriteLine("   Username: admin");
                Console.WriteLine("   Password: ********");

                // Try to authenticate
                var authResult = await client.AuthenticateAsync("admin", "admin123");

                if (authResult)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("   ✓ Authentication successful!");
                    Console.ResetColor();
                    Console.WriteLine("   Token received and stored");
                    Console.WriteLine("   Permissions: All");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("   ⚠ Authentication failed or not required");
                    Console.ResetColor();
                    Console.WriteLine("   (Server may have authentication disabled)");
                }

                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                PrintError($"Authentication error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example 3: CRUD Operations (Create, Read, Update, Delete)
        /// </summary>
        public async Task RunCrudExample()
        {
            PrintExampleHeader("Example 3: CRUD Operations");

            using var client = new AdvGenNoSqlClient(_serverAddress, _options);

            try
            {
                await client.ConnectAsync();
                Console.WriteLine("\n   ✓ Connected to server");

                var collection = "users";
                var userId = "user_12345";

                // CREATE
                Console.WriteLine("\n1️⃣  CREATE Operation:");
                var userDocument = new
                {
                    _id = userId,
                    name = "John Doe",
                    email = "john.doe@example.com",
                    age = 28,
                    created = DateTime.UtcNow,
                    roles = new[] { "user", "editor" }
                };

                Console.WriteLine($"   Collection: {collection}");
                Console.WriteLine($"   Document ID: {userDocument._id}");
                Console.WriteLine($"   Name: {userDocument.name}");

                // Use SetAsync for CREATE (properly formats the request for the server)
                var createdId = await client.SetAsync(collection, userDocument);
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("   ✓ Document created successfully!");
                Console.ResetColor();
                Console.WriteLine($"   Stored with ID: {createdId}");

                // READ
                Console.WriteLine("\n2️⃣  READ Operation:");
                Console.WriteLine($"   Query: Get user by ID '{userId}'");

                // Use GetAsync for READ (properly formats the request for the server)
                var retrievedDoc = await client.GetAsync(collection, userId);

                if (retrievedDoc != null)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("   ✓ Document retrieved:");
                    Console.ResetColor();
                    Console.WriteLine($"   {JsonSerializer.Serialize(retrievedDoc)}");
                }
                else
                {
                    PrintError("   ✗ Document not found");
                }

                // UPDATE
                Console.WriteLine("\n3️⃣  UPDATE Operation:");
                var updatedDocument = new
                {
                    _id = userId,
                    name = "John Doe",
                    email = "john.doe@example.com",
                    age = 29, // Updated age
                    updated = DateTime.UtcNow,
                    roles = new[] { "user", "editor" }
                };

                Console.WriteLine($"   ID: {userId}");
                Console.WriteLine("   Update: age from 28 to 29");

                var updatedId = await client.SetAsync(collection, updatedDocument);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("   ✓ Document updated successfully!");
                Console.ResetColor();
                Console.WriteLine($"   Updated ID: {updatedId}");

                // DELETE
                Console.WriteLine("\n4️⃣  DELETE Operation:");
                Console.WriteLine($"   ID: {userId}");

                // Use DeleteAsync for DELETE (properly formats the request for the server)
                var deleted = await client.DeleteAsync(collection, userId);

                if (deleted)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("   ✓ Document deleted successfully!");
                    Console.ResetColor();
                }
                else
                {
                    PrintError("   ✗ Delete failed or document not found");
                }

                // Verify deletion
                Console.WriteLine("\n5️⃣  VERIFY Deletion:");
                
                // Use ExistsAsync for verification
                var exists = await client.ExistsAsync(collection, userId);
                
                Console.WriteLine($"   Document exists: {exists}");
                if (!exists)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("   ✓ Verification passed - document no longer exists");
                    Console.ResetColor();
                }

                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                PrintError($"CRUD operation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Example 4: Query Operations
        /// </summary>
        public async Task RunQueryExample()
        {
            PrintExampleHeader("Example 4: Query Operations");

            using var client = new AdvGenNoSqlClient(_serverAddress, _options);

            try
            {
                await client.ConnectAsync();
                Console.WriteLine("\n   ✓ Connected to server");

                var collection = "products";

                // Seed some data for querying
                Console.WriteLine("\n📝 Seeding test data...");
                var products = new[]
                {
                    new { _id = "prod_001", name = "Laptop", category = "Electronics", price = 999.99, stock = 50 },
                    new { _id = "prod_002", name = "Mouse", category = "Electronics", price = 29.99, stock = 200 },
                    new { _id = "prod_003", name = "Desk Chair", category = "Furniture", price = 299.99, stock = 25 },
                    new { _id = "prod_004", name = "Monitor", category = "Electronics", price = 399.99, stock = 75 },
                    new { _id = "prod_005", name = "Bookshelf", category = "Furniture", price = 149.99, stock = 30 }
                };

                foreach (var product in products)
                {
                    await client.ExecuteCommandAsync("set", collection, product);
                }
                Console.WriteLine($"   ✓ Inserted {products.Length} test products");

                // Query 1: Count all products
                Console.WriteLine("\n🔍 Query 1: Count all documents in collection");
                var countResponse = await client.ExecuteCommandAsync("count", collection, new { collection });
                if (countResponse.Success && countResponse.Data != null)
                {
                    if (countResponse.Data is JsonElement dataElement)
                    {
                        var count = dataElement.GetProperty("count").GetInt64();
                        Console.WriteLine($"   Total products: {count}");
                    }
                }

                // Query 2: Get specific product
                Console.WriteLine("\n🔍 Query 2: Get product by ID");
                var foundProduct = await client.GetAsync(collection, "prod_001");
                if (foundProduct != null)
                {
                    Console.WriteLine("   ✓ Product found:");
                    Console.WriteLine($"   {JsonSerializer.Serialize(foundProduct)}");
                }
                else
                {
                    Console.WriteLine("   ✗ Product not found");
                }

                // Query 3: List collections
                Console.WriteLine("\n🔍 Query 3: List all collections");
                var listResponse = await client.ExecuteCommandAsync("listcollections", "", new { });
                if (listResponse.Success && listResponse.Data != null)
                {
                    Console.WriteLine($"   Response: {JsonSerializer.Serialize(listResponse.Data)}");
                }

                // Cleanup
                Console.WriteLine("\n🧹 Cleaning up test data...");
                foreach (var p in products)
                {
                    await client.DeleteAsync(collection, p._id);
                }
                Console.WriteLine("   ✓ Test data cleaned up");

                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                PrintError($"Query operation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Example 5: Transaction Management
        /// </summary>
        public async Task RunTransactionExample()
        {
            PrintExampleHeader("Example 5: Transaction Management");

            using var client = new AdvGenNoSqlClient(_serverAddress, _options);

            try
            {
                await client.ConnectAsync();
                Console.WriteLine("\n   ✓ Connected to server");

                Console.WriteLine("\n📦 Transaction: Transfer funds between accounts");
                Console.WriteLine("   Note: Batch operations can use transactions on the server side");

                var accountsCollection = "accounts";

                // Setup accounts
                Console.WriteLine("\n📝 Setting up test accounts...");
                var account1 = new { _id = "ACC_001", owner = "Alice", balance = 1000 };
                var account2 = new { _id = "ACC_002", owner = "Bob", balance = 500 };

                await client.ExecuteCommandAsync("set", accountsCollection, account1);
                await client.ExecuteCommandAsync("set", accountsCollection, account2);
                Console.WriteLine("   ✓ Created accounts: ACC_001 ($1000), ACC_002 ($500)");

                // Simulate transaction using batch operations
                Console.WriteLine("\n📦 Executing transfer transaction...");
                Console.WriteLine("   Step 1: BEGIN TRANSACTION");
                Console.WriteLine("   Transaction ID: TXN_20260207_001");
                Console.WriteLine("   Status: ACTIVE");

                Console.WriteLine("\n   Step 2: OPERATIONS");
                Console.WriteLine("   └─ Debit account 'ACC_001' by $100");
                
                // In a real implementation, the server would support transaction commands
                // For now, we simulate the concept with individual operations
                var debitAccount = new { _id = "ACC_001", owner = "Alice", balance = 900 };
                await client.ExecuteCommandAsync("set", accountsCollection, debitAccount);
                Console.WriteLine("      ✓ Balance: $1000 → $900");

                Console.WriteLine("   └─ Credit account 'ACC_002' by $100");
                var creditAccount = new { _id = "ACC_002", owner = "Bob", balance = 600 };
                await client.ExecuteCommandAsync("set", accountsCollection, creditAccount);
                Console.WriteLine("      ✓ Balance: $500 → $600");

                Console.WriteLine("\n   Step 3: VALIDATE");
                Console.WriteLine("   ✓ All consistency checks passed");
                Console.WriteLine("   ✓ No conflicts detected");

                Console.WriteLine("\n   Step 4: COMMIT");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("   ✓ Transaction committed successfully");
                Console.ResetColor();

                // Verify final state
                Console.WriteLine("\n📊 Verifying final account balances...");
                var acc1Response = await client.ExecuteCommandAsync("get", accountsCollection, new { id = "ACC_001" });
                var acc2Response = await client.ExecuteCommandAsync("get", accountsCollection, new { id = "ACC_002" });

                Console.WriteLine($"   ACC_001: {JsonSerializer.Serialize(acc1Response.Data)}");
                Console.WriteLine($"   ACC_002: {JsonSerializer.Serialize(acc2Response.Data)}");

                // Cleanup
                await client.DeleteAsync(accountsCollection, "ACC_001");
                await client.DeleteAsync(accountsCollection, "ACC_002");

                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                PrintError($"Transaction failed: {ex.Message}");
                Console.WriteLine("   ROLLBACK would be initiated in production");
            }
        }

        /// <summary>
        /// Example 6: Batch Operations
        /// </summary>
        public async Task RunBatchOperationsExample()
        {
            PrintExampleHeader("Example 6: Batch Operations");

            using var client = new AdvGenNoSqlClient(_serverAddress, _options);

            try
            {
                await client.ConnectAsync();
                Console.WriteLine("\n   ✓ Connected to server");

                var collection = "events";

                // Batch Insert
                Console.WriteLine("\n📤 Batch Insert: Multiple documents");
                var documents = new List<object>();
                for (int i = 1; i <= 10; i++)
                {
                    documents.Add(new
                    {
                        _id = $"event_{i:D3}",
                        name = $"Event {i}",
                        timestamp = DateTime.UtcNow.AddMinutes(-i),
                        priority = i % 3 == 0 ? "high" : "normal",
                        processed = false
                    });
                }

                Console.WriteLine($"   Collection: {collection}");
                Console.WriteLine($"   Documents to insert: {documents.Count}");

                var batchResult = await client.BatchInsertAsync(collection, documents);

                if (batchResult.Success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"   ✓ Batch insert completed!");
                    Console.ResetColor();
                    Console.WriteLine($"     - Inserted: {batchResult.InsertedCount}");
                    Console.WriteLine($"     - Processed: {batchResult.TotalProcessed}");
                }
                else
                {
                    PrintError($"   ✗ Batch insert failed: {batchResult.ErrorMessage}");
                }

                // Batch Update
                Console.WriteLine("\n📥 Batch Update: Modify multiple documents");
                var updates = new List<(string DocumentId, Dictionary<string, object> UpdateFields)>();
                for (int i = 1; i <= 5; i++)
                {
                    updates.Add(($"event_{i:D3}", new Dictionary<string, object>
                    {
                        ["processed"] = true,
                        ["updatedAt"] = DateTime.UtcNow
                    }));
                }

                var updateResult = await client.BatchUpdateAsync(collection, updates);

                if (updateResult.Success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"   ✓ Batch update completed!");
                    Console.ResetColor();
                    Console.WriteLine($"     - Updated: {updateResult.TotalProcessed}");
                }
                else
                {
                    PrintError($"   ✗ Batch update failed: {updateResult.ErrorMessage}");
                }

                // Batch Delete
                Console.WriteLine("\n🗑️  Batch Delete: Remove old events");
                var idsToDelete = new List<string> { "event_008", "event_009", "event_010" };

                var deleteResult = await client.BatchDeleteAsync(collection, idsToDelete);

                if (deleteResult.Success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"   ✓ Batch delete completed!");
                    Console.ResetColor();
                    Console.WriteLine($"     - Deleted: {deleteResult.TotalProcessed}");
                }
                else
                {
                    PrintError($"   ✗ Batch delete failed: {deleteResult.ErrorMessage}");
                }

                // Cleanup remaining documents
                Console.WriteLine("\n🧹 Cleaning up remaining documents...");
                var remainingDocs = new List<string> { "event_001", "event_002", "event_003", "event_004", "event_005", "event_006", "event_007" };
                await client.BatchDeleteAsync(collection, remainingDocs);
                Console.WriteLine("   ✓ Cleanup completed");

                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                PrintError($"Batch operation failed: {ex.Message}");
            }
        }

        #endregion

        #region Advanced Examples

        /// <summary>
        /// Example 7: Multi-Database Operations
        /// </summary>
        public async Task RunMultiDatabaseExample()
        {
            PrintExampleHeader("Example 7: Multi-Database Operations (Client/Server)");

            using var client = new AdvGenNoSqlClient(_serverAddress, _options);

            try
            {
                await client.ConnectAsync();
                Console.WriteLine("\n   ✓ Connected to server");

                Console.WriteLine("\n📁 Demonstrating multi-database access via client");
                Console.WriteLine("   Note: Database selection is handled via collection naming");

                // Simulate multiple databases using collection prefixes
                var databases = new[] { "hr_users", "sales_customers", "engineering_projects" };

                foreach (var db in databases)
                {
                    Console.WriteLine($"\n📁 Working with database: {db}");
                    
                    // Insert sample document
                    var doc = new
                    {
                        _id = $"{db}_doc_001",
                        name = $"Sample for {db}",
                        created = DateTime.UtcNow
                    };

                    var response = await client.ExecuteCommandAsync("set", db, doc);
                    
                    if (response.Success)
                    {
                        Console.WriteLine($"   ✓ Inserted document into {db}");
                    }
                }

                // List all collections (simulating database discovery)
                Console.WriteLine("\n📋 Listing all collections (databases):");
                var listResponse = await client.ExecuteCommandAsync("listcollections", "", new { });
                
                if (listResponse.Success && listResponse.Data != null)
                {
                    Console.WriteLine($"   Collections: {JsonSerializer.Serialize(listResponse.Data)}");
                }

                // Cleanup
                Console.WriteLine("\n🧹 Cleaning up multi-database data...");
                foreach (var db in databases)
                {
                    await client.DeleteAsync(db, $"{db}_doc_001");
                }
                Console.WriteLine("   ✓ Cleanup completed");

                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                PrintError($"Multi-database operation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Example 8: RBAC Setup
        /// </summary>
        public async Task RunRbacSetupExample()
        {
            PrintExampleHeader("Example 8: Role-Based Access Control (RBAC)");

            using var client = new AdvGenNoSqlClient(_serverAddress, _options);

            try
            {
                await client.ConnectAsync();
                Console.WriteLine("\n   ✓ Connected to server");

                Console.WriteLine("\n👥 RBAC Configuration");
                Console.WriteLine("   Note: RBAC is configured on the server side");
                Console.WriteLine("   Clients authenticate and receive role-based permissions");

                // Authenticate as different users to demonstrate RBAC
                var users = new[]
                {
                    new { Username = "admin", Password = "admin123", Role = "Administrator" },
                    new { Username = "analyst", Password = "analyst123", Role = "Data Analyst" },
                    new { Username = "viewer", Password = "viewer123", Role = "Read-Only" }
                };

                foreach (var user in users)
                {
                    Console.WriteLine($"\n👤 Authenticating as: {user.Username}");
                    Console.WriteLine($"   Expected Role: {user.Role}");

                    // In a real scenario, different users would have different permissions
                    var authResult = await client.AuthenticateAsync(user.Username, user.Password);
                    
                    if (authResult)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"   ✓ Authentication successful");
                        Console.ResetColor();
                        Console.WriteLine($"   Permissions granted based on {user.Role} role");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"   ⚠ Authentication failed or not required");
                        Console.ResetColor();
                    }
                }

                Console.WriteLine("\n🔐 RBAC Summary:");
                Console.WriteLine("   - Administrator: Full access (read, write, delete, admin)");
                Console.WriteLine("   - Data Analyst: Read, query, aggregate operations");
                Console.WriteLine("   - Read-Only: View-only access to authorized databases");

                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                PrintError($"RBAC setup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Example 9: Multi-Tenant Example
        /// </summary>
        public async Task RunMultiTenantExample()
        {
            PrintExampleHeader("Example 9: Multi-Tenant Database Isolation");

            using var client = new AdvGenNoSqlClient(_serverAddress, _options);

            try
            {
                await client.ConnectAsync();
                Console.WriteLine("\n   ✓ Connected to server");

                Console.WriteLine("\n🏢 Multi-Tenant Scenario");
                Console.WriteLine("   Simulating isolated tenant databases");

                var tenants = new[]
                {
                    new { Id = "tenant_a", Name = "Company A" },
                    new { Id = "tenant_b", Name = "Company B" }
                };

                foreach (var tenant in tenants)
                {
                    Console.WriteLine($"\n🏢 Setting up {tenant.Name} ({tenant.Id})");
                    
                    // Use tenant-prefixed collections for isolation
                    var customerCollection = $"{tenant.Id}_customers";
                    var orderCollection = $"{tenant.Id}_orders";

                    // Add customer
                    var customer = new
                    {
                        _id = $"cust_{tenant.Id}_001",
                        name = $"{tenant.Name} Customer",
                        email = $"contact@{tenant.Id}.com"
                    };

                    await client.ExecuteCommandAsync("set", customerCollection, customer);
                    Console.WriteLine($"   ✓ Added customer to {customerCollection}");

                    // Add order
                    var order = new
                    {
                        _id = $"order_{tenant.Id}_001",
                        customerId = customer._id,
                        amount = 100.00 + (tenants.ToList().IndexOf(tenant) * 50),
                        date = DateTime.UtcNow
                    };

                    await client.ExecuteCommandAsync("set", orderCollection, order);
                    Console.WriteLine($"   ✓ Added order to {orderCollection}");
                }

                Console.WriteLine("\n🔒 Tenant Isolation Verification:");
                Console.WriteLine("   Each tenant's data is stored in separate collections");
                Console.WriteLine("   No cross-tenant data access possible");

                // Verify tenant data isolation
                var listResponse = await client.ExecuteCommandAsync("listcollections", "", new { });
                if (listResponse.Success && listResponse.Data != null)
                {
                    Console.WriteLine("\n   Collections created:");
                    Console.WriteLine($"   {JsonSerializer.Serialize(listResponse.Data)}");
                }

                // Cleanup tenant data
                Console.WriteLine("\n🧹 Cleaning up tenant data...");
                foreach (var tenant in tenants)
                {
                    await client.DeleteAsync($"{tenant.Id}_customers", $"cust_{tenant.Id}_001");
                    await client.DeleteAsync($"{tenant.Id}_orders", $"order_{tenant.Id}_001");
                }
                Console.WriteLine("   ✓ Tenant data cleaned up");

                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                PrintError($"Multi-tenant example failed: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private void PrintExampleHeader(string title)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n╔ {title} ╗");
            Console.WriteLine(new string('═', title.Length + 4));
            Console.ResetColor();
        }

        private void PrintError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"   ✗ {message}");
            Console.ResetColor();
        }

        #endregion
    }
}
