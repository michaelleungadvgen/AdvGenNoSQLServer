// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Sharding;

/// <summary>
/// Represents a node in a sharded cluster.
/// </summary>
public class ShardNode
{
    /// <summary>
    /// Gets or sets the unique identifier for this shard.
    /// </summary>
    public string ShardId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the display name for this shard.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the host address for this shard.
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// Gets or sets the port number for this shard.
    /// </summary>
    public int Port { get; set; } = 9090;

    /// <summary>
    /// Gets or sets the priority of this shard (higher = preferred for reads).
    /// </summary>
    public int Priority { get; set; } = 1;

    /// <summary>
    /// Gets or sets the weight of this shard for load balancing.
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether this shard is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of connections to this shard.
    /// </summary>
    public int MaxConnections { get; set; } = 100;

    /// <summary>
    /// Gets or sets tags for this shard (used for tagged routing).
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Gets or sets custom metadata for this shard.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets the date and time when this shard was added.
    /// </summary>
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the endpoint string for this shard.
    /// </summary>
    public string Endpoint => $"{Host}:{Port}";

    /// <summary>
    /// Creates a clone of this shard node.
    /// </summary>
    /// <returns>A new instance with the same properties.</returns>
    public ShardNode Clone()
    {
        return new ShardNode
        {
            ShardId = ShardId,
            Name = Name,
            Host = Host,
            Port = Port,
            Priority = Priority,
            Weight = Weight,
            IsActive = IsActive,
            MaxConnections = MaxConnections,
            Tags = new List<string>(Tags),
            Metadata = new Dictionary<string, string>(Metadata),
            AddedAt = AddedAt
        };
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Name} ({ShardId[..8]}...) @ {Endpoint}";
    }
}

/// <summary>
/// Represents a range of hash values assigned to a shard (for range-based sharding).
/// </summary>
public class ShardRange
{
    /// <summary>
    /// Gets or sets the unique identifier for this range.
    /// </summary>
    public string RangeId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the shard ID this range belongs to.
    /// </summary>
    public string ShardId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the minimum hash value (inclusive).
    /// </summary>
    public int MinHash { get; set; } = int.MinValue;

    /// <summary>
    /// Gets or sets the maximum hash value (exclusive).
    /// </summary>
    public int MaxHash { get; set; } = int.MaxValue;

    /// <summary>
    /// Gets or sets a description of this range.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Checks if a hash value falls within this range.
    /// </summary>
    /// <param name="hash">The hash value to check.</param>
    /// <returns>True if the hash is within range.</returns>
    public bool Contains(int hash)
    {
        return hash >= MinHash && hash < MaxHash;
    }

    /// <summary>
    /// Gets the size of this range.
    /// </summary>
    public long RangeSize => (long)MaxHash - MinHash;

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Range [{MinHash}, {MaxHash}) -> Shard {ShardId[..8]}";
    }
}

/// <summary>
/// Statistics for a shard node.
/// </summary>
public class ShardStatistics
{
    /// <summary>
    /// Gets or sets the shard ID.
    /// </summary>
    public string ShardId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of documents stored on this shard.
    /// </summary>
    public long DocumentCount { get; set; }

    /// <summary>
    /// Gets or sets the total size in bytes stored on this shard.
    /// </summary>
    public long StorageBytes { get; set; }

    /// <summary>
    /// Gets or sets the number of active connections to this shard.
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// Gets or sets the total number of requests to this shard.
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// Gets or sets the average request latency in milliseconds.
    /// </summary>
    public double AverageLatencyMs { get; set; }

    /// <summary>
    /// Gets or sets the number of errors on this shard.
    /// </summary>
    public long ErrorCount { get; set; }

    /// <summary>
    /// Gets or sets the last time statistics were updated.
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the storage size in a human-readable format.
    /// </summary>
    public string StorageSizeHuman
    {
        get
        {
            var bytes = StorageBytes;
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            while (bytes >= 1024 && order < sizes.Length - 1)
            {
                order++;
                bytes /= 1024;
            }
            return $"{bytes:0.##} {sizes[order]}";
        }
    }

    /// <summary>
    /// Gets the error rate as a percentage.
    /// </summary>
    public double ErrorRate => TotalRequests > 0 ? (ErrorCount / (double)TotalRequests) * 100 : 0;
}

/// <summary>
/// Aggregated statistics for the entire shard cluster.
/// </summary>
public class ShardingClusterStatistics
{
    /// <summary>
    /// Gets or sets the total number of shards.
    /// </summary>
    public int TotalShards { get; set; }

    /// <summary>
    /// Gets or sets the number of active shards.
    /// </summary>
    public int ActiveShards { get; set; }

    /// <summary>
    /// Gets or sets the total number of documents across all shards.
    /// </summary>
    public long TotalDocuments { get; set; }

    /// <summary>
    /// Gets or sets the total storage size across all shards.
    /// </summary>
    public long TotalStorageBytes { get; set; }

    /// <summary>
    /// Gets or sets the total number of active connections.
    /// </summary>
    public int TotalActiveConnections { get; set; }

    /// <summary>
    /// Gets or sets the total number of requests.
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// Gets or sets the average cluster latency in milliseconds.
    /// </summary>
    public double AverageClusterLatencyMs { get; set; }

    /// <summary>
    /// Gets or sets per-shard statistics.
    /// </summary>
    public List<ShardStatistics> ShardStats { get; set; } = new();

    /// <summary>
    /// Gets the most loaded shard by document count.
    /// </summary>
    public ShardStatistics? MostLoadedShard =>
        ShardStats.OrderByDescending(s => s.DocumentCount).FirstOrDefault();

    /// <summary>
    /// Gets the least loaded shard by document count.
    /// </summary>
    public ShardStatistics? LeastLoadedShard =>
        ShardStats.OrderBy(s => s.DocumentCount).FirstOrDefault();

    /// <summary>
    /// Calculates the standard deviation of load across shards.
    /// </summary>
    public double LoadStandardDeviation
    {
        get
        {
            if (ShardStats.Count == 0) return 0;
            var avg = ShardStats.Average(s => s.DocumentCount);
            var variance = ShardStats.Average(s => Math.Pow(s.DocumentCount - avg, 2));
            return Math.Sqrt(variance);
        }
    }
}
