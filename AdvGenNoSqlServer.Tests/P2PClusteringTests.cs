// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Clustering;
using AdvGenNoSqlServer.Network.Clustering;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for P2P clustering components.
    /// </summary>
    public class P2PClusteringTests
    {
        #region NodeIdentity Tests

        [Fact]
        public void NodeIdentity_Create_GeneratesValidGuid()
        {
            var node = NodeIdentity.Create("test-cluster", "localhost", 9090, 9092);

            Assert.NotNull(node.NodeId);
            Assert.Equal(32, node.NodeId.Length); // GUID without dashes
            Assert.Equal("test-cluster", node.ClusterId);
            Assert.Equal("localhost", node.Host);
            Assert.Equal(9090, node.Port);
            Assert.Equal(9092, node.P2PPort);
            Assert.Equal(NodeState.Joining, node.State);
        }

        [Fact]
        public void NodeIdentity_Clone_CreatesDeepCopy()
        {
            var original = new NodeIdentity
            {
                NodeId = "test-id",
                ClusterId = "cluster",
                Host = "localhost",
                Port = 9090,
                P2PPort = 9092,
                Tags = new[] { "tag1", "tag2" },
                PublicKey = new byte[] { 1, 2, 3 }
            };

            var clone = original.Clone();

            Assert.Equal(original.NodeId, clone.NodeId);
            Assert.Equal(original.Tags, clone.Tags);
            Assert.Equal(original.PublicKey, clone.PublicKey);
            
            // Modify clone should not affect original
            clone.Tags[0] = "modified";
            clone.PublicKey[0] = 99;
            
            Assert.Equal("tag1", original.Tags[0]);
            Assert.Equal(1, original.PublicKey[0]);
        }

        [Theory]
        [InlineData("localhost", 9092, "localhost:9092")]
        [InlineData("192.168.1.1", 10000, "192.168.1.1:10000")]
        public void NodeIdentity_GetP2PEndpoint_ReturnsCorrectFormat(string host, int port, string expected)
        {
            var node = new NodeIdentity
            {
                NodeId = "test",
                ClusterId = "cluster",
                Host = host,
                Port = 9090,
                P2PPort = port
            };

            Assert.Equal(expected, node.GetP2PEndpoint());
        }

        [Fact]
        public void NodeIdentity_ToString_ContainsRelevantInfo()
        {
            var node = NodeIdentity.Create("cluster", "localhost", 9090, 9092);
            node.State = NodeState.Active;

            var str = node.ToString();

            Assert.Contains(node.NodeId[..8], str);
            Assert.Contains("Active", str);
            Assert.Contains("localhost:9092", str);
        }

        #endregion

        #region NodeInfo Tests

        [Fact]
        public void NodeInfo_FromIdentity_CreatesCorrectMapping()
        {
            var identity = new NodeIdentity
            {
                NodeId = "node-123",
                ClusterId = "cluster",
                Host = "localhost",
                Port = 9090,
                P2PPort = 9092,
                State = NodeState.Active,
                JoinedAt = DateTime.UtcNow.AddHours(-1),
                LastSeenAt = DateTime.UtcNow,
                Tags = new[] { "primary" }
            };

            var info = NodeInfo.FromIdentity(identity);

            Assert.Equal(identity.NodeId, info.NodeId);
            Assert.Equal(identity.Host, info.Host);
            Assert.Equal(identity.P2PPort, info.P2PPort);
            Assert.Equal(identity.State, info.State);
            Assert.Equal(identity.JoinedAt, info.JoinedAt);
            Assert.Equal(identity.LastSeenAt, info.LastSeenAt);
            Assert.Equal(identity.Tags, info.Tags);
        }

        #endregion

        #region ClusterInfo Tests

        [Fact]
        public void ClusterInfo_ActiveNodeCount_ExcludesNonActiveNodes()
        {
            var info = new ClusterInfo
            {
                ClusterId = "test",
                ClusterName = "Test Cluster",
                Nodes = new List<NodeInfo>
                {
                    new() { NodeId = "1", Host = "h1", P2PPort = 1, State = NodeState.Active },
                    new() { NodeId = "2", Host = "h2", P2PPort = 2, State = NodeState.Active },
                    new() { NodeId = "3", Host = "h3", P2PPort = 3, State = NodeState.Dead },
                    new() { NodeId = "4", Host = "h4", P2PPort = 4, State = NodeState.Joining }
                }
            };

            Assert.Equal(2, info.ActiveNodeCount);
            Assert.Equal(4, info.TotalNodeCount);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 2)]
        [InlineData(5, 3)]
        [InlineData(6, 4)]
        public void ClusterInfo_QuorumSize_CalculatedCorrectly(int nodeCount, int expectedQuorum)
        {
            var nodes = Enumerable.Range(0, nodeCount)
                .Select(i => new NodeInfo 
                { 
                    NodeId = i.ToString(), 
                    Host = "h", 
                    P2PPort = i,
                    State = NodeState.Active 
                })
                .ToList();

            var info = new ClusterInfo
            {
                ClusterId = "test",
                ClusterName = "Test",
                Nodes = nodes
            };

            Assert.Equal(expectedQuorum, info.QuorumSize);
        }

        [Fact]
        public void ClusterInfo_IsWritable_WhenHasHealthyLeader()
        {
            var info = new ClusterInfo
            {
                ClusterId = "test",
                ClusterName = "Test",
                Leader = new NodeInfo { NodeId = "leader", Host = "h", P2PPort = 1 },
                Health = ClusterHealth.Healthy
            };

            Assert.True(info.IsWritable);
        }

        [Fact]
        public void ClusterInfo_IsWritable_FalseWhenUnhealthy()
        {
            var info = new ClusterInfo
            {
                ClusterId = "test",
                ClusterName = "Test",
                Leader = new NodeInfo { NodeId = "leader", Host = "h", P2PPort = 1 },
                Health = ClusterHealth.Unhealthy
            };

            Assert.False(info.IsWritable);
        }

        [Fact]
        public void ClusterInfo_GetSummary_ContainsKeyInfo()
        {
            var info = new ClusterInfo
            {
                ClusterId = "cluster-123",
                ClusterName = "Production",
                Nodes = new List<NodeInfo>
                {
                    new() { NodeId = "1", Host = "h1", P2PPort = 1, State = NodeState.Active },
                    new() { NodeId = "2", Host = "h2", P2PPort = 2, State = NodeState.Active },
                    new() { NodeId = "3", Host = "h3", P2PPort = 3, State = NodeState.Dead }
                },
                Health = ClusterHealth.Degraded
            };

            var summary = info.GetSummary();

            Assert.Contains("Production", summary);
            Assert.Contains("cluster-123", summary);
            Assert.Contains("2/3", summary);
            Assert.Contains("Degraded", summary);
        }

        #endregion

        #region P2PConfiguration Tests

        [Fact]
        public void P2PConfiguration_Validate_ValidConfig_ReturnsTrue()
        {
            var config = new P2PConfiguration
            {
                ClusterId = "test-cluster",
                P2PPort = 9092,
                ConnectionTimeout = TimeSpan.FromSeconds(5),
                HeartbeatInterval = TimeSpan.FromSeconds(1),
                DeadNodeTimeout = TimeSpan.FromSeconds(30)
            };

            var isValid = config.Validate(out var errors);

            Assert.True(isValid);
            Assert.Empty(errors);
        }

        [Fact]
        public void P2PConfiguration_Validate_MissingClusterId_ReturnsFalse()
        {
            var config = new P2PConfiguration
            {
                ClusterId = "",
                P2PPort = 9092
            };

            var isValid = config.Validate(out var errors);

            Assert.False(isValid);
            Assert.Contains(errors, e => e.Contains("ClusterId"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(65536)]
        public void P2PConfiguration_Validate_InvalidPort_ReturnsFalse(int port)
        {
            var config = new P2PConfiguration
            {
                ClusterId = "test",
                P2PPort = port
            };

            var isValid = config.Validate(out var errors);

            Assert.False(isValid);
            Assert.Contains(errors, e => e.Contains("Port"));
        }

        [Fact]
        public void P2PConfiguration_Validate_DeadNodeTimeoutTooShort_ReturnsFalse()
        {
            var config = new P2PConfiguration
            {
                ClusterId = "test",
                P2PPort = 9092,
                HeartbeatInterval = TimeSpan.FromSeconds(5),
                DeadNodeTimeout = TimeSpan.FromSeconds(3) // Less than heartbeat
            };

            var isValid = config.Validate(out var errors);

            Assert.False(isValid);
            Assert.Contains(errors, e => e.Contains("DeadNodeTimeout"));
        }

        [Fact]
        public void P2PConfiguration_GetAdvertiseAddress_UsesAdvertiseWhenSet()
        {
            var config = new P2PConfiguration
            {
                BindAddress = "0.0.0.0",
                AdvertiseAddress = "192.168.1.100"
            };

            Assert.Equal("192.168.1.100", config.GetAdvertiseAddress());
        }

        [Fact]
        public void P2PConfiguration_GetAdvertiseAddress_FallsBackToBind()
        {
            var config = new P2PConfiguration
            {
                BindAddress = "127.0.0.1"
            };

            Assert.Equal("127.0.0.1", config.GetAdvertiseAddress());
        }

        #endregion

        #region ClusterManager Tests

        [Fact]
        public async Task ClusterManager_CreateCluster_Success()
        {
            var config = new P2PConfiguration { ClusterId = "test", ClusterName = "Test" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);

            var result = await manager.CreateClusterAsync("My Cluster");

            Assert.True(result.Success);
            Assert.NotNull(result.ClusterInfo);
            Assert.True(manager.IsClusterMember);
            Assert.True(manager.IsLeader);
        }

        [Fact]
        public async Task ClusterManager_CreateCluster_AlreadyMember_Fails()
        {
            var config = new P2PConfiguration { ClusterId = "test" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);
            await manager.CreateClusterAsync("First");

            var result = await manager.CreateClusterAsync("Second");

            Assert.False(result.Success);
            Assert.Contains("Already a member", result.ErrorMessage);
        }

        [Fact]
        public async Task ClusterManager_JoinCluster_Success()
        {
            var config = new P2PConfiguration { ClusterId = "test" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);

            var result = await manager.JoinClusterAsync("seed:9092", new JoinOptions { SeedNode = "seed:9092" });

            Assert.True(result.Success);
            Assert.True(manager.IsClusterMember);
        }

        [Fact]
        public async Task ClusterManager_LeaveCluster_Success()
        {
            var config = new P2PConfiguration { ClusterId = "test" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);
            await manager.CreateClusterAsync("Test");

            var result = await manager.LeaveClusterAsync(new LeaveOptions());

            Assert.True(result.Success);
            Assert.False(manager.IsClusterMember);
        }

        [Fact]
        public async Task ClusterManager_GetClusterInfo_ReturnsCorrectInfo()
        {
            var config = new P2PConfiguration 
            { 
                ClusterId = "cluster-123", 
                ClusterName = "Test Cluster",
                Mode = ClusterMode.LeaderFollower
            };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);
            await manager.CreateClusterAsync("Test");

            var info = await manager.GetClusterInfoAsync();

            Assert.Equal("cluster-123", info.ClusterId);
            Assert.Equal("Test Cluster", info.ClusterName);
            Assert.Equal(ClusterMode.LeaderFollower, info.Mode);
            Assert.True(info.HasLeader);
            Assert.Equal(1, info.TotalNodeCount);
        }

        [Fact]
        public async Task ClusterManager_RequestLeaderElection_Success()
        {
            var config = new P2PConfiguration { ClusterId = "test" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);
            await manager.CreateClusterAsync("Test");

            var result = await manager.RequestLeaderElectionAsync();

            Assert.True(result);
            Assert.True(manager.IsLeader);
        }

        [Fact]
        public async Task ClusterManager_UpdateNodeState_UpdatesState()
        {
            var config = new P2PConfiguration { ClusterId = "test" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);

            var result = await manager.UpdateNodeStateAsync(NodeState.Syncing);

            Assert.True(result);
            Assert.Equal(NodeState.Syncing, manager.LocalNode.State);
        }

        [Fact]
        public async Task ClusterManager_AddOrUpdateNode_AddsNewNode()
        {
            var config = new P2PConfiguration { ClusterId = "test" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);
            await manager.CreateClusterAsync("Test");

            var newNode = new NodeInfo 
            { 
                NodeId = "new-node", 
                Host = "other", 
                P2PPort = 9092,
                State = NodeState.Active 
            };

            var added = await manager.AddOrUpdateNodeAsync(newNode);

            Assert.True(added);
            var nodes = await manager.GetNodesAsync();
            Assert.Contains(nodes, n => n.NodeId == "new-node");
        }

        [Fact]
        public async Task ClusterManager_RemoveNode_RemovesNode()
        {
            var config = new P2PConfiguration { ClusterId = "test" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);
            await manager.CreateClusterAsync("Test");
            await manager.AddOrUpdateNodeAsync(new NodeInfo 
            { 
                NodeId = "other", 
                Host = "h", 
                P2PPort = 1,
                State = NodeState.Active 
            });

            var removed = await manager.RemoveNodeAsync("other");

            Assert.True(removed);
            var nodes = await manager.GetNodesAsync();
            Assert.DoesNotContain(nodes, n => n.NodeId == "other");
        }

        [Fact]
        public async Task ClusterManager_RemoveNode_CannotRemoveSelf()
        {
            var config = new P2PConfiguration { ClusterId = "test" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);
            await manager.CreateClusterAsync("Test");

            var removed = await manager.RemoveNodeAsync(manager.LocalNode.NodeId);

            Assert.False(removed);
        }

        #endregion

        #region Event Tests

        [Fact]
        public async Task ClusterManager_NodeJoinedEvent_RaisedOnAdd()
        {
            var config = new P2PConfiguration { ClusterId = "test" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);
            await manager.CreateClusterAsync("Test");

            NodeJoinedEventArgs? eventArgs = null;
            manager.NodeJoined += (s, e) => eventArgs = e;

            await manager.AddOrUpdateNodeAsync(new NodeInfo 
            { 
                NodeId = "new", 
                Host = "h", 
                P2PPort = 1,
                State = NodeState.Active 
            });

            Assert.NotNull(eventArgs);
            Assert.Equal("new", eventArgs.Node.NodeId);
        }

        [Fact]
        public async Task ClusterManager_LeaderChangedEvent_RaisedOnElection()
        {
            var config = new P2PConfiguration { ClusterId = "test" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);
            await manager.CreateClusterAsync("Test");

            LeaderChangedEventArgs? eventArgs = null;
            manager.LeaderChanged += (s, e) => eventArgs = e;

            await manager.RequestLeaderElectionAsync();

            Assert.NotNull(eventArgs);
            Assert.NotNull(eventArgs.NewLeader);
        }

        [Fact]
        public async Task ClusterManager_NodeStateChangedEvent_RaisedOnUpdate()
        {
            var config = new P2PConfiguration { ClusterId = "test" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);
            await manager.CreateClusterAsync("Test");

            NodeStateChangedEventArgs? eventArgs = null;
            manager.NodeStateChanged += (s, e) => eventArgs = e;

            await manager.UpdateNodeStateAsync(NodeState.Leaving);

            Assert.NotNull(eventArgs);
            Assert.Equal(NodeState.Active, eventArgs.PreviousState);
            Assert.Equal(NodeState.Leaving, eventArgs.NewState);
        }

        #endregion

        #region P2PMessage Tests

        [Fact]
        public void JoinRequestMessage_HasCorrectType()
        {
            var msg = new JoinRequestMessage 
            { 
                SenderId = "test",
                NodeIdentity = NodeIdentity.Create("c", "h", 1, 2),
                ClusterSecretHash = "hash"
            };

            Assert.Equal(P2PMessageType.JoinRequest, msg.MessageType);
        }

        [Fact]
        public void JoinResponseMessage_HasCorrectType()
        {
            var msg = new JoinResponseMessage 
            { 
                SenderId = "test",
                Accepted = true 
            };

            Assert.Equal(P2PMessageType.JoinResponse, msg.MessageType);
        }

        [Fact]
        public void HeartbeatMessage_HasCorrectProperties()
        {
            var msg = new HeartbeatMessage
            {
                SenderId = "test",
                NodeState = NodeState.Active,
                SequenceNumber = 42
            };

            Assert.Equal(P2PMessageType.Heartbeat, msg.MessageType);
            Assert.Equal(NodeState.Active, msg.NodeState);
            Assert.Equal(42, msg.SequenceNumber);
        }

        [Fact]
        public void GossipMessage_HasCorrectProperties()
        {
            var msg = new GossipMessage
            {
                SenderId = "test",
                Generation = 1,
                Version = 5,
                NodeStates = new Dictionary<string, NodeState> { { "node1", NodeState.Active } },
                Heartbeats = new Dictionary<string, long> { { "node1", 100 } }
            };

            Assert.Equal(P2PMessageType.Gossip, msg.MessageType);
            Assert.Equal(1, msg.Generation);
            Assert.Equal(5, msg.Version);
        }

        #endregion

        #region HandshakeResult Tests

        [Fact]
        public void HandshakeResult_Success_CreatesValidResult()
        {
            var result = HandshakeResult.Success("node-123");

            Assert.True(result.Success);
            Assert.Equal("node-123", result.NodeId);
            Assert.Null(result.ErrorMessage);
        }

        [Fact]
        public void HandshakeResult_Failed_CreatesValidResult()
        {
            var result = HandshakeResult.Failed("Connection refused");

            Assert.False(result.Success);
            Assert.Equal("Connection refused", result.ErrorMessage);
            Assert.Null(result.NodeId);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task ClusterManager_FullLifecycle_Works()
        {
            var config = new P2PConfiguration { ClusterId = "test", ClusterName = "Test Cluster" };
            var manager = new ClusterManager(config);
            manager.InitializeLocalNode("localhost", 9090);

            // Create cluster
            var createResult = await manager.CreateClusterAsync("Test Cluster");
            Assert.True(createResult.Success);
            Assert.True(manager.IsLeader);

            // Add nodes
            for (int i = 1; i <= 3; i++)
            {
                await manager.AddOrUpdateNodeAsync(new NodeInfo
                {
                    NodeId = $"node-{i}",
                    Host = $"host-{i}",
                    P2PPort = 9000 + i,
                    State = NodeState.Active
                });
            }

            var info = await manager.GetClusterInfoAsync();
            Assert.Equal(3, info.ActiveNodeCount);
            Assert.Equal(4, info.TotalNodeCount);

            // Remove a node
            await manager.RemoveNodeAsync("node-2");
            info = await manager.GetClusterInfoAsync();
            Assert.Equal(3, info.TotalNodeCount);

            // Leave cluster
            var leaveResult = await manager.LeaveClusterAsync(new LeaveOptions());
            Assert.True(leaveResult.Success);
            Assert.False(manager.IsClusterMember);

            manager.Dispose();
        }

        #endregion
    }
}
