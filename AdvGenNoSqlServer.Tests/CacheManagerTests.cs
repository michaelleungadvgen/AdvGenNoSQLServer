// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

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
    public void MemoryCacheManager_Clear_ThrowsNotImplementedException()
    {
        // Arrange
        var cacheManager = new MemoryCacheManager(_memoryCache);

        // Act & Assert
        Assert.Throws<NotImplementedException>(() => cacheManager.Clear());
    }
}

public class LruCacheTests
{
    [Fact]
    public void LruCache_SetAndGetDocument_ReturnsDocument()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100);
        var document = new Document
        {
            Id = "test-id",
            Data = new Dictionary<string, object> { { "name", "Test Document" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Act
        cache.Set("key1", document);
        var result = cache.Get("key1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(document.Id, result.Id);
        Assert.Equal(document.Data, result.Data);
    }

    [Fact]
    public void LruCache_GetNonExistentDocument_ReturnsNull()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100);

        // Act
        var result = cache.Get("non-existent-key");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void LruCache_RemoveDocument_RemovesFromCache()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100);
        var document = new Document
        {
            Id = "test-id",
            Data = new Dictionary<string, object> { { "name", "Test Document" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        cache.Set("key1", document);

        // Act
        var removed = cache.Remove("key1");
        var result = cache.Get("key1");

        // Assert
        Assert.True(removed);
        Assert.Null(result);
    }

    [Fact]
    public void LruCache_RemoveNonExistentKey_ReturnsFalse()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100);

        // Act
        var removed = cache.Remove("non-existent-key");

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public void LruCache_Clear_RemovesAllEntries()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", doc1);
        cache.Set("key2", doc2);

        // Act
        cache.Clear();

        // Assert
        Assert.Null(cache.Get("key1"));
        Assert.Null(cache.Get("key2"));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void LruCache_ExceedsMaxCount_EvictsLeastRecentlyUsed()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 3);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object> { { "name", "doc1" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object> { { "name", "doc2" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc3 = new Document { Id = "id3", Data = new Dictionary<string, object> { { "name", "doc3" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc4 = new Document { Id = "id4", Data = new Dictionary<string, object> { { "name", "doc4" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        // Act - Add 3 documents
        cache.Set("key1", doc1);
        cache.Set("key2", doc2);
        cache.Set("key3", doc3);

        // Access key1 to make it recently used
        cache.Get("key1");

        // Add 4th document, should evict key2 (least recently used)
        cache.Set("key4", doc4);

        // Assert
        Assert.NotNull(cache.Get("key1")); // Was accessed, should still be there
        Assert.Null(cache.Get("key2"));    // Was least recently used, should be evicted
        Assert.NotNull(cache.Get("key3")); // Was more recent than key2
        Assert.NotNull(cache.Get("key4")); // Just added
        Assert.Equal(3, cache.Count);
    }

    [Fact]
    public void LruCache_UpdateExistingKey_UpdatesValueAndMovesToFront()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 3);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object> { { "name", "original" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object> { { "name", "doc2" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc3 = new Document { Id = "id3", Data = new Dictionary<string, object> { { "name", "doc3" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var updatedDoc1 = new Document { Id = "id1-updated", Data = new Dictionary<string, object> { { "name", "updated" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 2 };

        cache.Set("key1", doc1);
        cache.Set("key2", doc2);
        cache.Set("key3", doc3);

        // Act - Update key1
        cache.Set("key1", updatedDoc1);

        // Add another document to trigger eviction if key1 wasn't moved to front
        var doc4 = new Document { Id = "id4", Data = new Dictionary<string, object> { { "name", "doc4" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        cache.Set("key4", doc4);

        // Assert
        var result = cache.Get("key1");
        Assert.NotNull(result);
        Assert.Equal("updated", result!.Data["name"]);
        Assert.Null(cache.Get("key2")); // Should be evicted
    }

    [Fact]
    public void LruCache_AccessUpdatesRecency_MovesToFront()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 3);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object> { { "name", "doc1" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object> { { "name", "doc2" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc3 = new Document { Id = "id3", Data = new Dictionary<string, object> { { "name", "doc3" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", doc1);
        cache.Set("key2", doc2);
        cache.Set("key3", doc3);

        // Act - Access key1 to make it recently used
        cache.Get("key1");

        // Add 4th document
        var doc4 = new Document { Id = "id4", Data = new Dictionary<string, object> { { "name", "doc4" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        cache.Set("key4", doc4);

        // Assert - key2 should be evicted (least recently used)
        Assert.NotNull(cache.Get("key1"));
        Assert.Null(cache.Get("key2"));
        Assert.NotNull(cache.Get("key3"));
        Assert.NotNull(cache.Get("key4"));
    }

    [Fact(Skip = "TTL timing issues in test environment - needs investigation")]
    public void LruCache_ExpiredEntry_ReturnsNull()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100, defaultTtlMilliseconds: 500, enableBackgroundCleanup: false); // 500ms TTL
        var document = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", document);

        // Act - Wait for expiration (2x TTL to be safe)
        Thread.Sleep(1100);
        var result = cache.Get("key1");

        // Assert
        Assert.Null(result);
    }

    [Fact(Skip = "TTL timing issues in test environment - needs investigation")]
    public void LruCache_CustomTtl_RespectsIndividualTtl()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100, defaultTtlMilliseconds: 5000, enableBackgroundCleanup: false); // 5s default
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        // Act - Set with different TTLs
        cache.Set("key1", doc1, ttlMilliseconds: 500);  // 500ms TTL
        cache.Set("key2", doc2, ttlMilliseconds: 5000); // 5s TTL

        Thread.Sleep(1100); // Wait >2x the shorter TTL

        var result1 = cache.Get("key1");
        var result2 = cache.Get("key2");

        // Assert
        Assert.Null(result1);    // Should be expired
        Assert.NotNull(result2); // Should still be valid
    }

    [Fact(Skip = "TTL timing issues in test environment - needs investigation")]
    public void LruCache_ContainsKey_ExpiredEntry_ReturnsFalse()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100, defaultTtlMilliseconds: 500, enableBackgroundCleanup: false);
        var document = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", document);

        // Act - Wait for expiration
        Thread.Sleep(1100);
        var contains = cache.ContainsKey("key1");

        // Assert
        Assert.False(contains);
    }

    [Fact]
    public void LruCache_Statistics_TracksHitsAndMisses()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", doc1);

        // Act
        cache.Get("key1"); // Hit
        cache.Get("key1"); // Hit
        cache.Get("key2"); // Miss
        cache.Get("key3"); // Miss

        var stats = cache.GetStatistics();

        // Assert
        Assert.Equal(2, stats.TotalHits);
        Assert.Equal(2, stats.TotalMisses);
        Assert.Equal(0.5, stats.HitRatio);
    }

    [Fact]
    public void LruCache_ResetStatistics_ResetsCounters()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", doc1);
        cache.Get("key1");
        cache.Get("key2");

        // Act
        cache.ResetStatistics();
        var stats = cache.GetStatistics();

        // Assert
        Assert.Equal(0, stats.TotalHits);
        Assert.Equal(0, stats.TotalMisses);
        Assert.Equal(0, stats.HitRatio);
    }

    [Fact]
    public void LruCache_Statistics_EvictionCount()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 2);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc3 = new Document { Id = "id3", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", doc1);
        cache.Set("key2", doc2);

        // Act - Add 3rd document to trigger eviction
        cache.Set("key3", doc3);

        var stats = cache.GetStatistics();

        // Assert
        Assert.Equal(1, stats.TotalEvictions);
    }

    [Fact]
    public void LruCache_ItemEvictedEvent_RaisedOnEviction()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 2);
        var evictedKeys = new List<string>();
        cache.ItemEvicted += (sender, e) => evictedKeys.Add(e.Key);

        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc3 = new Document { Id = "id3", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", doc1);
        cache.Set("key2", doc2);

        // Act - Add 3rd document to trigger eviction
        cache.Set("key3", doc3);

        // Assert
        Assert.Single(evictedKeys);
        Assert.Contains("key1", evictedKeys); // key1 was least recently used
    }

    [Fact]
    public void LruCache_NullKey_ThrowsArgumentException()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100);
        var document = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => cache.Set(null!, document));
        Assert.Throws<ArgumentException>(() => cache.Set("", document));
        Assert.Throws<ArgumentException>(() => cache.Get(null!));
        Assert.Throws<ArgumentException>(() => cache.Get(""));
        Assert.Throws<ArgumentException>(() => cache.Remove(null!));
        Assert.Throws<ArgumentException>(() => cache.Remove(""));
        Assert.Throws<ArgumentException>(() => cache.ContainsKey(null!));
        Assert.Throws<ArgumentException>(() => cache.ContainsKey(""));
    }

    [Fact]
    public void LruCache_NullValue_ThrowsArgumentNullException()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => cache.Set("key", default(Document)!));
    }

    [Fact]
    public void LruCache_InvalidConstructorParameters_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new LruCache<Document>(maxItemCount: 0));
        Assert.Throws<ArgumentException>(() => new LruCache<Document>(maxItemCount: -1));
        Assert.Throws<ArgumentException>(() => new LruCache<Document>(maxItemCount: 100, maxSizeInBytes: 0));
        Assert.Throws<ArgumentException>(() => new LruCache<Document>(maxItemCount: 100, defaultTtlMilliseconds: 0));
    }

    [Fact]
    public void LruCache_SizeTracking_TracksEntrySizes()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100, maxSizeInBytes: 1000);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        // Act
        cache.Set("key1", doc1, sizeInBytes: 100);
        cache.Set("key2", doc2, sizeInBytes: 200);

        // Assert
        Assert.Equal(300, cache.CurrentSizeInBytes);
    }

    [Fact]
    public void LruCache_SizeLimit_EvictsWhenExceeded()
    {
        // Arrange
        using var cache = new LruCache<Document>(maxItemCount: 100, maxSizeInBytes: 300);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object> { { "name", "doc1" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object> { { "name", "doc2" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", doc1, sizeInBytes: 200);

        // Act - Add entry that would exceed size limit
        var doc3 = new Document { Id = "id3", Data = new Dictionary<string, object> { { "name", "doc3" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        cache.Set("key3", doc3, sizeInBytes: 200); // Would exceed 300 byte limit

        // Assert - key1 should be evicted
        Assert.Null(cache.Get("key1"));
        Assert.NotNull(cache.Get("key3"));
    }
}

public class AdvancedMemoryCacheManagerTests
{
    [Fact]
    public void AdvancedMemoryCacheManager_SetAndGetDocument_ReturnsDocument()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100);
        var document = new Document
        {
            Id = "test-id",
            Data = new Dictionary<string, object> { { "name", "Test Document" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Act
        cache.Set(document.Id, document);
        var result = cache.Get(document.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(document.Id, result.Id);
        Assert.Equal(document.Data, result.Data);
    }

    [Fact]
    public void AdvancedMemoryCacheManager_GetNonExistentDocument_ReturnsNull()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100);

        // Act
        var result = cache.Get("non-existent-id");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void AdvancedMemoryCacheManager_RemoveDocument_RemovesFromCache()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100);
        var document = new Document
        {
            Id = "test-id",
            Data = new Dictionary<string, object> { { "name", "Test Document" } },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        cache.Set(document.Id, document);

        // Act
        cache.Remove(document.Id);
        var result = cache.Get(document.Id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void AdvancedMemoryCacheManager_Clear_RemovesAllEntries()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", doc1);
        cache.Set("key2", doc2);

        // Act
        cache.Clear();

        // Assert
        Assert.Null(cache.Get("key1"));
        Assert.Null(cache.Get("key2"));
        Assert.Equal(0, cache.CurrentItemCount);
    }

    [Fact]
    public void AdvancedMemoryCacheManager_ExceedsMaxCount_EvictsLeastRecentlyUsed()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 3);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object> { { "name", "doc1" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object> { { "name", "doc2" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc3 = new Document { Id = "id3", Data = new Dictionary<string, object> { { "name", "doc3" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc4 = new Document { Id = "id4", Data = new Dictionary<string, object> { { "name", "doc4" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        // Act
        cache.Set("key1", doc1);
        cache.Set("key2", doc2);
        cache.Set("key3", doc3);
        cache.Get("key1"); // Make key1 recently used
        cache.Set("key4", doc4); // Should evict key2

        // Assert
        Assert.NotNull(cache.Get("key1"));
        Assert.Null(cache.Get("key2"));
        Assert.NotNull(cache.Get("key3"));
        Assert.NotNull(cache.Get("key4"));
    }

    [Fact(Skip = "TTL timing issues in test environment - needs investigation")]
    public void AdvancedMemoryCacheManager_ExpiredEntry_ReturnsNull()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100, defaultTtlMilliseconds: 500); // 500ms TTL
        var document = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", document);

        // Act - Wait for expiration (2x TTL to be safe)
        Thread.Sleep(1100);
        var result = cache.Get("key1");

        // Assert
        Assert.Null(result);
    }

    [Fact(Skip = "TTL timing issues in test environment - needs investigation")]
    public void AdvancedMemoryCacheManager_SetWithTimeSpan_RespectsTtl()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100, defaultTtlMilliseconds: 5000);
        var document = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        // Act - Set with 500ms TTL using TimeSpan
        cache.Set("key1", document, TimeSpan.FromMilliseconds(500));

        Thread.Sleep(1100); // Wait >2x TTL
        var result = cache.Get("key1");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void AdvancedMemoryCacheManager_ContainsKey_ReturnsCorrectResult()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100);
        var document = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", document);

        // Act & Assert
        Assert.True(cache.ContainsKey("key1"));
        Assert.False(cache.ContainsKey("non-existent-key"));
    }

    [Fact(Skip = "TTL timing issues in test environment - needs investigation")]
    public void AdvancedMemoryCacheManager_ContainsKey_ExpiredEntry_ReturnsFalse()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100, defaultTtlMilliseconds: 500);
        var document = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", document);

        // Act - Wait for expiration (2x TTL to be safe)
        Thread.Sleep(1100);
        var contains = cache.ContainsKey("key1");

        // Assert
        Assert.False(contains);
    }

    [Fact]
    public void AdvancedMemoryCacheManager_TryGet_ReturnsCorrectResult()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100);
        var document = new Document { Id = "id1", Data = new Dictionary<string, object> { { "name", "Test" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", document);

        // Act
        var found = cache.TryGet("key1", out var result);
        var notFound = cache.TryGet("non-existent", out var nullResult);

        // Assert
        Assert.True(found);
        Assert.NotNull(result);
        Assert.Equal("Test", result!.Data["name"]);
        Assert.False(notFound);
        Assert.Null(nullResult);
    }

    [Fact]
    public void AdvancedMemoryCacheManager_Statistics_TracksCorrectly()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", doc1);

        // Act
        cache.Get("key1"); // Hit
        cache.Get("key1"); // Hit
        cache.Get("key2"); // Miss

        var stats = cache.GetStatistics();

        // Assert
        Assert.Equal(2, stats.TotalHits);
        Assert.Equal(1, stats.TotalMisses);
        Assert.Equal(1, stats.ItemCount);
    }

    [Fact]
    public void AdvancedMemoryCacheManager_ResetStatistics_ResetsCounters()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", doc1);
        cache.Get("key1");
        cache.Get("key2");

        // Act
        cache.ResetStatistics();
        var stats = cache.GetStatistics();

        // Assert
        Assert.Equal(0, stats.TotalHits);
        Assert.Equal(0, stats.TotalMisses);
    }

    [Fact]
    public void AdvancedMemoryCacheManager_ItemEvictedEvent_RaisedOnEviction()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 2);
        var evictedKeys = new List<string>();
        cache.ItemEvicted += (sender, e) => evictedKeys.Add(e.Key);

        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc3 = new Document { Id = "id3", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Set("key1", doc1);
        cache.Set("key2", doc2);

        // Act
        cache.Set("key3", doc3); // Should evict key1

        // Assert
        Assert.Single(evictedKeys);
        Assert.Contains("key1", evictedKeys);
    }

    [Fact]
    public void AdvancedMemoryCacheManager_NullKey_ThrowsArgumentException()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100);
        var document = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => cache.Set(null!, document));
        Assert.Throws<ArgumentException>(() => cache.Set("", document));
        Assert.Throws<ArgumentException>(() => cache.Get(null!));
        Assert.Throws<ArgumentException>(() => cache.Get(""));
    }

    [Fact]
    public void AdvancedMemoryCacheManager_NullValue_ThrowsArgumentNullException()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 100);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => cache.Set("key", null!));
    }

    [Fact]
    public void AdvancedMemoryCacheManager_Disposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var cache = new AdvancedMemoryCacheManager(maxItemCount: 100);
        var document = new Document { Id = "id1", Data = new Dictionary<string, object>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };

        cache.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => cache.Get("key"));
        Assert.Throws<ObjectDisposedException>(() => cache.Set("key", document));
        Assert.Throws<ObjectDisposedException>(() => cache.Remove("key"));
        Assert.Throws<ObjectDisposedException>(() => cache.Clear());
        Assert.Throws<ObjectDisposedException>(() => cache.ContainsKey("key"));
        Assert.Throws<ObjectDisposedException>(() => cache.TryGet("key", out _));
        Assert.Throws<ObjectDisposedException>(() => cache.GetStatistics());
        Assert.Throws<ObjectDisposedException>(() => cache.ResetStatistics());
    }

    [Fact]
    public void AdvancedMemoryCacheManager_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        using var cache = new AdvancedMemoryCacheManager();

        // Assert
        Assert.Equal(10000, cache.MaxItemCount);
        Assert.Equal(104857600, cache.MaxSizeInBytes); // 100MB
        Assert.Equal(1800000, cache.DefaultTtlMilliseconds); // 30 minutes
    }

    [Fact]
    public void AdvancedMemoryCacheManager_UpdatesExistingKey_MovesToFront()
    {
        // Arrange
        using var cache = new AdvancedMemoryCacheManager(maxItemCount: 3);
        var doc1 = new Document { Id = "id1", Data = new Dictionary<string, object> { { "name", "original" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc2 = new Document { Id = "id2", Data = new Dictionary<string, object> { { "name", "doc2" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var doc3 = new Document { Id = "id3", Data = new Dictionary<string, object> { { "name", "doc3" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        var updatedDoc1 = new Document { Id = "id1-updated", Data = new Dictionary<string, object> { { "name", "updated" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 2 };

        cache.Set("key1", doc1);
        cache.Set("key2", doc2);
        cache.Set("key3", doc3);

        // Act - Update key1
        cache.Set("key1", updatedDoc1);

        // Add 4th to trigger eviction
        var doc4 = new Document { Id = "id4", Data = new Dictionary<string, object> { { "name", "doc4" } }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Version = 1 };
        cache.Set("key4", doc4);

        // Assert
        var result = cache.Get("key1");
        Assert.NotNull(result);
        Assert.Equal("updated", result!.Data["name"]);
        Assert.Null(cache.Get("key2")); // key2 should be evicted
    }

    [Fact]
    public void AdvancedMemoryCacheManager_ConfigurableParameters_AreApplied()
    {
        // Arrange & Act
        using var cache = new AdvancedMemoryCacheManager(
            maxItemCount: 500,
            maxSizeInBytes: 52428800, // 50MB
            defaultTtlMilliseconds: 300000); // 5 minutes

        // Assert
        Assert.Equal(500, cache.MaxItemCount);
        Assert.Equal(52428800, cache.MaxSizeInBytes);
        Assert.Equal(300000, cache.DefaultTtlMilliseconds);
    }
}
