// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Indexing;
using System.Text.Json;

namespace AdvGenNoSqlServer.Examples;

/// <summary>
/// AdvGenNoSQL Server Examples - Entry Point
///
/// This application provides comprehensive examples demonstrating:
/// - Basic storage operations (local)
/// - Index operations (local)
/// - Client/Server mode (requires running server)
/// - Complete walkthrough of all NoSQL features
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║     AdvGenNoSQL Server - Examples Application              ║");
        Console.WriteLine("║     MIT License - Lightweight & High Performance          ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

        // Parse command line arguments
        string serverAddress = "localhost:9091"; // Default address
        bool useArgsForServer = false;

        if (args.Length > 0)
        {
            if (args[0].StartsWith("--server=") || args[0].StartsWith("-s="))
            {
                serverAddress = args[0].Split('=', 2)[1];
                Console.WriteLine($"Using server address from command line: {serverAddress}");
                useArgsForServer = true;
            }
            else if (args[0] == "--help" || args[0] == "-h")
            {
                ShowHelp();
                return;
            }
            else if (args[0] == "--run-all")
            {
                // Run all examples with default server address
                Console.WriteLine($"Running ALL Client/Server examples with server: {serverAddress}");
                try
                {
                    var examples = new ClientServerExamples(serverAddress);
                    await examples.RunAllExamplesAsync();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n❌ Error: {ex.Message}");
                    Console.WriteLine($"   StackTrace: {ex.StackTrace}");
                    Console.ResetColor();
                }
                return;
            }
            else
            {
                Console.WriteLine($"Invalid argument: {args[0]}");
                ShowHelp();
                return;
            }
        }

        // If server address was provided via args, run all examples automatically
        if (useArgsForServer)
        {
            Console.WriteLine($"Running ALL Client/Server examples with server: {serverAddress}");
            try
            {
                var examples = new ClientServerExamples(serverAddress);
                await examples.RunAllExamplesAsync();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ Error: {ex.Message}");
                Console.WriteLine($"   StackTrace: {ex.StackTrace}");
                Console.ResetColor();
            }
            return;
        }

        // Show menu for interactive mode
        while (true)
        {
            Console.WriteLine("\n📋 Available Examples:\n");
            Console.WriteLine("  LOCAL EXAMPLES (No server required):");
            Console.WriteLine("    1. Storage Examples - Document CRUD operations");
            Console.WriteLine("    2. Index Examples - B-tree index operations");
            Console.WriteLine();
            Console.WriteLine("  CLIENT/SERVER EXAMPLES (Requires running server):");
            Console.WriteLine("    3. Connection & Authentication");
            Console.WriteLine("    4. CRUD Operations");
            Console.WriteLine("    5. Query Operations");
            Console.WriteLine("    6. Transaction Management");
            Console.WriteLine("    7. Batch Operations");
            Console.WriteLine("    8. Multi-Database Operations");
            Console.WriteLine("    9. RBAC (Role-Based Access Control)");
            Console.WriteLine("   10. Multi-Tenant Isolation");
            Console.WriteLine("   11. Run ALL Client/Server Examples");
            Console.WriteLine();
            Console.WriteLine("   12. Run ALL Local Examples");
            Console.WriteLine("   13. Run ALL Examples (Local + Client/Server)");
            Console.WriteLine("    0. Exit");
            Console.Write($"\nSelect option (0-13) [Current server: {serverAddress}]: ");

            var choice = Console.ReadLine();

            try
            {
                switch (choice)
                {
                    // Local Examples
                    case "1":
                        await RunStorageExample();
                        break;
                    case "2":
                        RunIndexExample();
                        break;

                    // Client/Server Examples - Individual
                    case "3":
                        await RunClientServerExample(serverAddress, examples => examples.RunConnectionExample());
                        await RunClientServerExample(serverAddress, examples => examples.RunAuthenticationExample());
                        break;
                    case "4":
                        await RunClientServerExample(serverAddress, examples => examples.RunCrudExample());
                        break;
                    case "5":
                        await RunClientServerExample(serverAddress, examples => examples.RunQueryExample());
                        break;
                    case "6":
                        await RunClientServerExample(serverAddress, examples => examples.RunTransactionExample());
                        break;
                    case "7":
                        await RunClientServerExample(serverAddress, examples => examples.RunBatchOperationsExample());
                        break;
                    case "8":
                        await RunClientServerExample(serverAddress, examples => examples.RunMultiDatabaseExample());
                        break;
                    case "9":
                        await RunClientServerExample(serverAddress, examples => examples.RunRbacSetupExample());
                        break;
                    case "10":
                        await RunClientServerExample(serverAddress, examples => examples.RunMultiTenantExample());
                        break;

                    // Run All Examples
                    case "11":
                        await RunAllClientServerExamples(serverAddress);
                        break;
                    case "12":
                        await RunStorageExample();
                        Console.WriteLine();
                        RunIndexExample();
                        break;
                    case "13":
                        await RunStorageExample();
                        Console.WriteLine();
                        RunIndexExample();
                        Console.WriteLine();
                        await RunAllClientServerExamples(serverAddress);
                        break;

                    case "0":
                        Console.WriteLine("\n👋 Goodbye!");
                        return;

                    default:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("\n⚠ Invalid option. Please select 0-13.");
                        Console.ResetColor();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ Error: {ex.Message}");
                Console.WriteLine($"   StackTrace: {ex.StackTrace}");
                Console.ResetColor();
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);
            Console.Clear();
        }
    }

    #region Helper Methods

    /// <summary>
    /// Shows help information for command line arguments
    /// </summary>
    static void ShowHelp()
    {
        Console.WriteLine("\n📖 AdvGenNoSQL Examples - Command Line Help");
        Console.WriteLine("Usage: dotnet run [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --server=<address> or -s=<address>");
        Console.WriteLine("      Specify the server address to connect to (format: host:port)");
        Console.WriteLine("      Default: localhost:9091");
        Console.WriteLine("  --help or -h");
        Console.WriteLine("      Show this help information");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --server=localhost:9091");
        Console.WriteLine("  dotnet run -s=192.168.1.100:9091");
        Console.WriteLine("  dotnet run --help");
    }

    /// <summary>
    /// Runs a specific client/server example
    /// </summary>
    static async Task RunClientServerExample(string serverAddress, Func<ClientServerExamples, Task> exampleFunc)
    {
        var examples = new ClientServerExamples(serverAddress);
        await exampleFunc(examples);
    }

    /// <summary>
    /// Runs all client/server examples
    /// </summary>
    static async Task RunAllClientServerExamples(string serverAddress)
    {
        var examples = new ClientServerExamples(serverAddress);
        await examples.RunAllExamplesAsync();
    }

    #endregion

    #region Local Examples

    /// <summary>
    /// Demonstrates basic document storage operations
    /// </summary>
    static async Task RunStorageExample()
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  LOCAL EXAMPLE: Document Storage Operations                ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

        // Create a persistent document store (use fresh directory each run)
        var dataPath = Path.Combine(Path.GetTempPath(), $"AdvGenNoSqlExample_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(dataPath);

            var store = new PersistentDocumentStore(dataPath);
            await store.InitializeAsync();

            Console.WriteLine($"Data stored at: {dataPath}");

            // Insert documents
            var user1 = new Document
            {
                Id = "user1",
                Data = new Dictionary<string, object>
                {
                    ["name"] = "Alice",
                    ["email"] = "alice@example.com",
                    ["age"] = 30
                },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };

            var user2 = new Document
            {
                Id = "user2",
                Data = new Dictionary<string, object>
                {
                    ["name"] = "Bob",
                    ["email"] = "bob@example.com",
                    ["age"] = 25
                },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };

            await store.InsertAsync("users", user1);
            await store.InsertAsync("users", user2);
            Console.WriteLine("✓ Inserted 2 users");

            // Retrieve a document
            var retrieved = await store.GetAsync("users", "user1");
            if (retrieved != null)
            {
                Console.WriteLine($"\n✓ Retrieved user1: {JsonSerializer.Serialize(retrieved.Data)}");
            }

            // Update a document
            user1.Data!["age"] = 31;
            user1.UpdatedAt = DateTime.UtcNow;
            await store.UpdateAsync("users", user1);
            Console.WriteLine("✓ Updated user1's age to 31");

            // Get all documents
            var allUsers = await store.GetAllAsync("users");
            Console.WriteLine($"\n✓ All users ({allUsers.Count()}):");
            foreach (var user in allUsers)
            {
                Console.WriteLine($"  - {user.Data?["name"]}: {user.Data?["email"]}");
            }

            // Count documents
            var count = await store.CountAsync("users");
            Console.WriteLine($"\n✓ Total users: {count}");

            // Delete a document
            await store.DeleteAsync("users", "user2");
            Console.WriteLine("✓ Deleted user2");

            // Save changes to disk
            await store.SaveChangesAsync();
            Console.WriteLine("✓ Changes saved to disk");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✅ Storage Example Complete!");
            Console.ResetColor();
        }
        finally
        {
            // Cleanup
            try
            {
                if (Directory.Exists(dataPath))
                {
                    Directory.Delete(dataPath, recursive: true);
                }
            }
            catch { /* Best effort cleanup */ }
        }
    }

    /// <summary>
    /// Demonstrates B-tree index operations
    /// </summary>
    static void RunIndexExample()
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  LOCAL EXAMPLE: B-tree Index Operations                    ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

        // Create a B-tree index for user IDs
        var index = new BTreeIndex<int, string>("user_id_idx", "users", "id", minDegree: 3);
        Console.WriteLine("✓ Created B-tree index with minDegree=3");

        // Insert items
        var users = new[]
        {
            (10, "Alice"),
            (20, "Bob"),
            (5, "Charlie"),
            (15, "Diana"),
            (25, "Eve"),
            (8, "Frank"),
            (12, "Grace"),
            (30, "Henry")
        };

        foreach (var (id, name) in users)
        {
            index.Insert(id, name);
        }
        Console.WriteLine($"✓ Inserted {users.Length} users");

        // Search for a specific key using TryGetValue
        if (index.TryGetValue(15, out var foundValue))
        {
            Console.WriteLine($"\n✓ Search for ID 15: {foundValue}");
        }

        // Get all values for a key
        var values = index.GetValues(15);
        Console.WriteLine($"✓ GetValues for ID 15: {string.Join(", ", values)}");

        // Range query
        var rangeResult = index.RangeQuery(10, 25);
        Console.WriteLine($"\n✓ Range query (10-25):");
        foreach (var item in rangeResult)
        {
            Console.WriteLine($"  ID {item.Key}: {item.Value}");
        }

        // Get all items in order
        var allItems = index.GetAll().ToList();
        Console.WriteLine($"\n✓ All items in sorted order:");
        foreach (var item in allItems)
        {
            Console.WriteLine($"  ID {item.Key}: {item.Value}");
        }

        // Index statistics
        Console.WriteLine($"\n✓ Index statistics:");
        Console.WriteLine($"  Count: {index.Count}");
        Console.WriteLine($"  Height: {index.Height}");

        // Check if key exists
        Console.WriteLine($"\n✓ Contains ID 15: {index.ContainsKey(15)}");
        Console.WriteLine($"✓ Contains ID 100: {index.ContainsKey(100)}");

        // Delete a key
        index.Delete(15);
        Console.WriteLine("\n✓ Deleted ID 15");
        Console.WriteLine($"✓ Contains ID 15: {index.ContainsKey(15)}");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n✅ Index Example Complete!");
        Console.ResetColor();
    }

    #endregion
}