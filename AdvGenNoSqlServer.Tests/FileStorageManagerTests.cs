using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage.Storage;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class FileStorageManagerTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly FileStorageManager _storageManager;

    public FileStorageManagerTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"FileStorageTest_{Guid.NewGuid()}");
        _storageManager = new FileStorageManager(_testBasePath);
    }

    [Fact]
    public async Task FileStorageManager_SaveAndLoadDocument_ReturnsDocument()
    {
        // Arrange
        var collectionName = "test-collection";
        var document = new Document
        {
            Id = "test-id",
            Data = new System.Collections.Generic.Dictionary<string, object> { { "name", "Test Document" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Act
        await _storageManager.SaveDocumentAsync(collectionName, document);
        var result = await _storageManager.LoadDocumentAsync(collectionName, document.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(document.Id, result.Id);
        Assert.Equal(document.CreatedAt, result.CreatedAt);
        Assert.Equal(document.UpdatedAt, result.UpdatedAt);
        Assert.Equal(document.Version, result.Version);
        
        // Compare dictionary contents
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.True(result.Data.ContainsKey("name"));
        Assert.Equal("Test Document", result.Data["name"].ToString());
    }

    [Fact]
    public async Task FileStorageManager_LoadNonExistentDocument_ReturnsNull()
    {
        // Arrange
        var collectionName = "test-collection";

        // Act
        var result = await _storageManager.LoadDocumentAsync(collectionName, "non-existent-id");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FileStorageManager_DeleteDocument_RemovesFromStorage()
    {
        // Arrange
        var collectionName = "test-collection";
        var document = new Document
        {
            Id = "test-id",
            Data = new System.Collections.Generic.Dictionary<string, object> { { "name", "Test Document" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        await _storageManager.SaveDocumentAsync(collectionName, document);

        // Act
        await _storageManager.DeleteDocumentAsync(collectionName, document.Id);
        var result = await _storageManager.LoadDocumentAsync(collectionName, document.Id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FileStorageManager_DocumentExists_ReturnsCorrectValue()
    {
        // Arrange
        var collectionName = "test-collection";
        var document = new Document
        {
            Id = "test-id",
            Data = new System.Collections.Generic.Dictionary<string, object> { { "name", "Test Document" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Act
        var existsBefore = await _storageManager.DocumentExistsAsync(collectionName, document.Id);
        await _storageManager.SaveDocumentAsync(collectionName, document);
        var existsAfter = await _storageManager.DocumentExistsAsync(collectionName, document.Id);

        // Assert
        Assert.False(existsBefore);
        Assert.True(existsAfter);
    }

    [Fact]
    public async Task FileStorageManager_ListDocuments_ReturnsDocumentIds()
    {
        // Arrange
        var collectionName = "test-collection";
        var document1 = new Document
        {
            Id = "test-id-1",
            Data = new System.Collections.Generic.Dictionary<string, object> { { "name", "Test Document 1" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        var document2 = new Document
        {
            Id = "test-id-2",
            Data = new System.Collections.Generic.Dictionary<string, object> { { "name", "Test Document 2" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        await _storageManager.SaveDocumentAsync(collectionName, document1);
        await _storageManager.SaveDocumentAsync(collectionName, document2);

        // Act
        var documents = await _storageManager.ListDocumentsAsync(collectionName);

        // Assert
        Assert.Contains("test-id-1", documents);
        Assert.Contains("test-id-2", documents);
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testBasePath))
        {
            Directory.Delete(_testBasePath, true);
        }
    }
}