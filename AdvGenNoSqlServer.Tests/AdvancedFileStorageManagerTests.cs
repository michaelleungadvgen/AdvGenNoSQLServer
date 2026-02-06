using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage.Storage;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class AdvancedFileStorageManagerTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly AdvancedFileStorageManager _storageManager;

    public AdvancedFileStorageManagerTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"AdvancedFileStorageTest_{Guid.NewGuid()}");
        _storageManager = new AdvancedFileStorageManager(_testBasePath, TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task AdvancedFileStorageManager_SaveAndLoadDocument_ReturnsDocument()
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
        Assert.Equal(document.Version, result.Version);
        
        // Compare dictionary contents
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.True(result.Data.ContainsKey("name"));
        Assert.Equal("Test Document", result.Data["name"].ToString());
    }

    [Fact]
    public async Task AdvancedFileStorageManager_ConcurrentAccess_WorksCorrectly()
    {
        // Arrange
        var collectionName = "test-collection";
        var tasks = new Task[10];

        // Act
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                var document = new Document
                {
                    Id = $"test-id-{index}",
                    Data = new System.Collections.Generic.Dictionary<string, object> { { "name", $"Test Document {index}" } },
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Version = 1
                };

                await _storageManager.SaveDocumentAsync(collectionName, document);
                var result = await _storageManager.LoadDocumentAsync(collectionName, document.Id);
                Assert.NotNull(result);
                Assert.Equal(document.Id, result.Id);
                
                // Compare dictionary contents
                Assert.NotNull(result.Data);
                Assert.Single(result.Data);
                Assert.True(result.Data.ContainsKey("name"));
                Assert.Equal($"Test Document {index}", result.Data["name"].ToString());
            });
        }

        await Task.WhenAll(tasks);

        // Assert
        var documents = await _storageManager.ListDocumentsAsync(collectionName);
        Assert.Equal(10, documents.Count());
    }

    public void Dispose()
    {
        _storageManager.Dispose();

        // Clean up test directory
        if (Directory.Exists(_testBasePath))
        {
            Directory.Delete(_testBasePath, true);
        }
    }
}