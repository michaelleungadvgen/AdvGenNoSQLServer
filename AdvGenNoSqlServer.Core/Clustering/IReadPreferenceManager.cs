// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Clustering;

/// <summary>
/// Interface for managing read preferences in a clustered NoSQL environment.
/// Read preferences control how read operations are distributed across replica nodes,
/// allowing applications to balance between consistency, availability, and latency.
/// </summary>
public interface IReadPreferenceManager
{
    /// <summary>
    /// Selects a node for a read operation based on the configured read preference options.
    /// </summary>
    /// <param name="options">The read preference options to use for node selection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result containing the selected node or error information</returns>
    Task<ReadPreferenceResult> SelectNodeAsync(ReadPreferenceOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects a node for a read operation using the default options.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result containing the selected node or error information</returns>
    Task<ReadPreferenceResult> SelectNodeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current primary node in the cluster.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The primary node identity, or null if no primary is available</returns>
    Task<NodeIdentity?> GetPrimaryNodeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all secondary nodes in the cluster.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of secondary node identities</returns>
    Task<IReadOnlyList<NodeIdentity>> GetSecondaryNodesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all available nodes (both primary and secondaries) that match the specified criteria.
    /// </summary>
    /// <param name="includeUnhealthy">Whether to include nodes that are currently unhealthy</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of node information objects</returns>
    Task<IReadOnlyList<NodeReadInfo>> GetAvailableNodesAsync(bool includeUnhealthy = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the latency measurement for a specific node.
    /// </summary>
    /// <param name="nodeId">The node ID</param>
    /// <param name="latencyMs">The measured latency in milliseconds</param>
    void UpdateNodeLatency(string nodeId, double latencyMs);

    /// <summary>
    /// Updates the health status of a specific node.
    /// </summary>
    /// <param name="nodeId">The node ID</param>
    /// <param name="isHealthy">Whether the node is healthy</param>
    /// <param name="replicationLagMs">The current replication lag in milliseconds (for secondaries)</param>
    void UpdateNodeHealth(string nodeId, bool isHealthy, long replicationLagMs = 0);

    /// <summary>
    /// Gets the current read preference statistics.
    /// </summary>
    /// <returns>The current statistics</returns>
    ReadPreferenceStatistics GetStatistics();

    /// <summary>
    /// Resets the read preference statistics.
    /// </summary>
    void ResetStatistics();

    /// <summary>
    /// Sets the default read preference options to use when none are specified.
    /// </summary>
    /// <param name="options">The default options</param>
    void SetDefaultOptions(ReadPreferenceOptions options);

    /// <summary>
    /// Gets the default read preference options.
    /// </summary>
    /// <returns>The default options</returns>
    ReadPreferenceOptions GetDefaultOptions();

    /// <summary>
    /// Event raised when a node is selected for a read operation.
    /// </summary>
    event EventHandler<NodeSelectedEventArgs>? NodeSelected;

    /// <summary>
    /// Event raised when no suitable node could be found for a read operation.
    /// </summary>
    event EventHandler<NodeSelectionFailedEventArgs>? NodeSelectionFailed;
}

/// <summary>
/// Event arguments for node selection events.
/// </summary>
public class NodeSelectedEventArgs : EventArgs
{
    /// <summary>
    /// The selected node identity.
    /// </summary>
    public required NodeIdentity Node { get; init; }

    /// <summary>
    /// The read preference mode used for selection.
    /// </summary>
    public required ReadPreferenceMode Mode { get; init; }

    /// <summary>
    /// The time taken to select the node in milliseconds.
    /// </summary>
    public double SelectionTimeMs { get; init; }

    /// <summary>
    /// Whether the selected node is the primary.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// The measured latency to the selected node.
    /// </summary>
    public double? LatencyMs { get; init; }

    /// <summary>
    /// The timestamp when the selection occurred.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event arguments for node selection failure events.
/// </summary>
public class NodeSelectionFailedEventArgs : EventArgs
{
    /// <summary>
    /// The read preference mode that was attempted.
    /// </summary>
    public required ReadPreferenceMode Mode { get; init; }

    /// <summary>
    /// The error message explaining why selection failed.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// The number of nodes that were considered.
    /// </summary>
    public int NodesConsidered { get; init; }

    /// <summary>
    /// The number of nodes that were excluded.
    /// </summary>
    public int NodesExcluded { get; init; }

    /// <summary>
    /// The timestamp when the failed selection occurred.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
