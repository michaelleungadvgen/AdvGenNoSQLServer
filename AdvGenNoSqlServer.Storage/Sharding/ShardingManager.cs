// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Sharding;
using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Storage.Sharding;

/// <summary>
/// Implementation of the sharding manager for coordinating shard operations.
/// </summary>
public class ShardingManager : IShardingManager
{
    private readonly ShardConfiguration _configuration;
    private readonly IShardKey _shardKey;
    private readonly ShardRouter _router;
    private readonly ConcurrentDictionary<string, ShardStatistics> _statistics;
    private readonly Timer? _statsUpdateTimer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardingManager"/> class.
    /// </summary>
    /// <param name="configuration">The shard configuration.</param>
    /// <param name="shardKey">The shard key for routing.</param>
    public ShardingManager(ShardConfiguration configuration, IShardKey shardKey)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _shardKey = shardKey ?? throw new ArgumentNullException(nameof(shardKey));
        _router = new ShardRouter(configuration);
        _statistics = new ConcurrentDictionary<string, ShardStatistics>();

        // Initialize statistics for all shards
        foreach (var shard in configuration.Shards)
        {
            _statistics[shard.ShardId] = new ShardStatistics { ShardId = shard.ShardId };
        }

        // Setup periodic stats update timer (if needed in the future)
        // _statsUpdateTimer = new Timer(UpdateStatistics, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <inheritdoc />
    public IShardRouter Router => _router;

    /// <inheritdoc />
    public ShardConfiguration Configuration => _configuration;

    /// <inheritdoc />
    public IShardKey ShardKey => _shardKey;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Validate that all shards are accessible
        // In a real implementation, this would ping each shard
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ShardNode GetShardForDocument(string collection, string documentId)
    {
        if (string.IsNullOrEmpty(collection)) throw new ArgumentNullException(nameof(collection));
        if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

        // Create a temporary document to route
        var tempDoc = new Document
        {
            Id = documentId,
            // Note: Document doesn't have CollectionName, we track it separately if needed
            Data = new Dictionary<string, object> { ["_id"] = documentId }
        };

        return _router.RouteDocument(tempDoc, _shardKey);
    }

    /// <inheritdoc />
    public ShardNode GetShardForDocument(Document document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        return _router.RouteDocument(document, _shardKey);
    }

    /// <inheritdoc />
    public IReadOnlyList<ShardNode> GetShardsForQuery(string collection, Dictionary<string, object>? filter)
    {
        if (string.IsNullOrEmpty(collection)) throw new ArgumentNullException(nameof(collection));

        // For now, return all shards (scatter-gather)
        // In a future optimization, we could analyze the filter to prune shards
        return _router.GetAllActiveShards();
    }

    /// <inheritdoc />
    public IReadOnlyList<ShardNode> GetAllShards()
    {
        return _router.GetAllActiveShards();
    }

    /// <inheritdoc />
    public Task AddShardAsync(ShardNode shard, CancellationToken cancellationToken = default)
    {
        if (shard == null) throw new ArgumentNullException(nameof(shard));

        cancellationToken.ThrowIfCancellationRequested();

        _router.AddShard(shard);
        _statistics[shard.ShardId] = new ShardStatistics { ShardId = shard.ShardId };

        ShardAdded?.Invoke(this, new ShardEventArgs { Shard = shard });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RemoveShardAsync(string shardId, bool migrateData = true, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(shardId)) throw new ArgumentNullException(nameof(shardId));

        var shard = _router.RouteByShardId(shardId);
        if (shard == null)
            throw new ShardNotFoundException($"Shard {shardId} not found.");

        if (migrateData)
        {
            // Migrate data to other shards before removing
            var targetShards = _router.GetAllActiveShards().Where(s => s.ShardId != shardId).ToList();
            if (targetShards.Count == 0)
                throw new ShardingException("Cannot remove shard: no other shards available for data migration.");

            // In a real implementation, we would migrate data here
            // For now, we just simulate the migration
            await Task.Delay(100, cancellationToken);
        }

        _router.RemoveShard(shardId);
        _statistics.TryRemove(shardId, out _);

        ShardRemoved?.Invoke(this, new ShardEventArgs { Shard = shard });
    }

    /// <inheritdoc />
    public async Task<ShardMigrationResult> MigrateDataAsync(
        ShardMigrationOptions options,
        IProgress<ShardMigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        options.Validate();

        var startTime = DateTime.UtcNow;

        try
        {
            var sourceShard = _router.RouteByShardId(options.SourceShardId);
            var destShard = _router.RouteByShardId(options.DestinationShardId);

            if (sourceShard == null)
                return ShardMigrationResult.CreateFailure($"Source shard {options.SourceShardId} not found.");

            if (destShard == null)
                return ShardMigrationResult.CreateFailure($"Destination shard {options.DestinationShardId} not found.");

            // Report initial progress
            progress?.Report(new ShardMigrationProgress
            {
                SourceShardId = options.SourceShardId,
                DestinationShardId = options.DestinationShardId,
                CurrentOperation = "Initializing migration",
                CurrentBatch = 0,
                TotalBatches = 1
            });

            // Simulate migration
            // In a real implementation, this would:
            // 1. Query documents from source shard
            // 2. Filter by hash range if specified
            // 3. Insert into destination shard in batches
            // 4. Delete from source if DeleteSourceAfterMigration is true

            await Task.Delay(500, cancellationToken);

            var duration = DateTime.UtcNow - startTime;

            // Return simulated success result
            return ShardMigrationResult.CreateSuccess(
                documents: options.BatchSize * 2, // Simulated
                bytes: options.BatchSize * 1024,  // Simulated
                duration: duration);
        }
        catch (OperationCanceledException)
        {
            return ShardMigrationResult.CreateFailure("Migration was cancelled.");
        }
        catch (Exception ex)
        {
            return ShardMigrationResult.CreateFailure($"Migration failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<ShardRebalanceResult> RebalanceAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // Check if rebalancing is needed
        if (!NeedsRebalancing())
        {
            return Task.FromResult(ShardRebalanceResult.CreateSuccess(0, 0, TimeSpan.Zero, new List<ShardMigrationResult>()));
        }

        // In a real implementation, this would:
        // 1. Calculate target distribution
        // 2. Identify documents that need to move
        // 3. Perform migrations in parallel (up to MaxConcurrentCrossShardOps)
        // 4. Update routing tables

        var duration = DateTime.UtcNow - startTime;

        var result = ShardRebalanceResult.CreateSuccess(
            docsMoved: 0,
            migrations: 0,
            duration: duration,
            results: new List<ShardMigrationResult>());

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<ShardingClusterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        long totalDocuments = 0;
        long totalStorageBytes = 0;
        int totalActiveConnections = 0;
        long totalRequests = 0;
        double sumLatency = 0;
        int activeStatsCount = 0;
        var shardStats = new List<ShardStatistics>(_statistics.Count);

        foreach (var kvp in _statistics)
        {
            var s = kvp.Value;
            totalDocuments += s.DocumentCount;
            totalStorageBytes += s.StorageBytes;
            totalActiveConnections += s.ActiveConnections;
            totalRequests += s.TotalRequests;
            sumLatency += s.AverageLatencyMs;
            activeStatsCount++;
            shardStats.Add(s);
        }

        var clusterStats = new ShardingClusterStatistics
        {
            TotalShards = _configuration.Shards.Count,
            ActiveShards = _router.GetAllActiveShards().Count,
            TotalDocuments = totalDocuments,
            TotalStorageBytes = totalStorageBytes,
            TotalActiveConnections = totalActiveConnections,
            TotalRequests = totalRequests,
            AverageClusterLatencyMs = activeStatsCount > 0 ? sumLatency / activeStatsCount : 0,
            ShardStats = shardStats
        };

        return Task.FromResult(clusterStats);
    }

    /// <inheritdoc />
    public Task<ShardStatistics?> GetShardStatisticsAsync(string shardId, CancellationToken cancellationToken = default)
    {
        _statistics.TryGetValue(shardId, out var stats);
        return Task.FromResult(stats);
    }

    /// <inheritdoc />
    public bool NeedsRebalancing()
    {
        if (_statistics.Count < 2) return false;

        long totalDocs = 0;
        long minDocs = long.MaxValue;
        long maxDocs = long.MinValue;
        int activeStatsCount = 0;

        foreach (var kvp in _statistics)
        {
            var s = kvp.Value;
            if (s.DocumentCount > 0)
            {
                activeStatsCount++;
                totalDocs += s.DocumentCount;
                if (s.DocumentCount < minDocs) minDocs = s.DocumentCount;
                if (s.DocumentCount > maxDocs) maxDocs = s.DocumentCount;
            }
        }

        if (activeStatsCount < 2) return false;

        var avgLoad = (double)totalDocs / activeStatsCount;
        var imbalance = (maxDocs - minDocs) / avgLoad;

        // Check if the difference between max and min exceeds the threshold
        return imbalance > _configuration.RebalanceThreshold;
    }

    /// <inheritdoc />
    public event EventHandler<ShardEventArgs>? ShardAdded;

    /// <inheritdoc />
    public event EventHandler<ShardEventArgs>? ShardRemoved;

    /// <inheritdoc />
    public event EventHandler<ShardStatisticsEventArgs>? StatisticsUpdated;

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _statsUpdateTimer?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Updates statistics for a shard.
    /// </summary>
    /// <param name="shardId">The shard ID.</param>
    /// <param name="update">Action to update the statistics.</param>
    public void UpdateShardStatistics(string shardId, Action<ShardStatistics> update)
    {
        var stats = _statistics.GetOrAdd(shardId, _ => new ShardStatistics { ShardId = shardId });
        update(stats);
        stats.LastUpdated = DateTime.UtcNow;

        StatisticsUpdated?.Invoke(this, new ShardStatisticsEventArgs { Statistics = stats });
    }
}
