// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Sharding;

namespace AdvGenNoSqlServer.Storage.Sharding;

/// <summary>
/// Extension methods for adding sharding support to document stores.
/// </summary>
public static class ShardingExtensions
{
    /// <summary>
    /// Wraps a document store with sharding support.
    /// </summary>
    /// <param name="store">The base document store (used as a template for shard stores).</param>
    /// <param name="configuration">The shard configuration.</param>
    /// <param name="shardKey">The shard key for routing.</param>
    /// <param name="shardStores">Dictionary mapping shard IDs to their document stores.</param>
    /// <returns>A sharding-enabled document store.</returns>
    public static ShardingDocumentStore WithSharding(
        this IDocumentStore store,
        ShardConfiguration configuration,
        IShardKey shardKey,
        Dictionary<string, IDocumentStore> shardStores)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        if (shardKey == null) throw new ArgumentNullException(nameof(shardKey));
        if (shardStores == null) throw new ArgumentNullException(nameof(shardStores));

        var shardingManager = new ShardingManager(configuration, shardKey);
        return new ShardingDocumentStore(shardingManager, shardStores);
    }

    /// <summary>
    /// Wraps a document store with sharding support using a single local store for all shards.
    /// This is useful for testing or development scenarios.
    /// </summary>
    /// <param name="store">The base document store.</param>
    /// <param name="configuration">The shard configuration.</param>
    /// <param name="shardKey">The shard key for routing.</param>
    /// <returns>A sharding-enabled document store.</returns>
    public static ShardingDocumentStore WithLocalSharding(
        this IDocumentStore store,
        ShardConfiguration configuration,
        IShardKey shardKey)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        if (shardKey == null) throw new ArgumentNullException(nameof(shardKey));

        // Create a dictionary where all shards point to the same store
        // In a real scenario, each shard would have its own store
        var shardStores = configuration.Shards.ToDictionary(
            shard => shard.ShardId,
            _ => store);

        return store.WithSharding(configuration, shardKey, shardStores);
    }

    /// <summary>
    /// Creates a sharding manager for the given configuration.
    /// </summary>
    /// <param name="configuration">The shard configuration.</param>
    /// <param name="shardKey">The shard key for routing.</param>
    /// <returns>A sharding manager.</returns>
    public static IShardingManager CreateShardingManager(
        this ShardConfiguration configuration,
        IShardKey shardKey)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        if (shardKey == null) throw new ArgumentNullException(nameof(shardKey));

        return new ShardingManager(configuration, shardKey);
    }

    /// <summary>
    /// Adds a shard to the configuration.
    /// </summary>
    /// <param name="configuration">The shard configuration.</param>
    /// <param name="name">The shard name.</param>
    /// <param name="host">The host address.</param>
    /// <param name="port">The port number.</param>
    /// <param name="tags">Optional tags for the shard.</param>
    /// <returns>The updated configuration.</returns>
    public static ShardConfiguration AddShard(
        this ShardConfiguration configuration,
        string name,
        string host = "localhost",
        int port = 9090,
        params string[] tags)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

        var shard = new ShardNode
        {
            Name = name,
            Host = host,
            Port = port,
            Tags = tags?.ToList() ?? new List<string>()
        };

        configuration.Shards.Add(shard);
        return configuration;
    }

    /// <summary>
    /// Configures range-based sharding with equal-sized ranges.
    /// </summary>
    /// <param name="configuration">The shard configuration.</param>
    /// <returns>The updated configuration.</returns>
    public static ShardConfiguration WithEqualRanges(this ShardConfiguration configuration)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        configuration.Ranges.Clear();
        var shardCount = configuration.Shards.Count;
        if (shardCount == 0) return configuration;

        var rangeSize = (long)uint.MaxValue / shardCount;
        
        for (int i = 0; i < shardCount; i++)
        {
            var minHash = i == 0 ? int.MinValue : (int)(int.MinValue + i * rangeSize);
            var maxHash = i == shardCount - 1 ? int.MaxValue : (int)(int.MinValue + (i + 1) * rangeSize);
            
            configuration.Ranges.Add(new ShardRange
            {
                ShardId = configuration.Shards[i].ShardId,
                MinHash = minHash,
                MaxHash = maxHash,
                Description = $"Range {i + 1} of {shardCount}"
            });
        }

        return configuration;
    }

    /// <summary>
    /// Configures consistent hashing for the shard configuration.
    /// </summary>
    /// <param name="configuration">The shard configuration.</param>
    /// <param name="virtualNodesPerShard">Number of virtual nodes per physical shard.</param>
    /// <returns>The updated configuration.</returns>
    public static ShardConfiguration WithConsistentHashing(
        this ShardConfiguration configuration,
        int virtualNodesPerShard = 150)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        configuration.VirtualNodesPerShard = virtualNodesPerShard;
        configuration.Ranges.Clear(); // Clear ranges to use consistent hashing
        
        return configuration;
    }
}
