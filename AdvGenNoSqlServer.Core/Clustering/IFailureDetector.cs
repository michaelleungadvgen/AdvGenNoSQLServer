// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Event args for node failure detection.
    /// </summary>
    public class NodeFailedEventArgs : EventArgs
    {
        /// <summary>
        /// The node that failed.
        /// </summary>
        public required NodeInfo Node { get; set; }

        /// <summary>
        /// Time since the node was last seen.
        /// </summary>
        public TimeSpan TimeSinceLastSeen { get; set; }

        /// <summary>
        /// Reason for the failure detection.
        /// </summary>
        public required string Reason { get; set; }
    }

    /// <summary>
    /// Event args for node recovery detection.
    /// </summary>
    public class NodeRecoveredEventArgs : EventArgs
    {
        /// <summary>
        /// The node that recovered.
        /// </summary>
        public required NodeInfo Node { get; set; }

        /// <summary>
        /// Time the node was considered failed.
        /// </summary>
        public TimeSpan TimeFailed { get; set; }
    }

    /// <summary>
    /// Event args for node suspicion (suspected to be failed but not confirmed).
    /// </summary>
    public class NodeSuspectedEventArgs : EventArgs
    {
        /// <summary>
        /// The node that is suspected to have failed.
        /// </summary>
        public required NodeInfo Node { get; set; }

        /// <summary>
        /// Number of confirmations from other nodes.
        /// </summary>
        public int ConfirmationCount { get; set; }
    }

    /// <summary>
    /// Interface for node failure detection in a cluster.
    /// Implements SWIM-style failure detection with suspicion mechanism.
    /// </summary>
    public interface IFailureDetector
    {
        /// <summary>
        /// Starts the failure detector.
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the failure detector.
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Records that a node has been seen (heartbeat received).
        /// </summary>
        void RecordHeartbeat(string nodeId);

        /// <summary>
        /// Gets the status of a node from the failure detector's perspective.
        /// </summary>
        NodeStatus GetNodeStatus(string nodeId);

        /// <summary>
        /// Gets statistics about failure detection.
        /// </summary>
        FailureDetectorStats GetStats();

        /// <summary>
        /// Event raised when a node is suspected to have failed.
        /// </summary>
        event EventHandler<NodeSuspectedEventArgs>? NodeSuspected;

        /// <summary>
        /// Event raised when a node is confirmed to have failed.
        /// </summary>
        event EventHandler<NodeFailedEventArgs>? NodeFailed;

        /// <summary>
        /// Event raised when a previously failed node recovers.
        /// </summary>
        event EventHandler<NodeRecoveredEventArgs>? NodeRecovered;
    }

    /// <summary>
    /// Status of a node from the failure detector's perspective.
    /// </summary>
    public enum NodeStatus
    {
        /// <summary>
        /// Node is alive and responding.
        /// </summary>
        Alive,

        /// <summary>
        /// Node is suspected to have failed (unconfirmed).
        /// </summary>
        Suspected,

        /// <summary>
        /// Node is confirmed to have failed.
        /// </summary>
        Failed,

        /// <summary>
        /// Node status is unknown (no information).
        /// </summary>
        Unknown
    }

    /// <summary>
    /// Statistics for failure detection.
    /// </summary>
    public class FailureDetectorStats
    {
        /// <summary>
        /// Total number of nodes being monitored.
        /// </summary>
        public int TotalNodes { get; set; }

        /// <summary>
        /// Number of nodes currently alive.
        /// </summary>
        public int AliveNodes { get; set; }

        /// <summary>
        /// Number of nodes currently suspected.
        /// </summary>
        public int SuspectedNodes { get; set; }

        /// <summary>
        /// Number of nodes currently failed.
        /// </summary>
        public int FailedNodes { get; set; }

        /// <summary>
        /// Total number of heartbeats received.
        /// </summary>
        public long TotalHeartbeatsReceived { get; set; }

        /// <summary>
        /// Total number of failures detected.
        /// </summary>
        public long TotalFailuresDetected { get; set; }

        /// <summary>
        /// Total number of suspicions raised.
        /// </summary>
        public long TotalSuspicionsRaised { get; set; }

        /// <summary>
        /// Time when the detector was started.
        /// </summary>
        public DateTime StartedAt { get; set; }
    }
}
