// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using System.Text.Json;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Comprehensive tests for HybridDocumentStore - a hybrid in-memory/disk storage implementation
/// </summary>
public class HybridDocumentStoreTests : IAsyncLifetime
{
    private readonly string _testBasePath;
    private HybridDocumentStore _store = null!;

    public HybridDocumentStoreTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"HybridDocumentStoreTests_{Guid.NewGuid()}");
    }

    public async Task InitializeAsync()
    {
        _store = new HybridDocumentStore(_testBasePath);
        await _store.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();

        // Clean up test directory
        if (Directory.Exists(_testBasePath))
        {
            try
            {
                Directory.Delete(_testBasePath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Initialization Tests

    [Fact]
    public async Task Constructor_CreatesStore_WithDefaultState()
    {
        // Arrange & Act
        await using var store = new HybridDocumentStore(_testBasePath + "_new");

        // Assert
        Assert.False(store.IsInitialized);
        Assert.Equal(0, store.PendingWrites);
    }

    [Fact]
    public async Task InitializeAsync_CreatesBaseDirectory_WhenNotExists()
    {
        // Arrange
        var newPath = Path.Combine(Path.GetTempPath(), $"HybridTest_{Guid.NewGuid()}");

        try
        {
            await using var store = new HybridDocumentStore(newPath);

            // Act
            await store.InitializeAsync();

            // Assert
            Assert.True(store.IsInitialized);
            Assert.True(Directory.Exists(newPath));
        }
        finally
        {
            if (Directory.Exists(newPath))
            {
                Directory.Delete(newPath, true);
            }
        }
    }

    [Fact]
    public async Task InitializeAsync_LoadsExistingData_FromDisk()
    {
        // Arrange - Create data on disk first
        var collectionPath = Path.Combine(_testBasePath, "preexisting");
        Directory.CreateDirectory(collectionPath);

        var document = new Document
        {
            Id = "preexisting-doc",
            Data = new Dictionary<string, object> { { "name", "Test" } }
        };
        var json = JsonSerializer.Serialize(document);
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "preexisting-doc.json"), json);

        // Create a new store instance
        await _store.DisposeAsync();
        _store = new HybridDocumentStore(_testBasePath);

        // Act
        await _store.InitializeAsync();

        // Assert
        var loaded = await _store.GetAsync("preexisting", "preexisting-doc");
        Assert.NotNull(loaded);
        Assert.Equal("preexisting-doc", loaded.Id);
    }

    [Fact]
    public async Task InitializeAsync_CanBeCalledMultipleTimes_Safely()
    {
        // Act - Call initialize multiple times
        await _store.InitializeAsync();
        await _store.InitializeAsync();
        await _store.InitializeAsync();

        // Assert - Should not throw and store should be initialized
        Assert.True(_store.IsInitialized);
    }

    [Fact]
    public async Task Operations_ThrowException_WhenNotInitialized()
    {
        // Arrange
        await using var uninitializedStore = new HybridDocumentStore(_testBasePath + "_uninit");
        var doc = new Document { Id = "test" };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uninitializedStore.InsertAsync("collection", doc));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uninitializedStore.GetAsync("collection", "test"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uninitializedStore.GetAllAsync("collection"));
    }

    #endregion

    #region Insert Tests

    [Fact]
    public async Task InsertAsync_AddsDocument_ToCache()
    {
        // Arrange
        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { { "name", "Test Document" } }
        };

        // Act
        var result = await _store.InsertAsync("testcollection", document);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("doc1", result.Id);
        Assert.Equal(1, result.Version);
        Assert.True(result.CreatedAt > DateTime.MinValue);
        Assert.Equal(result.CreatedAt, result.UpdatedAt);
    }

    [Fact]
    public async Task InsertAsync_QueuesWrite_ToDisk()
    {
        // Arrange
        var document = new Document
        {
            Id = "disk-write-test",
            Data = new Dictionary<string, object> { { "value", 42 } }
        };

        // Act
        await _store.InsertAsync("disktest", document);
        await _store.FlushAsync();

        // Assert - File should exist on disk
        var filePath = Path.Combine(_testBasePath, "disktest", "disk-write-test.json");
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task InsertAsync_ThrowsException_ForDuplicateId()
    {
        // Arrange
        var document = new Document { Id = "duplicate" };
        await _store.InsertAsync("collection", document);

        // Act & Assert
        await Assert.ThrowsAsync<DocumentAlreadyExistsException>(() =>
            _store.InsertAsync("collection", new Document { Id = "duplicate" }));
    }

    [Fact]
    public async Task InsertAsync_AllowsSameId_InDifferentCollections()
    {
        // Arrange
        var doc1 = new Document { Id = "sameid" };
        var doc2 = new Document { Id = "sameid" };

        // Act
        await _store.InsertAsync("collection1", doc1);
        await _store.InsertAsync("collection2", doc2);

        // Assert
        var retrieved1 = await _store.GetAsync("collection1", "sameid");
        var retrieved2 = await _store.GetAsync("collection2", "sameid");
        Assert.NotNull(retrieved1);
        Assert.NotNull(retrieved2);
    }

    #endregion

    #region Get Tests

    [Fact]
    public async Task GetAsync_ReturnsDocument_FromCache()
    {
        // Arrange
        var document = new Document
        {
            Id = "cached-doc",
            Data = new Dictionary<string, object> { { "cached", true } }
        };
        await _store.InsertAsync("cache", document);

        // Act
        var result = await _store.GetAsync("cache", "cached-doc");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("cached-doc", result.Id);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_ForNonExistentDocument()
    {
        // Act
        var result = await _store.GetAsync("nonexistent", "missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_LoadsFromDisk_OnCacheMiss()
    {
        // Arrange - Insert and flush to disk
        var document = new Document
        {
            Id = "disk-doc",
            Data = new Dictionary<string, object> { { "source", "disk" } }
        };
        await _store.InsertAsync("diskload", document);
        await _store.FlushAsync();

        // Create new store instance (simulates restart - cache is empty)
        await _store.DisposeAsync();
        _store = new HybridDocumentStore(_testBasePath);
        await _store.InitializeAsync();

        // Act
        var result = await _store.GetAsync("diskload", "disk-doc");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("disk-doc", result.Id);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllDocuments_InCollection()
    {
        // Arrange
        await _store.InsertAsync("getall", new Document { Id = "doc1" });
        await _store.InsertAsync("getall", new Document { Id = "doc2" });
        await _store.InsertAsync("getall", new Document { Id = "doc3" });

        // Act
        var results = (await _store.GetAllAsync("getall")).ToList();

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Contains(results, d => d.Id == "doc1");
        Assert.Contains(results, d => d.Id == "doc2");
        Assert.Contains(results, d => d.Id == "doc3");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmpty_ForNonExistentCollection()
    {
        // Act
        var results = await _store.GetAllAsync("nonexistent");

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task UpdateAsync_UpdatesDocument_InCache()
    {
        // Arrange
        var document = new Document
        {
            Id = "update-test",
            Data = new Dictionary<string, object> { { "version", "original" } }
        };
        await _store.InsertAsync("updates", document);

        // Act
        var updated = new Document
        {
            Id = "update-test",
            Data = new Dictionary<string, object> { { "version", "updated" } }
        };
        var result = await _store.UpdateAsync("updates", updated);

        // Assert
        Assert.Equal(2, result.Version);
        Assert.True(result.UpdatedAt > result.CreatedAt);
    }

    [Fact]
    public async Task UpdateAsync_QueuesWrite_ToDisk()
    {
        // Arrange
        var document = new Document { Id = "update-disk" };
        await _store.InsertAsync("updates", document);
        await _store.FlushAsync();

        // Act
        var updated = new Document
        {
            Id = "update-disk",
            Data = new Dictionary<string, object> { { "updated", true } }
        };
        await _store.UpdateAsync("updates", updated);
        await _store.FlushAsync();

        // Assert - Read from disk
        var filePath = Path.Combine(_testBasePath, "updates", "update-disk.json");
        var json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("updated", json);
    }

    [Fact]
    public async Task UpdateAsync_ThrowsException_ForNonExistentDocument()
    {
        // Act & Assert
        await Assert.ThrowsAsync<DocumentNotFoundException>(() =>
            _store.UpdateAsync("collection", new Document { Id = "missing" }));
    }

    [Fact]
    public async Task UpdateAsync_ThrowsException_ForNonExistentCollection()
    {
        // Act & Assert
        await Assert.ThrowsAsync<DocumentNotFoundException>(() =>
            _store.UpdateAsync("nonexistent", new Document { Id = "doc" }));
    }

    [Fact]
    public async Task UpdateAsync_IncrementsVersion_OnEachUpdate()
    {
        // Arrange
        var document = new Document { Id = "version-test" };
        await _store.InsertAsync("versions", document);

        // Act
        await _store.UpdateAsync("versions", new Document { Id = "version-test" });
        await _store.UpdateAsync("versions", new Document { Id = "version-test" });
        var result = await _store.UpdateAsync("versions", new Document { Id = "version-test" });

        // Assert
        Assert.Equal(4, result.Version);
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task DeleteAsync_RemovesDocument_FromCache()
    {
        // Arrange
        await _store.InsertAsync("delete", new Document { Id = "to-delete" });

        // Act
        var result = await _store.DeleteAsync("delete", "to-delete");

        // Assert
        Assert.True(result);
        var exists = await _store.ExistsAsync("delete", "to-delete");
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile_FromDisk()
    {
        // Arrange
        await _store.InsertAsync("delete", new Document { Id = "disk-delete" });
        await _store.FlushAsync();
        var filePath = Path.Combine(_testBasePath, "delete", "disk-delete.json");
        Assert.True(File.Exists(filePath));

        // Act
        await _store.DeleteAsync("delete", "disk-delete");
        await _store.FlushAsync();

        // Assert
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_ForNonExistentDocument()
    {
        // Act
        var result = await _store.DeleteAsync("collection", "nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_ForNonExistentCollection()
    {
        // Act
        var result = await _store.DeleteAsync("nonexistent", "doc");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Exists Tests

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_ForExistingDocument()
    {
        // Arrange
        await _store.InsertAsync("exists", new Document { Id = "exists-doc" });

        // Act
        var result = await _store.ExistsAsync("exists", "exists-doc");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_ForNonExistentDocument()
    {
        // Act
        var result = await _store.ExistsAsync("exists", "missing");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_ForNonExistentCollection()
    {
        // Act
        var result = await _store.ExistsAsync("nonexistent", "doc");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Count Tests

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await _store.InsertAsync("count", new Document { Id = "doc1" });
        await _store.InsertAsync("count", new Document { Id = "doc2" });
        await _store.InsertAsync("count", new Document { Id = "doc3" });

        // Act
        var count = await _store.CountAsync("count");

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task CountAsync_ReturnsZero_ForEmptyCollection()
    {
        // Arrange
        await _store.CreateCollectionAsync("empty");

        // Act
        var count = await _store.CountAsync("empty");

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CountAsync_ReturnsZero_ForNonExistentCollection()
    {
        // Act
        var count = await _store.CountAsync("nonexistent");

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region Collection Management Tests

    [Fact]
    public async Task CreateCollectionAsync_CreatesCollection_InCacheAndDisk()
    {
        // Act
        await _store.CreateCollectionAsync("newcollection");

        // Assert
        var collections = await _store.GetCollectionsAsync();
        Assert.Contains("newcollection", collections);
        Assert.True(Directory.Exists(Path.Combine(_testBasePath, "newcollection")));
    }

    [Fact]
    public async Task DropCollectionAsync_RemovesCollection_FromCacheAndDisk()
    {
        // Arrange
        await _store.CreateCollectionAsync("todrop");
        await _store.InsertAsync("todrop", new Document { Id = "doc" });
        await _store.FlushAsync();

        // Act
        var result = await _store.DropCollectionAsync("todrop");

        // Assert
        Assert.True(result);
        var collections = await _store.GetCollectionsAsync();
        Assert.DoesNotContain("todrop", collections);
        Assert.False(Directory.Exists(Path.Combine(_testBasePath, "todrop")));
    }

    [Fact]
    public async Task DropCollectionAsync_ReturnsFalse_ForNonExistentCollection()
    {
        // Act
        var result = await _store.DropCollectionAsync("nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetCollectionsAsync_ReturnsAllCollections()
    {
        // Arrange
        await _store.CreateCollectionAsync("col1");
        await _store.CreateCollectionAsync("col2");
        await _store.CreateCollectionAsync("col3");

        // Act
        var collections = (await _store.GetCollectionsAsync()).ToList();

        // Assert
        Assert.Contains("col1", collections);
        Assert.Contains("col2", collections);
        Assert.Contains("col3", collections);
    }

    [Fact]
    public async Task ClearCollectionAsync_RemovesAllDocuments_ButKeepsCollection()
    {
        // Arrange
        await _store.InsertAsync("toclear", new Document { Id = "doc1" });
        await _store.InsertAsync("toclear", new Document { Id = "doc2" });
        await _store.FlushAsync();

        // Act
        await _store.ClearCollectionAsync("toclear");

        // Assert
        var count = await _store.CountAsync("toclear");
        Assert.Equal(0, count);

        // Directory should still exist
        Assert.True(Directory.Exists(Path.Combine(_testBasePath, "toclear")));
    }

    #endregion

    #region Flush and Save Tests

    [Fact]
    public async Task FlushAsync_WaitsForPendingWrites()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            await _store.InsertAsync("flush", new Document { Id = $"doc{i}" });
        }

        // Act
        await _store.FlushAsync();

        // Assert
        Assert.Equal(0, _store.PendingWrites);

        // Verify all files exist
        for (int i = 0; i < 10; i++)
        {
            var filePath = Path.Combine(_testBasePath, "flush", $"doc{i}.json");
            Assert.True(File.Exists(filePath));
        }
    }

    [Fact]
    public async Task SaveAllAsync_SavesAllCachedData_ToDisk()
    {
        // Arrange
        await _store.InsertAsync("saveall1", new Document { Id = "doc1" });
        await _store.InsertAsync("saveall2", new Document { Id = "doc2" });

        // Act
        await _store.SaveAllAsync();

        // Assert
        Assert.True(File.Exists(Path.Combine(_testBasePath, "saveall1", "doc1.json")));
        Assert.True(File.Exists(Path.Combine(_testBasePath, "saveall2", "doc2.json")));
    }

    #endregion

    #region Validation Tests

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public async Task InsertAsync_ThrowsException_ForInvalidCollectionName(string? collectionName)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.InsertAsync(collectionName!, new Document { Id = "doc" }));
    }

    [Theory]
    [InlineData("../parent")]
    [InlineData("sub/path")]
    [InlineData("back\\slash")]
    [InlineData("..")]
    public async Task InsertAsync_ThrowsException_ForPathTraversalAttempts(string collectionName)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.InsertAsync(collectionName, new Document { Id = "doc" }));
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task ConcurrentInserts_AreThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();
        var insertCount = 100;

        // Act
        for (int i = 0; i < insertCount; i++)
        {
            var docId = $"concurrent-{i}";
            tasks.Add(_store.InsertAsync("concurrent", new Document { Id = docId }));
        }

        await Task.WhenAll(tasks);

        // Assert
        var count = await _store.CountAsync("concurrent");
        Assert.Equal(insertCount, count);
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_AreThreadSafe()
    {
        // Arrange
        await _store.InsertAsync("readwrite", new Document { Id = "shared" });
        var tasks = new List<Task>();

        // Act - Mix of reads and writes
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(_store.GetAsync("readwrite", "shared"));
            tasks.Add(_store.UpdateAsync("readwrite", new Document { Id = "shared" }));
        }

        // Assert - Should not throw
        await Task.WhenAll(tasks);
        var doc = await _store.GetAsync("readwrite", "shared");
        Assert.NotNull(doc);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_CompletesGracefully()
    {
        // Arrange
        var store = new HybridDocumentStore(_testBasePath + "_dispose");
        await store.InitializeAsync();
        await store.InsertAsync("dispose", new Document { Id = "doc" });

        // Act & Assert - Should not throw
        await store.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        var store = new HybridDocumentStore(_testBasePath + "_multidispose");
        await store.InitializeAsync();

        // Act & Assert - Should not throw
        await store.DisposeAsync();
        await store.DisposeAsync();
        await store.DisposeAsync();
    }

    #endregion
}
