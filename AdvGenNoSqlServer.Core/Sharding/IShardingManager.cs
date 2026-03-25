// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Sharding;

/// <summary>
/// Interface for managing sharded document storage.
/// </summary>
public interface IShardingManager : IDisposable
{
    /// <summary>
    /// Gets the shard router.
    /// </summary>
    IShardRouter Router { get; }

    /// <summary>
    /// Gets the shard configuration.
    /// </summary>
    ShardConfiguration Configuration { get; }

    /// <summary>
    /// Gets the shard key used for routing.
    /// </summary>
    IShardKey ShardKey { get; }

    /// <summary>
    /// Initializes the sharding manager.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the shard node for a specific document.
    /// </summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="documentId">The document ID.</param>
    /// <returns>The shard node that should contain this document.</returns>
    ShardNode GetShardForDocument(string collection, string documentId);

    /// <summary>
    /// Gets the shard node for a document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The shard node.</returns>
    ShardNode GetShardForDocument(Document document);

    /// <summary>
    /// Gets all shards that may contain documents matching a query.
    /// </summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="filter">The query filter.</param>
    /// <returns>The list of relevant shard nodes.</returns>
    IReadOnlyList<ShardNode> GetShardsForQuery(string collection, Dictionary<string, object>? filter);

    /// <summary>
    /// Gets all active shards for scatter-gather operations.
    /// </summary>
    /// <returns>All active shard nodes.</returns>
    IReadOnlyList<ShardNode> GetAllShards();

    /// <summary>
    /// Adds a new shard to the cluster.
    /// </summary>
    /// <param name="shard">The shard to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddShardAsync(ShardNode shard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a shard from the cluster.
    /// </summary>
    /// <param name="shardId">The shard ID to remove.</param>
    /// <param name="migrateData">Whether to migrate data before removing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveShardAsync(string shardId, bool migrateData = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates data between shards.
    /// </summary>
    /// <param name="options">The migration options.</param>
    /// <param name="progress">Progress reporting.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The migration result.</returns>
    Task<ShardMigrationResult> MigrateDataAsync(
        ShardMigrationOptions options,
        IProgress<ShardMigrationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebalances data across shards.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of rebalancing operations.</returns>
    Task<ShardRebalanceResult> RebalanceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics for all shards.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cluster-wide statistics.</returns>
    Task<ShardingClusterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics for a specific shard.
    /// </summary>
    /// <param name="shardId">The shard ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The shard statistics.</returns>
    Task<ShardStatistics?> GetShardStatisticsAsync(string shardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the cluster needs rebalancing.
    /// </summary>
    /// <returns>True if rebalancing is recommended.</returns>
    bool NeedsRebalancing();

    /// <summary>
    /// Event raised when a shard is added.
    /// </summary>
    event EventHandler<ShardEventArgs>? ShardAdded;

    /// <summary>
    /// Event raised when a shard is removed.
    /// </summary>
    event EventHandler<ShardEventArgs>? ShardRemoved;

    /// <summary>
    /// Event raised when shard statistics are updated.
    /// </summary>
    event EventHandler<ShardStatisticsEventArgs>? StatisticsUpdated;
}

/// <summary>
/// Event args for shard events.
/// </summary>
public class ShardEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the shard involved in the event.
    /// </summary>
    public ShardNode Shard { get; set; } = null!;

    /// <summary>
    /// Gets or sets the timestamp of the event.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Event args for shard statistics updates.
/// </summary>
public class ShardStatisticsEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the shard statistics.
    /// </summary>
    public ShardStatistics Statistics { get; set; } = null!;

    /// <summary>
    /// Gets or sets the timestamp of the update.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Progress information for shard migration.
/// </summary>
public class ShardMigrationProgress
{
    /// <summary>
    /// Gets or sets the total number of documents to migrate.
    /// </summary>
    public long TotalDocuments { get; set; }

    /// <summary>
    /// Gets or sets the number of documents migrated so far.
    /// </summary>
    public long MigratedDocuments { get; set; }

    /// <summary>
    /// Gets or sets the percentage complete.
    /// </summary>
    public double PercentComplete => TotalDocuments > 0 ? (MigratedDocuments / (double)TotalDocuments) * 100 : 0;

    /// <summary>
    /// Gets or sets the current batch number.
    /// </summary>
    public int CurrentBatch { get; set; }

    /// <summary>
    /// Gets or sets the total number of batches.
    /// </summary>
    public int TotalBatches { get; set; }

    /// <summary>
    /// Gets or sets the elapsed time.
    /// </summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>
    /// Gets or sets the estimated remaining time.
    /// </summary>
    public TimeSpan? EstimatedRemaining { get; set; }

    /// <summary>
    /// Gets or sets the current operation being performed.
    /// </summary>
    public string? CurrentOperation { get; set; }

    /// <summary>
    /// Gets or sets the source shard ID.
    /// </summary>
    public string? SourceShardId { get; set; }

    /// <summary>
    /// Gets or sets the destination shard ID.
    /// </summary>
    public string? DestinationShardId { get; set; }
}

/// <summary>
/// Result of a shard rebalancing operation.
/// </summary>
public class ShardRebalanceResult
{
    /// <summary>
    /// Gets or sets whether rebalancing was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the number of documents moved.
    /// </summary>
    public long DocumentsMoved { get; set; }

    /// <summary>
    /// Gets or sets the number of migration operations performed.
    /// </summary>
    public int MigrationsPerformed { get; set; }

    /// <summary>
    /// Gets or sets the duration of rebalancing.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the error message if rebalancing failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the individual migration results.
    /// </summary>
    public List<ShardMigrationResult> MigrationResults { get; set; } = new();

    /// <summary>
    /// Gets or sets the timestamp when rebalancing completed.
    /// </summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static ShardRebalanceResult CreateSuccess(long docsMoved, int migrations, TimeSpan duration, List<ShardMigrationResult> results)
    {
        return new ShardRebalanceResult
        {
            Success = true,
            DocumentsMoved = docsMoved,
            MigrationsPerformed = migrations,
            Duration = duration,
            MigrationResults = results,
            CompletedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static ShardRebalanceResult CreateFailure(string error)
    {
        return new ShardRebalanceResult
        {
            Success = false,
            ErrorMessage = error,
            CompletedAt = DateTime.UtcNow
        };
    }
}
