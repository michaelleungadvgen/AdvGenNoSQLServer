// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;

namespace AdvGenNoSqlServer.Example.ConsoleApp
{
    /// <summary>
    /// Examples demonstrating Capped Collections - fixed-size collections that automatically
    /// remove oldest documents when size or document count limits are exceeded.
    /// 
    /// This example shows:
    /// - Creating capped collections with size and document limits
    /// - Automatic document eviction when limits are exceeded
    /// - Natural order iteration (insertion order)
    /// - Log storage and event streaming use cases
    /// - Monitoring collection statistics
    /// </summary>
    public class CappedCollectionsExamples
    {
        private readonly string _baseDataPath;

        public CappedCollectionsExamples(string baseDataPath = "./data/capped_examples")
        {
            _baseDataPath = baseDataPath;
        }

        /// <summary>
        /// Run all capped collection examples
        /// </summary>
        public async Task RunAllExamplesAsync()
        {
            Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  Capped Collections Examples                               ║");
            Console.WriteLine("║  Fixed-size collections with automatic oldest removal      ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

            try
            {
                // Ensure clean state
                CleanupDataDirectory();

                // Run examples
                await RunExample1_BasicCappedCollection();
                await RunExample2_LogStorage();
                await RunExample3_EventStreaming();

                Console.WriteLine("\n✅ All Capped Collections examples completed successfully!\n");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ Example failed: {ex.Message}");
                Console.WriteLine($"   StackTrace: {ex.StackTrace}");
                Console.ResetColor();
                throw;
            }
            finally
            {
                // Cleanup
                CleanupDataDirectory();
            }
        }

        /// <summary>
        /// Example 1: Basic Capped Collection
        /// Demonstrates creating a capped collection and observing automatic document removal
        /// </summary>
        private async Task RunExample1_BasicCappedCollection()
        {
            PrintExampleHeader("Example 1: Basic Capped Collection with Document Limit");

            // Create underlying document store
            var baseStore = new DocumentStore();
            var cappedStore = new CappedDocumentStore(baseStore);

            // Create a capped collection with max 5 documents
            Console.WriteLine("\n📦 Creating capped collection 'sensor_readings'");
            Console.WriteLine("   Max Documents: 5");
            Console.WriteLine("   Max Size: 1 MB (not the limiting factor here)");

            await cappedStore.CreateCappedCollectionAsync("sensor_readings", new CappedCollectionOptions
            {
                MaxDocuments = 5,
                EnforceMaxDocuments = true,
                MaxSizeBytes = 1024 * 1024, // 1 MB
                EnforceMaxSize = true
            });

            // Subscribe to trim events
            cappedStore.CappedCollectionTrimmed += (sender, e) =>
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n   ⚠ Collection trimmed! Removed {e.RemovedCount} oldest document(s)");
                Console.ResetColor();
            };

            // Insert 8 documents (3 should be automatically removed)
            Console.WriteLine("\n📥 Inserting 8 sensor readings (only 5 will be kept):\n");

            for (int i = 1; i <= 8; i++)
            {
                var reading = new Document
                {
                    Id = $"reading_{i:D3}",
                    Data = new Dictionary<string, object>
                    {
                        ["sensor_id"] = $"sensor_{(i % 3) + 1}",
                        ["temperature"] = 20 + i * 2.5,
                        ["humidity"] = 40 + i * 3,
                        ["timestamp"] = DateTime.UtcNow.AddMinutes(i).ToString("O"),
                        ["sequence"] = i
                    }
                };

                await cappedStore.InsertAsync("sensor_readings", reading);
                Console.WriteLine($"   ✓ Inserted reading_{i:D3} (temp: {20 + i * 2.5:F1}°C)");
                
                // Small delay to show progression
                await Task.Delay(50);
            }

            // Show current state
            var stats = cappedStore.GetCappedCollectionStats("sensor_readings");
            Console.WriteLine($"\n📊 Collection Statistics:");
            Console.WriteLine($"   Current Document Count: {stats?.DocumentCount ?? 0}");
            Console.WriteLine($"   Max Documents Allowed: {stats?.MaxDocuments}");
            Console.WriteLine($"   Size: {FormatBytes(stats?.TotalSizeBytes ?? 0)}");

            // Show remaining documents in natural order (oldest first)
            Console.WriteLine("\n📋 Remaining documents in natural order (oldest → newest):");
            var remainingDocs = await cappedStore.GetAllInNaturalOrderAsync("sensor_readings");
            foreach (var doc in remainingDocs)
            {
                var seq = doc.Data.TryGetValue("sequence", out var s) ? s : "?";
                var temp = doc.Data.TryGetValue("temperature", out var t) ? t : "?";
                Console.WriteLine($"   • reading_{seq} (temp: {temp}°C)");
            }

            // Show most recent documents
            Console.WriteLine("\n🔄 Most recent 3 documents:");
            var recentDocs = await cappedStore.GetRecentAsync("sensor_readings", 3);
            foreach (var doc in recentDocs)
            {
                var seq = doc.Data.TryGetValue("sequence", out var s) ? s : "?";
                var temp = doc.Data.TryGetValue("temperature", out var t) ? t : "?";
                Console.WriteLine($"   • reading_{seq} (temp: {temp}°C)");
            }

            Console.WriteLine("\n✅ Example 1 completed - Demonstrated automatic document eviction!");
        }

        /// <summary>
        /// Example 2: Log Storage with Capped Collection
        /// Demonstrates using capped collections for application log storage
        /// </summary>
        private async Task RunExample2_LogStorage()
        {
            PrintExampleHeader("Example 2: Application Log Storage");

            // Create document store
            var baseStore = new DocumentStore();
            var cappedStore = new CappedDocumentStore(baseStore);

            // Create a capped collection for logs (max 100KB, ~50 log entries)
            Console.WriteLine("\n📦 Creating capped collection 'application_logs'");
            Console.WriteLine("   Max Size: 100 KB");
            Console.WriteLine("   Use Case: Store recent application logs (auto-removes old ones)");

            await cappedStore.CreateCappedCollectionAsync("application_logs", new CappedCollectionOptions
            {
                MaxSizeBytes = 100 * 1024, // 100 KB
                EnforceMaxSize = true,
                EnforceMaxDocuments = false // Size-based only
            });

            int trimmedCount = 0;
            cappedStore.CappedCollectionTrimmed += (sender, e) =>
            {
                trimmedCount += e.RemovedCount;
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"   [Trim event: {e.RemovedCount} old log entries removed to make space]");
                Console.ResetColor();
            };

            // Simulate application generating logs
            Console.WriteLine("\n📝 Simulating application log generation:\n");

            var logLevels = new[] { "INFO", "DEBUG", "WARN", "INFO", "INFO", "ERROR", "INFO", "DEBUG" };
            var logMessages = new[]
            {
                "User login successful",
                "Processing request ID: {0}",
                "Database query executed in {0}ms",
                "Cache hit ratio: {0}%",
                "Background job started",
                "Connection pool exhausted",
                "Request completed",
                "Memory usage: {0}MB"
            };

            var random = new Random(42); // Seeded for reproducibility

            for (int i = 1; i <= 100; i++)
            {
                var level = logLevels[random.Next(logLevels.Length)];
                var messageTemplate = logMessages[random.Next(logMessages.Length)];
                var message = string.Format(messageTemplate, random.Next(10, 1000));

                var logEntry = new Document
                {
                    Id = $"log_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{i:D4}",
                    Data = new Dictionary<string, object>
                    {
                        ["timestamp"] = DateTime.UtcNow.ToString("O"),
                        ["level"] = level,
                        ["message"] = message,
                        ["request_id"] = $"req_{random.Next(10000, 99999)}",
                        ["thread_id"] = random.Next(1, 20),
                        ["sequence"] = i
                    }
                };

                await cappedStore.InsertAsync("application_logs", logEntry);

                // Show progress every 20 logs
                if (i % 20 == 0)
                {
                    var currentCount = await cappedStore.CountAsync("application_logs");
                    Console.WriteLine($"   Generated {i} logs... Current stored: {currentCount} (trimmed: {trimmedCount})");
                }

                await Task.Delay(10);
            }

            // Show final statistics
            var logStats = cappedStore.GetCappedCollectionStats("application_logs");
            Console.WriteLine($"\n📊 Final Log Collection Statistics:");
            Console.WriteLine($"   Total Log Entries Generated: 100");
            Console.WriteLine($"   Currently Stored: {logStats?.DocumentCount ?? 0}");
            Console.WriteLine($"   Automatically Removed: {trimmedCount}");
            Console.WriteLine($"   Storage Used: {FormatBytes(logStats?.TotalSizeBytes ?? 0)} / {FormatBytes(logStats?.MaxSizeBytes ?? 0)}");

            // Show most recent error logs
            Console.WriteLine("\n🔍 Most recent ERROR-level logs:");
            var allLogs = await cappedStore.GetRecentAsync("application_logs");
            var errorLogs = allLogs
                .Where(d => d.Data.TryGetValue("level", out var lvl) && lvl?.ToString() == "ERROR")
                .Take(3);

            foreach (var log in errorLogs)
            {
                var msg = log.Data.TryGetValue("message", out var m) ? m : "?";
                var ts = log.Data.TryGetValue("timestamp", out var t) ? t : "?";
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"   [ERROR] {msg}");
                Console.ResetColor();
                Console.WriteLine($"           Time: {ts}");
            }

            Console.WriteLine("\n✅ Example 2 completed - Demonstrated log storage with size-based rotation!");
        }

        /// <summary>
        /// Example 3: Event Streaming / Circular Buffer
        /// Demonstrates using capped collections as a circular buffer for event streaming
        /// </summary>
        private async Task RunExample3_EventStreaming()
        {
            PrintExampleHeader("Example 3: Event Streaming (Circular Buffer)");

            // Create document store
            var baseStore = new DocumentStore();
            var cappedStore = new CappedDocumentStore(baseStore);

            // Create a capped collection for events (exactly 10 events - perfect for circular buffer)
            Console.WriteLine("\n📦 Creating capped collection 'event_stream'");
            Console.WriteLine("   Max Documents: 10 (circular buffer)");
            Console.WriteLine("   Use Case: Store last 10 events for real-time monitoring");

            await cappedStore.CreateCappedCollectionAsync("event_stream", new CappedCollectionOptions
            {
                MaxDocuments = 10,
                EnforceMaxDocuments = true,
                EnforceMaxSize = false
            });

            // Track events being removed
            var removedEvents = new List<string>();
            cappedStore.CappedCollectionTrimmed += (sender, e) =>
            {
                removedEvents.AddRange(e.RemovedDocumentIds);
            };

            Console.WriteLine("\n🔄 Simulating event stream (circular buffer with capacity 10):\n");

            // Simulate event stream
            var eventTypes = new[] { "user_action", "system_metric", "alert", "heartbeat" };
            var eventData = new Dictionary<string, Func<object>>
            {
                ["user_action"] = () => new Dictionary<string, object>
                {
                    ["action"] = new[] { "click", "scroll", "login", "logout", "purchase" }[new Random().Next(5)],
                    ["user_id"] = $"user_{new Random().Next(1000, 9999)}",
                    ["page"] = $"/page/{new Random().Next(1, 10)}"
                },
                ["system_metric"] = () => new Dictionary<string, object>
                {
                    ["cpu_percent"] = new Random().Next(10, 95),
                    ["memory_mb"] = new Random().Next(512, 4096),
                    ["disk_io"] = new Random().Next(0, 1000)
                },
                ["alert"] = () => new Dictionary<string, object>
                {
                    ["severity"] = new[] { "info", "warning", "critical" }[new Random().Next(3)],
                    ["component"] = $"service_{new Random().Next(1, 5)}",
                    ["message"] = "Threshold exceeded"
                },
                ["heartbeat"] = () => new Dictionary<string, object>
                {
                    ["service"] = $"worker_{new Random().Next(1, 10)}",
                    ["status"] = "healthy",
                    ["uptime_seconds"] = new Random().Next(60, 86400)
                }
            };

            var random = new Random(123); // Seeded for reproducibility

            // Generate 25 events (only last 10 should remain)
            for (int i = 1; i <= 25; i++)
            {
                var eventType = eventTypes[random.Next(eventTypes.Length)];
                var eventId = $"evt_{DateTime.UtcNow:HHmmssfff}_{i:D3}";

                var evt = new Document
                {
                    Id = eventId,
                    Data = new Dictionary<string, object>
                    {
                        ["timestamp"] = DateTime.UtcNow.ToString("O"),
                        ["type"] = eventType,
                        ["sequence"] = i,
                        ["payload"] = eventData[eventType]()
                    }
                };

                await cappedStore.InsertAsync("event_stream", evt);

                // Visual representation of the circular buffer
                if (i <= 15) // Show visual for first 15 to demonstrate rotation
                {
                    var currentCount = await cappedStore.CountAsync("event_stream");
                    var bufferVisual = new string('█', (int)currentCount).PadRight(10, '░');
                    var action = i <= 10 ? "added" : "rotated";
                    Console.WriteLine($"   Event {i:D2} {action} [{bufferVisual}] ({currentCount}/10)");
                }
                else if (i == 16)
                {
                    Console.WriteLine($"   ... (events 16-25 continue rotation)");
                }

                await Task.Delay(30);
            }

            // Show final buffer state
            Console.WriteLine("\n📊 Circular Buffer Final State:");
            var eventStats = cappedStore.GetCappedCollectionStats("event_stream");
            Console.WriteLine($"   Total Events Generated: 25");
            Console.WriteLine($"   Buffer Capacity: 10");
            Console.WriteLine($"   Currently Stored: {eventStats?.DocumentCount ?? 0}");
            Console.WriteLine($"   Events Rotated Out: {removedEvents.Count}");

            // Show buffer contents
            Console.WriteLine("\n📋 Current Buffer Contents (oldest → newest):");
            var bufferContents = await cappedStore.GetAllInNaturalOrderAsync("event_stream");
            
            Console.WriteLine("   ┌─────────┬─────────────┬──────────────────────────────────────┐");
            Console.WriteLine("   │ Seq #   │ Type        │ Timestamp                            │");
            Console.WriteLine("   ├─────────┼─────────────┼──────────────────────────────────────┤");
            
            foreach (var doc in bufferContents)
            {
                var seq = doc.Data.TryGetValue("sequence", out var s) ? s.ToString()?.PadLeft(3) ?? "?" : "?";
                var type = doc.Data.TryGetValue("type", out var t) ? t.ToString()?.PadRight(11) ?? "?" : "?";
                var ts = doc.Data.TryGetValue("timestamp", out var time) 
                    ? DateTime.Parse(time?.ToString() ?? DateTime.MinValue.ToString()).ToString("HH:mm:ss.fff") 
                    : "?";
                
                // Color code by event type
                var originalColor = Console.ForegroundColor;
                Console.Write("   │ ");
                Console.Write($"{seq}     │ ");
                
                switch (type.Trim())
                {
                    case "user_action":
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        break;
                    case "system_metric":
                        Console.ForegroundColor = ConsoleColor.Green;
                        break;
                    case "alert":
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                    case "heartbeat":
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        break;
                }
                Console.Write(type);
                Console.ForegroundColor = originalColor;
                Console.WriteLine($" │ {ts}                    │");
            }
            Console.WriteLine("   └─────────┴─────────────┴──────────────────────────────────────┘");

            // Show event type distribution
            Console.WriteLine("\n📈 Event Type Distribution in Buffer:");
            var allEvents = await cappedStore.GetAllAsync("event_stream");
            var typeGroups = allEvents
                .GroupBy(d => d.Data.TryGetValue("type", out var t) ? t?.ToString() : "unknown")
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count);

            foreach (var group in typeGroups)
            {
                var bar = new string('█', group.Count);
                Console.WriteLine($"   {group.Type?.PadRight(13)}: {bar} ({group.Count})");
            }

            Console.WriteLine("\n✅ Example 3 completed - Demonstrated circular buffer pattern!");
            Console.WriteLine("\n💡 Key Takeaway: Capped collections are perfect for:");
            Console.WriteLine("   • Log storage with automatic rotation");
            Console.WriteLine("   • Event streaming / circular buffers");
            Console.WriteLine("   • Time-series data with fixed retention");
            Console.WriteLine("   • High-throughput data where old data has no value");
        }

        #region Helper Methods

        private void PrintExampleHeader(string title)
        {
            Console.WriteLine("\n" + new string('─', 60));
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  {title}");
            Console.ResetColor();
            Console.WriteLine(new string('─', 60));
        }

        private void CleanupDataDirectory()
        {
            try
            {
                if (Directory.Exists(_baseDataPath))
                {
                    Directory.Delete(_baseDataPath, recursive: true);
                }
            }
            catch { /* Ignore cleanup errors */ }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        #endregion
    }
}
