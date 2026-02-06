using AdvGenNoSqlServer.Core.Caching;
using AdvGenNoSqlServer.Core.Models;
using Microsoft.Extensions.Caching.Memory;

namespace AdvGenNoSqlServer.Tests;

public class CacheManagerTests
{
    private readonly IMemoryCache _memoryCache;

    public CacheManagerTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
    }

    [Fact]
    public void MemoryCacheManager_SetAndGetDocument_ReturnsDocument()
    {
        // Arrange
        var cacheManager = new MemoryCacheManager(_memoryCache);
        var document = new Document
        {
            Id = "test-id",
            Data = new Dictionary<string, object> { { "name", "Test Document" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Act
        cacheManager.Set(document.Id, document);
        var result = cacheManager.Get(document.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(document.Id, result.Id);
        Assert.Equal(document.Data, result.Data);
    }

    [Fact]
    public void MemoryCacheManager_GetNonExistentDocument_ReturnsNull()
    {
        // Arrange
        var cacheManager = new MemoryCacheManager(_memoryCache);

        // Act
        var result = cacheManager.Get("non-existent-id");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void MemoryCacheManager_RemoveDocument_RemovesFromCache()
    {
        // Arrange
        var cacheManager = new MemoryCacheManager(_memoryCache);
        var document = new Document
        {
            Id = "test-id",
            Data = new Dictionary<string, object> { { "name", "Test Document" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        cacheManager.Set(document.Id, document);

        // Act
        cacheManager.Remove(document.Id);
        var result = cacheManager.Get(document.Id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void AdvancedMemoryCacheManager_Clear_RemovesAllEntries()
    {
        // Arrange
        var cacheManager = new AdvancedMemoryCacheManager(_memoryCache);
        var document1 = new Document
        {
            Id = "test-id-1",
            Data = new Dictionary<string, object> { { "name", "Test Document 1" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        var document2 = new Document
        {
            Id = "test-id-2",
            Data = new Dictionary<string, object> { { "name", "Test Document 2" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        cacheManager.Set(document1.Id, document1);
        cacheManager.Set(document2.Id, document2);

        // Act
        cacheManager.Clear();

        // Assert
        Assert.Null(cacheManager.Get(document1.Id));
        Assert.Null(cacheManager.Get(document2.Id));
    }
}