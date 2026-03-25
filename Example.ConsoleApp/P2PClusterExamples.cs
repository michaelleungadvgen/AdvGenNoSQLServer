// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Clustering;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;

namespace AdvGenNoSqlServer.Example.ConsoleApp
{
    /// <summary>
    /// P2P Cluster Examples - Demonstrates distributed clustering capabilities
    /// 
    /// These examples show how to:
    /// - Join a cluster using seed nodes
    /// - Discover nodes via gossip protocol
    /// - Elect leaders using Raft consensus
    /// - Replicate data across cluster nodes
    /// - Use read preferences for distributed reads
    /// - Handle node failover scenarios
    /// </summary>
    public static class P2PClusterExamples
    {
        /// <summary>
        /// Example 1: Cluster Join - Demonstrate joining a cluster using seed nodes
        /// </summary>
        public static async Task ClusterJoinExample()
        {
            Console.WriteLine("\n📦 EXAMPLE 1: Cluster Join");
            Console.WriteLine(new string('-', 50));

            try
            {
                // Create cluster configuration with seed nodes
                var config = new P2PConfiguration
                {
                    ClusterId = "demo-cluster",
                    ClusterName = "Demo Cluster",
                    NodeId = Guid.NewGuid().ToString("N")[..8],
                    BindAddress = "127.0.0.1",
                    P2PPort = 9092,
                    Discovery = new DiscoveryConfiguration
                    {
                        Method = "StaticSeeds",
                        Seeds = new[] { "192.168.1.10:9092", "192.168.1.11:9092" }
                    },
                    Security = new P2PSecurityConfiguration
                    {
                        ClusterSecret = "demo-cluster-secret-key"
                    }
                };

                Console.WriteLine("📝 Cluster Configuration:");
                Console.WriteLine($"   Cluster ID: {config.ClusterId}");
                Console.WriteLine($"   Cluster Name: {config.ClusterName}");
                Console.WriteLine($"   Node ID: {config.NodeId}");
                Console.WriteLine($"   P2P Port: {config.P2PPort}");
                Console.WriteLine($"   Discovery Method: {config.Discovery.Method}");
                Console.WriteLine($"   Seed Nodes: {string.Join(", ", config.Discovery.Seeds)}");

                // Create cluster manager
                var clusterManager = new ClusterManager(config);

                // Initialize local node
                clusterManager.InitializeLocalNode("127.0.0.1", 9090);

                Console.WriteLine("\n🔑 Local Node Initialized");
                Console.WriteLine($"   Node ID: {clusterManager.LocalNode.NodeId}");
                Console.WriteLine($"   Endpoint: {clusterManager.LocalNode.GetP2PEndpoint()}");

                Console.WriteLine("\n🔗 Attempting to create cluster...");
                
                // Create cluster (for demo purposes)
                var result = await clusterManager.CreateClusterAsync("Demo Cluster");
                
                if (result.Success)
                {
                    Console.WriteLine("✅ Successfully created cluster!");
                    var clusterInfo = await clusterManager.GetClusterInfoAsync();
                    Console.WriteLine($"   Cluster Health: {clusterInfo.Health}");
                    Console.WriteLine($"   Active Nodes: {clusterInfo.ActiveNodeCount}");
                    Console.WriteLine($"   Has Leader: {clusterInfo.HasLeader}");
                }
                else
                {
                    Console.WriteLine($"❌ Failed to create cluster: {result.ErrorMessage}");
                }

                // Cleanup
                clusterManager.Dispose();
                Console.WriteLine("\n✨ Cluster join example completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error in cluster join example: {ex.Message}");
                Console.WriteLine("   Note: This is a simulation - real cluster requires multiple running nodes.");
            }
        }

        /// <summary>
        /// Example 2: Node Discovery - Show automatic node discovery via gossip protocol
        /// </summary>
        public static async Task NodeDiscoveryExample()
        {
            Console.WriteLine("\n📦 EXAMPLE 2: Node Discovery via Gossip Protocol");
            Console.WriteLine(new string('-', 50));

            try
            {
                // Create cluster configuration
                var config = new P2PConfiguration
                {
                    ClusterId = "gossip-demo-cluster",
                    NodeId = "node-" + Guid.NewGuid().ToString("N")[..6],
                    BindAddress = "127.0.0.1",
                    P2PPort = 9093,
                    HeartbeatInterval = TimeSpan.FromSeconds(5),
                    DeadNodeTimeout = TimeSpan.FromSeconds(30),
                    Discovery = new DiscoveryConfiguration
                    {
                        Method = "StaticSeeds",
                        Seeds = new[] { "127.0.0.1:9092" }
                    }
                };

                Console.WriteLine("📝 Gossip Configuration:");
                Console.WriteLine($"   Heartbeat Interval: {config.HeartbeatInterval.TotalSeconds}s");
                Console.WriteLine($"   Dead Node Timeout: {config.DeadNodeTimeout.TotalSeconds}s");

                // Create cluster manager
                var clusterManager = new ClusterManager(config);
                clusterManager.InitializeLocalNode("127.0.0.1", 9090);

                // Create initial cluster
                await clusterManager.CreateClusterAsync("Gossip Demo Cluster");
                Console.WriteLine("\n🏗️  Created initial cluster");

                Console.WriteLine("\n👥 Simulating Cluster Nodes:");
                Console.WriteLine($"   This Node: {config.NodeId} (Leader)");
                Console.WriteLine("   Other Nodes: node-001, node-002, node-003");

                // Demonstrate gossip protocol
                Console.WriteLine("\n🔄 Simulating Gossip Protocol...");
                
                var gossipRounds = 3;
                for (int round = 1; round <= gossipRounds; round++)
                {
                    Console.WriteLine($"\n   Round {round}:");
                    var nodes = await clusterManager.GetNodesAsync();
                    
                    // Show local node info
                    var localInfo = NodeInfo.FromIdentity(clusterManager.LocalNode);
                    localInfo.IsLeader = clusterManager.IsLeader;
                    Console.WriteLine($"      {localInfo.NodeId} - 🟢 Active - Term: {localInfo.Term}");
                    
                    foreach (var nodeInfo in nodes)
                    {
                        var state = nodeInfo.State == NodeState.Active ? "🟢 Active" : "🔴 Inactive";
                        Console.WriteLine($"      {nodeInfo.NodeId} - {state} - Term: {nodeInfo.Term}");
                    }
                    
                    await Task.Delay(300); // Simulate gossip delay
                }

                // Show cluster health
                var clusterInfo = await clusterManager.GetClusterInfoAsync();
                Console.WriteLine($"\n📊 Cluster Health: {clusterInfo.Health}");
                Console.WriteLine($"   Active Nodes: {clusterInfo.ActiveNodeCount}/{clusterInfo.TotalNodeCount}");
                Console.WriteLine($"   Quorum Size: {clusterInfo.QuorumSize}");
                Console.WriteLine($"   Is Writable: {clusterInfo.IsWritable}");

                // Demonstrate node failure detection
                Console.WriteLine("\n💥 Simulating Node Failure Detection...");
                Console.WriteLine("   If a node misses 3 consecutive heartbeats (15s),");
                Console.WriteLine("   it will be marked as DEAD and removed from active nodes.");
                Console.WriteLine("   ✅ Gossip protocol maintains cluster membership automatically!");

                // Cleanup
                clusterManager.Dispose();
                Console.WriteLine("\n✨ Node discovery example completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error in node discovery example: {ex.Message}");
            }
        }

        /// <summary>
        /// Example 3: Leader Election - Demonstrate Raft-based leader election
        /// </summary>
        public static async Task LeaderElectionExample()
        {
            Console.WriteLine("\n📦 EXAMPLE 3: Leader Election (Raft Consensus)");
            Console.WriteLine(new string('-', 50));

            try
            {
                // Create Raft configuration
                var raftConfig = new RaftConfiguration
                {
                    ElectionTimeoutMinMs = 150,
                    ElectionTimeoutMaxMs = 300,
                    HeartbeatIntervalMs = 50
                };

                Console.WriteLine("📝 Raft Configuration:");
                Console.WriteLine($"   Election Timeout: {raftConfig.ElectionTimeoutMinMs}-{raftConfig.ElectionTimeoutMaxMs}ms (randomized)");
                Console.WriteLine($"   Heartbeat Interval: {raftConfig.HeartbeatIntervalMs}ms");

                // Create cluster for Raft
                var clusterConfig = new P2PConfiguration
                {
                    ClusterId = "raft-demo",
                    NodeId = "raft-node-1",
                    BindAddress = "127.0.0.1"
                };

                var clusterManager = new ClusterManager(clusterConfig);
                clusterManager.InitializeLocalNode("127.0.0.1", 9090);

                Console.WriteLine("\n🏁 Starting Raft Consensus...");
                Console.WriteLine($"   Initial State: Follower");
                Console.WriteLine($"   Current Term: 0");

                // Simulate adding other nodes
                var otherNodes = new[] { "raft-node-2", "raft-node-3", "raft-node-4", "raft-node-5" };
                Console.WriteLine("\n👥 Simulating cluster with 5 nodes:");
                Console.WriteLine($"   This Node: {clusterConfig.NodeId}");
                Console.WriteLine($"   Other Nodes: {string.Join(", ", otherNodes)}");

                // Simulate leader election process
                Console.WriteLine("\n🗳️  Simulating Leader Election...");
                await Task.Delay(200); // Simulate election timeout

                // Request votes from other nodes (simulated)
                Console.WriteLine($"   Candidate {clusterConfig.NodeId} requesting votes...");
                Console.WriteLine($"   Term: 1");
                Console.WriteLine($"   Last Log Index: 0");
                Console.WriteLine($"   Last Log Term: 0");

                // Simulate receiving votes
                int votesReceived = 3; // Simulate majority
                int totalNodes = 5;
                Console.WriteLine($"   Votes Received: {votesReceived}/{totalNodes} (Majority: {(totalNodes / 2) + 1})");

                if (votesReceived >= (totalNodes / 2) + 1)
                {
                    Console.WriteLine("   ✅ Won election! Became LEADER");
                    
                    // Simulate sending heartbeats
                    Console.WriteLine("\n💓 Sending Heartbeats as Leader...");
                    for (int i = 0; i < 3; i++)
                    {
                        Console.WriteLine($"   Heartbeat {i + 1} sent to all followers");
                        await Task.Delay(50);
                    }
                }

                // Show Raft concepts
                Console.WriteLine("\n📊 Raft Concepts Demonstrated:");
                Console.WriteLine("   Election Timeout: Randomized 150-300ms to prevent split votes");
                Console.WriteLine("   Heartbeat: Leader sends periodic heartbeats to maintain authority");
                Console.WriteLine("   Term: Monotonically increasing counter for election cycles");
                Console.WriteLine("   Quorum: Majority required (n/2+1) for leader election");

                // Show simulated statistics
                Console.WriteLine("\n📊 Simulated Raft Statistics:");
                Console.WriteLine("   Current Term: 1");
                Console.WriteLine("   Current Leader: raft-node-1");
                Console.WriteLine("   Log Entries: 0");
                Console.WriteLine("   Committed Index: 0");
                Console.WriteLine("   Election Count: 1");

                // Cleanup
                clusterManager.Dispose();
                
                Console.WriteLine("\n✨ Leader election example completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error in leader election example: {ex.Message}");
            }
        }

        /// <summary>
        /// Example 4: Data Replication - Show write replication across cluster nodes
        /// </summary>
        public static async Task DataReplicationExample()
        {
            Console.WriteLine("\n📦 EXAMPLE 4: Data Replication");
            Console.WriteLine(new string('-', 50));

            try
            {
                // Create cluster configuration first
                var clusterConfig = new P2PConfiguration
                {
                    ClusterId = "replication-demo",
                    NodeId = "primary-node",
                    BindAddress = "127.0.0.1"
                };

                var clusterManager = new ClusterManager(clusterConfig);
                clusterManager.InitializeLocalNode("127.0.0.1", 9090);
                await clusterManager.CreateClusterAsync("Replication Demo");

                // Create replication configuration
                var replicationConfig = new ReplicationConfiguration
                {
                    ReplicationFactor = 3,
                    WriteQuorum = 2
                };

                Console.WriteLine("📝 Replication Configuration:");
                Console.WriteLine($"   Replication Factor: {replicationConfig.ReplicationFactor}");
                Console.WriteLine($"   Write Quorum: {replicationConfig.WriteQuorum}");

                // Create replication manager
                var replicationManager = new ReplicationManager(clusterManager, replicationConfig);
                await replicationManager.StartAsync();

                Console.WriteLine("\n📦 Creating Sample Document Store...");
                var documentStore = new DocumentStore();

                // Create sample document
                var document = new Document
                {
                    Id = "user-123",
                    Data = new Dictionary<string, object?>
                    {
                        ["name"] = "John Doe",
                        ["email"] = "john@example.com",
                        ["role"] = "admin",
                        ["createdAt"] = DateTime.UtcNow
                    }
                };

                Console.WriteLine("\n📝 Sample Document:");
                Console.WriteLine($"   ID: {document.Id}");
                Console.WriteLine($"   Name: {document.Data["name"]}");
                Console.WriteLine($"   Email: {document.Data["email"]}");

                // Configure replication for collection
                await replicationManager.SetReplicationFactorAsync("users", replicationConfig.ReplicationFactor);
                Console.WriteLine("\n⚙️  Configured replication for 'users' collection");
                Console.WriteLine($"   Replication Factor: {replicationConfig.ReplicationFactor}");

                // Simulate replication event
                Console.WriteLine("\n🔄 Simulating Write Replication...");
                
                var operationId = Guid.NewGuid().ToString();
                Console.WriteLine($"   Operation ID: {operationId}");
                Console.WriteLine($"   Type: Insert");
                Console.WriteLine($"   Collection: users");
                Console.WriteLine($"   Document ID: {document.Id}");

                // Simulate waiting for acknowledgments
                Console.WriteLine($"\n⏳ Waiting for acknowledgments (quorum: {replicationConfig.WriteQuorum})...");
                
                var targetNodes = new[] { "replica-1", "replica-2", "replica-3" };
                int ackCount = 0;
                
                foreach (var node in targetNodes)
                {
                    // Simulate acknowledgment
                    await Task.Delay(100);
                    ackCount++;
                    Console.WriteLine($"   ✅ Ack from {node} ({ackCount}/{replicationConfig.WriteQuorum})");
                    
                    if (ackCount >= replicationConfig.WriteQuorum)
                    {
                        Console.WriteLine($"   🎉 Quorum reached! Write confirmed.");
                        break;
                    }
                }

                // Show replication statistics
                var stats = replicationManager.Statistics;
                Console.WriteLine("\n📊 Replication Statistics:");
                Console.WriteLine($"   Total Events Sent: {stats.TotalEventsSent}");
                Console.WriteLine($"   Events Acknowledged: {stats.TotalEventsAcknowledged}");
                Console.WriteLine($"   Events Failed: {stats.TotalFailures}");
                Console.WriteLine($"   Success Rate: {(stats.TotalEventsSent > 0 ? (stats.TotalEventsAcknowledged * 100.0 / stats.TotalEventsSent) : 0):F1}%");
                Console.WriteLine($"   Average Latency: {stats.AverageLatencyMs:F2}ms");

                // Show per-node statistics
                Console.WriteLine("\n📊 Per-Node Statistics:");
                foreach (var nodeStats in stats.PerNodeStats)
                {
                    Console.WriteLine($"   {nodeStats.Key}: {nodeStats.Value.EventsSent} sent, {nodeStats.Value.EventsAcknowledged} acked");
                }

                // Cleanup
                await replicationManager.StopAsync();
                replicationManager.Dispose();
                clusterManager.Dispose();
                
                Console.WriteLine("\n✨ Data replication example completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error in data replication example: {ex.Message}");
            }
        }

        /// <summary>
        /// Example 5: Read Preference - Demonstrate reading from different node types
        /// </summary>
        public static async Task ReadPreferenceExample()
        {
            Console.WriteLine("\n📦 EXAMPLE 5: Read Preference");
            Console.WriteLine(new string('-', 50));

            try
            {
                // Create cluster configuration
                var clusterConfig = new P2PConfiguration
                {
                    ClusterId = "readpref-demo",
                    NodeId = "local-node",
                    BindAddress = "127.0.0.1"
                };

                var clusterManager = new ClusterManager(clusterConfig);
                clusterManager.InitializeLocalNode("127.0.0.1", 9090);

                // Create read preference manager
                var readPreferenceManager = new ReadPreferenceManager(clusterManager);

                Console.WriteLine("📝 Read Preference Modes:");
                foreach (ReadPreferenceMode mode in Enum.GetValues(typeof(ReadPreferenceMode)))
                {
                    var description = mode switch
                    {
                        ReadPreferenceMode.Primary => "Read from primary only (strong consistency)",
                        ReadPreferenceMode.PrimaryPreferred => "Prefer primary, fallback to secondary",
                        ReadPreferenceMode.Secondary => "Read from secondary only (load balancing)",
                        ReadPreferenceMode.SecondaryPreferred => "Prefer secondary, fallback to primary",
                        ReadPreferenceMode.Nearest => "Read from nearest node by latency",
                        _ => "Unknown"
                    };
                    Console.WriteLine($"   {mode}: {description}");
                }

                Console.WriteLine("\n👥 Simulated Cluster Nodes:");
                Console.WriteLine("   node-primary (PRIMARY)");
                Console.WriteLine("      Endpoint: 10.0.0.1:9090, Latency: 5ms, Load: 30%");
                Console.WriteLine("      Tags: region=us-west, tier=primary");
                Console.WriteLine("   node-secondary-1 (SECONDARY)");
                Console.WriteLine("      Endpoint: 10.0.0.2:9090, Latency: 8ms, Load: 40%");
                Console.WriteLine("      Tags: region=us-west, tier=secondary");
                Console.WriteLine("   node-secondary-2 (SECONDARY)");
                Console.WriteLine("      Endpoint: 10.0.0.3:9090, Latency: 12ms, Load: 20%");
                Console.WriteLine("      Tags: region=us-east, tier=secondary");
                Console.WriteLine("   node-analytics (SECONDARY)");
                Console.WriteLine("      Endpoint: 10.0.0.4:9090, Latency: 15ms, Load: 60%");
                Console.WriteLine("      Tags: region=us-west, tier=analytics");

                // Demonstrate different read preference modes
                var modes = new[] 
                { 
                    ReadPreferenceMode.Primary, 
                    ReadPreferenceMode.PrimaryPreferred,
                    ReadPreferenceMode.Secondary,
                    ReadPreferenceMode.SecondaryPreferred,
                    ReadPreferenceMode.Nearest
                };

                Console.WriteLine("\n🎯 Read Preference Selections:");
                foreach (var mode in modes)
                {
                    var options = new ReadPreferenceOptions { Mode = mode };
                    var result = await readPreferenceManager.SelectNodeAsync(options);
                    
                    Console.WriteLine($"\n   Mode: {mode}");
                    if (result.Success)
                    {
                        Console.WriteLine($"   ✅ Selected: {result.SelectedNode?.NodeId}");
                        Console.WriteLine($"      Latency: {result.LatencyMs}ms");
                    }
                    else
                    {
                        Console.WriteLine($"   ❌ No suitable node found: {result.ErrorMessage}");
                    }
                }

                // Demonstrate tag-based filtering
                Console.WriteLine("\n🏷️  Tag-Based Filtering:");
                Console.WriteLine("   Tags: region=us-west, tier=secondary");
                Console.WriteLine("   Mode: Secondary");
                Console.WriteLine("   ✅ Would select: node-secondary-1");
                Console.WriteLine("      (matches all specified tags)");

                // Show statistics
                var stats = readPreferenceManager.GetStatistics();
                Console.WriteLine("\n📊 Read Preference Statistics:");
                Console.WriteLine($"   Total Selections: {stats.TotalSelections}");
                Console.WriteLine($"   Successful Selections: {stats.SuccessfulSelections}");
                Console.WriteLine($"   Failed Selections: {stats.FailedSelections}");
                var successRate = stats.TotalSelections > 0 ? (double)stats.SuccessfulSelections / stats.TotalSelections : 0;
                Console.WriteLine($"   Success Rate: {successRate:P1}");

                // Cleanup
                readPreferenceManager.Dispose();
                clusterManager.Dispose();
                
                Console.WriteLine("\n✨ Read preference example completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error in read preference example: {ex.Message}");
            }
        }

        /// <summary>
        /// Example 6: Failover Demo - Show automatic failover when a node goes down
        /// </summary>
        public static async Task FailoverDemoExample()
        {
            Console.WriteLine("\n📦 EXAMPLE 6: Failover Demo");
            Console.WriteLine(new string('-', 50));

            try
            {
                // Create cluster configuration
                var config = new P2PConfiguration
                {
                    ClusterId = "failover-demo",
                    NodeId = "local-node",
                    HeartbeatInterval = TimeSpan.FromSeconds(2),
                    DeadNodeTimeout = TimeSpan.FromSeconds(6)
                };

                var clusterManager = new ClusterManager(config);
                clusterManager.InitializeLocalNode("127.0.0.1", 9090);

                // Create cluster
                await clusterManager.CreateClusterAsync("Failover Demo Cluster");
                Console.WriteLine("🏗️  Created 3-node cluster");

                Console.WriteLine("\n👑 Initial Cluster State:");
                Console.WriteLine("   Leader: node-1 (PRIMARY)");
                Console.WriteLine("   Followers: node-2, node-3");

                var initialInfo = await clusterManager.GetClusterInfoAsync();
                Console.WriteLine($"   Health: {initialInfo.Health}");
                Console.WriteLine($"   Quorum: {initialInfo.QuorumSize} nodes required");

                // Simulate normal operations
                Console.WriteLine("\n✅ Normal operation - All nodes healthy");
                Console.WriteLine("   Writes: Accepted (quorum available)");
                Console.WriteLine("   Reads: Served from any node");

                // Simulate primary failure
                Console.WriteLine("\n💥 SIMULATING PRIMARY FAILURE (node-1)...");
                Console.WriteLine("   Waiting for failure detection...");
                await Task.Delay(500);

                Console.WriteLine($"\n📊 After Primary Failure:");
                Console.WriteLine("   Active Nodes: 2/3");
                Console.WriteLine("   Health: Degraded");
                Console.WriteLine("   Is Writable: True (quorum still maintained)");
                Console.WriteLine("   ✅ Quorum still maintained - Cluster operational");

                // Trigger leader election
                Console.WriteLine("\n🗳️  Triggering Leader Election...");
                await Task.Delay(200);
                
                Console.WriteLine("   ✅ New leader elected: node-2");
                Console.WriteLine("   Failover time: ~3 seconds");

                // Show recovered state
                Console.WriteLine($"\n📊 Recovered Cluster State:");
                Console.WriteLine("   New Leader: node-2");
                Console.WriteLine("   Followers: node-3");
                Console.WriteLine("   Failed: node-1 (will rejoin when available)");
                Console.WriteLine("   Health: Healthy");

                // Simulate old primary rejoining
                Console.WriteLine("\n🔄 SIMULATING NODE RECOVERY (node-1 rejoining)...");
                Console.WriteLine("   Node-1 state: Syncing (catching up on missed data)");
                
                await Task.Delay(300);
                Console.WriteLine("   Node-1 state: Active (fully rejoined)");

                Console.WriteLine($"\n📊 Final Cluster State:");
                Console.WriteLine("   Leader: node-2");
                Console.WriteLine("   Followers: node-1, node-3");
                Console.WriteLine("   Active Nodes: 3/3");
                Console.WriteLine("   Health: Healthy");
                Console.WriteLine("   Is Writable: True");

                // Show failover statistics
                Console.WriteLine("\n📊 Failover Statistics:");
                Console.WriteLine("   Detection Time: ~2 seconds (heartbeat timeout)");
                Console.WriteLine("   Election Time: ~1 second");
                Console.WriteLine("   Total Failover Time: ~3 seconds");
                Console.WriteLine("   Data Loss: 0 (semi-synchronous replication)");

                // Cleanup
                clusterManager.Dispose();
                
                Console.WriteLine("\n✨ Failover demo example completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error in failover demo example: {ex.Message}");
            }
        }

        /// <summary>
        /// Run all P2P cluster examples
        /// </summary>
        public static async Task RunAllExamples()
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("  P2P CLUSTER EXAMPLES");
            Console.WriteLine("  Distributed Clustering & Replication");
            Console.WriteLine(new string('═', 60));

            await ClusterJoinExample();
            await NodeDiscoveryExample();
            await LeaderElectionExample();
            await DataReplicationExample();
            await ReadPreferenceExample();
            await FailoverDemoExample();

            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("  ALL P2P CLUSTER EXAMPLES COMPLETED");
            Console.WriteLine(new string('═', 60));
        }
    }
}
