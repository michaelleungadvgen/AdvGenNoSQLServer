// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.WriteConcern;
using AdvGenNoSqlServer.Storage;

namespace AdvGenNoSqlServer.Example.ConsoleApp;

/// <summary>
/// Examples demonstrating Write Concern configuration for controlling durability guarantees
/// </summary>
public static class WriteConcernExamples
{
    /// <summary>
    /// Run all Write Concern examples
    /// </summary>
    public static async Task RunAllExamplesAsync()
    {
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              Write Concern Configuration Examples            ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝\n");

        await Example1_BasicWriteConcernLevels();
        await Example2_PerCollectionWriteConcern();
        await Example3_WriteConcernWithBatchOperations();
    }

    /// <summary>
    /// Example 1: Basic Write Concern Levels
    /// Demonstrates the four standard write concern levels and their trade-offs
    /// </summary>
    public static async Task Example1_BasicWriteConcernLevels()
    {
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Example 1: Basic Write Concern Levels                       │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────┘\n");

        // Create a document store
        var documentStore = new DocumentStore();

        Console.WriteLine("Write Concern controls the durability guarantees for write operations.");
        Console.WriteLine("Higher durability = Lower performance | Lower durability = Higher performance\n");

        // 1. Unacknowledged - Fastest but potential data loss
        Console.WriteLine("1. Unacknowledged (w: 0)");
        Console.WriteLine("   - Fastest write performance");
        Console.WriteLine("   - No confirmation from server");
        Console.WriteLine("   - Risk of silent data loss\n");

        var unacknowledgedManager = new WriteConcernManager();
        unacknowledgedManager.SetDefaultWriteConcern(WriteConcern.Unacknowledged);
        var unacknowledgedStore = new WriteConcernDocumentStore(documentStore, unacknowledgedManager);

        var doc1 = new Document
        {
            Id = "log_001",
            Data = new Dictionary<string, object>
            {
                ["level"] = "INFO",
                ["message"] = "Application started",
                ["timestamp"] = DateTime.UtcNow
            }
        };

        await unacknowledgedStore.InsertAsync("logs", doc1);
        Console.WriteLine("   ✓ Log entry inserted with Unacknowledged concern (fast, no confirmation)\n");

        // 2. Acknowledged - Default, balanced
        Console.WriteLine("2. Acknowledged (w: 1)");
        Console.WriteLine("   - Waits for primary server acknowledgment");
        Console.WriteLine("   - Good balance of performance and safety");
        Console.WriteLine("   - Default for most use cases\n");

        var acknowledgedManager = new WriteConcernManager();
        acknowledgedManager.SetDefaultWriteConcern(WriteConcern.Acknowledged);
        var acknowledgedStore = new WriteConcernDocumentStore(documentStore, acknowledgedManager);

        var doc2 = new Document
        {
            Id = "user_001",
            Data = new Dictionary<string, object>
            {
                ["username"] = "johndoe",
                ["email"] = "john@example.com",
                ["role"] = "user"
            }
        };

        await acknowledgedStore.InsertAsync("users", doc2);
        Console.WriteLine("   ✓ User document inserted with Acknowledged concern (balanced)\n");

        // 3. Journaled - Crash recovery guarantee
        Console.WriteLine("3. Journaled (w: 1, j: true)");
        Console.WriteLine("   - Write flushed to journal before returning");
        Console.WriteLine("   - Survives server crashes");
        Console.WriteLine("   - Slightly higher latency\n");

        var journaledManager = new WriteConcernManager();
        journaledManager.SetDefaultWriteConcern(WriteConcern.Journaled);
        var journaledStore = new WriteConcernDocumentStore(documentStore, journaledManager);

        var doc3 = new Document
        {
            Id = "payment_001",
            Data = new Dictionary<string, object>
            {
                ["amount"] = 99.99,
                ["currency"] = "USD",
                ["status"] = "completed",
                ["customerId"] = "cust_12345"
            }
        };

        await journaledStore.InsertAsync("payments", doc3);
        Console.WriteLine("   ✓ Payment record inserted with Journaled concern (crash-safe)\n");

        // 4. Majority - Strongest durability
        Console.WriteLine("4. Majority (w: \"majority\")");
        Console.WriteLine("   - Acknowledged by majority of nodes");
        Console.WriteLine("   - Strongest durability in clusters");
        Console.WriteLine("   - Highest latency, requires cluster\n");

        var majorityManager = new WriteConcernManager();
        majorityManager.SetDefaultWriteConcern(WriteConcern.Majority);
        var majorityStore = new WriteConcernDocumentStore(documentStore, majorityManager);

        var doc4 = new Document
        {
            Id = "config_001",
            Data = new Dictionary<string, object>
            {
                ["setting"] = "max_connections",
                ["value"] = 10000,
                ["updatedBy"] = "admin"
            }
        };

        await majorityStore.InsertAsync("config", doc4);
        Console.WriteLine("   ✓ Config entry inserted with Majority concern (cluster-safe)\n");

        // Display statistics
        Console.WriteLine("Write Concern Statistics:");
        var stats = unacknowledgedManager.GetStatistics();
        Console.WriteLine($"   Total operations: {stats.TotalOperations}");
        Console.WriteLine($"   Acknowledged: {stats.AcknowledgedOperations}");
        Console.WriteLine($"   Unacknowledged: {stats.UnacknowledgedOperations}");
        Console.WriteLine($"   Average latency: {stats.AverageLatencyMs:F2}ms\n");
    }

    /// <summary>
    /// Example 2: Per-Collection Write Concern
    /// Demonstrates setting different write concerns for different collections based on data criticality
    /// </summary>
    public static async Task Example2_PerCollectionWriteConcern()
    {
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Example 2: Per-Collection Write Concern                     │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────┘\n");

        var documentStore = new DocumentStore();
        var writeConcernManager = new WriteConcernManager();

        Console.WriteLine("Different collections can have different durability requirements.");
        Console.WriteLine("Configure per-collection write concern based on data criticality:\n");

        // Configure write concerns for different collection types
        Console.WriteLine("Configuring per-collection write concerns:");

        // Logs - Unacknowledged (low priority, high volume)
        await writeConcernManager.SetCollectionWriteConcernAsync("logs", WriteConcern.Unacknowledged);
        Console.WriteLine("  • logs collection: Unacknowledged (high throughput, acceptable loss)");

        // Analytics - Acknowledged (default)
        await writeConcernManager.SetCollectionWriteConcernAsync("analytics", WriteConcern.Acknowledged);
        Console.WriteLine("  • analytics collection: Acknowledged (balanced)");

        // Users - Journaled (important data)
        await writeConcernManager.SetCollectionWriteConcernAsync("users", WriteConcern.Journaled);
        Console.WriteLine("  • users collection: Journaled (crash recovery required)");

        // Financial - Majority (critical data)
        await writeConcernManager.SetCollectionWriteConcernAsync("transactions", WriteConcern.Majority);
        Console.WriteLine("  • transactions collection: Majority (critical, cluster-safe)\n");

        // Create the write concern enabled store
        var store = new WriteConcernDocumentStore(documentStore, writeConcernManager);

        // Insert documents into different collections
        Console.WriteLine("Inserting documents into different collections:");

        // Logs - fast, unacknowledged
        var logDoc = new Document
        {
            Id = "log_001",
            Data = new Dictionary<string, object>
            {
                ["level"] = "DEBUG",
                ["message"] = "Debug information",
                ["timestamp"] = DateTime.UtcNow
            }
        };
        await store.InsertAsync("logs", logDoc);
        Console.WriteLine("  ✓ Log entry inserted (Unacknowledged)");

        // Analytics - default acknowledged
        var analyticsDoc = new Document
        {
            Id = "event_001",
            Data = new Dictionary<string, object>
            {
                ["event"] = "page_view",
                ["url"] = "/products",
                ["userId"] = "user_123"
            }
        };
        await store.InsertAsync("analytics", analyticsDoc);
        Console.WriteLine("  ✓ Analytics event inserted (Acknowledged)");

        // Users - journaled for crash safety
        var userDoc = new Document
        {
            Id = "user_123",
            Data = new Dictionary<string, object>
            {
                ["username"] = "alice",
                ["email"] = "alice@example.com",
                ["createdAt"] = DateTime.UtcNow
            }
        };
        await store.InsertAsync("users", userDoc);
        Console.WriteLine("  ✓ User document inserted (Journaled)");

        // Transactions - majority for critical financial data
        var transactionDoc = new Document
        {
            Id = "txn_001",
            Data = new Dictionary<string, object>
            {
                ["from"] = "account_a",
                ["to"] = "account_b",
                ["amount"] = 1000.00,
                ["currency"] = "USD",
                ["status"] = "completed"
            }
        };
        await store.InsertAsync("transactions", transactionDoc);
        Console.WriteLine("  ✓ Transaction record inserted (Majority)\n");

        // Show per-collection statistics
        Console.WriteLine("Per-Collection Configuration:");
        var collections = new[] { "logs", "analytics", "users", "transactions" };
        foreach (var collection in collections)
        {
            var concern = writeConcernManager.GetWriteConcernForCollection(collection);
            Console.WriteLine($"  • {collection}: W={concern.W}, Journal={concern.IsJournaled}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Example 3: Write Concern with Batch Operations
    /// Demonstrates using write concern with batch insert operations
    /// </summary>
    public static async Task Example3_WriteConcernWithBatchOperations()
    {
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Example 3: Write Concern with Batch Operations              │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────┘\n");

        var documentStore = new DocumentStore();
        var writeConcernManager = new WriteConcernManager();

        Console.WriteLine("Batch operations can use specific write concerns for bulk data loading.\n");

        // Configure different write concerns for different batch scenarios
        await writeConcernManager.SetCollectionWriteConcernAsync("bulk_logs", WriteConcern.Unacknowledged);
        await writeConcernManager.SetCollectionWriteConcernAsync("bulk_data", WriteConcern.Acknowledged);

        var store = new WriteConcernDocumentStore(documentStore, writeConcernManager);

        // Scenario 1: Fast bulk log ingestion
        Console.WriteLine("Scenario 1: Fast Bulk Log Ingestion (Unacknowledged)");
        var logBatch = new List<Document>();
        for (int i = 1; i <= 10; i++)
        {
            logBatch.Add(new Document
            {
                Id = $"bulk_log_{i:D3}",
                Data = new Dictionary<string, object>
                {
                    ["level"] = i % 3 == 0 ? "ERROR" : "INFO",
                    ["message"] = $"Log message #{i}",
                    ["timestamp"] = DateTime.UtcNow.AddSeconds(-i)
                }
            });
        }

        var startTime = DateTime.UtcNow;
        foreach (var doc in logBatch)
        {
            await store.InsertAsync("bulk_logs", doc);
        }
        var elapsed = DateTime.UtcNow - startTime;
        Console.WriteLine($"  ✓ Inserted {logBatch.Count} log entries in {elapsed.TotalMilliseconds:F1}ms");
        Console.WriteLine($"    (Unacknowledged = fastest, some loss acceptable)\n");

        // Scenario 2: Reliable bulk data import
        Console.WriteLine("Scenario 2: Reliable Bulk Data Import (Acknowledged)");
        var dataBatch = new List<Document>();
        for (int i = 1; i <= 10; i++)
        {
            dataBatch.Add(new Document
            {
                Id = $"record_{i:D3}",
                Data = new Dictionary<string, object>
                {
                    ["id"] = i,
                    ["name"] = $"Product {i}",
                    ["price"] = 10.99m * i,
                    ["category"] = $"Category {i % 5}"
                }
            });
        }

        startTime = DateTime.UtcNow;
        foreach (var doc in dataBatch)
        {
            await store.InsertAsync("bulk_data", doc);
        }
        elapsed = DateTime.UtcNow - startTime;
        Console.WriteLine($"  ✓ Inserted {dataBatch.Count} data records in {elapsed.TotalMilliseconds:F1}ms");
        Console.WriteLine($"    (Acknowledged = balanced, data safety ensured)\n");

        // Scenario 3: Custom write concern with timeout
        Console.WriteLine("Scenario 3: Custom Write Concern with Timeout");
        var customConcern = WriteConcern.Majority.WithTimeout(TimeSpan.FromSeconds(5));
        await writeConcernManager.SetCollectionWriteConcernAsync("critical_import", customConcern);

        var criticalDoc = new Document
        {
            Id = "critical_001",
            Data = new Dictionary<string, object>
            {
                ["type"] = "financial_report",
                ["period"] = "Q4-2025",
                ["totalRevenue"] = 1000000.00,
                ["status"] = "final"
            }
        };

        await store.InsertAsync("critical_import", criticalDoc);
        Console.WriteLine($"  ✓ Critical document inserted with Majority concern");
        Console.WriteLine($"    (Timeout: 5 seconds for cluster acknowledgment)\n");

        // Display final statistics
        Console.WriteLine("Batch Operation Statistics:");
        var stats = writeConcernManager.GetStatistics();
        Console.WriteLine($"  Total operations: {stats.TotalOperations}");
        Console.WriteLine($"  Average latency: {stats.AverageLatencyMs:F2}ms");
        Console.WriteLine($"  Acknowledged: {stats.AcknowledgedOperations}");
        Console.WriteLine($"  Unacknowledged: {stats.UnacknowledgedOperations}\n");

        // Summary
        Console.WriteLine("Write Concern Best Practices:");
        Console.WriteLine("  • Use Unacknowledged for high-volume, low-priority data (logs, metrics)");
        Console.WriteLine("  • Use Acknowledged for general application data");
        Console.WriteLine("  • Use Journaled for data requiring crash recovery");
        Console.WriteLine("  • Use Majority for critical data in clustered deployments");
        Console.WriteLine("  • Set timeouts appropriately for your network latency\n");
    }
}
