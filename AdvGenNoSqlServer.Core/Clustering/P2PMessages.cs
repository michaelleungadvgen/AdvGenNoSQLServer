// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Base class for all P2P messages.
    /// </summary>
    public abstract class P2PMessage
    {
        /// <summary>
        /// Unique message identifier.
        /// </summary>
        public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Type of the message.
        /// </summary>
        public abstract P2PMessageType MessageType { get; }

        /// <summary>
        /// Node ID of the sender.
        /// </summary>
        public required string SenderId { get; set; }

        /// <summary>
        /// Timestamp when the message was created.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// HMAC signature for message authentication.
        /// </summary>
        public byte[] Signature { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Current term in Raft consensus.
        /// </summary>
        public long Term { get; set; }
    }

    /// <summary>
    /// Types of P2P messages.
    /// </summary>
    public enum P2PMessageType
    {
        /// <summary>
        /// Request to join the cluster.
        /// </summary>
        JoinRequest,

        /// <summary>
        /// Response to a join request.
        /// </summary>
        JoinResponse,

        /// <summary>
        /// Periodic heartbeat between nodes.
        /// </summary>
        Heartbeat,

        /// <summary>
        /// Request to leave the cluster.
        /// </summary>
        LeaveRequest,

        /// <summary>
        /// Gossip state propagation.
        /// </summary>
        Gossip,

        /// <summary>
        /// Request for node information.
        /// </summary>
        NodeInfoRequest,

        /// <summary>
        /// Response with node information.
        /// </summary>
        NodeInfoResponse,

        /// <summary>
        /// Leader election vote request.
        /// </summary>
        VoteRequest,

        /// <summary>
        /// Leader election vote response.
        /// </summary>
        VoteResponse,

        /// <summary>
        /// Raft append entries request.
        /// </summary>
        AppendEntriesRequest,

        /// <summary>
        /// Raft append entries response.
        /// </summary>
        AppendEntriesResponse,

        /// <summary>
        /// Data replication event.
        /// </summary>
        ReplicationEvent,

        /// <summary>
        /// Replication acknowledgement.
        /// </summary>
        ReplicationAck
    }

    /// <summary>
    /// Request to join a cluster.
    /// </summary>
    public class JoinRequestMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.JoinRequest;

        /// <summary>
        /// Identity of the node requesting to join.
        /// </summary>
        public required NodeIdentity NodeIdentity { get; set; }

        /// <summary>
        /// Hash of the cluster secret for authentication.
        /// </summary>
        public required string ClusterSecretHash { get; set; }
    }

    /// <summary>
    /// Response to a join request.
    /// </summary>
    public class JoinResponseMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.JoinResponse;

        /// <summary>
        /// Whether the join was accepted.
        /// </summary>
        public bool Accepted { get; set; }

        /// <summary>
        /// Error message if join was rejected.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Current cluster information.
        /// </summary>
        public ClusterInfo? ClusterInfo { get; set; }

        /// <summary>
        /// List of known nodes in the cluster.
        /// </summary>
        public List<NodeInfo> KnownNodes { get; set; } = new();

        /// <summary>
        /// Current leader node ID.
        /// </summary>
        public string? LeaderNodeId { get; set; }
    }

    /// <summary>
    /// Heartbeat message for failure detection.
    /// </summary>
    public class HeartbeatMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.Heartbeat;

        /// <summary>
        /// Current node state.
        /// </summary>
        public NodeState NodeState { get; set; }

        /// <summary>
        /// Monotonic counter for ordering.
        /// </summary>
        public long SequenceNumber { get; set; }
    }

    /// <summary>
    /// Request to leave the cluster gracefully.
    /// </summary>
    public class LeaveRequestMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.LeaveRequest;

        /// <summary>
        /// Reason for leaving.
        /// </summary>
        public string Reason { get; set; } = "Graceful shutdown";
    }

    /// <summary>
    /// Gossip message for state propagation.
    /// </summary>
    public class GossipMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.Gossip;

        /// <summary>
        /// Monotonic generation counter.
        /// </summary>
        public long Generation { get; set; }

        /// <summary>
        /// State version.
        /// </summary>
        public long Version { get; set; }

        /// <summary>
        /// Known node states.
        /// </summary>
        public Dictionary<string, NodeState> NodeStates { get; set; } = new();

        /// <summary>
        /// Node heartbeat counters.
        /// </summary>
        public Dictionary<string, long> Heartbeats { get; set; } = new();
    }

    /// <summary>
    /// Request for node information.
    /// </summary>
    public class NodeInfoRequestMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.NodeInfoRequest;

        /// <summary>
        /// Node IDs being requested (empty = all nodes).
        /// </summary>
        public List<string> RequestedNodeIds { get; set; } = new();
    }

    /// <summary>
    /// Response with node information.
    /// </summary>
    public class NodeInfoResponseMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.NodeInfoResponse;

        /// <summary>
        /// Requested node information.
        /// </summary>
        public List<NodeInfo> Nodes { get; set; } = new();
    }

    /// <summary>
    /// Raft vote request for leader election.
    /// </summary>
    public class VoteRequestMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.VoteRequest;

        /// <summary>
        /// Candidate's node ID.
        /// </summary>
        public required string CandidateId { get; set; }

        /// <summary>
        /// Index of candidate's last log entry.
        /// </summary>
        public long LastLogIndex { get; set; }

        /// <summary>
        /// Term of candidate's last log entry.
        /// </summary>
        public long LastLogTerm { get; set; }
    }

    /// <summary>
    /// Raft vote response.
    /// </summary>
    public class VoteResponseMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.VoteResponse;

        /// <summary>
        /// Whether the vote was granted.
        /// </summary>
        public bool VoteGranted { get; set; }

        /// <summary>
        /// Voter's current term.
        /// </summary>
        public long VoterTerm { get; set; }
    }

    /// <summary>
    /// Replication event for data synchronization.
    /// </summary>
    public class ReplicationMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.ReplicationEvent;

        /// <summary>
        /// Unique operation identifier.
        /// </summary>
        public required string OperationId { get; set; }

        /// <summary>
        /// Type of operation.
        /// </summary>
        public ReplicationOperationType OperationType { get; set; }

        /// <summary>
        /// Collection being modified.
        /// </summary>
        public required string Collection { get; set; }

        /// <summary>
        /// Document ID being modified.
        /// </summary>
        public required string DocumentId { get; set; }

        /// <summary>
        /// WAL sequence number.
        /// </summary>
        public long SequenceNumber { get; set; }

        /// <summary>
        /// Checksum for data integrity.
        /// </summary>
        public byte[] Checksum { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Types of replication operations.
    /// </summary>
    public enum ReplicationOperationType
    {
        Insert,
        Update,
        Delete
    }

    /// <summary>
    /// Acknowledgement of a replication event.
    /// </summary>
    public class ReplicationAckMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.ReplicationAck;

        /// <summary>
        /// Operation ID being acknowledged.
        /// </summary>
        public required string OperationId { get; set; }

        /// <summary>
        /// Whether the operation was successfully applied.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if failed.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Raft AppendEntries RPC request for log replication.
    /// </summary>
    public class AppendEntriesRequestMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.AppendEntriesRequest;

        /// <summary>
        /// Leader's node ID.
        /// </summary>
        public required string LeaderId { get; set; }

        /// <summary>
        /// Index of log entry immediately preceding new ones.
        /// </summary>
        public long PrevLogIndex { get; set; }

        /// <summary>
        /// Term of prevLogIndex entry.
        /// </summary>
        public long PrevLogTerm { get; set; }

        /// <summary>
        /// Log entries to store (empty for heartbeat).
        /// </summary>
        public List<RaftLogEntry> Entries { get; set; } = new();

        /// <summary>
        /// Leader's commit index.
        /// </summary>
        public long LeaderCommit { get; set; }

        /// <summary>
        /// Whether this is a heartbeat (no entries).
        /// </summary>
        public bool IsHeartbeat => Entries.Count == 0;
    }

    /// <summary>
    /// Raft AppendEntries RPC response.
    /// </summary>
    public class AppendEntriesResponseMessage : P2PMessage
    {
        /// <inheritdoc/>
        public override P2PMessageType MessageType => P2PMessageType.AppendEntriesResponse;

        /// <summary>
        /// Current term of the follower.
        /// </summary>
        public long CurrentTerm { get; set; }

        /// <summary>
        /// Whether the append was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Conflicting index hint for optimization.
        /// </summary>
        public long ConflictIndex { get; set; }

        /// <summary>
        /// Follower's node ID.
        /// </summary>
        public required string FollowerId { get; set; }
    }
}
