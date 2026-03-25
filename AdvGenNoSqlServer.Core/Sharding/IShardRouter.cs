// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Sharding;

/// <summary>
/// Interface for routing documents to shards.
/// </summary>
public interface IShardRouter
{
    /// <summary>
    /// Gets the shard configuration.
    /// </summary>
    ShardConfiguration Configuration { get; }

    /// <summary>
    /// Routes a document to the appropriate shard.
    /// </summary>
    /// <param name="document">The document to route.</param>
    /// <param name="shardKey">The shard key to use for routing.</param>
    /// <returns>The shard node that should store this document.</returns>
    /// <exception cref="ShardingException">Thrown when routing fails.</exception>
    ShardNode RouteDocument(Document document, IShardKey shardKey);

    /// <summary>
    /// Routes based on a pre-computed hash value.
    /// </summary>
    /// <param name="hash">The hash value to route.</param>
    /// <returns>The shard node.</returns>
    ShardNode RouteByHash(int hash);

    /// <summary>
    /// Routes to a shard by its ID.
    /// </summary>
    /// <param name="shardId">The shard ID.</param>
    /// <returns>The shard node, or null if not found.</returns>
    ShardNode? RouteByShardId(string shardId);

    /// <summary>
    /// Routes to a shard by its tag (for tagged sharding).
    /// </summary>
    /// <param name="tag">The shard tag.</param>
    /// <returns>The shard node, or null if not found.</returns>
    ShardNode? RouteByTag(string tag);

    /// <summary>
    /// Gets all shards that may contain documents matching the given hash range.
    /// </summary>
    /// <param name="minHash">The minimum hash value.</param>
    /// <param name="maxHash">The maximum hash value.</param>
    /// <returns>The list of shard nodes that overlap with the range.</returns>
    IReadOnlyList<ShardNode> RouteByRange(int minHash, int maxHash);

    /// <summary>
    /// Gets all active shards for scatter-gather queries.
    /// </summary>
    /// <returns>The list of all active shard nodes.</returns>
    IReadOnlyList<ShardNode> GetAllActiveShards();

    /// <summary>
    /// Gets the primary shard for a given document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="shardKey">The shard key.</param>
    /// <returns>The primary shard node.</returns>
    ShardNode GetPrimaryShard(Document document, IShardKey shardKey);

    /// <summary>
    /// Gets the replica shards for a given document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="shardKey">The shard key.</param>
    /// <returns>The list of replica shard nodes.</returns>
    IReadOnlyList<ShardNode> GetReplicaShards(Document document, IShardKey shardKey);

    /// <summary>
    /// Adds a new shard to the routing table.
    /// </summary>
    /// <param name="shard">The shard to add.</param>
    void AddShard(ShardNode shard);

    /// <summary>
    /// Removes a shard from the routing table.
    /// </summary>
    /// <param name="shardId">The ID of the shard to remove.</param>
    /// <returns>True if the shard was removed.</returns>
    bool RemoveShard(string shardId);

    /// <summary>
    /// Updates an existing shard in the routing table.
    /// </summary>
    /// <param name="shard">The updated shard information.</param>
    void UpdateShard(ShardNode shard);

    /// <summary>
    /// Gets the current distribution of hashes across shards.
    /// </summary>
    /// <returns>A dictionary mapping shard IDs to their hash ranges.</returns>
    IReadOnlyDictionary<string, IReadOnlyList<ShardRange>> GetShardDistribution();

    /// <summary>
    /// Event raised when the shard routing changes.
    /// </summary>
    event EventHandler<ShardRoutingChangedEventArgs>? RoutingChanged;
}

/// <summary>
/// Event args for shard routing changes.
/// </summary>
public class ShardRoutingChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the type of change.
    /// </summary>
    public RoutingChangeType ChangeType { get; set; }

    /// <summary>
    /// Gets or sets the affected shard.
    /// </summary>
    public ShardNode? Shard { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the change.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets an optional message describing the change.
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Types of routing changes.
/// </summary>
public enum RoutingChangeType
{
    /// <summary>
    /// A shard was added.
    /// </summary>
    ShardAdded,

    /// <summary>
    /// A shard was removed.
    /// </summary>
    ShardRemoved,

    /// <summary>
    /// A shard was updated.
    /// </summary>
    ShardUpdated,

    /// <summary>
    /// Shard ranges were rebalanced.
    /// </summary>
    Rebalanced,

    /// <summary>
    /// A shard failed over.
    /// </summary>
    Failover
}

/// <summary>
/// Exception for sharding errors.
/// </summary>
public class ShardingException : Exception
{
    public ShardingException(string message) : base(message) { }
    public ShardingException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Exception for when no shard can be found for routing.
/// </summary>
public class ShardNotFoundException : ShardingException
{
    public ShardNotFoundException(string message) : base(message) { }
    public ShardNotFoundException(string message, Exception inner) : base(message, inner) { }
}
