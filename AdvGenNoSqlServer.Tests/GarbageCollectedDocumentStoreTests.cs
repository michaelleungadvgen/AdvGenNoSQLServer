// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Integration tests for GarbageCollectedDocumentStore
/// </summary>
public class GarbageCollectedDocumentStoreTests : IDisposable
{
    private readonly string _testDataPath;

    public GarbageCollectedDocumentStoreTests()
    {
        _testDataPath = Path.Combine(Path.GetTempPath(), $"nosql_gc_store_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDataPath))
            {
                Directory.Delete(_testDataPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task Constructor_WithDefaultOptions_CreatesStore()
    {
        using var store = new GarbageCollectedDocumentStore(_testDataPath);
        await store.InitializeAsync();

        Assert.NotNull(store.GarbageCollector);
        Assert.Equal(_testDataPath, store.DataPath);
    }

    [Fact]
    public async Task Constructor_WithExternalGarbageCollector_UsesProvided()
    {
        var gc = new GarbageCollector();
        using var store = new GarbageCollectedDocumentStore(_testDataPath, gc);
        await store.InitializeAsync();

        Assert.Same(gc, store.GarbageCollector);
    }

    [Fact]
    public async Task DeleteAsync_RemovesDocumentAndCreatesTombstone()
    {
        var gcOptions = new GarbageCollectorOptions
        {
            Enabled = true,
            RetentionPeriod = TimeSpan.FromHours(1),
            EnableBackgroundCollection = false
        };

        using var store = new GarbageCollectedDocumentStore(_testDataPath, gcOptions);
        await store.InitializeAsync();

        // Insert a document
        var doc = new Document
        {
            Id = "test-doc-1",
            Data = new Dictionary<string, object> { { "name", "Test" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        await store.InsertAsync("users", doc);
        Assert.Equal(1L, await store.CountAsync("users"));

        // Delete the document
        var deleted = await store.DeleteAsync("users", "test-doc-1");
        Assert.True(deleted);
        Assert.Equal(0L, await store.CountAsync("users"));

        // Verify tombstone was created
        var tombstones = store.GarbageCollector.GetTombstones("users").ToList();
        Assert.Single(tombstones);
        Assert.Equal("test-doc-1", tombstones[0].DocumentId);
        Assert.Equal("users", tombstones[0].CollectionName);
        Assert.Equal(1, tombstones[0].DocumentVersion);
        Assert.NotNull(tombstones[0].FilePath);
    }

    [Fact]
    public async Task DeleteAsync_NonExistingDocument_DoesNotCreateTombstone()
    {
        var gcOptions = new GarbageCollectorOptions
        {
            Enabled = true,
            EnableBackgroundCollection = false
        };

        using var store = new GarbageCollectedDocumentStore(_testDataPath, gcOptions);
        await store.InitializeAsync();
        await store.CreateCollectionAsync("users");

        // Try to delete non-existing document
        var deleted = await store.DeleteAsync("users", "non-existing");
        Assert.False(deleted);

        // Verify no tombstone was created
        var tombstones = store.GarbageCollector.GetTombstones("users").ToList();
        Assert.Empty(tombstones);
    }

    [Fact]
    public async Task DeleteAsync_MultipleDocuments_CreatesMultipleTombstones()
    {
        var gcOptions = new GarbageCollectorOptions
        {
            Enabled = true,
            EnableBackgroundCollection = false
        };

        using var store = new GarbageCollectedDocumentStore(_testDataPath, gcOptions);
        await store.InitializeAsync();

        // Insert multiple documents
        for (int i = 0; i < 5; i++)
        {
            var doc = new Document
            {
                Id = $"doc-{i}",
                Data = new Dictionary<string, object> { { "index", i } },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };
            await store.InsertAsync("items", doc);
        }

        Assert.Equal(5L, await store.CountAsync("items"));

        // Delete some documents
        await store.DeleteAsync("items", "doc-0");
        await store.DeleteAsync("items", "doc-2");
        await store.DeleteAsync("items", "doc-4");

        Assert.Equal(2L, await store.CountAsync("items"));

        // Verify tombstones
        var tombstones = store.GarbageCollector.GetTombstones("items").ToList();
        Assert.Equal(3, tombstones.Count);
        Assert.Contains(tombstones, t => t.DocumentId == "doc-0");
        Assert.Contains(tombstones, t => t.DocumentId == "doc-2");
        Assert.Contains(tombstones, t => t.DocumentId == "doc-4");
    }

    [Fact]
    public async Task DropCollectionAsync_RemovesCollectionAndCreatesTombstones()
    {
        var gcOptions = new GarbageCollectorOptions
        {
            Enabled = true,
            EnableBackgroundCollection = false
        };

        using var store = new GarbageCollectedDocumentStore(_testDataPath, gcOptions);
        await store.InitializeAsync();

        // Insert documents
        for (int i = 0; i < 3; i++)
        {
            var doc = new Document
            {
                Id = $"user-{i}",
                Data = new Dictionary<string, object> { { "name", $"User {i}" } },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };
            await store.InsertAsync("users", doc);
        }

        // Drop collection
        var dropped = await store.DropCollectionAsync("users");
        Assert.True(dropped);

        // Verify tombstones created for all documents
        var tombstones = store.GarbageCollector.GetTombstones("users").ToList();
        Assert.Equal(3, tombstones.Count);
    }

    [Fact]
    public async Task CollectGarbageAsync_CleansExpiredTombstones()
    {
        var gcOptions = new GarbageCollectorOptions
        {
            Enabled = true,
            RetentionPeriod = TimeSpan.FromMilliseconds(100),
            EnableBackgroundCollection = false
        };

        using var store = new GarbageCollectedDocumentStore(_testDataPath, gcOptions);
        await store.InitializeAsync();

        // Insert and delete a document
        var doc = new Document
        {
            Id = "temp-doc",
            Data = new Dictionary<string, object> { { "temp", true } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        await store.InsertAsync("temp", doc);
        var documentPath = Path.Combine(_testDataPath, "temp", "temp-doc.json");
        Assert.True(File.Exists(documentPath));

        await store.DeleteAsync("temp", "temp-doc");
        Assert.True(store.GarbageCollector.HasTombstone("temp-doc"));

        // Wait for retention period
        await Task.Delay(200);

        // Run garbage collection
        var cleaned = await store.CollectGarbageAsync();

        Assert.Equal(1, cleaned);
        Assert.False(store.GarbageCollector.HasTombstone("temp-doc"));
        Assert.False(File.Exists(documentPath));

        var stats = store.GetGarbageCollectionStats();
        Assert.Equal(1, stats.CleanedTombstones);
        Assert.Equal(1, stats.DocumentsPhysicallyDeleted);
    }

    [Fact]
    public async Task GetGarbageCollectionStats_ReturnsCorrectStats()
    {
        var gcOptions = new GarbageCollectorOptions
        {
            Enabled = true,
            EnableBackgroundCollection = false
        };

        using var store = new GarbageCollectedDocumentStore(_testDataPath, gcOptions);
        await store.InitializeAsync();

        // Insert and delete some documents
        for (int i = 0; i < 3; i++)
        {
            var doc = new Document
            {
                Id = $"doc-{i}",
                Data = new Dictionary<string, object>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };
            await store.InsertAsync("test", doc);
            await store.DeleteAsync("test", $"doc-{i}");
        }

        var stats = store.GetGarbageCollectionStats();
        Assert.Equal(3, stats.TotalTombstones);
        Assert.Equal(0, stats.CleanedTombstones);
    }

    [Fact]
    public async Task Integration_FullLifecycle_WorksCorrectly()
    {
        var gcOptions = new GarbageCollectorOptions
        {
            Enabled = true,
            RetentionPeriod = TimeSpan.FromMilliseconds(50),
            MaxTombstonesPerRun = 100,
            EnableBackgroundCollection = false
        };

        using var store = new GarbageCollectedDocumentStore(_testDataPath, gcOptions);
        await store.InitializeAsync();

        // Phase 1: Insert documents
        for (int i = 0; i < 10; i++)
        {
            var doc = new Document
            {
                Id = $"item-{i:00}",
                Data = new Dictionary<string, object> 
                { 
                    { "name", $"Item {i}" },
                    { "value", i * 10 }
                },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };
            await store.InsertAsync("inventory", doc);
        }

        Assert.Equal(10L, await store.CountAsync("inventory"));

        // Phase 2: Delete some documents
        await store.DeleteAsync("inventory", "item-02");
        await store.DeleteAsync("inventory", "item-05");
        await store.DeleteAsync("inventory", "item-07");

        Assert.Equal(7L, await store.CountAsync("inventory"));
        Assert.Equal(3, store.GarbageCollector.GetTombstones("inventory").Count());

        // Phase 3: Wait and run garbage collection
        await Task.Delay(100);
        var cleaned = await store.CollectGarbageAsync();

        Assert.Equal(3, cleaned);
        Assert.Equal(0, store.GarbageCollector.GetTombstones("inventory").Count());

        // Phase 4: Verify remaining documents are intact
        Assert.Equal(7L, await store.CountAsync("inventory"));
        
        var item01 = await store.GetAsync("inventory", "item-01");
        Assert.NotNull(item01);
        Assert.Equal("Item 1", item01.Data!["name"]);

        var item02 = await store.GetAsync("inventory", "item-02");
        Assert.Null(item02);

        // Phase 5: Verify stats
        var stats = store.GetGarbageCollectionStats();
        Assert.Equal(0, stats.TotalTombstones);
        Assert.Equal(3, stats.CleanedTombstones);
        Assert.Equal(3, stats.DocumentsPhysicallyDeleted);
        Assert.NotNull(stats.LastCollectionRun);
    }

    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        var store = new GarbageCollectedDocumentStore(_testDataPath);
        await store.InitializeAsync();

        // Should not throw
        store.Dispose();
    }

    [Fact]
    public async Task Concurrent_Deletions_AreTrackedCorrectly()
    {
        var gcOptions = new GarbageCollectorOptions
        {
            Enabled = true,
            EnableBackgroundCollection = false
        };

        using var store = new GarbageCollectedDocumentStore(_testDataPath, gcOptions);
        await store.InitializeAsync();

        // Insert documents
        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var doc = new Document
                {
                    Id = $"concurrent-{index:00}",
                    Data = new Dictionary<string, object> { { "index", index } },
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Version = 1
                };
                await store.InsertAsync("concurrent", doc);
            }));
        }
        await Task.WhenAll(tasks);

        Assert.Equal(20L, await store.CountAsync("concurrent"));

        // Delete documents concurrently
        tasks.Clear();
        for (int i = 0; i < 20; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                await store.DeleteAsync("concurrent", $"concurrent-{index:00}");
            }));
        }
        await Task.WhenAll(tasks);

        Assert.Equal(0L, await store.CountAsync("concurrent"));
        Assert.Equal(20, store.GarbageCollector.GetTombstones("concurrent").Count());
    }
}
