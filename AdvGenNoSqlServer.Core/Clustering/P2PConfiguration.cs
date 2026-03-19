// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Configuration for P2P cluster networking.
    /// </summary>
    public class P2PConfiguration
    {
        /// <summary>
        /// Whether clustering is enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Unique identifier for the cluster.
        /// </summary>
        public string ClusterId { get; set; } = "default-cluster";

        /// <summary>
        /// Human-readable name for the cluster.
        /// </summary>
        public string ClusterName { get; set; } = "Default Cluster";

        /// <summary>
        /// Unique identifier for this node (auto-generated if empty).
        /// </summary>
        public string NodeId { get; set; } = "";

        /// <summary>
        /// Hostname or IP address to bind to for P2P connections.
        /// </summary>
        public string BindAddress { get; set; } = "0.0.0.0";

        /// <summary>
        /// Port for P2P inter-node communication.
        /// </summary>
        public int P2PPort { get; set; } = 9092;

        /// <summary>
        /// Address to advertise to other nodes (if different from bind address).
        /// </summary>
        public string? AdvertiseAddress { get; set; }

        /// <summary>
        /// Port to advertise (if different from P2P port).
        /// </summary>
        public int? AdvertisePort { get; set; }

        /// <summary>
        /// Connection timeout for P2P connections.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Interval between heartbeat messages.
        /// </summary>
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Timeout before considering a node dead.
        /// </summary>
        public TimeSpan DeadNodeTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Cluster mode (LeaderFollower, MultiLeader, Leaderless).
        /// </summary>
        public ClusterMode Mode { get; set; } = ClusterMode.LeaderFollower;

        /// <summary>
        /// Node discovery configuration.
        /// </summary>
        public DiscoveryConfiguration Discovery { get; set; } = new();

        /// <summary>
        /// Security configuration for P2P connections.
        /// </summary>
        public P2PSecurityConfiguration Security { get; set; } = new();

        /// <summary>
        /// Replication configuration.
        /// </summary>
        public ReplicationConfiguration Replication { get; set; } = new();

        /// <summary>
        /// Gets the effective advertise address.
        /// </summary>
        public string GetAdvertiseAddress() => AdvertiseAddress ?? BindAddress;

        /// <summary>
        /// Gets the effective advertise port.
        /// </summary>
        public int GetAdvertisePort() => AdvertisePort ?? P2PPort;

        /// <summary>
        /// Validates the configuration.
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(ClusterId))
                errors.Add("ClusterId cannot be empty");

            if (P2PPort < 1 || P2PPort > 65535)
                errors.Add("P2PPort must be between 1 and 65535");

            if (ConnectionTimeout <= TimeSpan.Zero)
                errors.Add("ConnectionTimeout must be positive");

            if (HeartbeatInterval <= TimeSpan.Zero)
                errors.Add("HeartbeatInterval must be positive");

            if (DeadNodeTimeout <= HeartbeatInterval)
                errors.Add("DeadNodeTimeout must be greater than HeartbeatInterval");

            return errors.Count == 0;
        }
    }

    /// <summary>
    /// Configuration for node discovery.
    /// </summary>
    public class DiscoveryConfiguration
    {
        /// <summary>
        /// Discovery method (StaticSeeds, Dns, Multicast, Kubernetes, Consul).
        /// </summary>
        public string Method { get; set; } = "StaticSeeds";

        /// <summary>
        /// List of seed nodes for static discovery (format: host:port).
        /// </summary>
        public string[] Seeds { get; set; } = Array.Empty<string>();

        /// <summary>
        /// DNS name for DNS-based discovery.
        /// </summary>
        public string? DnsName { get; set; }

        /// <summary>
        /// Multicast group for LAN discovery.
        /// </summary>
        public string? MulticastGroup { get; set; }

        /// <summary>
        /// Interval to refresh discovery.
        /// </summary>
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Security configuration for P2P connections.
    /// </summary>
    public class P2PSecurityConfiguration
    {
        /// <summary>
        /// Whether to require mutual TLS for inter-node communication.
        /// </summary>
        public bool RequireMutualTls { get; set; } = true;

        /// <summary>
        /// Path to the certificate file.
        /// </summary>
        public string? CertificatePath { get; set; }

        /// <summary>
        /// Path to the private key file.
        /// </summary>
        public string? PrivateKeyPath { get; set; }

        /// <summary>
        /// Path to the CA certificate file.
        /// </summary>
        public string? CaCertificatePath { get; set; }

        /// <summary>
        /// Shared cluster secret for additional authentication layer.
        /// </summary>
        public string? ClusterSecret { get; set; }

        /// <summary>
        /// Whether to sign messages.
        /// </summary>
        public bool SignMessages { get; set; } = true;

        /// <summary>
        /// Signature algorithm (Ed25519 or HMAC-SHA256).
        /// </summary>
        public string SignatureAlgorithm { get; set; } = "HMAC-SHA256";

        /// <summary>
        /// Maximum age of messages to prevent replay attacks.
        /// </summary>
        public TimeSpan MessageMaxAge { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Allowed certificate thumbprints for additional validation.
        /// </summary>
        public string[] AllowedThumbprints { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Configuration for data replication.
    /// </summary>
    public class ReplicationConfiguration
    {
        /// <summary>
        /// Replication strategy (Synchronous, SemiSynchronous, Asynchronous).
        /// </summary>
        public string Strategy { get; set; } = "SemiSynchronous";

        /// <summary>
        /// Number of replicas to maintain.
        /// </summary>
        public int ReplicationFactor { get; set; } = 3;

        /// <summary>
        /// Number of acknowledgements required for writes.
        /// </summary>
        public int WriteQuorum { get; set; } = 2;

        /// <summary>
        /// Number of acknowledgements required for reads.
        /// </summary>
        public int ReadQuorum { get; set; } = 1;

        /// <summary>
        /// Timeout for synchronous replication.
        /// </summary>
        public TimeSpan SyncTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }
}
