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
    /// Represents the current state of the P2P manager.
    /// </summary>
    public enum P2PManagerState
    {
        /// <summary>
        /// The manager is initializing and components are being set up.
        /// </summary>
        Initializing,

        /// <summary>
        /// The manager is running and all components are active.
        /// </summary>
        Running,

        /// <summary>
        /// The manager is gracefully shutting down.
        /// </summary>
        Stopping,

        /// <summary>
        /// The manager has stopped.
        /// </summary>
        Stopped,

        /// <summary>
        /// The manager encountered an error and is in a failed state.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Event arguments for P2P manager state changes.
    /// </summary>
    public class P2PManagerStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The previous state.
        /// </summary>
        public P2PManagerState PreviousState { get; set; }

        /// <summary>
        /// The new state.
        /// </summary>
        public P2PManagerState NewState { get; set; }

        /// <summary>
        /// The time of the state change.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Error message if transitioning to Failed state.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Event arguments for peer connection status changes.
    /// </summary>
    public class PeerConnectionEventArgs : EventArgs
    {
        /// <summary>
        /// Information about the peer node.
        /// </summary>
        public required NodeInfo Node { get; set; }

        /// <summary>
        /// Whether the peer is now connected.
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// The time of the connection change.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Error message if connection failed.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Event arguments for cluster topology changes.
    /// </summary>
    public class ClusterTopologyChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The previous number of nodes in the cluster.
        /// </summary>
        public int PreviousNodeCount { get; set; }

        /// <summary>
        /// The current number of nodes in the cluster.
        /// </summary>
        public int CurrentNodeCount { get; set; }

        /// <summary>
        /// Information about the node that changed (joined or left).
        /// </summary>
        public NodeInfo? ChangedNode { get; set; }

        /// <summary>
        /// Whether a node joined (true) or left (false).
        /// </summary>
        public bool NodeJoined { get; set; }

        /// <summary>
        /// The time of the topology change.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Event arguments for replication status changes.
    /// </summary>
    public class ReplicationStatusEventArgs : EventArgs
    {
        /// <summary>
        /// The source node of the replication event.
        /// </summary>
        public required string SourceNodeId { get; set; }

        /// <summary>
        /// The operation type.
        /// </summary>
        public ReplicationEventType OperationType { get; set; }

        /// <summary>
        /// The collection affected.
        /// </summary>
        public string? Collection { get; set; }

        /// <summary>
        /// Whether the replication was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if replication failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// The time of the replication event.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Event arguments for P2P errors.
    /// </summary>
    public class P2PErrorEventArgs : EventArgs
    {
        /// <summary>
        /// The error code.
        /// </summary>
        public required string ErrorCode { get; set; }

        /// <summary>
        /// The error message.
        /// </summary>
        public required string ErrorMessage { get; set; }

        /// <summary>
        /// The exception if available.
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// The component that raised the error.
        /// </summary>
        public string? Component { get; set; }

        /// <summary>
        /// The time of the error.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Statistics for an individual P2P node.
    /// </summary>
    public class P2PNodeStatistics
    {
        /// <summary>
        /// The node ID.
        /// </summary>
        public required string NodeId { get; set; }

        /// <summary>
        /// The node endpoint.
        /// </summary>
        public string? Endpoint { get; set; }

        /// <summary>
        /// Current connection state.
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// Current node state.
        /// </summary>
        public NodeState State { get; set; }

        /// <summary>
        /// Whether this node is the leader.
        /// </summary>
        public bool IsLeader { get; set; }

        /// <summary>
        /// Last time a message was received from this node.
        /// </summary>
        public DateTime? LastMessageReceived { get; set; }

        /// <summary>
        /// Average latency to this node in milliseconds.
        /// </summary>
        public double AverageLatencyMs { get; set; }

        /// <summary>
        /// Number of messages sent to this node.
        /// </summary>
        public long MessagesSent { get; set; }

        /// <summary>
        /// Number of messages received from this node.
        /// </summary>
        public long MessagesReceived { get; set; }

        /// <summary>
        /// Number of replication events sent to this node.
        /// </summary>
        public long ReplicationEventsSent { get; set; }

        /// <summary>
        /// Number of replication events acknowledged by this node.
        /// </summary>
        public long ReplicationEventsAcked { get; set; }

        /// <summary>
        /// Replication lag in milliseconds.
        /// </summary>
        public double ReplicationLagMs { get; set; }
    }

    /// <summary>
    /// Comprehensive statistics for the P2P manager.
    /// </summary>
    public class P2PManagerStatistics
    {
        /// <summary>
        /// Current state of the P2P manager.
        /// </summary>
        public P2PManagerState State { get; set; }

        /// <summary>
        /// Time when the manager was started.
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// Uptime of the manager.
        /// </summary>
        public TimeSpan? Uptime => StartedAt.HasValue ? DateTime.UtcNow - StartedAt.Value : null;

        /// <summary>
        /// Total number of nodes in the cluster (including local).
        /// </summary>
        public int TotalNodes { get; set; }

        /// <summary>
        /// Number of currently connected nodes.
        /// </summary>
        public int ConnectedNodes { get; set; }

        /// <summary>
        /// Number of nodes in each state.
        /// </summary>
        public Dictionary<NodeState, int> NodesByState { get; set; } = new();

        /// <summary>
        /// Per-node statistics.
        /// </summary>
        public List<P2PNodeStatistics> NodeStatistics { get; set; } = new();

        /// <summary>
        /// Gossip protocol statistics.
        /// </summary>
        public GossipStats? GossipStatistics { get; set; }

        /// <summary>
        /// Replication statistics.
        /// </summary>
        public ReplicationStatistics? ReplicationStatistics { get; set; }

        /// <summary>
        /// Total number of P2P errors encountered.
        /// </summary>
        public long TotalErrors { get; set; }

        /// <summary>
        /// Number of successful cluster joins.
        /// </summary>
        public long SuccessfulJoins { get; set; }

        /// <summary>
        /// Number of failed cluster joins.
        /// </summary>
        public long FailedJoins { get; set; }

        /// <summary>
        /// Time when statistics were calculated.
        /// </summary>
        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Configuration options for the P2P manager.
    /// </summary>
    public class P2PManagerOptions
    {
        /// <summary>
        /// Whether to enable automatic cluster joining via seed discovery.
        /// </summary>
        public bool EnableAutoJoin { get; set; } = true;

        /// <summary>
        /// Whether to enable the gossip protocol.
        /// </summary>
        public bool EnableGossip { get; set; } = true;

        /// <summary>
        /// Whether to enable Raft consensus for leader election.
        /// </summary>
        public bool EnableRaft { get; set; } = true;

        /// <summary>
        /// Whether to enable data replication.
        /// </summary>
        public bool EnableReplication { get; set; } = true;

        /// <summary>
        /// Whether to enable automatic conflict resolution.
        /// </summary>
        public bool EnableConflictResolution { get; set; } = true;

        /// <summary>
        /// Strategy to use for conflict resolution.
        /// </summary>
        public ConflictResolutionStrategy ConflictResolutionStrategy { get; set; } = ConflictResolutionStrategy.LastWriteWins;

        /// <summary>
        /// Timeout for cluster join operations.
        /// </summary>
        public TimeSpan JoinTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Timeout for graceful leave operations.
        /// </summary>
        public TimeSpan LeaveTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Interval for collecting statistics.
        /// </summary>
        public TimeSpan StatisticsInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Maximum number of reconnection attempts for failed nodes.
        /// </summary>
        public int MaxReconnectionAttempts { get; set; } = 3;

        /// <summary>
        /// Delay between reconnection attempts.
        /// </summary>
        public TimeSpan ReconnectionDelay { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Validates the options.
        /// </summary>
        public void Validate()
        {
            if (JoinTimeout <= TimeSpan.Zero)
                throw new ArgumentException("Join timeout must be positive", nameof(JoinTimeout));

            if (LeaveTimeout <= TimeSpan.Zero)
                throw new ArgumentException("Leave timeout must be positive", nameof(LeaveTimeout));

            if (StatisticsInterval <= TimeSpan.Zero)
                throw new ArgumentException("Statistics interval must be positive", nameof(StatisticsInterval));

            if (MaxReconnectionAttempts < 0)
                throw new ArgumentException("Max reconnection attempts must be non-negative", nameof(MaxReconnectionAttempts));

            if (ReconnectionDelay <= TimeSpan.Zero)
                throw new ArgumentException("Reconnection delay must be positive", nameof(ReconnectionDelay));
        }
    }

    /// <summary>
    /// Interface for the P2P manager that coordinates all P2P clustering components.
    /// This is the central coordinator for P2P operations including cluster membership,
    /// gossip protocol, Raft consensus, data replication, and conflict resolution.
    /// </summary>
    public interface IP2PManager : IDisposable
    {
        /// <summary>
        /// Gets the current state of the P2P manager.
        /// </summary>
        P2PManagerState State { get; }

        /// <summary>
        /// Gets the local node identity.
        /// </summary>
        NodeIdentity LocalNode { get; }

        /// <summary>
        /// Gets the current cluster information.
        /// </summary>
        ClusterInfo? ClusterInfo { get; }

        /// <summary>
        /// Gets whether this node is connected to a cluster.
        /// </summary>
        bool IsClusterConnected { get; }

        /// <summary>
        /// Gets whether this node is the current leader.
        /// </summary>
        bool IsLeader { get; }

        /// <summary>
        /// Gets the P2P configuration.
        /// </summary>
        P2PConfiguration Configuration { get; }

        /// <summary>
        /// Gets the manager options.
        /// </summary>
        P2PManagerOptions Options { get; }

        /// <summary>
        /// Gets the cluster manager.
        /// </summary>
        IClusterManager ClusterManager { get; }

        /// <summary>
        /// Gets the gossip protocol (if enabled).
        /// </summary>
        IGossipProtocol? GossipProtocol { get; }

        /// <summary>
        /// Gets the replication manager (if enabled).
        /// </summary>
        IReplicationManager? ReplicationManager { get; }

        /// <summary>
        /// Initializes the P2P manager and all components.
        /// </summary>
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>
        /// Starts the P2P manager and all components.
        /// </summary>
        Task StartAsync(CancellationToken ct = default);

        /// <summary>
        /// Stops the P2P manager and all components gracefully.
        /// </summary>
        Task StopAsync(CancellationToken ct = default);

        /// <summary>
        /// Joins an existing cluster using seed discovery.
        /// </summary>
        Task<JoinResult> JoinClusterAsync(CancellationToken ct = default);

        /// <summary>
        /// Creates a new cluster with this node as the initial member.
        /// </summary>
        Task<JoinResult> CreateClusterAsync(string clusterName, CancellationToken ct = default);

        /// <summary>
        /// Leaves the current cluster gracefully.
        /// </summary>
        Task<LeaveResult> LeaveClusterAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets comprehensive statistics about the P2P system.
        /// </summary>
        Task<P2PManagerStatistics> GetStatisticsAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets the current leader node information.
        /// </summary>
        Task<NodeInfo?> GetLeaderAsync(CancellationToken ct = default);

        /// <summary>
        /// Requests a leader election.
        /// </summary>
        Task<bool> RequestLeaderElectionAsync(CancellationToken ct = default);

        /// <summary>
        /// Replicates a write operation to other nodes.
        /// </summary>
        Task<ReplicationResult> ReplicateWriteAsync(ReplicationEvent evt, CancellationToken ct = default);

        /// <summary>
        /// Gets the health status of the P2P system.
        /// </summary>
        Task<P2PHealthStatus> GetHealthStatusAsync(CancellationToken ct = default);

        /// <summary>
        /// Event raised when the manager state changes.
        /// </summary>
        event EventHandler<P2PManagerStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Event raised when a peer connection status changes.
        /// </summary>
        event EventHandler<PeerConnectionEventArgs>? PeerConnectionChanged;

        /// <summary>
        /// Event raised when the cluster topology changes.
        /// </summary>
        event EventHandler<ClusterTopologyChangedEventArgs>? ClusterTopologyChanged;

        /// <summary>
        /// Event raised when replication status changes.
        /// </summary>
        event EventHandler<ReplicationStatusEventArgs>? ReplicationStatusChanged;

        /// <summary>
        /// Event raised when an error occurs in the P2P system.
        /// </summary>
        event EventHandler<P2PErrorEventArgs>? ErrorOccurred;
    }

    /// <summary>
    /// Health status for the P2P system.
    /// </summary>
    public class P2PHealthStatus
    {
        /// <summary>
        /// Whether the P2P system is healthy.
        /// </summary>
        public bool IsHealthy { get; set; }

        /// <summary>
        /// Overall health status.
        /// </summary>
        public HealthStatus Status { get; set; }

        /// <summary>
        /// Health status message.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Component-specific health statuses.
        /// </summary>
        public Dictionary<string, ComponentHealth> ComponentHealth { get; set; } = new();

        /// <summary>
        /// Time when the health status was calculated.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Health status for an individual component.
    /// </summary>
    public class ComponentHealth
    {
        /// <summary>
        /// Component name.
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// Whether the component is healthy.
        /// </summary>
        public bool IsHealthy { get; set; }

        /// <summary>
        /// Status message.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Last error if any.
        /// </summary>
        public string? LastError { get; set; }
    }

    /// <summary>
    /// Overall health status enumeration.
    /// </summary>
    public enum HealthStatus
    {
        /// <summary>
        /// System is healthy.
        /// </summary>
        Healthy,

        /// <summary>
        /// System is degraded but functional.
        /// </summary>
        Degraded,

        /// <summary>
        /// System is unhealthy.
        /// </summary>
        Unhealthy
    }
}
