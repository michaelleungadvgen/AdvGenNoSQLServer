// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Sharding;

namespace AdvGenNoSqlServer.Storage.Sharding;

/// <summary>
/// Document store wrapper that provides transparent sharding support.
/// Routes documents to appropriate shards based on a shard key.
/// </summary>
public class ShardingDocumentStore : IDocumentStore
{
    private readonly IShardingManager _shardingManager;
    private readonly Dictionary<string, IDocumentStore> _shardStores;
    private readonly ShardingReadPreference _readPreference;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardingDocumentStore"/> class.
    /// </summary>
    /// <param name="shardingManager">The sharding manager.</param>
    /// <param name="shardStores">A dictionary mapping shard IDs to document stores.</param>
    public ShardingDocumentStore(
        IShardingManager shardingManager,
        Dictionary<string, IDocumentStore> shardStores)
    {
        _shardingManager = shardingManager ?? throw new ArgumentNullException(nameof(shardingManager));
        _shardStores = shardStores ?? throw new ArgumentNullException(nameof(shardStores));
        _readPreference = shardingManager.Configuration.ReadPreference;
    }

    /// <summary>
    /// Gets the sharding manager.
    /// </summary>
    public IShardingManager ShardingManager => _shardingManager;

    /// <inheritdoc />
    public async Task<Document> InsertAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateInputs(collectionName, document);

        var shard = _shardingManager.GetShardForDocument(document);
        var store = GetStoreForShard(shard.ShardId);

        var result = await store.InsertAsync(collectionName, document, cancellationToken);

        // Update statistics
        if (_shardingManager is ShardingManager sm)
        {
            sm.UpdateShardStatistics(shard.ShardId, s => s.DocumentCount++);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Document?> GetAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

        var shard = _shardingManager.GetShardForDocument(collectionName, documentId);
        var store = GetStoreForShard(shard.ShardId);

        var doc = await store.GetAsync(collectionName, documentId, cancellationToken);

        // Update statistics
        if (_shardingManager is ShardingManager sm)
        {
            sm.UpdateShardStatistics(shard.ShardId, s => s.TotalRequests++);
        }

        return doc;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Document>> GetManyAsync(
        string collectionName,
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (documentIds == null) throw new ArgumentNullException(nameof(documentIds));

        var idList = documentIds.ToList();
        if (idList.Count == 0) return new List<Document>();

        // Group IDs by shard
        var shardGroups = new Dictionary<string, List<string>>();
        foreach (var id in idList)
        {
            var shard = _shardingManager.GetShardForDocument(collectionName, id);
            if (!shardGroups.ContainsKey(shard.ShardId))
                shardGroups[shard.ShardId] = new List<string>();
            shardGroups[shard.ShardId].Add(id);
        }

        // Query each shard in parallel
        var tasks = shardGroups.Select(async kvp =>
        {
            var store = GetStoreForShard(kvp.Key);
            var docs = await store.GetManyAsync(collectionName, kvp.Value, cancellationToken);

            // Update statistics
            if (_shardingManager is ShardingManager sm)
            {
                sm.UpdateShardStatistics(kvp.Key, s => s.TotalRequests += kvp.Value.Count);
            }

            return docs;
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(docs => docs).ToList();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Document>> GetAllAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(collectionName)) throw new ArgumentNullException(nameof(collectionName));

        // Scatter-gather: query all shards and combine results
        var shards = _shardingManager.GetAllShards();
        var tasks = shards.Select(async shard =>
        {
            var store = GetStoreForShard(shard.ShardId);
            var docs = await store.GetAllAsync(collectionName, cancellationToken);

            // Update statistics
            if (_shardingManager is ShardingManager sm)
            {
                sm.UpdateShardStatistics(shard.ShardId, s => s.TotalRequests++);
            }

            return docs;
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(docs => docs).ToList();
    }

    /// <inheritdoc />
    public async Task<Document> UpdateAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateInputs(collectionName, document);

        var shard = _shardingManager.GetShardForDocument(document);
        var store = GetStoreForShard(shard.ShardId);

        var result = await store.UpdateAsync(collectionName, document, cancellationToken);

        // Update statistics
        if (_shardingManager is ShardingManager sm)
        {
            sm.UpdateShardStatistics(shard.ShardId, s => s.TotalRequests++);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

        var shard = _shardingManager.GetShardForDocument(collectionName, documentId);
        var store = GetStoreForShard(shard.ShardId);

        var result = await store.DeleteAsync(collectionName, documentId, cancellationToken);

        // Update statistics
        if (result && _shardingManager is ShardingManager sm)
        {
            sm.UpdateShardStatistics(shard.ShardId, s =>
            {
                s.DocumentCount = Math.Max(0, s.DocumentCount - 1);
                s.TotalRequests++;
            });
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

        var shard = _shardingManager.GetShardForDocument(collectionName, documentId);
        var store = GetStoreForShard(shard.ShardId);

        var result = await store.ExistsAsync(collectionName, documentId, cancellationToken);

        // Update statistics
        if (_shardingManager is ShardingManager sm)
        {
            sm.UpdateShardStatistics(shard.ShardId, s => s.TotalRequests++);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(collectionName)) throw new ArgumentNullException(nameof(collectionName));

        // Scatter-gather: count from all shards and sum
        var shards = _shardingManager.GetAllShards();
        var tasks = shards.Select(async shard =>
        {
            var store = GetStoreForShard(shard.ShardId);
            var count = await store.CountAsync(collectionName, cancellationToken);

            // Update statistics
            if (_shardingManager is ShardingManager sm)
            {
                sm.UpdateShardStatistics(shard.ShardId, s => s.TotalRequests++);
            }

            return count;
        });

        var results = await Task.WhenAll(tasks);
        return results.Sum();
    }

    /// <inheritdoc />
    public async Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(collectionName)) throw new ArgumentNullException(nameof(collectionName));

        // Create collection on all shards
        var shards = _shardingManager.GetAllShards();
        var tasks = shards.Select(shard =>
        {
            var store = GetStoreForShard(shard.ShardId);
            return store.CreateCollectionAsync(collectionName, cancellationToken);
        });

        await Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    public async Task<bool> DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(collectionName)) throw new ArgumentNullException(nameof(collectionName));

        // Drop collection from all shards
        var shards = _shardingManager.GetAllShards();
        var tasks = shards.Select(async shard =>
        {
            var store = GetStoreForShard(shard.ShardId);
            return await store.DropCollectionAsync(collectionName, cancellationToken);
        });

        var results = await Task.WhenAll(tasks);
        return results.Any(r => r); // Return true if any shard dropped the collection
    }

    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Get collections from all shards and combine
        var shards = _shardingManager.GetAllShards();
        var tasks = shards.Select(async shard =>
        {
            var store = GetStoreForShard(shard.ShardId);
            return await store.GetCollectionsAsync(cancellationToken);
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(cols => cols).Distinct();
    }

    /// <inheritdoc />
    public async Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(collectionName)) throw new ArgumentNullException(nameof(collectionName));

        // Clear collection on all shards
        var shards = _shardingManager.GetAllShards();
        var tasks = shards.Select(shard =>
        {
            var store = GetStoreForShard(shard.ShardId);
            return store.ClearCollectionAsync(collectionName, cancellationToken);
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Gets per-shard document counts for a collection.
    /// </summary>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary mapping shard IDs to document counts.</returns>
    public async Task<Dictionary<string, long>> GetShardDocumentCountsAsync(
        string collectionName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(collectionName)) throw new ArgumentNullException(nameof(collectionName));

        var shards = _shardingManager.GetAllShards();
        var tasks = shards.Select(async shard =>
        {
            var store = GetStoreForShard(shard.ShardId);
            var count = await store.CountAsync(collectionName, cancellationToken);
            return (shard.ShardId, count);
        });

        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(r => r.ShardId, r => r.count);
    }

    /// <summary>
    /// Gets the shard distribution statistics for a collection.
    /// </summary>
    /// <param name="collectionName">The collection name.</param>
    /// <returns>Distribution information.</returns>
    public ShardDistributionInfo GetShardDistribution(string collectionName)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(collectionName)) throw new ArgumentNullException(nameof(collectionName));

        var distribution = _shardingManager.Router.GetShardDistribution();
        var shardStats = new List<ShardStatsInfo>();

        foreach (var kvp in distribution)
        {
            if (_shardingManager.GetShardStatisticsAsync(kvp.Key).Result is ShardStatistics stats)
            {
                shardStats.Add(new ShardStatsInfo
                {
                    ShardId = kvp.Key,
                    DocumentCount = stats.DocumentCount,
                    StorageBytes = stats.StorageBytes,
                    HashRanges = kvp.Value.Select(r => (r.MinHash, r.MaxHash)).ToList()
                });
            }
        }

        return new ShardDistributionInfo
        {
            Collection = collectionName,
            TotalShards = distribution.Count,
            ShardStats = shardStats
        };
    }

    private IDocumentStore GetStoreForShard(string shardId)
    {
        if (!_shardStores.TryGetValue(shardId, out var store))
        {
            throw new ShardNotFoundException($"No document store found for shard {shardId}");
        }
        return store;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ShardingDocumentStore));
    }

    private static void ValidateInputs(string collectionName, Document document)
    {
        if (string.IsNullOrEmpty(collectionName)) throw new ArgumentNullException(nameof(collectionName));
        if (document == null) throw new ArgumentNullException(nameof(document));
    }

    /// <summary>
    /// Disposes the sharding document store.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Information about shard distribution for a collection.
/// </summary>
public class ShardDistributionInfo
{
    /// <summary>
    /// Gets or sets the collection name.
    /// </summary>
    public string Collection { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total number of shards.
    /// </summary>
    public int TotalShards { get; set; }

    /// <summary>
    /// Gets or sets per-shard statistics.
    /// </summary>
    public List<ShardStatsInfo> ShardStats { get; set; } = new();
}

/// <summary>
/// Statistics for a single shard.
/// </summary>
public class ShardStatsInfo
{
    /// <summary>
    /// Gets or sets the shard ID.
    /// </summary>
    public string ShardId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the document count.
    /// </summary>
    public long DocumentCount { get; set; }

    /// <summary>
    /// Gets or sets the storage size in bytes.
    /// </summary>
    public long StorageBytes { get; set; }

    /// <summary>
    /// Gets or sets the hash ranges assigned to this shard.
    /// </summary>
    public List<(int Min, int Max)> HashRanges { get; set; } = new();
}
