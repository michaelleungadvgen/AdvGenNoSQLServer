// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Indexing;
using System.Text.Json;

namespace AdvGenNoSqlServer.Examples;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== AdvGen NoSQL Server Examples ===\n");

        // Run different examples based on command line args
        var example = args.Length > 0 ? args[0].ToLower() : "storage";

        switch (example)
        {
            case "client":
                await RunClientExample();
                break;
            case "storage":
                await RunStorageExample();
                break;
            case "index":
                RunIndexExample();
                break;
            case "all":
                await RunStorageExample();
                Console.WriteLine();
                RunIndexExample();
                Console.WriteLine();
                await RunClientExample();
                break;
            default:
                ShowHelp();
                break;
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine("Usage: AdvGenNoSqlServer.Examples [example]");
        Console.WriteLine();
        Console.WriteLine("Available examples:");
        Console.WriteLine("  storage  - Document storage operations (default)");
        Console.WriteLine("  index    - B-tree index operations");
        Console.WriteLine("  client   - Client connection to server");
        Console.WriteLine("  all      - Run all examples");
    }

    /// <summary>
    /// Demonstrates basic document storage operations
    /// </summary>
    static async Task RunStorageExample()
    {
        Console.WriteLine("--- Document Storage Example ---\n");

        // Create a persistent document store
        var dataPath = Path.Combine(Path.GetTempPath(), "AdvGenNoSqlExample");
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
        Console.WriteLine("Inserted 2 users");

        // Retrieve a document
        var retrieved = await store.GetAsync("users", "user1");
        if (retrieved != null)
        {
            Console.WriteLine($"\nRetrieved user1: {JsonSerializer.Serialize(retrieved.Data)}");
        }

        // Update a document
        user1.Data!["age"] = 31;
        user1.UpdatedAt = DateTime.UtcNow;
        await store.UpdateAsync("users", user1);
        Console.WriteLine("Updated user1's age to 31");

        // Get all documents
        var allUsers = await store.GetAllAsync("users");
        Console.WriteLine($"\nAll users ({allUsers.Count()}):");
        foreach (var user in allUsers)
        {
            Console.WriteLine($"  - {user.Data?["name"]}: {user.Data?["email"]}");
        }

        // Count documents
        var count = await store.CountAsync("users");
        Console.WriteLine($"\nTotal users: {count}");

        // Delete a document
        await store.DeleteAsync("users", "user2");
        Console.WriteLine("Deleted user2");

        // Save changes to disk
        await store.SaveChangesAsync();
        Console.WriteLine("Changes saved to disk");

        Console.WriteLine("\n--- Storage Example Complete ---");
    }

    /// <summary>
    /// Demonstrates B-tree index operations
    /// </summary>
    static void RunIndexExample()
    {
        Console.WriteLine("--- B-tree Index Example ---\n");

        // Create a B-tree index for user IDs
        var index = new BTreeIndex<int, string>("user_id_idx", "users", "id", minDegree: 3);
        Console.WriteLine("Created B-tree index with minDegree=3");

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
        Console.WriteLine($"Inserted {users.Length} users");

        // Search for a specific key using TryGetValue
        if (index.TryGetValue(15, out var foundValue))
        {
            Console.WriteLine($"\nSearch for ID 15: {foundValue}");
        }

        // Get all values for a key
        var values = index.GetValues(15);
        Console.WriteLine($"GetValues for ID 15: {string.Join(", ", values)}");

        // Range query
        var rangeResult = index.RangeQuery(10, 25);
        Console.WriteLine($"\nRange query (10-25):");
        foreach (var item in rangeResult)
        {
            Console.WriteLine($"  ID {item.Key}: {item.Value}");
        }

        // Get all items in order
        var allItems = index.GetAll().ToList();
        Console.WriteLine($"\nAll items in sorted order:");
        foreach (var item in allItems)
        {
            Console.WriteLine($"  ID {item.Key}: {item.Value}");
        }

        // Index statistics
        Console.WriteLine($"\nIndex statistics:");
        Console.WriteLine($"  Count: {index.Count}");
        Console.WriteLine($"  Height: {index.Height}");

        // Check if key exists
        Console.WriteLine($"\nContains ID 15: {index.ContainsKey(15)}");
        Console.WriteLine($"Contains ID 100: {index.ContainsKey(100)}");

        // Delete a key
        index.Delete(15);
        Console.WriteLine("\nDeleted ID 15");
        Console.WriteLine($"Contains ID 15: {index.ContainsKey(15)}");

        Console.WriteLine("\n--- Index Example Complete ---");
    }

    /// <summary>
    /// Demonstrates client connection to server
    /// </summary>
    static async Task RunClientExample()
    {
        Console.WriteLine("--- Client Connection Example ---\n");
        Console.WriteLine("NOTE: This example requires the server to be running on localhost:9090");
        Console.WriteLine();

        var options = new AdvGenNoSqlClientOptions
        {
            ConnectionTimeout = 5000,
            EnableKeepAlive = true,
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        };

        using var client = new AdvGenNoSqlClient("localhost:9090", options);

        try
        {
            Console.WriteLine("Connecting to server...");
            await client.ConnectAsync();
            Console.WriteLine("Connected successfully!");

            // Ping the server
            Console.WriteLine("\nPinging server...");
            var pingResult = await client.PingAsync();
            Console.WriteLine($"Ping result: {(pingResult ? "Success" : "Failed")}");

            // Execute a command
            Console.WriteLine("\nExecuting insert command...");
            var insertCommand = JsonSerializer.Serialize(new
            {
                command = "insert",
                collection = "test",
                document = new { _id = "doc1", message = "Hello, NoSQL!" }
            });
            var response = await client.ExecuteCommandAsync("insert", insertCommand);
            Console.WriteLine($"Command response: {response}");

            // Batch operations example
            Console.WriteLine("\nExecuting batch insert...");
            var documents = new List<object>
            {
                new { _id = "batch1", name = "Document 1" },
                new { _id = "batch2", name = "Document 2" },
                new { _id = "batch3", name = "Document 3" }
            };

            var batchResult = await client.BatchInsertAsync("test", documents);
            Console.WriteLine($"Batch insert: {batchResult.InsertedCount} documents inserted");

            // Disconnect
            Console.WriteLine("\nDisconnecting...");
            await client.DisconnectAsync();
            Console.WriteLine("Disconnected");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine("\nMake sure the server is running:");
            Console.WriteLine("  cd AdvGenNoSqlServer.Server");
            Console.WriteLine("  dotnet run");
        }

        Console.WriteLine("\n--- Client Example Complete ---");
    }
}
