// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Sharding;
using AdvGenNoSqlServer.Storage.Sharding;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for sharding infrastructure.
/// </summary>
public class ShardingTests
{
    #region ShardKey Tests

    [Fact]
    public void ShardKey_Constructor_WithSingleFieldPath_ShouldCreateHashStrategy()
    {
        var key = new ShardKey("userId");
        
        Assert.Equal(ShardKeyStrategy.Hash, key.Strategy);
        Assert.Single(key.FieldPaths);
        Assert.Equal("userId", key.FieldPaths[0]);
    }

    [Fact]
    public void ShardKey_Constructor_WithStrategy_ShouldSetStrategy()
    {
        var key = new ShardKey("tenantId", ShardKeyStrategy.Range);
        
        Assert.Equal(ShardKeyStrategy.Range, key.Strategy);
    }

    [Fact]
    public void ShardKey_Constructor_WithOptions_ShouldValidate()
    {
        var options = new ShardKeyOptions
        {
            Strategy = ShardKeyStrategy.Hash,
            FieldPaths = new List<string> { "tenantId", "userId" }
        };
        
        var key = new ShardKey(options);
        
        Assert.Equal(ShardKeyStrategy.Hash, key.Strategy);
        Assert.Equal(2, key.FieldPaths.Count);
    }

    [Fact]
    public void ShardKey_Constructor_WithEmptyFieldPath_ShouldThrow()
    {
        var options = new ShardKeyOptions
        {
            FieldPaths = new List<string>()
        };
        
        Assert.Throws<ValidationException>(() => new ShardKey(options));
    }

    [Fact]
    public void ShardKey_ExtractKeyValue_WithSimpleField_ShouldReturnValue()
    {
        var key = new ShardKey("userId");
        var doc = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { ["userId"] = "user123" }
        };
        
        var value = key.ExtractKeyValue(doc);
        
        Assert.Equal("user123", value);
    }

    [Fact]
    public void ShardKey_ExtractKeyValue_WithNestedField_ShouldReturnValue()
    {
        var key = new ShardKey("profile.userId");
        var doc = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object>
            {
                ["profile"] = new Dictionary<string, object> { ["userId"] = "user456" }
            }
        };
        
        var value = key.ExtractKeyValue(doc);
        
        Assert.Equal("user456", value);
    }

    [Fact]
    public void ShardKey_ExtractKeyValue_WithCompositeKey_ShouldConcatenateValues()
    {
        var options = new ShardKeyOptions
        {
            Strategy = ShardKeyStrategy.Hash,
            FieldPaths = new List<string> { "tenantId", "userId" }
        };
        var key = new ShardKey(options);
        var doc = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object>
            {
                ["tenantId"] = "tenant1",
                ["userId"] = "user123"
            }
        };
        
        var value = key.ExtractKeyValue(doc);
        
        Assert.Equal("tenant1|user123", value);
    }

    [Fact]
    public void ShardKey_ComputeShardHash_SameValue_ShouldProduceSameHash()
    {
        var key = new ShardKey("userId");
        var doc1 = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { ["userId"] = "user123" }
        };
        var doc2 = new Document
        {
            Id = "doc2",
            Data = new Dictionary<string, object> { ["userId"] = "user123" }
        };
        
        var hash1 = key.ComputeShardHash(doc1);
        var hash2 = key.ComputeShardHash(doc2);
        
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShardKey_ComputeShardHash_DifferentValues_ShouldLikelyProduceDifferentHashes()
    {
        var key = new ShardKey("userId");
        var doc1 = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { ["userId"] = "user123" }
        };
        var doc2 = new Document
        {
            Id = "doc2",
            Data = new Dictionary<string, object> { ["userId"] = "user456" }
        };
        
        var hash1 = key.ComputeShardHash(doc1);
        var hash2 = key.ComputeShardHash(doc2);
        
        // Hashes should almost certainly be different for different values
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ShardKey_CreateHashKey_ShouldCreateHashStrategy()
    {
        var key = ShardKeyExtensions.CreateHashKey("userId");
        
        Assert.Equal(ShardKeyStrategy.Hash, key.Strategy);
        Assert.Equal("userId", key.FieldPaths[0]);
    }

    [Fact]
    public void ShardKey_CreateRangeKey_ShouldCreateRangeStrategy()
    {
        var key = ShardKeyExtensions.CreateRangeKey("timestamp");
        
        Assert.Equal(ShardKeyStrategy.Range, key.Strategy);
    }

    [Fact]
    public void ShardKey_CreateCompositeHashKey_ShouldCreateCompositeKey()
    {
        var key = ShardKeyExtensions.CreateCompositeHashKey("tenantId", "userId", "region");
        
        Assert.Equal(ShardKeyStrategy.Hash, key.Strategy);
        Assert.Equal(3, key.FieldPaths.Count);
    }

    #endregion

    #region ShardConfiguration Tests

    [Fact]
    public void ShardConfiguration_Validate_WithValidConfig_ShouldNotThrow()
    {
        var config = new ShardConfiguration
        {
            ClusterName = "test-cluster",
            Shards = new List<ShardNode>
            {
                new() { Name = "shard1", Host = "localhost", Port = 9091 },
                new() { Name = "shard2", Host = "localhost", Port = 9092 }
            }
        };
        
        config.Validate(); // Should not throw
    }

    [Fact]
    public void ShardConfiguration_Validate_WithEmptyClusterName_ShouldThrow()
    {
        var config = new ShardConfiguration
        {
            ClusterName = "",
            Shards = new List<ShardNode> { new() { Name = "shard1" } }
        };
        
        Assert.Throws<ValidationException>(() => config.Validate());
    }

    [Fact]
    public void ShardConfiguration_Validate_WithNoShards_ShouldThrow()
    {
        var config = new ShardConfiguration
        {
            ClusterName = "test-cluster",
            Shards = new List<ShardNode>()
        };
        
        Assert.Throws<ValidationException>(() => config.Validate());
    }

    [Fact]
    public void ShardConfiguration_Validate_WithDuplicateShardIds_ShouldThrow()
    {
        var config = new ShardConfiguration
        {
            ClusterName = "test-cluster",
            Shards = new List<ShardNode>
            {
                new() { ShardId = "same-id", Name = "shard1" },
                new() { ShardId = "same-id", Name = "shard2" }
            }
        };
        
        Assert.Throws<ValidationException>(() => config.Validate());
    }

    [Fact]
    public void ShardConfiguration_WithEqualRanges_ShouldDistributeEvenly()
    {
        var config = new ShardConfiguration
        {
            ClusterName = "test-cluster",
            Shards = new List<ShardNode>
            {
                new() { Name = "shard1" },
                new() { Name = "shard2" },
                new() { Name = "shard3" }
            }
        };
        
        config.WithEqualRanges();
        
        Assert.Equal(3, config.Ranges.Count);
        
        // Verify ranges cover entire int range
        Assert.Equal(int.MinValue, config.Ranges[0].MinHash);
        Assert.Equal(int.MaxValue, config.Ranges[2].MaxHash);
    }

    [Fact]
    public void ShardConfiguration_WithConsistentHashing_ShouldSetVirtualNodes()
    {
        var config = new ShardConfiguration();
        
        config.WithConsistentHashing(200);
        
        Assert.Equal(200, config.VirtualNodesPerShard);
        Assert.Empty(config.Ranges);
    }

    [Fact]
    public void ShardConfiguration_AddShard_ShouldAddShardToList()
    {
        var config = new ShardConfiguration { ClusterName = "test" };
        
        config.AddShard("shard1", "localhost", 9091, "tag1", "tag2");
        
        Assert.Single(config.Shards);
        Assert.Equal("shard1", config.Shards[0].Name);
        Assert.Equal("localhost", config.Shards[0].Host);
        Assert.Equal(9091, config.Shards[0].Port);
        Assert.Equal(2, config.Shards[0].Tags.Count);
    }

    #endregion

    #region ShardRouter Tests

    [Fact]
    public void ShardRouter_Constructor_WithValidConfig_ShouldInitialize()
    {
        var config = CreateTestConfiguration(3);
        
        var router = new ShardRouter(config);
        
        Assert.NotNull(router);
        Assert.Equal(config, router.Configuration);
    }

    [Fact]
    public void ShardRouter_RouteByHash_WithConsistentHashing_ShouldRouteToActiveShard()
    {
        var config = CreateTestConfiguration(3);
        config.WithConsistentHashing();
        var router = new ShardRouter(config);
        
        var shard = router.RouteByHash(12345);
        
        Assert.NotNull(shard);
        Assert.True(shard.IsActive);
    }

    [Fact]
    public void ShardRouter_RouteByHash_WithRangeSharding_ShouldRouteToCorrectShard()
    {
        var config = CreateTestConfiguration(3);
        config.WithEqualRanges();
        var router = new ShardRouter(config);
        
        // Route with hash in first range
        var shard1 = router.RouteByHash(int.MinValue);
        Assert.NotNull(shard1);
        
        // Route with hash in last range
        var shard2 = router.RouteByHash(int.MaxValue - 1);
        Assert.NotNull(shard2);
    }

    [Fact]
    public void ShardRouter_RouteDocument_WithHashStrategy_ShouldRouteConsistently()
    {
        var config = CreateTestConfiguration(3);
        var router = new ShardRouter(config);
        var shardKey = new ShardKey("userId");
        
        var doc = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { ["userId"] = "user123" }
        };
        
        var shard1 = router.RouteDocument(doc, shardKey);
        var shard2 = router.RouteDocument(doc, shardKey);
        
        Assert.Equal(shard1.ShardId, shard2.ShardId);
    }

    [Fact]
    public void ShardRouter_RouteByTag_WithMatchingTag_ShouldReturnTaggedShard()
    {
        var config = CreateTestConfiguration(3);
        config.Shards[0].Tags.Add("us-east");
        var router = new ShardRouter(config);
        
        var shard = router.RouteByTag("us-east");
        
        Assert.NotNull(shard);
        Assert.Contains("us-east", shard.Tags);
    }

    [Fact]
    public void ShardRouter_RouteByTag_WithNoMatch_ShouldReturnNull()
    {
        var config = CreateTestConfiguration(2);
        var router = new ShardRouter(config);
        
        var shard = router.RouteByTag("non-existent");
        
        Assert.Null(shard);
    }

    [Fact]
    public void ShardRouter_GetAllActiveShards_ShouldReturnOnlyActiveShards()
    {
        var config = CreateTestConfiguration(3);
        config.Shards[1].IsActive = false;
        var router = new ShardRouter(config);
        
        var shards = router.GetAllActiveShards();
        
        Assert.Equal(2, shards.Count);
        Assert.DoesNotContain(shards, s => s.ShardId == config.Shards[1].ShardId);
    }

    [Fact]
    public void ShardRouter_AddShard_ShouldAddNewShard()
    {
        var config = CreateTestConfiguration(2);
        var router = new ShardRouter(config);
        var newShard = new ShardNode { Name = "shard3", Host = "localhost", Port = 9093 };
        
        router.AddShard(newShard);
        
        var retrieved = router.RouteByShardId(newShard.ShardId);
        Assert.NotNull(retrieved);
        Assert.Equal("shard3", retrieved.Name);
    }

    [Fact]
    public void ShardRouter_RemoveShard_ShouldRemoveShard()
    {
        var config = CreateTestConfiguration(3);
        var router = new ShardRouter(config);
        var shardIdToRemove = config.Shards[1].ShardId;
        
        var removed = router.RemoveShard(shardIdToRemove);
        
        Assert.True(removed);
        var shard = router.RouteByShardId(shardIdToRemove);
        Assert.Null(shard);
    }

    [Fact]
    public void ShardRouter_RemoveShard_NonExistent_ShouldReturnFalse()
    {
        var config = CreateTestConfiguration(2);
        var router = new ShardRouter(config);
        
        var removed = router.RemoveShard("non-existent-id");
        
        Assert.False(removed);
    }

    [Fact]
    public void ShardRouter_UpdateShard_ShouldUpdateExistingShard()
    {
        var config = CreateTestConfiguration(2);
        var router = new ShardRouter(config);
        var shardId = config.Shards[0].ShardId;
        var updatedShard = new ShardNode
        {
            ShardId = shardId,
            Name = "updated-name",
            Host = "new-host",
            Port = 9999
        };
        
        router.UpdateShard(updatedShard);
        
        var retrieved = router.RouteByShardId(shardId);
        Assert.Equal("updated-name", retrieved?.Name);
        Assert.Equal("new-host", retrieved?.Host);
        Assert.Equal(9999, retrieved?.Port);
    }

    [Fact]
    public void ShardRouter_UpdateShard_NonExistent_ShouldThrow()
    {
        var config = CreateTestConfiguration(2);
        var router = new ShardRouter(config);
        var updatedShard = new ShardNode
        {
            ShardId = "non-existent",
            Name = "updated"
        };
        
        Assert.Throws<ShardNotFoundException>(() => router.UpdateShard(updatedShard));
    }

    [Fact]
    public void ShardRouter_RoutingChanged_ShouldFireEvent()
    {
        var config = CreateTestConfiguration(2);
        var router = new ShardRouter(config);
        var eventFired = false;
        ShardRoutingChangedEventArgs? eventArgs = null;
        
        router.RoutingChanged += (s, e) =>
        {
            eventFired = true;
            eventArgs = e;
        };
        
        router.AddShard(new ShardNode { Name = "new-shard" });
        
        Assert.True(eventFired);
        Assert.NotNull(eventArgs);
        Assert.Equal(RoutingChangeType.ShardAdded, eventArgs.ChangeType);
    }

    #endregion

    #region ShardingManager Tests

    [Fact]
    public async Task ShardingManager_InitializeAsync_ShouldComplete()
    {
        var config = CreateTestConfiguration(3);
        var shardKey = new ShardKey("userId");
        var manager = new ShardingManager(config, shardKey);
        
        await manager.InitializeAsync();
        
        Assert.True(true); // Should complete without exception
    }

    [Fact]
    public void ShardingManager_GetShardForDocument_WithId_ShouldReturnShard()
    {
        var config = CreateTestConfiguration(3);
        var shardKey = new ShardKey("userId");
        var manager = new ShardingManager(config, shardKey);
        
        var shard = manager.GetShardForDocument("users", "user123");
        
        Assert.NotNull(shard);
    }

    [Fact]
    public void ShardingManager_GetShardForDocument_WithDocument_ShouldReturnShard()
    {
        var config = CreateTestConfiguration(3);
        var shardKey = new ShardKey("userId");
        var manager = new ShardingManager(config, shardKey);
        var doc = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { ["userId"] = "user123" }
        };
        
        var shard = manager.GetShardForDocument(doc);
        
        Assert.NotNull(shard);
    }

    [Fact]
    public async Task ShardingManager_AddShardAsync_ShouldAddShard()
    {
        var config = CreateTestConfiguration(2);
        var shardKey = new ShardKey("userId");
        var manager = new ShardingManager(config, shardKey);
        var newShard = new ShardNode { Name = "shard3", Host = "localhost", Port = 9093 };
        var eventFired = false;
        
        manager.ShardAdded += (s, e) => eventFired = true;
        
        await manager.AddShardAsync(newShard);
        
        var shards = manager.GetAllShards();
        Assert.Equal(3, shards.Count);
        Assert.True(eventFired);
    }

    [Fact]
    public async Task ShardingManager_RemoveShardAsync_ShouldRemoveShard()
    {
        var config = CreateTestConfiguration(3);
        var shardKey = new ShardKey("userId");
        var manager = new ShardingManager(config, shardKey);
        var shardIdToRemove = config.Shards[1].ShardId;
        var eventFired = false;
        
        manager.ShardRemoved += (s, e) => eventFired = true;
        
        await manager.RemoveShardAsync(shardIdToRemove, migrateData: false);
        
        var shards = manager.GetAllShards();
        Assert.Equal(2, shards.Count);
        Assert.True(eventFired);
    }

    [Fact]
    public async Task ShardingManager_GetStatisticsAsync_ShouldReturnStats()
    {
        var config = CreateTestConfiguration(3);
        var shardKey = new ShardKey("userId");
        var manager = new ShardingManager(config, shardKey);
        
        var stats = await manager.GetStatisticsAsync();
        
        Assert.NotNull(stats);
        Assert.Equal(3, stats.TotalShards);
        Assert.Equal(3, stats.ActiveShards);
    }

    [Fact]
    public async Task ShardingManager_MigrateDataAsync_WithValidOptions_ShouldReturnSuccess()
    {
        var config = CreateTestConfiguration(3);
        var shardKey = new ShardKey("userId");
        var manager = new ShardingManager(config, shardKey);
        var options = new ShardMigrationOptions
        {
            SourceShardId = config.Shards[0].ShardId,
            DestinationShardId = config.Shards[1].ShardId,
            BatchSize = 100
        };
        
        var result = await manager.MigrateDataAsync(options);
        
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ShardingManager_MigrateDataAsync_WithInvalidSource_ShouldReturnFailure()
    {
        var config = CreateTestConfiguration(2);
        var shardKey = new ShardKey("userId");
        var manager = new ShardingManager(config, shardKey);
        var options = new ShardMigrationOptions
        {
            SourceShardId = "non-existent",
            DestinationShardId = config.Shards[1].ShardId
        };
        
        var result = await manager.MigrateDataAsync(options);
        
        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public void ShardingManager_NeedsRebalancing_WithEqualLoad_ShouldReturnFalse()
    {
        var config = CreateTestConfiguration(3);
        config.RebalanceThreshold = 0.5;
        var shardKey = new ShardKey("userId");
        var manager = new ShardingManager(config, shardKey);
        
        // Simulate equal load
        if (manager is ShardingManager sm)
        {
            sm.UpdateShardStatistics(config.Shards[0].ShardId, s => s.DocumentCount = 100);
            sm.UpdateShardStatistics(config.Shards[1].ShardId, s => s.DocumentCount = 105);
            sm.UpdateShardStatistics(config.Shards[2].ShardId, s => s.DocumentCount = 95);
        }
        
        Assert.False(manager.NeedsRebalancing());
    }

    [Fact]
    public void ShardingManager_UpdateShardStatistics_ShouldUpdateAndFireEvent()
    {
        var config = CreateTestConfiguration(2);
        var shardKey = new ShardKey("userId");
        var manager = new ShardingManager(config, shardKey);
        var eventFired = false;
        
        manager.StatisticsUpdated += (s, e) => eventFired = true;
        
        if (manager is ShardingManager sm)
        {
            sm.UpdateShardStatistics(config.Shards[0].ShardId, s => s.DocumentCount = 500);
        }
        
        Assert.True(eventFired);
    }

    #endregion

    #region Helper Methods

    private static ShardConfiguration CreateTestConfiguration(int shardCount)
    {
        var config = new ShardConfiguration
        {
            ClusterName = "test-cluster",
            Shards = new List<ShardNode>()
        };

        for (int i = 0; i < shardCount; i++)
        {
            config.Shards.Add(new ShardNode
            {
                Name = $"shard{i + 1}",
                Host = "localhost",
                Port = 9090 + i + 1
            });
        }

        return config;
    }

    #endregion
}
