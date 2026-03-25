// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Sharding;

/// <summary>
/// Configuration for sharding.
/// </summary>
public class ShardConfiguration
{
    /// <summary>
    /// Gets or sets the unique identifier for this sharding configuration.
    /// </summary>
    public string ConfigurationId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the name of this shard cluster.
    /// </summary>
    public string ClusterName { get; set; } = "default";

    /// <summary>
    /// Gets or sets the default number of virtual nodes per physical shard (for consistent hashing).
    /// </summary>
    public int VirtualNodesPerShard { get; set; } = 150;

    /// <summary>
    /// Gets or sets whether to enable automatic rebalancing.
    /// </summary>
    public bool EnableAutoRebalancing { get; set; } = false;

    /// <summary>
    /// Gets or sets the threshold (as a ratio) for triggering rebalancing.
    /// </summary>
    public double RebalanceThreshold { get; set; } = 0.2; // 20% difference

    /// <summary>
    /// Gets or sets the replication factor for shard redundancy.
    /// </summary>
    public int ReplicationFactor { get; set; } = 1;

    /// <summary>
    /// Gets or sets the read preference for sharded queries.
    /// </summary>
    public ShardingReadPreference ReadPreference { get; set; } = ShardingReadPreference.Primary;

    /// <summary>
    /// Gets or sets the timeout for cross-shard operations.
    /// </summary>
    public TimeSpan CrossShardTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the maximum number of concurrent cross-shard operations.
    /// </summary>
    public int MaxConcurrentCrossShardOps { get; set; } = 100;

    /// <summary>
    /// Gets or sets whether to enable cross-shard transactions.
    /// </summary>
    public bool EnableCrossShardTransactions { get; set; } = false;

    /// <summary>
    /// Gets the list of shard nodes.
    /// </summary>
    public List<ShardNode> Shards { get; set; } = new();

    /// <summary>
    /// Gets the list of shard ranges (for range-based sharding).
    /// </summary>
    public List<ShardRange> Ranges { get; set; } = new();

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <exception cref="ValidationException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClusterName))
        {
            throw new ValidationException("Cluster name cannot be empty.");
        }

        if (VirtualNodesPerShard < 1)
        {
            throw new ValidationException("Virtual nodes per shard must be at least 1.");
        }

        if (RebalanceThreshold < 0 || RebalanceThreshold > 1)
        {
            throw new ValidationException("Rebalance threshold must be between 0 and 1.");
        }

        if (ReplicationFactor < 1)
        {
            throw new ValidationException("Replication factor must be at least 1.");
        }

        if (Shards.Count == 0)
        {
            throw new ValidationException("At least one shard must be configured.");
        }

        // Validate shard uniqueness
        var shardIds = new HashSet<string>();
        foreach (var shard in Shards)
        {
            if (!shardIds.Add(shard.ShardId))
            {
                throw new ValidationException($"Duplicate shard ID: {shard.ShardId}");
            }
        }

        // Validate ranges if specified
        if (Ranges.Count > 0)
        {
            ValidateRanges();
        }
    }

    private void ValidateRanges()
    {
        // Check for overlapping ranges
        var sortedRanges = Ranges.OrderBy(r => r.MinHash).ToList();
        for (int i = 0; i < sortedRanges.Count - 1; i++)
        {
            if (sortedRanges[i].MaxHash > sortedRanges[i + 1].MinHash)
            {
                throw new ValidationException(
                    $"Overlapping ranges detected: {sortedRanges[i].RangeId} and {sortedRanges[i + 1].RangeId}");
            }
        }

        // Validate all shards referenced by ranges exist
        var shardIdSet = new HashSet<string>(Shards.Select(s => s.ShardId));
        foreach (var range in Ranges)
        {
            if (!shardIdSet.Contains(range.ShardId))
            {
                throw new ValidationException(
                    $"Range {range.RangeId} references non-existent shard {range.ShardId}");
            }
        }
    }

    /// <summary>
    /// Creates a deep clone of this configuration.
    /// </summary>
    /// <returns>A new configuration with copied values.</returns>
    public ShardConfiguration Clone()
    {
        return new ShardConfiguration
        {
            ConfigurationId = ConfigurationId,
            ClusterName = ClusterName,
            VirtualNodesPerShard = VirtualNodesPerShard,
            EnableAutoRebalancing = EnableAutoRebalancing,
            RebalanceThreshold = RebalanceThreshold,
            ReplicationFactor = ReplicationFactor,
            ReadPreference = ReadPreference,
            CrossShardTimeout = CrossShardTimeout,
            MaxConcurrentCrossShardOps = MaxConcurrentCrossShardOps,
            EnableCrossShardTransactions = EnableCrossShardTransactions,
            Shards = Shards.Select(s => s.Clone()).ToList(),
            Ranges = Ranges.Select(r => new ShardRange
            {
                RangeId = r.RangeId,
                ShardId = r.ShardId,
                MinHash = r.MinHash,
                MaxHash = r.MaxHash,
                Description = r.Description
            }).ToList()
        };
    }
}

/// <summary>
/// Read preference options for sharded queries.
/// </summary>
public enum ShardingReadPreference
{
    /// <summary>
    /// Read from the primary shard only.
    /// </summary>
    Primary,

    /// <summary>
    /// Read from primary, fallback to secondary.
    /// </summary>
    PrimaryPreferred,

    /// <summary>
    /// Read from any available shard (including replicas).
    /// </summary>
    Nearest,

    /// <summary>
    /// Read from the shard with the lowest latency.
    /// </summary>
    LowestLatency,

    /// <summary>
    /// Read from the shard with the lowest load.
    /// </summary>
    LowestLoad
}

/// <summary>
/// Options for shard migration.
/// </summary>
public class ShardMigrationOptions
{
    /// <summary>
    /// Gets or sets the source shard ID.
    /// </summary>
    public string SourceShardId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the destination shard ID.
    /// </summary>
    public string DestinationShardId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional collection name to migrate (null = all collections).
    /// </summary>
    public string? CollectionName { get; set; }

    /// <summary>
    /// Gets or sets the hash range to migrate (for partial migration).
    /// </summary>
    public (int Min, int Max)? HashRange { get; set; }

    /// <summary>
    /// Gets or sets the batch size for migration.
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the delay between batches.
    /// </summary>
    public TimeSpan BatchDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets whether to delete data from source after migration.
    /// </summary>
    public bool DeleteSourceAfterMigration { get; set; } = true;

    /// <summary>
    /// Validates the migration options.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceShardId))
            throw new ValidationException("Source shard ID cannot be empty.");

        if (string.IsNullOrWhiteSpace(DestinationShardId))
            throw new ValidationException("Destination shard ID cannot be empty.");

        if (SourceShardId == DestinationShardId)
            throw new ValidationException("Source and destination shards must be different.");

        if (BatchSize < 1)
            throw new ValidationException("Batch size must be at least 1.");
    }
}

/// <summary>
/// Result of a shard migration operation.
/// </summary>
public class ShardMigrationResult
{
    /// <summary>
    /// Gets or sets whether the migration was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the number of documents migrated.
    /// </summary>
    public long DocumentsMigrated { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes migrated.
    /// </summary>
    public long BytesMigrated { get; set; }

    /// <summary>
    /// Gets or sets the duration of the migration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the error message if migration failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when migration completed.
    /// </summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static ShardMigrationResult CreateSuccess(long documents, long bytes, TimeSpan duration)
    {
        return new ShardMigrationResult
        {
            Success = true,
            DocumentsMigrated = documents,
            BytesMigrated = bytes,
            Duration = duration,
            CompletedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static ShardMigrationResult CreateFailure(string error)
    {
        return new ShardMigrationResult
        {
            Success = false,
            ErrorMessage = error,
            CompletedAt = DateTime.UtcNow
        };
    }
}
