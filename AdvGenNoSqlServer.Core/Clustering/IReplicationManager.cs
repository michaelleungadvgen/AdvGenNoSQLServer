// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Types of replication events.
    /// </summary>
    public enum ReplicationEventType
    {
        /// <summary>
        /// Document insert operation.
        /// </summary>
        Insert,

        /// <summary>
        /// Document update operation.
        /// </summary>
        Update,

        /// <summary>
        /// Document delete operation.
        /// </summary>
        Delete,

        /// <summary>
        /// Collection drop operation.
        /// </summary>
        DropCollection,

        /// <summary>
        /// Index creation operation.
        /// </summary>
        CreateIndex,

        /// <summary>
        /// Index drop operation.
        /// </summary>
        DropIndex
    }

    /// <summary>
    /// Represents a data change event for replication.
    /// </summary>
    public class ReplicationEvent
    {
        /// <summary>
        /// Unique identifier for this replication event.
        /// </summary>
        public string OperationId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// ID of the node that originated this event.
        /// </summary>
        public required string SourceNodeId { get; set; }

        /// <summary>
        /// Type of operation being replicated.
        /// </summary>
        public required ReplicationEventType Type { get; set; }

        /// <summary>
        /// Name of the collection affected.
        /// </summary>
        public required string Collection { get; set; }

        /// <summary>
        /// ID of the document affected (null for collection-level operations).
        /// </summary>
        public string? DocumentId { get; set; }

        /// <summary>
        /// The document data (for inserts/updates).
        /// </summary>
        public Document? Document { get; set; }

        /// <summary>
        /// Previous document state (for updates/deletes, used for conflict resolution).
        /// </summary>
        public Document? PreviousDocument { get; set; }

        /// <summary>
        /// Sequence number from the Write-Ahead Log.
        /// </summary>
        public required long SequenceNumber { get; set; }

        /// <summary>
        /// Timestamp when the event occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Checksum of the document data for integrity verification.
        /// </summary>
        public byte[] Checksum { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// The term in which this event was generated (for Raft consensus).
        /// </summary>
        public long Term { get; set; }

        /// <summary>
        /// Creates a replication event for a document insert.
        /// </summary>
        public static ReplicationEvent Insert(string sourceNodeId, string collection, Document document, long sequenceNumber, long term)
        {
            return new ReplicationEvent
            {
                SourceNodeId = sourceNodeId,
                Type = ReplicationEventType.Insert,
                Collection = collection,
                DocumentId = document.Id,
                Document = document,
                SequenceNumber = sequenceNumber,
                Term = term
            };
        }

        /// <summary>
        /// Creates a replication event for a document update.
        /// </summary>
        public static ReplicationEvent Update(string sourceNodeId, string collection, Document document, Document previousDocument, long sequenceNumber, long term)
        {
            return new ReplicationEvent
            {
                SourceNodeId = sourceNodeId,
                Type = ReplicationEventType.Update,
                Collection = collection,
                DocumentId = document.Id,
                Document = document,
                PreviousDocument = previousDocument,
                SequenceNumber = sequenceNumber,
                Term = term
            };
        }

        /// <summary>
        /// Creates a replication event for a document delete.
        /// </summary>
        public static ReplicationEvent Delete(string sourceNodeId, string collection, string documentId, Document previousDocument, long sequenceNumber, long term)
        {
            return new ReplicationEvent
            {
                SourceNodeId = sourceNodeId,
                Type = ReplicationEventType.Delete,
                Collection = collection,
                DocumentId = documentId,
                PreviousDocument = previousDocument,
                SequenceNumber = sequenceNumber,
                Term = term
            };
        }
    }

    /// <summary>
    /// Acknowledgment from a replica node.
    /// </summary>
    public class ReplicationAck
    {
        /// <summary>
        /// ID of the operation being acknowledged.
        /// </summary>
        public required string OperationId { get; set; }

        /// <summary>
        /// ID of the node sending the acknowledgment.
        /// </summary>
        public required string NodeId { get; set; }

        /// <summary>
        /// Whether the replication was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if replication failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Timestamp when the acknowledgment was sent.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The sequence number that was replicated.
        /// </summary>
        public long SequenceNumber { get; set; }

        /// <summary>
        /// Creates a successful acknowledgment.
        /// </summary>
        public static ReplicationAck SuccessAck(string operationId, string nodeId, long sequenceNumber)
        {
            return new ReplicationAck
            {
                OperationId = operationId,
                NodeId = nodeId,
                Success = true,
                SequenceNumber = sequenceNumber
            };
        }

        /// <summary>
        /// Creates a failed acknowledgment.
        /// </summary>
        public static ReplicationAck FailedAck(string operationId, string nodeId, string errorMessage, long sequenceNumber)
        {
            return new ReplicationAck
            {
                OperationId = operationId,
                NodeId = nodeId,
                Success = false,
                ErrorMessage = errorMessage,
                SequenceNumber = sequenceNumber
            };
        }
    }

    /// <summary>
    /// Synchronization status for a node.
    /// </summary>
    public class SyncStatus
    {
        /// <summary>
        /// ID of the node.
        /// </summary>
        public required string NodeId { get; set; }

        /// <summary>
        /// Whether the node is fully synchronized.
        /// </summary>
        public bool IsSynchronized { get; set; }

        /// <summary>
        /// The last sequence number replicated to this node.
        /// </summary>
        public long LastSequenceNumber { get; set; }

        /// <summary>
        /// Number of events pending replication.
        /// </summary>
        public long PendingEvents { get; set; }

        /// <summary>
        /// Estimated time to full synchronization.
        /// </summary>
        public TimeSpan? EstimatedTimeToSync { get; set; }

        /// <summary>
        /// Timestamp of last successful replication.
        /// </summary>
        public DateTime LastReplicationTime { get; set; }

        /// <summary>
        /// Replication lag in milliseconds.
        /// </summary>
        public double ReplicationLagMs { get; set; }
    }

    /// <summary>
    /// Statistics for replication monitoring.
    /// </summary>
    public class ReplicationStatistics
    {
        /// <summary>
        /// Total number of replication events sent.
        /// </summary>
        public long TotalEventsSent { get; set; }

        /// <summary>
        /// Total number of replication events acknowledged.
        /// </summary>
        public long TotalEventsAcknowledged { get; set; }

        /// <summary>
        /// Total number of replication failures.
        /// </summary>
        public long TotalFailures { get; set; }

        /// <summary>
        /// Number of events pending acknowledgment.
        /// </summary>
        public long PendingEvents { get; set; }

        /// <summary>
        /// Average replication latency in milliseconds.
        /// </summary>
        public double AverageLatencyMs { get; set; }

        /// <summary>
        /// Current replication throughput (events per second).
        /// </summary>
        public double ThroughputPerSecond { get; set; }

        /// <summary>
        /// Per-node replication statistics.
        /// </summary>
        public Dictionary<string, NodeReplicationStats> PerNodeStats { get; set; } = new();

        /// <summary>
        /// Timestamp when statistics were calculated.
        /// </summary>
        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Per-node replication statistics.
    /// </summary>
    public class NodeReplicationStats
    {
        /// <summary>
        /// Node ID.
        /// </summary>
        public required string NodeId { get; set; }

        /// <summary>
        /// Number of events sent to this node.
        /// </summary>
        public long EventsSent { get; set; }

        /// <summary>
        /// Number of events acknowledged by this node.
        /// </summary>
        public long EventsAcknowledged { get; set; }

        /// <summary>
        /// Number of failures for this node.
        /// </summary>
        public long Failures { get; set; }

        /// <summary>
        /// Average latency for this node.
        /// </summary>
        public double AverageLatencyMs { get; set; }

        /// <summary>
        /// Last seen timestamp.
        /// </summary>
        public DateTime LastSeen { get; set; }
    }

    /// <summary>
    /// Result of a replicate write operation.
    /// </summary>
    public class ReplicationResult
    {
        /// <summary>
        /// Whether the replication was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Number of nodes that acknowledged the write.
        /// </summary>
        public int AcknowledgedCount { get; set; }

        /// <summary>
        /// Required quorum for the write.
        /// </summary>
        public int RequiredQuorum { get; set; }

        /// <summary>
        /// Error message if replication failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// List of nodes that acknowledged.
        /// </summary>
        public List<string> AcknowledgingNodes { get; set; } = new();

        /// <summary>
        /// List of nodes that failed to acknowledge.
        /// </summary>
        public Dictionary<string, string> FailedNodes { get; set; } = new();

        /// <summary>
        /// Time taken for replication.
        /// </summary>
        public TimeSpan ReplicationTime { get; set; }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static ReplicationResult SuccessResult(int acknowledgedCount, int requiredQuorum, List<string> acknowledgingNodes, TimeSpan replicationTime)
        {
            return new ReplicationResult
            {
                Success = true,
                AcknowledgedCount = acknowledgedCount,
                RequiredQuorum = requiredQuorum,
                AcknowledgingNodes = acknowledgingNodes,
                ReplicationTime = replicationTime
            };
        }

        /// <summary>
        /// Creates a failed result.
        /// </summary>
        public static ReplicationResult FailureResult(int acknowledgedCount, int requiredQuorum, string errorMessage, Dictionary<string, string> failedNodes, TimeSpan replicationTime)
        {
            return new ReplicationResult
            {
                Success = false,
                AcknowledgedCount = acknowledgedCount,
                RequiredQuorum = requiredQuorum,
                ErrorMessage = errorMessage,
                FailedNodes = failedNodes,
                ReplicationTime = replicationTime
            };
        }
    }

    /// <summary>
    /// Interface for managing data replication across the cluster.
    /// </summary>
    public interface IReplicationManager
    {
        /// <summary>
        /// Configuration for replication behavior.
        /// </summary>
        ReplicationConfiguration Configuration { get; }

        /// <summary>
        /// Current replication statistics.
        /// </summary>
        ReplicationStatistics Statistics { get; }

        /// <summary>
        /// Event raised when a replication event is acknowledged.
        /// </summary>
        event EventHandler<ReplicationAck>? ReplicationAcknowledged;

        /// <summary>
        /// Event raised when a replication failure occurs.
        /// </summary>
        event EventHandler<ReplicationEvent>? ReplicationFailed;

        /// <summary>
        /// Sets the replication factor for a collection.
        /// </summary>
        Task SetReplicationFactorAsync(string collection, int factor, CancellationToken ct = default);

        /// <summary>
        /// Gets the replication factor for a collection.
        /// </summary>
        Task<int> GetReplicationFactorAsync(string collection, CancellationToken ct = default);

        /// <summary>
        /// Replicates a write operation to other nodes.
        /// </summary>
        Task<ReplicationResult> ReplicateWriteAsync(ReplicationEvent evt, CancellationToken ct = default);

        /// <summary>
        /// Replicates a batch of write operations.
        /// </summary>
        Task<ReplicationResult> ReplicateBatchAsync(IEnumerable<ReplicationEvent> events, CancellationToken ct = default);

        /// <summary>
        /// Waits for acknowledgments from replicas.
        /// </summary>
        Task<ReplicationResult> WaitForAcksAsync(string operationId, int requiredAcks, TimeSpan timeout, CancellationToken ct = default);

        /// <summary>
        /// Processes an acknowledgment from a replica.
        /// </summary>
        Task ProcessAckAsync(ReplicationAck ack, CancellationToken ct = default);

        /// <summary>
        /// Gets the synchronization status for a specific node.
        /// </summary>
        Task<SyncStatus> GetSyncStatusAsync(string nodeId, CancellationToken ct = default);

        /// <summary>
        /// Gets synchronization status for all nodes.
        /// </summary>
        Task<IReadOnlyList<SyncStatus>> GetAllSyncStatusAsync(CancellationToken ct = default);

        /// <summary>
        /// Requests a full synchronization from a node.
        /// </summary>
        Task RequestFullSyncAsync(string nodeId, CancellationToken ct = default);

        /// <summary>
        /// Applies a replication event to the local store (used when receiving from another node).
        /// </summary>
        Task ApplyReplicationEventAsync(ReplicationEvent evt, CancellationToken ct = default);

        /// <summary>
        /// Starts the replication manager.
        /// </summary>
        Task StartAsync(CancellationToken ct = default);

        /// <summary>
        /// Stops the replication manager.
        /// </summary>
        Task StopAsync(CancellationToken ct = default);
    }
}
