// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Represents the overall health status of a cluster.
    /// </summary>
    public enum ClusterHealth
    {
        /// <summary>
        /// All nodes are operational.
        /// </summary>
        Healthy,

        /// <summary>
        /// Some nodes are experiencing issues but cluster is functional.
        /// </summary>
        Degraded,

        /// <summary>
        /// Cluster is not functional (e.g., no leader, partition detected).
        /// </summary>
        Unhealthy,

        /// <summary>
        /// Cluster state is unknown (e.g., during startup).
        /// </summary>
        Unknown
    }

    /// <summary>
    /// Represents the operational mode of the cluster.
    /// </summary>
    public enum ClusterMode
    {
        /// <summary>
        /// Single leader handles writes, followers replicate.
        /// </summary>
        LeaderFollower,

        /// <summary>
        /// Multiple nodes accept writes with conflict resolution.
        /// </summary>
        MultiLeader,

        /// <summary>
        /// Any node handles any operation.
        /// </summary>
        Leaderless
    }

    /// <summary>
    /// Provides comprehensive information about a cluster.
    /// </summary>
    public class ClusterInfo
    {
        /// <summary>
        /// Unique identifier of the cluster.
        /// </summary>
        public required string ClusterId { get; set; }

        /// <summary>
        /// Human-readable name of the cluster.
        /// </summary>
        public required string ClusterName { get; set; }

        /// <summary>
        /// Current leader node information.
        /// </summary>
        public NodeInfo? Leader { get; set; }

        /// <summary>
        /// All nodes in the cluster.
        /// </summary>
        public IReadOnlyList<NodeInfo> Nodes { get; set; } = Array.Empty<NodeInfo>();

        /// <summary>
        /// Overall health status of the cluster.
        /// </summary>
        public ClusterHealth Health { get; set; } = ClusterHealth.Unknown;

        /// <summary>
        /// When the cluster was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Current cluster mode.
        /// </summary>
        public ClusterMode Mode { get; set; } = ClusterMode.LeaderFollower;

        /// <summary>
        /// Number of active nodes in the cluster.
        /// </summary>
        public int ActiveNodeCount => Nodes?.Count(n => n.State == NodeState.Active) ?? 0;

        /// <summary>
        /// Total number of nodes in the cluster.
        /// </summary>
        public int TotalNodeCount => Nodes?.Count ?? 0;

        /// <summary>
        /// Whether the cluster has an elected leader.
        /// </summary>
        public bool HasLeader => Leader != null;

        /// <summary>
        /// Whether the cluster can accept write operations.
        /// </summary>
        public bool IsWritable => HasLeader && Health != ClusterHealth.Unhealthy;

        /// <summary>
        /// Gets the quorum size (majority of nodes) for the cluster.
        /// </summary>
        public int QuorumSize => (TotalNodeCount / 2) + 1;

        /// <summary>
        /// Creates a summary string of the cluster state.
        /// </summary>
        public string GetSummary()
        {
            return $"Cluster '{ClusterName}' ({ClusterId}): {ActiveNodeCount}/{TotalNodeCount} nodes active, Health: {Health}, Leader: {Leader?.NodeId[..8] ?? "none"}";
        }

        /// <inheritdoc/>
        public override string ToString() => GetSummary();
    }

    /// <summary>
    /// Configuration options for cluster membership and discovery.
    /// </summary>
    public class JoinOptions
    {
        /// <summary>
        /// Seed node address to join through (format: host:port).
        /// </summary>
        public required string SeedNode { get; set; }

        /// <summary>
        /// Cluster secret for authentication.
        /// </summary>
        public string? ClusterSecret { get; set; }

        /// <summary>
        /// Tags to assign to this node.
        /// </summary>
        public string[] Tags { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Timeout for the join operation.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Options for gracefully leaving a cluster.
    /// </summary>
    public class LeaveOptions
    {
        /// <summary>
        /// Whether to replicate data to other nodes before leaving.
        /// </summary>
        public bool ReplicateData { get; set; } = true;

        /// <summary>
        /// Timeout for the leave operation.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Reason for leaving (for logging).
        /// </summary>
        public string Reason { get; set; } = "Graceful shutdown";
    }
}
