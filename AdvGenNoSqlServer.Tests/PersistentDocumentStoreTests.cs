// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for PersistentDocumentStore implementation
/// </summary>
public class PersistentDocumentStoreTests : IDisposable
{
    private readonly string _testDataPath;
    private readonly PersistentDocumentStore _store;

    public PersistentDocumentStoreTests()
    {
        // Create a unique test directory for each test run
        _testDataPath = Path.Combine(Path.GetTempPath(), $"NoSqlTests_{Guid.NewGuid()}");
        _store = new PersistentDocumentStore(_testDataPath);
    }

    public void Dispose()
    {
        // Cleanup test directory after tests
        if (Directory.Exists(_testDataPath))
        {
            try
            {
                Directory.Delete(_testDataPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Initialization Tests

    [Fact]
    public async Task InitializeAsync_CreatesDataDirectory()
    {
        // Act
        await _store.InitializeAsync();

        // Assert
        Assert.True(Directory.Exists(_testDataPath));
    }

    [Fact]
    public async Task InitializeAsync_LoadsExistingCollections()
    {
        // Arrange - Create a collection manually on disk
        var collectionPath = Path.Combine(_testDataPath, "existing-collection");
        Directory.CreateDirectory(collectionPath);
        var document = new Document
        {
            Id = "test-doc",
            Data = new Dictionary<string, object> { { "key", "value" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };
        var json = System.Text.Json.JsonSerializer.Serialize(document);
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "test-doc.json"), json);

        // Act
        await _store.InitializeAsync();
        var result = await _store.GetAsync("existing-collection", "test-doc");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-doc", result.Id);
        var value = result.Data!["key"];
        Assert.Equal("value", value.ToString());
    }

    [Fact]
    public void Constructor_NullDataPath_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new PersistentDocumentStore(""));
    }

    [Fact]
    public async Task Operation_WithoutInitialization_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.GetAsync("test", "doc"));
    }

    #endregion

    #region CRUD Tests

    [Fact]
    public async Task InsertAsync_ValidDocument_SavesToDisk()
    {
        // Arrange
        await _store.InitializeAsync();
        var document = new Document
        {
            Id = "persist-test",
            Data = new Dictionary<string, object> { { "name", "Test Document" } }
        };

        // Act
        var result = await _store.InsertAsync("users", document);

        // Assert
        Assert.NotNull(result);
        var filePath = Path.Combine(_testDataPath, "users", "persist-test.json");
        Assert.True(File.Exists(filePath));

        var json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("persist-test", json);
        Assert.Contains("Test Document", json);
    }

    [Fact]
    public async Task GetAsync_AfterReload_ReturnsDocument()
    {
        // Arrange
        await _store.InitializeAsync();
        var document = new Document
        {
            Id = "reload-test",
            Data = new Dictionary<string, object> { { "status", "active" } }
        };
        await _store.InsertAsync("products", document);

        // Act - Create a new store instance pointing to the same data path
        var newStore = new PersistentDocumentStore(_testDataPath);
        await newStore.InitializeAsync();
        var result = await newStore.GetAsync("products", "reload-test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("reload-test", result.Id);
        var status = result.Data!["status"];
        Assert.Equal("active", status.ToString());
    }

    [Fact]
    public async Task UpdateAsync_UpdatesDocumentOnDisk()
    {
        // Arrange
        await _store.InitializeAsync();
        var document = new Document
        {
            Id = "update-persist",
            Data = new Dictionary<string, object> { { "version", 1 } }
        };
        await _store.InsertAsync("items", document);

        // Act
        var updatedDocument = new Document
        {
            Id = "update-persist",
            Data = new Dictionary<string, object> { { "version", 2 } }
        };
        await _store.UpdateAsync("items", updatedDocument);

        // Assert
        var filePath = Path.Combine(_testDataPath, "items", "update-persist.json");
        var json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\"version\": 2", json);
    }

    [Fact]
    public async Task DeleteAsync_RemovesDocumentFromDisk()
    {
        // Arrange
        await _store.InitializeAsync();
        var document = new Document { Id = "delete-persist" };
        await _store.InsertAsync("temp", document);
        var filePath = Path.Combine(_testDataPath, "temp", "delete-persist.json");
        Assert.True(File.Exists(filePath));

        // Act
        var result = await _store.DeleteAsync("temp", "delete-persist");

        // Assert
        Assert.True(result);
        Assert.False(File.Exists(filePath));
    }

    #endregion

    #region Collection Management Tests

    [Fact]
    public async Task CreateCollectionAsync_CreatesDirectoryOnDisk()
    {
        // Arrange
        await _store.InitializeAsync();

        // Act
        await _store.CreateCollectionAsync("new-collection");

        // Assert
        var collectionPath = Path.Combine(_testDataPath, "new-collection");
        Assert.True(Directory.Exists(collectionPath));
    }

    [Fact]
    public async Task DropCollectionAsync_RemovesDirectoryFromDisk()
    {
        // Arrange
        await _store.InitializeAsync();
        await _store.CreateCollectionAsync("drop-collection");
        await _store.InsertAsync("drop-collection", new Document { Id = "doc1" });
        var collectionPath = Path.Combine(_testDataPath, "drop-collection");
        Assert.True(Directory.Exists(collectionPath));

        // Act
        var result = await _store.DropCollectionAsync("drop-collection");

        // Assert
        Assert.True(result);
        Assert.False(Directory.Exists(collectionPath));
    }

    [Fact]
    public async Task ClearCollectionAsync_RemovesFilesFromDisk()
    {
        // Arrange
        await _store.InitializeAsync();
        await _store.InsertAsync("clear-test", new Document { Id = "doc1" });
        await _store.InsertAsync("clear-test", new Document { Id = "doc2" });
        var collectionPath = Path.Combine(_testDataPath, "clear-test");
        Assert.Equal(2, Directory.GetFiles(collectionPath, "*.json").Length);

        // Act
        await _store.ClearCollectionAsync("clear-test");

        // Assert
        Assert.Empty(Directory.GetFiles(collectionPath, "*.json"));
    }

    [Fact]
    public async Task GetCollectionsAsync_ReturnsAllCollections()
    {
        // Arrange
        await _store.InitializeAsync();
        await _store.CreateCollectionAsync("collection-a");
        await _store.CreateCollectionAsync("collection-b");
        await _store.CreateCollectionAsync("collection-c");

        // Act
        var result = await _store.GetCollectionsAsync();

        // Assert
        Assert.Equal(3, result.Count());
        Assert.Contains("collection-a", result);
        Assert.Contains("collection-b", result);
        Assert.Contains("collection-c", result);
    }

    #endregion

    #region Persistence Tests

    [Fact]
    public async Task SaveChangesAsync_PersistsAllCollections()
    {
        // Arrange
        await _store.InitializeAsync();
        await _store.InsertAsync("col1", new Document { Id = "doc1", Data = new Dictionary<string, object> { { "data", 1 } } });
        await _store.InsertAsync("col2", new Document { Id = "doc2", Data = new Dictionary<string, object> { { "data", 2 } } });

        // Act
        await _store.SaveChangesAsync();

        // Assert
        Assert.True(File.Exists(Path.Combine(_testDataPath, "col1", "doc1.json")));
        Assert.True(File.Exists(Path.Combine(_testDataPath, "col2", "doc2.json")));
    }

    [Fact]
    public async Task SaveCollectionAsync_PersistsSpecificCollection()
    {
        // Arrange
        await _store.InitializeAsync();
        await _store.InsertAsync("save-col", new Document { Id = "save-doc", Data = new Dictionary<string, object> { { "key", "value" } } });

        // Act
        await _store.SaveCollectionAsync("save-col");

        // Assert
        var filePath = Path.Combine(_testDataPath, "save-col", "save-doc.json");
        Assert.True(File.Exists(filePath));
        var json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("save-doc", json);
    }

    [Fact]
    public async Task LoadCollectionAsync_LoadsDocumentsFromDisk()
    {
        // Arrange - Create files manually
        var collectionPath = Path.Combine(_testDataPath, "load-test");
        Directory.CreateDirectory(collectionPath);
        var doc1 = new Document { Id = "loaded1", Data = new Dictionary<string, object> { { "name", "Doc1" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "loaded2", Data = new Dictionary<string, object> { { "name", "Doc2" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "loaded1.json"), System.Text.Json.JsonSerializer.Serialize(doc1));
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "loaded2.json"), System.Text.Json.JsonSerializer.Serialize(doc2));

        // Act
        await _store.InitializeAsync();
        var count = await _store.CountAsync("load-test");
        var allDocs = await _store.GetAllAsync("load-test");

        // Assert
        Assert.Equal(2L, count);
        Assert.Contains(allDocs, d => d.Id == "loaded1");
        Assert.Contains(allDocs, d => d.Id == "loaded2");
    }

    [Fact]
    public async Task CollectionExistsOnDiskAsync_ExistingCollection_ReturnsTrue()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_testDataPath, "existing-col"));

        // Act
        var result = await _store.CollectionExistsOnDiskAsync("existing-col");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CollectionExistsOnDiskAsync_NonExistingCollection_ReturnsFalse()
    {
        // Act
        var result = await _store.CollectionExistsOnDiskAsync("non-existing-col");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Data Integrity Tests

    [Fact]
    public async Task DataPreserved_AfterReload()
    {
        // Arrange
        await _store.InitializeAsync();
        var document = new Document
        {
            Id = "preserve-test",
            Data = new Dictionary<string, object>
            {
                { "string", "value" },
                { "number", 42 },
                { "boolean", true }
            }
        };
        var inserted = await _store.InsertAsync("data-test", document);
        var originalVersion = inserted.Version;

        // Act - Create new store instance
        var newStore = new PersistentDocumentStore(_testDataPath);
        await newStore.InitializeAsync();
        var result = await newStore.GetAsync("data-test", "preserve-test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("preserve-test", result.Id);
        Assert.Equal("value", result.Data!["string"].ToString());
        // JSON numbers deserialize as JsonElement, need to get Int64 value
        var numberElement = (System.Text.Json.JsonElement)result.Data!["number"];
        Assert.Equal(42L, numberElement.GetInt64());
        var boolElement = (System.Text.Json.JsonElement)result.Data!["boolean"];
        Assert.True(boolElement.GetBoolean());
        Assert.Equal(originalVersion, result.Version); // Version should be preserved after reload
    }

    [Fact]
    public async Task CountAsync_AfterReload_ReturnsCorrectCount()
    {
        // Arrange
        await _store.InitializeAsync();
        await _store.InsertAsync("count-test", new Document { Id = "c1" });
        await _store.InsertAsync("count-test", new Document { Id = "c2" });
        await _store.InsertAsync("count-test", new Document { Id = "c3" });

        // Act - Create new store instance
        var newStore = new PersistentDocumentStore(_testDataPath);
        await newStore.InitializeAsync();
        var count = await newStore.CountAsync("count-test");

        // Assert
        Assert.Equal(3L, count);
    }

    [Fact]
    public async Task ExistsAsync_AfterReload_ReturnsCorrectResult()
    {
        // Arrange
        await _store.InitializeAsync();
        await _store.InsertAsync("exists-test", new Document { Id = "existing" });

        // Act - Create new store instance
        var newStore = new PersistentDocumentStore(_testDataPath);
        await newStore.InitializeAsync();
        var exists = await newStore.ExistsAsync("exists-test", "existing");
        var notExists = await newStore.ExistsAsync("exists-test", "non-existing");

        // Assert
        Assert.True(exists);
        Assert.False(notExists);
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task ConcurrentInsertions_AllDocumentsPersisted()
    {
        // Arrange
        await _store.InitializeAsync();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                await _store.InsertAsync("concurrent", new Document { Id = $"concurrent-{index}" });
            }));
        }
        await Task.WhenAll(tasks);

        // Assert
        var count = await _store.CountAsync("concurrent");
        Assert.Equal(10L, count);

        var collectionPath = Path.Combine(_testDataPath, "concurrent");
        var files = Directory.GetFiles(collectionPath, "*.json");
        Assert.Equal(10, files.Length);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task InsertAsync_DocumentWithSpecialCharactersInId_PersistsCorrectly()
    {
        // Arrange
        await _store.InitializeAsync();
        var document = new Document
        {
            Id = "doc-with-spaces-and-123",
            Data = new Dictionary<string, object> { { "test", "value" } }
        };

        // Act
        await _store.InsertAsync("special", document);

        // Assert
        var result = await _store.GetAsync("special", "doc-with-spaces-and-123");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task InsertAsync_EmptyData_PersistsCorrectly()
    {
        // Arrange
        await _store.InitializeAsync();
        var document = new Document { Id = "empty-data" };

        // Act
        var result = await _store.InsertAsync("empty", document);

        // Assert
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DropCollectionAsync_NonExistentCollection_ReturnsFalse()
    {
        // Arrange
        await _store.InitializeAsync();

        // Act
        var result = await _store.DropCollectionAsync("non-existent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentDocument_ReturnsFalse()
    {
        // Arrange
        await _store.InitializeAsync();
        await _store.CreateCollectionAsync("delete-test");

        // Act
        var result = await _store.DeleteAsync("delete-test", "non-existent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SaveCollectionAsync_NonExistentCollection_ThrowsCollectionNotFoundException()
    {
        // Arrange
        await _store.InitializeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<CollectionNotFoundException>(() =>
            _store.SaveCollectionAsync("non-existent"));
    }

    #endregion

    #region IPersistentDocumentStore Interface Tests

    [Fact]
    public void DataPath_ReturnsCorrectPath()
    {
        // Assert
        Assert.Equal(_testDataPath, _store.DataPath);
    }

    [Fact]
    public async Task InsertAsync_ValidDocument_ReturnsDocumentWithMetadata()
    {
        // Arrange
        await _store.InitializeAsync();
        var document = new Document
        {
            Id = "metadata-test",
            Data = new Dictionary<string, object> { { "name", "Test" } }
        };

        // Act
        var result = await _store.InsertAsync("users", document);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("metadata-test", result.Id);
        Assert.True(result.Version == 1);
        Assert.True(result.CreatedAt > DateTime.MinValue);
        Assert.True(result.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentDocument_ThrowsDocumentNotFoundException()
    {
        // Arrange
        await _store.InitializeAsync();
        await _store.CreateCollectionAsync("users");
        var document = new Document
        {
            Id = "non-existent",
            Data = new Dictionary<string, object>()
        };

        // Act & Assert
        await Assert.ThrowsAsync<DocumentNotFoundException>(() =>
            _store.UpdateAsync("users", document));
    }

    [Fact]
    public async Task UpdateAsync_NonExistentCollection_ThrowsCollectionNotFoundException()
    {
        // Arrange
        await _store.InitializeAsync();
        var document = new Document
        {
            Id = "test",
            Data = new Dictionary<string, object>()
        };

        // Act & Assert
        await Assert.ThrowsAsync<CollectionNotFoundException>(() =>
            _store.UpdateAsync("non-existent-collection", document));
    }

    [Fact]
    public async Task GetAllAsync_EmptyCollection_ReturnsEmptyList()
    {
        // Arrange
        await _store.InitializeAsync();

        // Act
        var result = await _store.GetAllAsync("empty-collection");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task CountAsync_EmptyCollection_ReturnsZero()
    {
        // Arrange
        await _store.InitializeAsync();

        // Act
        var result = await _store.CountAsync("empty-collection");

        // Assert
        Assert.Equal(0L, result);
    }

    [Fact]
    public async Task GetCollectionsAsync_NoCollections_ReturnsEmptyList()
    {
        // Arrange
        await _store.InitializeAsync();

        // Act
        var result = await _store.GetCollectionsAsync();

        // Assert
        Assert.Empty(result);
    }

    #endregion
}
