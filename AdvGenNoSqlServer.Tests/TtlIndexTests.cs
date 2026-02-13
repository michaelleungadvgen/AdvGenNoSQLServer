// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Indexing;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for TTL (Time-To-Live) index functionality
/// </summary>
public class TtlIndexTests
{
    private static Document CreateDocument(string id, Dictionary<string, object>? data = null, DateTime? createdAt = null)
    {
        return new Document
        {
            Id = id,
            Data = data ?? new Dictionary<string, object>(),
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };
    }

    #region TTL Index Configuration Tests

    [Fact]
    public void TtlIndexConfiguration_CreateWithRequiredProperties_ShouldSucceed()
    {
        // Act
        var config = new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        };

        // Assert
        Assert.Equal("test_collection", config.CollectionName);
        Assert.Equal("expireAt", config.ExpireAfterField);
        Assert.Null(config.DefaultExpireAfter);
        Assert.False(config.ImmediateDeletion);
        Assert.Equal(TimeSpan.FromMinutes(1), config.CleanupInterval);
    }

    [Fact]
    public void TtlIndexConfiguration_CreateWithAllProperties_ShouldSucceed()
    {
        // Act
        var config = new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt",
            DefaultExpireAfter = TimeSpan.FromHours(24),
            ImmediateDeletion = true,
            CleanupInterval = TimeSpan.FromMinutes(5)
        };

        // Assert
        Assert.Equal("test_collection", config.CollectionName);
        Assert.Equal("expireAt", config.ExpireAfterField);
        Assert.Equal(TimeSpan.FromHours(24), config.DefaultExpireAfter);
        Assert.True(config.ImmediateDeletion);
        Assert.Equal(TimeSpan.FromMinutes(5), config.CleanupInterval);
    }

    #endregion

    #region TTL Index Service Tests

    [Fact]
    public void TtlIndexService_CreateTtlIndex_ShouldSucceed()
    {
        // Arrange
        using var service = new TtlIndexService();
        var config = new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        };

        // Act
        service.CreateTtlIndex(config);

        // Assert
        Assert.True(service.HasTtlIndex("test_collection"));
    }

    [Fact]
    public void TtlIndexService_CreateTtlIndex_WithDuplicateCollection_ShouldOverwrite()
    {
        // Arrange
        using var service = new TtlIndexService();
        var config1 = new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        };
        var config2 = new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expiresAt",
            DefaultExpireAfter = TimeSpan.FromHours(1)
        };

        // Act
        service.CreateTtlIndex(config1);
        service.CreateTtlIndex(config2);

        // Assert
        var retrieved = service.GetTtlIndexConfiguration("test_collection");
        Assert.NotNull(retrieved);
        Assert.Equal("expiresAt", retrieved.ExpireAfterField);
    }

    [Fact]
    public void TtlIndexService_DropTtlIndex_ExistingIndex_ShouldReturnTrue()
    {
        // Arrange
        using var service = new TtlIndexService();
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        // Act
        var result = service.DropTtlIndex("test_collection");

        // Assert
        Assert.True(result);
        Assert.False(service.HasTtlIndex("test_collection"));
    }

    [Fact]
    public void TtlIndexService_DropTtlIndex_NonExistingIndex_ShouldReturnFalse()
    {
        // Arrange
        using var service = new TtlIndexService();

        // Act
        var result = service.DropTtlIndex("nonexistent_collection");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TtlIndexService_GetTtlIndexConfiguration_ExistingIndex_ShouldReturnConfig()
    {
        // Arrange
        using var service = new TtlIndexService();
        var config = new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt",
            DefaultExpireAfter = TimeSpan.FromDays(30)
        };
        service.CreateTtlIndex(config);

        // Act
        var retrieved = service.GetTtlIndexConfiguration("test_collection");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("test_collection", retrieved.CollectionName);
        Assert.Equal("expireAt", retrieved.ExpireAfterField);
        Assert.Equal(TimeSpan.FromDays(30), retrieved.DefaultExpireAfter);
    }

    [Fact]
    public void TtlIndexService_GetTtlIndexConfiguration_NonExistingIndex_ShouldReturnNull()
    {
        // Arrange
        using var service = new TtlIndexService();

        // Act
        var retrieved = service.GetTtlIndexConfiguration("nonexistent_collection");

        // Assert
        Assert.Null(retrieved);
    }

    #endregion

    #region Document Registration Tests

    [Fact]
    public void TtlIndexService_RegisterDocument_WithExpireField_ShouldTrackDocument()
    {
        // Arrange
        using var service = new TtlIndexService();
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        var doc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddHours(1)
        });

        // Act
        service.RegisterDocument("test_collection", doc);

        // Assert
        var stats = service.GetStatistics();
        Assert.Equal(1, stats.DocumentsTracked);
    }

    [Fact]
    public void TtlIndexService_RegisterDocument_WithDefaultExpiration_ShouldUseDefault()
    {
        // Arrange
        using var service = new TtlIndexService();
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt",
            DefaultExpireAfter = TimeSpan.FromHours(1)
        });

        var doc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["name"] = "test"
        });

        // Act
        service.RegisterDocument("test_collection", doc);

        // Assert
        var stats = service.GetStatistics();
        Assert.Equal(1, stats.DocumentsTracked);
    }

    [Fact]
    public void TtlIndexService_RegisterDocument_NoTtlIndex_ShouldNotTrack()
    {
        // Arrange
        using var service = new TtlIndexService();

        var doc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddHours(1)
        });

        // Act
        service.RegisterDocument("test_collection", doc);

        // Assert
        var stats = service.GetStatistics();
        Assert.Equal(0, stats.DocumentsTracked);
    }

    [Fact]
    public void TtlIndexService_UnregisterDocument_TrackedDocument_ShouldRemove()
    {
        // Arrange
        using var service = new TtlIndexService();
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        var doc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddHours(1)
        });
        service.RegisterDocument("test_collection", doc);

        // Act
        service.UnregisterDocument("test_collection", "test_doc");

        // Assert
        var stats = service.GetStatistics();
        Assert.Equal(0, stats.DocumentsTracked);
    }

    [Fact]
    public void TtlIndexService_RegisterDocument_UpdateExisting_ShouldUpdateExpiration()
    {
        // Arrange
        using var service = new TtlIndexService();
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        var doc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddHours(1)
        });
        service.RegisterDocument("test_collection", doc);

        var updatedDoc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddHours(2),
            ["updated"] = true
        });

        // Act
        service.RegisterDocument("test_collection", updatedDoc);

        // Assert
        var stats = service.GetStatistics();
        Assert.Equal(1, stats.DocumentsTracked);
    }

    #endregion

    #region Cleanup Tests

    [Fact]
    public async Task TtlIndexService_CleanupExpiredDocuments_WithExpiredDocuments_ShouldRemove()
    {
        // Arrange
        var deletedDocs = new List<(string collection, string docId)>();
        using var service = new TtlIndexService(async (collection, docId) =>
        {
            deletedDocs.Add((collection, docId));
            return await Task.FromResult(true);
        });

        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        var expiredDoc = CreateDocument("expired_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddMinutes(-1)
        });
        service.RegisterDocument("test_collection", expiredDoc);

        // Act
        var expiredCount = await service.CleanupExpiredDocumentsAsync();

        // Assert
        Assert.Equal(1, expiredCount);
        Assert.Single(deletedDocs);
        Assert.Equal("test_collection", deletedDocs[0].collection);
        Assert.Equal("expired_doc", deletedDocs[0].docId);

        var stats = service.GetStatistics();
        Assert.Equal(1, stats.DocumentsExpired);
        Assert.True(stats.CleanupRuns > 0);
    }

    [Fact]
    public async Task TtlIndexService_CleanupExpiredDocuments_NoExpiredDocuments_ShouldRemoveNone()
    {
        // Arrange
        using var service = new TtlIndexService();
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        var futureDoc = CreateDocument("future_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddHours(1)
        });
        service.RegisterDocument("test_collection", futureDoc);

        // Act
        var expiredCount = await service.CleanupExpiredDocumentsAsync();

        // Assert
        Assert.Equal(0, expiredCount);
        var stats = service.GetStatistics();
        Assert.Equal(1, stats.DocumentsTracked);
    }

    [Fact]
    public async Task TtlIndexService_CleanupExpiredDocuments_MultipleCollections_ShouldHandleAll()
    {
        // Arrange
        var deletedDocs = new List<(string collection, string docId)>();
        using var service = new TtlIndexService(async (collection, docId) =>
        {
            deletedDocs.Add((collection, docId));
            return await Task.FromResult(true);
        });

        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "collection1",
            ExpireAfterField = "expireAt"
        });
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "collection2",
            ExpireAfterField = "expireAt"
        });

        var expiredDoc1 = CreateDocument("expired1", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddMinutes(-10)
        });
        var expiredDoc2 = CreateDocument("expired2", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddMinutes(-5)
        });

        service.RegisterDocument("collection1", expiredDoc1);
        service.RegisterDocument("collection2", expiredDoc2);

        // Act
        var expiredCount = await service.CleanupExpiredDocumentsAsync();

        // Assert
        Assert.Equal(2, expiredCount);
        Assert.Equal(2, deletedDocs.Count);
    }

    #endregion

    #region Event Tests

    [Fact]
    public async Task TtlIndexService_CleanupExpiredDocuments_ShouldRaiseEvent()
    {
        // Arrange
        using var service = new TtlIndexService(async (_, __) => await Task.FromResult(true));
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        DocumentsExpiredEventArgs? capturedEvent = null;
        service.DocumentsExpired += (sender, args) =>
        {
            capturedEvent = args;
        };

        var expiredDoc = CreateDocument("expired_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddMinutes(-1)
        });
        service.RegisterDocument("test_collection", expiredDoc);

        // Act
        await service.CleanupExpiredDocumentsAsync();

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal("test_collection", capturedEvent.CollectionName);
        Assert.Single(capturedEvent.DocumentIds);
        Assert.Equal("expired_doc", capturedEvent.DocumentIds[0]);
        Assert.True(capturedEvent.ExpirationTime <= DateTime.UtcNow);
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public void TtlIndexService_GetStatistics_InitialState_ShouldReturnZeroedStats()
    {
        // Arrange
        using var service = new TtlIndexService();

        // Act
        var stats = service.GetStatistics();

        // Assert
        Assert.Equal(0, stats.DocumentsExpired);
        Assert.Equal(0, stats.DocumentsTracked);
        Assert.Equal(0, stats.CleanupRuns);
        Assert.Equal(0, stats.AverageCleanupTimeMs);
        Assert.Equal(DateTime.MinValue, stats.LastCleanupTime);
    }

    [Fact]
    public async Task TtlIndexService_GetStatistics_AfterCleanup_ShouldReturnUpdatedStats()
    {
        // Arrange
        using var service = new TtlIndexService(async (_, __) => await Task.FromResult(true));
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        var expiredDoc = CreateDocument("expired_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddMinutes(-1)
        });
        service.RegisterDocument("test_collection", expiredDoc);
        await service.CleanupExpiredDocumentsAsync();

        // Act
        var stats = service.GetStatistics();

        // Assert
        Assert.Equal(1, stats.DocumentsExpired);
        Assert.Equal(0, stats.DocumentsTracked); // Document was cleaned up
        Assert.Equal(1, stats.CleanupRuns);
        Assert.True(stats.AverageCleanupTimeMs >= 0);
        Assert.True(stats.LastCleanupTime > DateTime.MinValue);
    }

    #endregion

    #region Start/Stop Tests

    [Fact]
    public async Task TtlIndexService_StartAsync_ShouldNotThrow()
    {
        // Arrange
        using var service = new TtlIndexService();
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        // Act & Assert
        await service.StartAsync();
        await service.StopAsync();
    }

    [Fact]
    public async Task TtlIndexService_StartAsync_AlreadyRunning_ShouldNotThrow()
    {
        // Arrange
        using var service = new TtlIndexService();
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        // Act & Assert
        await service.StartAsync();
        await service.StartAsync(); // Should not throw
        await service.StopAsync();
    }

    #endregion

    #region TtlDocumentStore Tests

    [Fact]
    public void TtlDocumentStore_Create_WithInnerStore_ShouldSucceed()
    {
        // Arrange
        var innerStore = new DocumentStore();

        // Act
        using var ttlStore = new TtlDocumentStore(innerStore);

        // Assert
        Assert.NotNull(ttlStore);
        Assert.Equal(innerStore, ttlStore.InnerStore);
    }

    [Fact]
    public void TtlDocumentStore_CreateTtlIndex_ShouldCreateIndex()
    {
        // Arrange
        var innerStore = new DocumentStore();
        using var ttlStore = new TtlDocumentStore(innerStore);

        // Act
        ttlStore.CreateTtlIndex("test_collection", "expireAt", TimeSpan.FromHours(1));

        // Assert
        Assert.True(ttlStore.TtlService.HasTtlIndex("test_collection"));
    }

    [Fact]
    public async Task TtlDocumentStore_InsertAsync_ShouldRegisterForTtl()
    {
        // Arrange
        var innerStore = new DocumentStore();
        using var ttlStore = new TtlDocumentStore(innerStore);
        ttlStore.CreateTtlIndex("test_collection", "expireAt");

        var doc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddHours(1)
        });

        // Act
        await ttlStore.InsertAsync("test_collection", doc);

        // Assert
        var stats = ttlStore.GetTtlStatistics();
        Assert.Equal(1, stats.DocumentsTracked);
    }

    [Fact]
    public async Task TtlDocumentStore_UpdateAsync_ShouldReRegisterForTtl()
    {
        // Arrange
        var innerStore = new DocumentStore();
        await innerStore.CreateCollectionAsync("test_collection");
        using var ttlStore = new TtlDocumentStore(innerStore);
        ttlStore.CreateTtlIndex("test_collection", "expireAt");

        var doc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddHours(1)
        });
        await ttlStore.InsertAsync("test_collection", doc);

        var updatedDoc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddHours(2),
            ["updated"] = true
        });

        // Act
        await ttlStore.UpdateAsync("test_collection", updatedDoc);

        // Assert
        var stats = ttlStore.GetTtlStatistics();
        Assert.Equal(1, stats.DocumentsTracked);
    }

    [Fact]
    public async Task TtlDocumentStore_DeleteAsync_ShouldUnregisterFromTtl()
    {
        // Arrange
        var innerStore = new DocumentStore();
        await innerStore.CreateCollectionAsync("test_collection");
        using var ttlStore = new TtlDocumentStore(innerStore);
        ttlStore.CreateTtlIndex("test_collection", "expireAt");

        var doc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddHours(1)
        });
        await ttlStore.InsertAsync("test_collection", doc);

        // Act
        await ttlStore.DeleteAsync("test_collection", "test_doc");

        // Assert
        var stats = ttlStore.GetTtlStatistics();
        Assert.Equal(0, stats.DocumentsTracked);
    }

    [Fact]
    public async Task TtlDocumentStore_DropCollectionAsync_ShouldDropTtlIndex()
    {
        // Arrange
        var innerStore = new DocumentStore();
        await innerStore.CreateCollectionAsync("test_collection");
        using var ttlStore = new TtlDocumentStore(innerStore);
        ttlStore.CreateTtlIndex("test_collection", "expireAt");

        // Act
        await ttlStore.DropCollectionAsync("test_collection");

        // Assert
        Assert.False(ttlStore.TtlService.HasTtlIndex("test_collection"));
    }

    [Fact]
    public async Task TtlDocumentStore_CleanupExpiredDocumentsAsync_ShouldRemoveExpired()
    {
        // Arrange
        var innerStore = new DocumentStore();
        await innerStore.CreateCollectionAsync("test_collection");
        using var ttlStore = new TtlDocumentStore(innerStore);
        ttlStore.CreateTtlIndex("test_collection", "expireAt");

        var expiredDoc = CreateDocument("expired_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddMinutes(-1)
        });
        await ttlStore.InsertAsync("test_collection", expiredDoc);

        // Act
        var cleanedCount = await ttlStore.CleanupExpiredDocumentsAsync();

        // Assert
        Assert.Equal(1, cleanedCount);
        Assert.False(await ttlStore.ExistsAsync("test_collection", "expired_doc"));
    }

    [Fact]
    public void TtlDocumentStore_Dispose_ShouldDisposeTtlService()
    {
        // Arrange
        var innerStore = new DocumentStore();
        var ttlStore = new TtlDocumentStore(innerStore);

        // Act
        ttlStore.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => ttlStore.InsertAsync("test", CreateDocument("1")).GetAwaiter().GetResult());
    }

    #endregion

    #region Timestamp Format Tests

    [Fact]
    public void TtlIndexService_RegisterDocument_WithUnixTimestamp_ShouldParse()
    {
        // Arrange
        using var service = new TtlIndexService();
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        var futureTimestamp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        var doc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = futureTimestamp
        });

        // Act
        service.RegisterDocument("test_collection", doc);

        // Assert
        var stats = service.GetStatistics();
        Assert.Equal(1, stats.DocumentsTracked);
    }

    [Fact]
    public void TtlIndexService_RegisterDocument_WithInvalidFormat_ShouldNotTrack()
    {
        // Arrange
        using var service = new TtlIndexService();
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        var doc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = "invalid-date"
        });

        // Act
        service.RegisterDocument("test_collection", doc);

        // Assert
        var stats = service.GetStatistics();
        Assert.Equal(0, stats.DocumentsTracked);
    }

    [Fact]
    public void TtlIndexService_RegisterDocument_WithDateTimeObject_ShouldTrack()
    {
        // Arrange
        using var service = new TtlIndexService();
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        var doc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTime.UtcNow.AddHours(1)
        });

        // Act
        service.RegisterDocument("test_collection", doc);

        // Assert
        var stats = service.GetStatistics();
        Assert.Equal(1, stats.DocumentsTracked);
    }

    [Fact]
    public void TtlIndexService_RegisterDocument_WithDateTimeOffset_ShouldTrack()
    {
        // Arrange
        using var service = new TtlIndexService();
        service.CreateTtlIndex(new TtlIndexConfiguration
        {
            CollectionName = "test_collection",
            ExpireAfterField = "expireAt"
        });

        var doc = CreateDocument("test_doc", new Dictionary<string, object>
        {
            ["expireAt"] = DateTimeOffset.UtcNow.AddHours(1)
        });

        // Act
        service.RegisterDocument("test_collection", doc);

        // Assert
        var stats = service.GetStatistics();
        Assert.Equal(1, stats.DocumentsTracked);
    }

    #endregion
}
