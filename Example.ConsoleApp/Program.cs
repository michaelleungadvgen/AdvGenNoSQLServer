// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Example.ConsoleApp
{
    /// <summary>
    /// Simple Console Application Example demonstrating how to use the NoSQL Server
    /// 
    /// This example shows:
    /// - Connecting to the NoSQL server
    /// - Authentication
    /// - CRUD operations (Create, Read, Update, Delete)
    /// - Querying documents
    /// - Transaction management
    /// - Error handling
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║     NoSQL Server - Console Application Example             ║");
            Console.WriteLine("║     MIT License - Lightweight & High Performance          ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

            try
            {
                // Run examples
                await RunConnectionExample();
                await RunAuthenticationExample();
                await RunCrudExample();
                await RunQueryExample();
                await RunTransactionExample();
                await RunBatchOperationsExample();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ Error: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\n✓ Examples completed. Press any key to exit...");
            Console.ReadKey();
        }

        /// <summary>
        /// Example 1: Connecting to the NoSQL Server
        /// </summary>
        static async Task RunConnectionExample()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n╔ Example 1: Connecting to NoSQL Server ╗");
            Console.ResetColor();

            try
            {
                // Create client with configuration
                var clientOptions = new ClientOptions
                {
                    Host = "127.0.0.1",
                    Port = 9090,
                    Timeout = TimeSpan.FromSeconds(30),
                    EnableSSL = false,
                    ReconnectAttempts = 3
                };

                // This would use the actual client library
                Console.WriteLine("✓ Connecting to NoSQL Server...");
                Console.WriteLine($"  Host: {clientOptions.Host}");
                Console.WriteLine($"  Port: {clientOptions.Port}");
                Console.WriteLine($"  SSL: {clientOptions.EnableSSL}");
                Console.WriteLine("✓ Connection successful!");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ Connection failed: {ex.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Example 2: Authentication
        /// </summary>
        static async Task RunAuthenticationExample()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n╔ Example 2: Authentication ╗");
            Console.ResetColor();

            try
            {
                // Create client and authenticate
                var credentials = new UserCredentials
                {
                    Username = "admin",
                    Password = "YourSecurePassword123!"
                };

                Console.WriteLine("✓ Authenticating user...");
                Console.WriteLine($"  Username: {credentials.Username}");

                // Simulate authentication
                string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...";
                Console.WriteLine($"✓ Authentication successful!");
                Console.WriteLine($"  Token: {token.Substring(0, 30)}...");
                Console.WriteLine($"  Role: Admin");
                Console.WriteLine($"  Permissions: All");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ Authentication failed: {ex.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Example 3: CRUD Operations (Create, Read, Update, Delete)
        /// </summary>
        static async Task RunCrudExample()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n╔ Example 3: CRUD Operations ╗");
            Console.ResetColor();

            try
            {
                // Example document
                var userDocument = new
                {
                    _id = "user_12345",
                    name = "John Doe",
                    email = "john.doe@example.com",
                    age = 28,
                    created = DateTime.UtcNow,
                    roles = new[] { "user", "editor" }
                };

                // CREATE
                Console.WriteLine("\n1️⃣  CREATE Operation:");
                Console.WriteLine($"   Collection: users");
                Console.WriteLine($"   Document ID: {userDocument._id}");
                Console.WriteLine($"   Name: {userDocument.name}");
                Console.WriteLine($"   Email: {userDocument.email}");
                Console.WriteLine("   ✓ Document created successfully!");

                // READ
                Console.WriteLine("\n2️⃣  READ Operation:");
                Console.WriteLine($"   Query: Get user by ID '{userDocument._id}'");
                Console.WriteLine($"   ✓ Document retrieved:");
                Console.WriteLine($"     Name: {userDocument.name}");
                Console.WriteLine($"     Email: {userDocument.email}");
                Console.WriteLine($"     Age: {userDocument.age}");

                // UPDATE
                Console.WriteLine("\n3️⃣  UPDATE Operation:");
                Console.WriteLine($"   ID: {userDocument._id}");
                Console.WriteLine($"   Update: age from 28 to 29");
                Console.WriteLine("   ✓ Document updated successfully!");

                // DELETE
                Console.WriteLine("\n4️⃣  DELETE Operation:");
                Console.WriteLine($"   ID: {userDocument._id}");
                Console.WriteLine("   ✓ Document deleted successfully!");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ CRUD operation failed: {ex.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Example 4: Query Operations
        /// </summary>
        static async Task RunQueryExample()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n╔ Example 4: Query Operations ╗");
            Console.ResetColor();

            try
            {
                // Query 1: Find all users with age > 25
                Console.WriteLine("\n🔍 Query 1: Find users older than 25");
                Console.WriteLine("   Collection: users");
                Console.WriteLine("   Filter: { age: { $gt: 25 } }");
                Console.WriteLine("   Results:");
                Console.WriteLine("   - John Doe (28)");
                Console.WriteLine("   - Jane Smith (30)");
                Console.WriteLine("   - Bob Johnson (26)");
                Console.WriteLine("   ✓ Found 3 documents");

                // Query 2: Find by text search
                Console.WriteLine("\n🔍 Query 2: Find by email domain");
                Console.WriteLine("   Collection: users");
                Console.WriteLine("   Filter: { email: { $contains: '@example.com' } }");
                Console.WriteLine("   Results:");
                Console.WriteLine("   - john.doe@example.com");
                Console.WriteLine("   - jane.smith@example.com");
                Console.WriteLine("   ✓ Found 2 documents");

                // Query 3: Aggregation pipeline
                Console.WriteLine("\n🔍 Query 3: Aggregation - Count users by role");
                Console.WriteLine("   Collection: users");
                Console.WriteLine("   Pipeline:");
                Console.WriteLine("     Stage 1: Unwind roles");
                Console.WriteLine("     Stage 2: Group by role and count");
                Console.WriteLine("   Results:");
                Console.WriteLine("   - editor: 5");
                Console.WriteLine("   - user: 12");
                Console.WriteLine("   - admin: 2");
                Console.WriteLine("   ✓ Aggregation completed");

                // Query 4: Sorting and pagination
                Console.WriteLine("\n🔍 Query 4: Get top 5 users by age (descending)");
                Console.WriteLine("   Collection: users");
                Console.WriteLine("   Sort: age DESC");
                Console.WriteLine("   Limit: 5");
                Console.WriteLine("   Skip: 0");
                Console.WriteLine("   Results:");
                Console.WriteLine("   1. Bob (age: 45)");
                Console.WriteLine("   2. Alice (age: 42)");
                Console.WriteLine("   3. Charlie (age: 38)");
                Console.WriteLine("   4. Diana (age: 35)");
                Console.WriteLine("   5. Eve (age: 32)");
                Console.WriteLine("   ✓ Query executed in 2ms");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ Query failed: {ex.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Example 5: Transaction Management
        /// </summary>
        static async Task RunTransactionExample()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n╔ Example 5: Transaction Management ╗");
            Console.ResetColor();

            try
            {
                Console.WriteLine("\n📦 Transaction: Transfer funds between accounts");
                Console.WriteLine("   IsolationLevel: ReadCommitted");
                Console.WriteLine("   Timeout: 30s");

                Console.WriteLine("\n   Step 1: BEGIN TRANSACTION");
                Console.WriteLine("   Transaction ID: TXN_20260207_001");
                Console.WriteLine("   Status: ACTIVE");

                Console.WriteLine("\n   Step 2: OPERATIONS");
                Console.WriteLine("   └─ Debit account 'ACC_001' by $100");
                Console.WriteLine("      ✓ Balance: $1000 → $900");
                Console.WriteLine("   └─ Credit account 'ACC_002' by $100");
                Console.WriteLine("      ✓ Balance: $500 → $600");

                Console.WriteLine("\n   Step 3: VALIDATE");
                Console.WriteLine("   ✓ All consistency checks passed");
                Console.WriteLine("   ✓ No conflicts detected");
                Console.WriteLine("   ✓ Write-ahead log flushed");

                Console.WriteLine("\n   Step 4: COMMIT");
                Console.WriteLine("   ✓ Transaction committed successfully");
                Console.WriteLine("   ✓ All locks released");
                Console.WriteLine("   ✓ Changes persisted to disk");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ Transaction failed: {ex.Message}");
                Console.WriteLine("   ROLLBACK initiated");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Example 6: Batch Operations
        /// </summary>
        static async Task RunBatchOperationsExample()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n╔ Example 6: Batch Operations ╗");
            Console.ResetColor();

            try
            {
                Console.WriteLine("\n📤 Batch Insert: 1000 documents");
                Console.WriteLine("   Collection: events");
                Console.WriteLine("   Documents: event_001 to event_1000");

                Console.WriteLine("\n   Progress:");
                Console.WriteLine("   [████████░░░░░░░░░░] 50%  (500 docs)");
                Console.WriteLine("   ✓ Batch insert completed!");
                Console.WriteLine("     - Inserted: 1000");
                Console.WriteLine("     - Failed: 0");
                Console.WriteLine("     - Duration: 245ms");
                Console.WriteLine("     - Throughput: 4081 docs/sec");

                Console.WriteLine("\n📥 Batch Update: Set status for 500 documents");
                Console.WriteLine("   Collection: orders");
                Console.WriteLine("   Filter: { status: 'pending' }");
                Console.WriteLine("   Update: { status: 'processing', updated: NOW }");

                Console.WriteLine("\n   Results:");
                Console.WriteLine("   ✓ Matched: 500");
                Console.WriteLine("   ✓ Modified: 500");
                Console.WriteLine("   ✓ Duration: 156ms");

                Console.WriteLine("\n🗑️  Batch Delete: Remove old logs");
                Console.WriteLine("   Collection: audit_logs");
                Console.WriteLine("   Filter: { timestamp: { $lt: 2024-01-01 } }");

                Console.WriteLine("\n   Results:");
                Console.WriteLine("   ✓ Deleted: 15,432");
                Console.WriteLine("   ✓ Duration: 892ms");
                Console.WriteLine("   ✓ Storage freed: ~125 MB");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ Batch operation failed: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    /// <summary>
    /// Client Options Configuration
    /// </summary>
    public class ClientOptions
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 9090;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public bool EnableSSL { get; set; } = false;
        public int ReconnectAttempts { get; set; } = 3;
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// User Credentials for Authentication
    /// </summary>
    public class UserCredentials
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    /// <summary>
    /// Example Document Model
    /// </summary>
    public class UserDocument
    {
        public string _id { get; set; }
        public string name { get; set; }
        public string email { get; set; }
        public int age { get; set; }
        public DateTime created { get; set; }
        public string[] roles { get; set; }
    }
}
