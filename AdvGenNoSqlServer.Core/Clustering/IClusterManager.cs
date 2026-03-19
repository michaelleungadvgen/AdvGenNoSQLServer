// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Manages cluster membership, node discovery, and coordination.
    /// </summary>
    public interface IClusterManager : IDisposable
    {
        /// <summary>
        /// Gets the current node's identity.
        /// </summary>
        NodeIdentity LocalNode { get; }

        /// <summary>
        /// Gets whether this node is currently a member of a cluster.
        /// </summary>
        bool IsClusterMember { get; }

        /// <summary>
        /// Gets whether this node is the current leader.
        /// </summary>
        bool IsLeader { get; }

        /// <summary>
        /// Gets comprehensive information about the cluster.
        /// </summary>
        Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default);

        /// <summary>
        /// Joins an existing cluster through a seed node.
        /// </summary>
        Task<JoinResult> JoinClusterAsync(string seedNode, JoinOptions options, CancellationToken ct = default);

        /// <summary>
        /// Creates a new single-node cluster.
        /// </summary>
        Task<JoinResult> CreateClusterAsync(string clusterName, CancellationToken ct = default);

        /// <summary>
        /// Gracefully leaves the current cluster.
        /// </summary>
        Task<LeaveResult> LeaveClusterAsync(LeaveOptions options, CancellationToken ct = default);

        /// <summary>
        /// Gets all nodes in the cluster.
        /// </summary>
        Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets information about a specific node.
        /// </summary>
        Task<NodeInfo?> GetNodeAsync(string nodeId, CancellationToken ct = default);

        /// <summary>
        /// Removes a dead node from the cluster.
        /// </summary>
        Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default);

        /// <summary>
        /// Gets the current leader node.
        /// </summary>
        Task<NodeInfo?> GetLeaderAsync(CancellationToken ct = default);

        /// <summary>
        /// Requests a leader election (for manual failover).
        /// </summary>
        Task<bool> RequestLeaderElectionAsync(CancellationToken ct = default);

        /// <summary>
        /// Updates the local node's state.
        /// </summary>
        Task<bool> UpdateNodeStateAsync(NodeState newState, CancellationToken ct = default);

        /// <summary>
        /// Event raised when a node joins the cluster.
        /// </summary>
        event EventHandler<NodeJoinedEventArgs>? NodeJoined;

        /// <summary>
        /// Event raised when a node leaves the cluster.
        /// </summary>
        event EventHandler<NodeLeftEventArgs>? NodeLeft;

        /// <summary>
        /// Event raised when the leader changes.
        /// </summary>
        event EventHandler<LeaderChangedEventArgs>? LeaderChanged;

        /// <summary>
        /// Event raised when a node's state changes.
        /// </summary>
        event EventHandler<NodeStateChangedEventArgs>? NodeStateChanged;
    }

    /// <summary>
    /// Result of a join cluster operation.
    /// </summary>
    public class JoinResult
    {
        /// <summary>
        /// Whether the join was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if join failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Information about the cluster after joining.
        /// </summary>
        public ClusterInfo? ClusterInfo { get; set; }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static JoinResult SuccessResult(ClusterInfo info)
        {
            return new JoinResult { Success = true, ClusterInfo = info };
        }

        /// <summary>
        /// Creates a failed result.
        /// </summary>
        public static JoinResult FailureResult(string error)
        {
            return new JoinResult { Success = false, ErrorMessage = error };
        }
    }

    /// <summary>
    /// Result of a leave cluster operation.
    /// </summary>
    public class LeaveResult
    {
        /// <summary>
        /// Whether the leave was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if leave failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static LeaveResult SuccessResult()
        {
            return new LeaveResult { Success = true };
        }

        /// <summary>
        /// Creates a failed result.
        /// </summary>
        public static LeaveResult FailureResult(string error)
        {
            return new LeaveResult { Success = false, ErrorMessage = error };
        }
    }

    /// <summary>
    /// Event arguments for node joined event.
    /// </summary>
    public class NodeJoinedEventArgs : EventArgs
    {
        /// <summary>
        /// The node that joined.
        /// </summary>
        public required NodeInfo Node { get; set; }

        /// <summary>
        /// When the event occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Event arguments for node left event.
    /// </summary>
    public class NodeLeftEventArgs : EventArgs
    {
        /// <summary>
        /// The node that left.
        /// </summary>
        public required NodeInfo Node { get; set; }

        /// <summary>
        /// Whether the node left gracefully.
        /// </summary>
        public bool Graceful { get; set; }

        /// <summary>
        /// When the event occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Event arguments for leader changed event.
    /// </summary>
    public class LeaderChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The previous leader (null if none).
        /// </summary>
        public NodeInfo? PreviousLeader { get; set; }

        /// <summary>
        /// The new leader (null if none).
        /// </summary>
        public NodeInfo? NewLeader { get; set; }

        /// <summary>
        /// The term number of the new leader.
        /// </summary>
        public long Term { get; set; }

        /// <summary>
        /// When the event occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Event arguments for node state changed event.
    /// </summary>
    public class NodeStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The node whose state changed.
        /// </summary>
        public required NodeInfo Node { get; set; }

        /// <summary>
        /// The previous state.
        /// </summary>
        public NodeState PreviousState { get; set; }

        /// <summary>
        /// The new state.
        /// </summary>
        public NodeState NewState { get; set; }

        /// <summary>
        /// When the event occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
