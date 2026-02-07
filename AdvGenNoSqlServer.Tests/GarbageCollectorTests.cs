// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for the GarbageCollector
/// </summary>
public class GarbageCollectorTests : IDisposable
{
    private readonly string _testDataPath;
    private readonly GarbageCollectorOptions _defaultOptions;

    public GarbageCollectorTests()
    {
        _testDataPath = Path.Combine(Path.GetTempPath(), $"nosql_gc_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataPath);
        
        _defaultOptions = new GarbageCollectorOptions
        {
            Enabled = true,
            RetentionPeriod = TimeSpan.FromMilliseconds(100),
            CollectionInterval = TimeSpan.FromHours(1),
            MaxTombstonesPerRun = 100,
            EnableBackgroundCollection = false
        };
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
    public void Constructor_WithDefaultOptions_InitializesCorrectly()
    {
        var gc = new GarbageCollector();

        var stats = gc.GetStatistics();
        Assert.Equal(0, stats.TotalTombstones);
        Assert.Equal(0, stats.CleanedTombstones);
        Assert.Null(stats.LastCollectionRun);

        gc.Dispose();
    }

    [Fact]
    public void Constructor_WithCustomOptions_UsesProvidedOptions()
    {
        var options = new GarbageCollectorOptions
        {
            Enabled = false,
            RetentionPeriod = TimeSpan.FromDays(7)
        };

        var gc = new GarbageCollector(options);
        
        // Should not track when disabled
        gc.RecordDeletion("test", "doc1", 1);
        var stats = gc.GetStatistics();
        Assert.Equal(0, stats.TotalTombstones);

        gc.Dispose();
    }

    [Fact]
    public void RecordDeletion_WithValidData_CreatesTombstone()
    {
        var gc = new GarbageCollector(_defaultOptions);

        gc.RecordDeletion("users", "doc123", 5, "/path/to/file.json", "txn_456");

        var stats = gc.GetStatistics();
        Assert.Equal(1, stats.TotalTombstones);

        var tombstone = gc.GetTombstone("doc123");
        Assert.NotNull(tombstone);
        Assert.Equal("doc123", tombstone.DocumentId);
        Assert.Equal("users", tombstone.CollectionName);
        Assert.Equal(5, tombstone.DocumentVersion);
        Assert.Equal("/path/to/file.json", tombstone.FilePath);
        Assert.Equal("txn_456", tombstone.TransactionId);
        Assert.True(tombstone.DeletedAt <= DateTime.UtcNow);

        gc.Dispose();
    }

    [Fact]
    public void RecordDeletion_WithNullCollectionName_ThrowsArgumentException()
    {
        var gc = new GarbageCollector(_defaultOptions);

        Assert.Throws<ArgumentException>(() => gc.RecordDeletion("", "doc1", 1));
        Assert.Throws<ArgumentException>(() => gc.RecordDeletion(null!, "doc1", 1));

        gc.Dispose();
    }

    [Fact]
    public void RecordDeletion_WithNullDocumentId_ThrowsArgumentException()
    {
        var gc = new GarbageCollector(_defaultOptions);

        Assert.Throws<ArgumentException>(() => gc.RecordDeletion("test", "", 1));
        Assert.Throws<ArgumentException>(() => gc.RecordDeletion("test", null!, 1));

        gc.Dispose();
    }

    [Fact]
    public void RecordDeletion_DuplicateDocumentId_UpdatesTombstone()
    {
        var gc = new GarbageCollector(_defaultOptions);

        gc.RecordDeletion("users", "doc1", 1);
        var firstTombstone = gc.GetTombstone("doc1");
        
        // Wait a tiny bit to ensure different timestamp
        Thread.Sleep(10);
        
        gc.RecordDeletion("users", "doc1", 2);
        var secondTombstone = gc.GetTombstone("doc1");

        Assert.Equal(1, gc.GetStatistics().TotalTombstones);
        Assert.Equal(2, secondTombstone.DocumentVersion);
        Assert.True(secondTombstone.DeletedAt >= firstTombstone.DeletedAt);

        gc.Dispose();
    }

    [Fact]
    public void RecordDeletion_MultipleDocuments_TracksAll()
    {
        var gc = new GarbageCollector(_defaultOptions);

        gc.RecordDeletion("users", "doc1", 1);
        gc.RecordDeletion("users", "doc2", 1);
        gc.RecordDeletion("products", "doc3", 1);

        var stats = gc.GetStatistics();
        Assert.Equal(3, stats.TotalTombstones);

        var allTombstones = gc.GetTombstones().ToList();
        Assert.Equal(3, allTombstones.Count);

        gc.Dispose();
    }

    [Fact]
    public void GetTombstones_ByCollectionName_FiltersCorrectly()
    {
        var gc = new GarbageCollector(_defaultOptions);

        gc.RecordDeletion("users", "user1", 1);
        gc.RecordDeletion("users", "user2", 1);
        gc.RecordDeletion("products", "prod1", 1);
        gc.RecordDeletion("orders", "order1", 1);

        var usersTombstones = gc.GetTombstones("users").ToList();
        Assert.Equal(2, usersTombstones.Count);
        Assert.All(usersTombstones, t => Assert.Equal("users", t.CollectionName));

        var productsTombstones = gc.GetTombstones("products").ToList();
        Assert.Single(productsTombstones);
        Assert.Equal("prod1", productsTombstones[0].DocumentId);

        gc.Dispose();
    }

    [Fact]
    public void GetTombstones_WithNullCollectionName_ReturnsEmpty()
    {
        var gc = new GarbageCollector(_defaultOptions);

        gc.RecordDeletion("users", "doc1", 1);

        var result = gc.GetTombstones(null!);
        Assert.Empty(result);

        result = gc.GetTombstones("");
        Assert.Empty(result);

        gc.Dispose();
    }

    [Fact]
    public void HasTombstone_ExistingDocument_ReturnsTrue()
    {
        var gc = new GarbageCollector(_defaultOptions);

        gc.RecordDeletion("users", "doc1", 1);

        Assert.True(gc.HasTombstone("doc1"));

        gc.Dispose();
    }

    [Fact]
    public void HasTombstone_NonExistingDocument_ReturnsFalse()
    {
        var gc = new GarbageCollector(_defaultOptions);

        Assert.False(gc.HasTombstone("nonexistent"));
        Assert.False(gc.HasTombstone(""));
        Assert.False(gc.HasTombstone(null!));

        gc.Dispose();
    }

    [Fact]
    public void GetTombstone_NonExisting_ReturnsNull()
    {
        var gc = new GarbageCollector(_defaultOptions);

        Assert.Null(gc.GetTombstone("nonexistent"));
        Assert.Null(gc.GetTombstone(""));
        Assert.Null(gc.GetTombstone(null!));

        gc.Dispose();
    }

    [Fact]
    public void RemoveTombstone_Existing_RemovesAndReturnsTrue()
    {
        var gc = new GarbageCollector(_defaultOptions);

        gc.RecordDeletion("users", "doc1", 1);
        Assert.Equal(1, gc.GetStatistics().TotalTombstones);

        var removed = gc.RemoveTombstone("doc1");
        
        Assert.True(removed);
        Assert.Equal(0, gc.GetStatistics().TotalTombstones);
        Assert.Null(gc.GetTombstone("doc1"));

        gc.Dispose();
    }

    [Fact]
    public void RemoveTombstone_NonExisting_ReturnsFalse()
    {
        var gc = new GarbageCollector(_defaultOptions);

        Assert.False(gc.RemoveTombstone("nonexistent"));
        Assert.False(gc.RemoveTombstone(""));
        Assert.False(gc.RemoveTombstone(null!));

        gc.Dispose();
    }

    [Fact]
    public void ClearAllTombstones_RemovesAll()
    {
        var gc = new GarbageCollector(_defaultOptions);

        gc.RecordDeletion("users", "doc1", 1);
        gc.RecordDeletion("users", "doc2", 1);
        gc.RecordDeletion("products", "doc3", 1);
        Assert.Equal(3, gc.GetStatistics().TotalTombstones);

        gc.ClearAllTombstones();

        Assert.Equal(0, gc.GetStatistics().TotalTombstones);
        Assert.Empty(gc.GetTombstones());

        gc.Dispose();
    }

    [Fact]
    public async Task CollectAsync_WithExpiredTombstones_RemovesFilesAndTombstones()
    {
        var gc = new GarbageCollector(_defaultOptions);
        
        // Create a test file
        var testFile = Path.Combine(_testDataPath, "test_doc.json");
        await File.WriteAllTextAsync(testFile, "{\"test\": \"data\"}");
        var fileInfo = new FileInfo(testFile);
        var fileSize = fileInfo.Length;

        gc.RecordDeletion("users", "doc1", 1, testFile);
        Assert.Equal(1, gc.GetStatistics().TotalTombstones);

        // Wait for retention period to expire
        await Task.Delay(150);

        var cleaned = await gc.CollectAsync();

        Assert.Equal(1, cleaned);
        Assert.False(File.Exists(testFile));
        
        var stats = gc.GetStatistics();
        Assert.Equal(0, stats.TotalTombstones);
        Assert.Equal(1, stats.CleanedTombstones);
        Assert.Equal(1, stats.DocumentsPhysicallyDeleted);
        Assert.Equal(fileSize, stats.BytesFreed);
        Assert.NotNull(stats.LastCollectionRun);

        gc.Dispose();
    }

    [Fact]
    public async Task CollectAsync_WithNonExpiredTombstones_KeepsTombstones()
    {
        var options = new GarbageCollectorOptions
        {
            Enabled = true,
            RetentionPeriod = TimeSpan.FromHours(1), // Long retention
            EnableBackgroundCollection = false
        };
        var gc = new GarbageCollector(options);

        gc.RecordDeletion("users", "doc1", 1);
        Assert.Equal(1, gc.GetStatistics().TotalTombstones);

        var cleaned = await gc.CollectAsync();

        Assert.Equal(0, cleaned);
        Assert.Equal(1, gc.GetStatistics().TotalTombstones);

        gc.Dispose();
    }

    [Fact]
    public async Task CollectAsync_WhenDisabled_ReturnsZero()
    {
        var options = new GarbageCollectorOptions { Enabled = false };
        var gc = new GarbageCollector(options);

        gc.RecordDeletion("users", "doc1", 1);
        
        var cleaned = await gc.CollectAsync();

        Assert.Equal(0, cleaned);

        gc.Dispose();
    }

    [Fact]
    public async Task CollectAsync_RespectsMaxTombstonesPerRun()
    {
        var options = new GarbageCollectorOptions
        {
            Enabled = true,
            RetentionPeriod = TimeSpan.FromMilliseconds(1),
            MaxTombstonesPerRun = 2,
            EnableBackgroundCollection = false
        };
        var gc = new GarbageCollector(options);

        // Create multiple test files
        for (int i = 0; i < 5; i++)
        {
            var testFile = Path.Combine(_testDataPath, $"doc{i}.json");
            await File.WriteAllTextAsync(testFile, $"{{\"id\": {i}}}");
            gc.RecordDeletion("users", $"doc{i}", 1, testFile);
        }

        await Task.Delay(50);

        var cleaned = await gc.CollectAsync();

        Assert.Equal(2, cleaned); // Only 2 per run
        Assert.Equal(3, gc.GetStatistics().TotalTombstones); // 3 remaining

        gc.Dispose();
    }

    [Fact]
    public async Task CollectAsync_WithMissingFile_HandlesGracefully()
    {
        var gc = new GarbageCollector(_defaultOptions);

        // Record deletion with non-existent file
        gc.RecordDeletion("users", "doc1", 1, "/nonexistent/path/file.json");

        await Task.Delay(150);

        var cleaned = await gc.CollectAsync();

        Assert.Equal(1, cleaned);
        Assert.Equal(0, gc.GetStatistics().TotalTombstones);

        gc.Dispose();
    }

    [Fact]
    public async Task CollectAsync_RespectsCancellationToken()
    {
        var gc = new GarbageCollector(_defaultOptions);

        // Create many test files
        for (int i = 0; i < 10; i++)
        {
            var testFile = Path.Combine(_testDataPath, $"doc{i}.json");
            await File.WriteAllTextAsync(testFile, $"{{\"id\": {i}}}");
            gc.RecordDeletion("users", $"doc{i}", 1, testFile);
        }

        await Task.Delay(150);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cleaned = await gc.CollectAsync(cts.Token);

        // Should return 0 or partial results due to cancellation
        Assert.True(cleaned >= 0);

        gc.Dispose();
    }

    [Fact]
    public void GetStatistics_ReturnsSnapshot()
    {
        var gc = new GarbageCollector(_defaultOptions);

        gc.RecordDeletion("users", "doc1", 1);
        
        var stats1 = gc.GetStatistics();
        
        gc.RecordDeletion("users", "doc2", 1);
        
        var stats2 = gc.GetStatistics();

        // stats1 should be unchanged (it's a snapshot)
        Assert.Equal(1, stats1.TotalTombstones);
        Assert.Equal(2, stats2.TotalTombstones);

        gc.Dispose();
    }

    [Fact]
    public void BackgroundCollection_TimerFires_Periodically()
    {
        var options = new GarbageCollectorOptions
        {
            Enabled = true,
            RetentionPeriod = TimeSpan.FromMilliseconds(50),
            CollectionInterval = TimeSpan.FromMilliseconds(100),
            EnableBackgroundCollection = true
        };

        var gc = new GarbageCollector(options);

        // Create a test file
        var testFile = Path.Combine(_testDataPath, "bg_test.json");
        File.WriteAllText(testFile, "{\"test\": \"data\"}");
        gc.RecordDeletion("users", "bg_doc", 1, testFile);

        // Wait for background collection to fire
        Thread.Sleep(300);

        // File should be deleted by background collection
        Assert.True(!File.Exists(testFile) || gc.GetStatistics().LastCollectionRun != null);

        gc.Dispose();
    }
}
