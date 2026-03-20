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
    /// Event args for gossip protocol state updates.
    /// </summary>
    public class GossipStateUpdatedEventArgs : EventArgs
    {
        /// <summary>
        /// The node whose state was updated.
        /// </summary>
        public required NodeInfo Node { get; set; }

        /// <summary>
        /// The previous state of the node.
        /// </summary>
        public NodeState PreviousState { get; set; }

        /// <summary>
        /// The new state of the node.
        /// </summary>
        public NodeState NewState { get; set; }

        /// <summary>
        /// The source of the update (node that sent the gossip).
        /// </summary>
        public required string SourceNodeId { get; set; }
    }

    /// <summary>
    /// Event args for gossip message received.
    /// </summary>
    public class GossipReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// The node that sent the gossip.
        /// </summary>
        public required NodeInfo Sender { get; set; }

        /// <summary>
        /// Number of node states in the gossip message.
        /// </summary>
        public int StateCount { get; set; }
    }

    /// <summary>
    /// Interface for gossip protocol implementation.
    /// Gossip protocols are used for efficient state propagation in distributed systems.
    /// </summary>
    public interface IGossipProtocol
    {
        /// <summary>
        /// Starts the gossip protocol.
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the gossip protocol.
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the local node's state.
        /// </summary>
        Task UpdateLocalStateAsync(NodeState newState, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current view of all node states in the cluster.
        /// </summary>
        IReadOnlyDictionary<string, NodeStateInfo> GetClusterState();

        /// <summary>
        /// Gets statistics about gossip protocol operation.
        /// </summary>
        GossipStats GetStats();

        /// <summary>
        /// Manually triggers a gossip round (useful for testing).
        /// </summary>
        Task TriggerGossipRoundAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Event raised when cluster state is updated via gossip.
        /// </summary>
        event EventHandler<GossipStateUpdatedEventArgs>? StateUpdated;

        /// <summary>
        /// Event raised when a gossip message is received.
        /// </summary>
        event EventHandler<GossipReceivedEventArgs>? GossipReceived;
    }

    /// <summary>
    /// Information about a node's state in the gossip protocol.
    /// </summary>
    public class NodeStateInfo
    {
        /// <summary>
        /// The node information.
        /// </summary>
        public required NodeInfo Node { get; set; }

        /// <summary>
        /// Current state of the node.
        /// </summary>
        public NodeState State { get; set; }

        /// <summary>
        /// Monotonic generation counter for state versioning.
        /// </summary>
        public long Generation { get; set; }

        /// <summary>
        /// Version within the generation for ordering updates.
        /// </summary>
        public long Version { get; set; }

        /// <summary>
        /// Last time this state was updated.
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Node ID of the last node that updated this state.
        /// </summary>
        public string LastUpdatedBy { get; set; } = string.Empty;

        /// <summary>
        /// Heartbeat sequence number for detecting missed messages.
        /// </summary>
        public long HeartbeatSequence { get; set; }
    }

    /// <summary>
    /// Statistics for gossip protocol operation.
    /// </summary>
    public class GossipStats
    {
        /// <summary>
        /// Total number of gossip messages sent.
        /// </summary>
        public long MessagesSent { get; set; }

        /// <summary>
        /// Total number of gossip messages received.
        /// </summary>
        public long MessagesReceived { get; set; }

        /// <summary>
        /// Total number of state updates propagated.
        /// </summary>
        public long StatesPropagated { get; set; }

        /// <summary>
        /// Number of gossip rounds executed.
        /// </summary>
        public long GossipRounds { get; set; }

        /// <summary>
        /// Average gossip message size in bytes.
        /// </summary>
        public double AverageMessageSizeBytes { get; set; }

        /// <summary>
        /// Time when the gossip protocol was started.
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// Current number of nodes in the gossip view.
        /// </summary>
        public int CurrentNodeCount { get; set; }
    }

    /// <summary>
    /// Configuration options for the gossip protocol.
    /// </summary>
    public class GossipOptions
    {
        /// <summary>
        /// Interval between gossip rounds.
        /// </summary>
        public TimeSpan GossipInterval { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Number of nodes to gossip to in each round (fanout).
        /// </summary>
        public int Fanout { get; set; } = 3;

        /// <summary>
        /// Maximum number of nodes to include in a gossip message.
        /// </summary>
        public int MaxNodesPerMessage { get; set; } = 10;

        /// <summary>
        /// Whether to use push-pull gossip (exchange states) or push-only.
        /// </summary>
        public bool UsePushPull { get; set; } = true;

        /// <summary>
        /// Timeout for gossip responses.
        /// </summary>
        public TimeSpan GossipTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Suspicion multiplier for SWIM-style failure detection.
        /// Higher values make the system more tolerant of false positives.
        /// </summary>
        public int SuspicionMultiplier { get; set; } = 4;

        /// <summary>
        /// Maximum suspicion timeout.
        /// </summary>
        public TimeSpan MaxSuspicionTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Whether to enable compression for gossip messages.
        /// </summary>
        public bool EnableCompression { get; set; } = false;

        /// <summary>
        /// Validates the gossip options.
        /// </summary>
        public void Validate()
        {
            if (GossipInterval <= TimeSpan.Zero)
                throw new ArgumentException("Gossip interval must be positive", nameof(GossipInterval));

            if (Fanout < 1)
                throw new ArgumentException("Fanout must be at least 1", nameof(Fanout));

            if (MaxNodesPerMessage < 1)
                throw new ArgumentException("Max nodes per message must be at least 1", nameof(MaxNodesPerMessage));

            if (GossipTimeout <= TimeSpan.Zero)
                throw new ArgumentException("Gossip timeout must be positive", nameof(GossipTimeout));

            if (SuspicionMultiplier < 1)
                throw new ArgumentException("Suspicion multiplier must be at least 1", nameof(SuspicionMultiplier));
        }
    }
}
