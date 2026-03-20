// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Clustering;

/// <summary>
/// Defines the read preference modes for distributing read operations across replica nodes.
/// </summary>
public enum ReadPreferenceMode
{
    /// <summary>
    /// Read from the primary node only. This is the default and provides the strongest consistency.
    /// If the primary is unavailable, the read operation will fail.
    /// </summary>
    Primary,

    /// <summary>
    /// Prefer the primary node, but fallback to a secondary if the primary is unavailable.
    /// Provides a balance between consistency and availability.
    /// </summary>
    PrimaryPreferred,

    /// <summary>
    /// Read from a secondary node only. Suitable for read-heavy workloads that can tolerate eventual consistency.
    /// If no secondary is available, the read operation will fail.
    /// </summary>
    Secondary,

    /// <summary>
    /// Prefer a secondary node, but fallback to the primary if no secondary is available.
    /// Useful for load balancing read operations while maintaining availability.
    /// </summary>
    SecondaryPreferred,

    /// <summary>
    /// Read from the nearest node based on network latency, regardless of whether it's primary or secondary.
    /// Best for geographically distributed applications where latency is critical.
    /// </summary>
    Nearest
}

/// <summary>
/// Represents a tag set used to target specific nodes in a cluster.
/// Tag sets allow routing read operations to nodes with specific characteristics,
/// such as analytics nodes, reporting nodes, or nodes in specific data centers.
/// </summary>
public class TagSet : Dictionary<string, string>
{
    /// <summary>
    /// Creates an empty tag set.
    /// </summary>
    public TagSet() : base(StringComparer.OrdinalIgnoreCase)
    {
    }

    /// <summary>
    /// Creates a tag set from an existing dictionary.
    /// </summary>
    public TagSet(IDictionary<string, string> tags) : base(tags, StringComparer.OrdinalIgnoreCase)
    {
    }

    /// <summary>
    /// Creates a tag set with a single tag.
    /// </summary>
    public TagSet(string key, string value) : base(StringComparer.OrdinalIgnoreCase)
    {
        this[key] = value;
    }

    /// <summary>
    /// Checks if this tag set matches the provided node tags.
    /// A match occurs when all tags in this set are present in the node tags with the same values.
    /// Matching is case-insensitive for both keys and values.
    /// </summary>
    /// <param name="nodeTags">The tags associated with a node</param>
    /// <returns>True if the node matches this tag set</returns>
    public bool Matches(IDictionary<string, string>? nodeTags)
    {
        if (nodeTags == null || Count == 0)
            return Count == 0;

        foreach (var tag in this)
        {
            // Find matching key case-insensitively
            var matchingKey = nodeTags.Keys.FirstOrDefault(k => 
                string.Equals(k, tag.Key, StringComparison.OrdinalIgnoreCase));
            
            if (matchingKey == null)
                return false;

            var value = nodeTags[matchingKey];
            if (!string.Equals(value, tag.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Creates a tag set for analytics workloads.
    /// </summary>
    public static TagSet Analytics => new("workload", "analytics");

    /// <summary>
    /// Creates a tag set for reporting workloads.
    /// </summary>
    public static TagSet Reporting => new("workload", "reporting");
}

/// <summary>
/// Configuration options for read preferences.
/// </summary>
public class ReadPreferenceOptions
{
    /// <summary>
    /// The read preference mode to use.
    /// </summary>
    public ReadPreferenceMode Mode { get; set; } = ReadPreferenceMode.Primary;

    /// <summary>
    /// Optional tag sets to filter eligible nodes. Multiple tag sets are evaluated in order.
    /// The first tag set that matches any node is used.
    /// </summary>
    public List<TagSet> TagSets { get; set; } = new();

    /// <summary>
    /// Maximum acceptable staleness for secondary reads in milliseconds.
    /// Secondary nodes with replication lag exceeding this value will not be selected.
    /// A value of 0 or less means no staleness limit.
    /// </summary>
    public long MaxStalenessMs { get; set; } = 0;

    /// <summary>
    /// Selection strategy for choosing between multiple eligible nodes.
    /// </summary>
    public NodeSelectionStrategy SelectionStrategy { get; set; } = NodeSelectionStrategy.RoundRobin;

    /// <summary>
    /// Timeout in milliseconds for health checks when selecting a node.
    /// </summary>
    public int HealthCheckTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Whether to perform health checks before selecting a node.
    /// </summary>
    public bool EnableHealthChecks { get; set; } = true;

    /// <summary>
    /// Default read preference options with Primary mode.
    /// </summary>
    public static ReadPreferenceOptions Primary => new()
    {
        Mode = ReadPreferenceMode.Primary
    };

    /// <summary>
    /// Default read preference options with SecondaryPreferred mode.
    /// </summary>
    public static ReadPreferenceOptions SecondaryPreferred => new()
    {
        Mode = ReadPreferenceMode.SecondaryPreferred
    };

    /// <summary>
    /// Default read preference options with Nearest mode for latency-based selection.
    /// </summary>
    public static ReadPreferenceOptions Nearest => new()
    {
        Mode = ReadPreferenceMode.Nearest,
        SelectionStrategy = NodeSelectionStrategy.LatencyBased
    };
}

/// <summary>
/// Strategies for selecting a node from multiple eligible candidates.
/// </summary>
public enum NodeSelectionStrategy
{
    /// <summary>
    /// Select nodes in a round-robin fashion for even load distribution.
    /// </summary>
    RoundRobin,

    /// <summary>
    /// Select a random node from the eligible candidates.
    /// </summary>
    Random,

    /// <summary>
    /// Select the node with the lowest measured latency.
    /// </summary>
    LatencyBased,

    /// <summary>
    /// Select the node with the lowest current load.
    /// </summary>
    LoadBased
}

/// <summary>
/// Represents the result of a read preference node selection operation.
/// </summary>
public class ReadPreferenceResult
{
    /// <summary>
    /// Whether a suitable node was found.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The selected node identity, or null if no node was selected.
    /// </summary>
    public NodeIdentity? SelectedNode { get; set; }

    /// <summary>
    /// The read preference mode used for this selection.
    /// </summary>
    public ReadPreferenceMode Mode { get; set; }

    /// <summary>
    /// The number of nodes that were considered during selection.
    /// </summary>
    public int NodesConsidered { get; set; }

    /// <summary>
    /// The number of nodes that were excluded due to filters or health checks.
    /// </summary>
    public int NodesExcluded { get; set; }

    /// <summary>
    /// The reason for selection failure, if applicable.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The measured latency to the selected node in milliseconds, if available.
    /// </summary>
    public double? LatencyMs { get; set; }

    /// <summary>
    /// The timestamp when this selection was made.
    /// </summary>
    public DateTime SelectionTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a successful result with the selected node.
    /// </summary>
    public static ReadPreferenceResult SuccessResult(NodeIdentity node, ReadPreferenceMode mode, int considered, int excluded, double? latency = null)
    {
        return new ReadPreferenceResult
        {
            Success = true,
            SelectedNode = node,
            Mode = mode,
            NodesConsidered = considered,
            NodesExcluded = excluded,
            LatencyMs = latency
        };
    }

    /// <summary>
    /// Creates a failure result with an error message.
    /// </summary>
    public static ReadPreferenceResult FailureResult(ReadPreferenceMode mode, string errorMessage, int considered = 0, int excluded = 0)
    {
        return new ReadPreferenceResult
        {
            Success = false,
            Mode = mode,
            ErrorMessage = errorMessage,
            NodesConsidered = considered,
            NodesExcluded = excluded
        };
    }
}

/// <summary>
/// Statistics for read preference operations, useful for monitoring and diagnostics.
/// </summary>
public class ReadPreferenceStatistics
{
    /// <summary>
    /// Total number of node selections performed.
    /// </summary>
    public long TotalSelections { get; set; }

    /// <summary>
    /// Number of successful node selections.
    /// </summary>
    public long SuccessfulSelections { get; set; }

    /// <summary>
    /// Number of failed node selections.
    /// </summary>
    public long FailedSelections { get; set; }

    /// <summary>
    /// Number of times the primary node was selected.
    /// </summary>
    public long PrimarySelections { get; set; }

    /// <summary>
    /// Number of times a secondary node was selected.
    /// </summary>
    public long SecondarySelections { get; set; }

    /// <summary>
    /// Average latency of node selections in milliseconds.
    /// </summary>
    public double AverageSelectionLatencyMs { get; set; }

    /// <summary>
    /// Timestamp of the last selection operation.
    /// </summary>
    public DateTime? LastSelectionTime { get; set; }

    /// <summary>
    /// Resets all statistics to zero.
    /// </summary>
    public void Reset()
    {
        TotalSelections = 0;
        SuccessfulSelections = 0;
        FailedSelections = 0;
        PrimarySelections = 0;
        SecondarySelections = 0;
        AverageSelectionLatencyMs = 0;
        LastSelectionTime = null;
    }
}

/// <summary>
/// Information about a node's current state for read preference decisions.
/// </summary>
public class NodeReadInfo
{
    /// <summary>
    /// The node identity.
    /// </summary>
    public required NodeIdentity Node { get; set; }

    /// <summary>
    /// Whether this node is the current primary/leader.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Whether this node is healthy and available for reads.
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// The current replication lag in milliseconds (for secondaries).
    /// </summary>
    public long ReplicationLagMs { get; set; }

    /// <summary>
    /// The node's tags for filtering.
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// The measured network latency to this node in milliseconds.
    /// </summary>
    public double LatencyMs { get; set; }

    /// <summary>
    /// The node's current load factor (0.0 to 1.0).
    /// </summary>
    public double LoadFactor { get; set; }

    /// <summary>
    /// Timestamp of the last successful health check.
    /// </summary>
    public DateTime? LastHealthCheck { get; set; }
}
