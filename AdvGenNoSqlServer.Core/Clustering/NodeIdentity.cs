// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Represents the identity and state of a node in the cluster.
    /// </summary>
    public class NodeIdentity
    {
        /// <summary>
        /// Unique GUID for this node.
        /// </summary>
        public required string NodeId { get; set; }

        /// <summary>
        /// Cluster membership identifier.
        /// </summary>
        public required string ClusterId { get; set; }

        /// <summary>
        /// IP address or hostname of the node.
        /// </summary>
        public required string Host { get; set; }

        /// <summary>
        /// TCP port for client connections.
        /// </summary>
        public required int Port { get; set; }

        /// <summary>
        /// Inter-node communication port (separate from client port).
        /// </summary>
        public required int P2PPort { get; set; }

        /// <summary>
        /// Public key for node authentication.
        /// </summary>
        public byte[] PublicKey { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Tags for node classification (e.g., "primary", "analytics", "region-us").
        /// </summary>
        public string[] Tags { get; set; } = Array.Empty<string>();

        /// <summary>
        /// When the node joined the cluster.
        /// </summary>
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Current state of the node in the cluster lifecycle.
        /// </summary>
        public NodeState State { get; set; } = NodeState.Joining;

        /// <summary>
        /// Last time this node was seen/updated.
        /// </summary>
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Node version for compatibility checking.
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// Creates a new node identity with a generated GUID.
        /// </summary>
        public static NodeIdentity Create(string clusterId, string host, int port, int p2pPort)
        {
            return new NodeIdentity
            {
                NodeId = Guid.NewGuid().ToString("N"),
                ClusterId = clusterId,
                Host = host,
                Port = port,
                P2PPort = p2pPort
            };
        }

        /// <summary>
        /// Creates a deep copy of this identity.
        /// </summary>
        public NodeIdentity Clone()
        {
            return new NodeIdentity
            {
                NodeId = NodeId,
                ClusterId = ClusterId,
                Host = Host,
                Port = Port,
                P2PPort = P2PPort,
                PublicKey = (byte[])PublicKey.Clone(),
                Tags = (string[])Tags.Clone(),
                JoinedAt = JoinedAt,
                State = State,
                LastSeenAt = LastSeenAt,
                Version = Version
            };
        }

        /// <summary>
        /// Gets the endpoint string for P2P connections.
        /// </summary>
        public string GetP2PEndpoint() => $"{Host}:{P2PPort}";

        /// <summary>
        /// Gets the endpoint string for client connections.
        /// </summary>
        public string GetClientEndpoint() => $"{Host}:{Port}";

        /// <inheritdoc/>
        public override string ToString() => $"Node({NodeId[..8]}.., {State}, {GetP2PEndpoint()})";
    }

    /// <summary>
    /// Represents the state of a node in the cluster lifecycle.
    /// </summary>
    public enum NodeState
    {
        /// <summary>
        /// Node is attempting to join the cluster.
        /// </summary>
        Joining,

        /// <summary>
        /// Node is catching up on data from other nodes.
        /// </summary>
        Syncing,

        /// <summary>
        /// Node is fully operational in the cluster.
        /// </summary>
        Active,

        /// <summary>
        /// Node is gracefully departing the cluster.
        /// </summary>
        Leaving,

        /// <summary>
        /// Node is unreachable or failed.
        /// </summary>
        Dead
    }

    /// <summary>
    /// Provides information about a node in the cluster.
    /// </summary>
    public class NodeInfo
    {
        /// <summary>
        /// Unique identifier of the node.
        /// </summary>
        public required string NodeId { get; set; }

        /// <summary>
        /// Host address of the node.
        /// </summary>
        public required string Host { get; set; }

        /// <summary>
        /// P2P port for inter-node communication.
        /// </summary>
        public required int P2PPort { get; set; }

        /// <summary>
        /// Current state of the node.
        /// </summary>
        public NodeState State { get; set; }

        /// <summary>
        /// When the node joined the cluster.
        /// </summary>
        public DateTime JoinedAt { get; set; }

        /// <summary>
        /// Last time the node was seen.
        /// </summary>
        public DateTime LastSeenAt { get; set; }

        /// <summary>
        /// Tags assigned to the node.
        /// </summary>
        public string[] Tags { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Current term in Raft consensus (if applicable).
        /// </summary>
        public long Term { get; set; }

        /// <summary>
        /// Whether this node is the current leader.
        /// </summary>
        public bool IsLeader { get; set; }

        /// <summary>
        /// Creates NodeInfo from a NodeIdentity.
        /// </summary>
        public static NodeInfo FromIdentity(NodeIdentity identity)
        {
            return new NodeInfo
            {
                NodeId = identity.NodeId,
                Host = identity.Host,
                P2PPort = identity.P2PPort,
                State = identity.State,
                JoinedAt = identity.JoinedAt,
                LastSeenAt = identity.LastSeenAt,
                Tags = (string[])identity.Tags.Clone()
            };
        }
    }
}
