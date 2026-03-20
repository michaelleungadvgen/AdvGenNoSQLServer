// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Clustering;
using Moq;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for the Gossip Protocol implementation.
    /// </summary>
    public class GossipProtocolTests : IDisposable
    {
        private readonly Mock<IClusterManager> _mockClusterManager;
        private readonly P2PConfiguration _configuration;
        private readonly GossipOptions _options;

        public GossipProtocolTests()
        {
            _mockClusterManager = new Mock<IClusterManager>();
            _configuration = new P2PConfiguration
            {
                NodeId = Guid.NewGuid().ToString("N"),
                ClusterId = "test-cluster",
                BindAddress = "127.0.0.1",
                P2PPort = 9092
            };
            _options = new GossipOptions
            {
                GossipInterval = TimeSpan.FromMilliseconds(100),
                Fanout = 2,
                MaxNodesPerMessage = 5
            };
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        [Fact]
        public void GossipOptions_Validate_ValidOptions_DoesNotThrow()
        {
            var options = new GossipOptions
            {
                GossipInterval = TimeSpan.FromSeconds(1),
                Fanout = 3,
                MaxNodesPerMessage = 10
            };

            options.Validate();
        }

        [Theory]
        [InlineData(0)] // Invalid: must be positive
        [InlineData(-1)] // Invalid: must be positive
        public void GossipOptions_Validate_InvalidGossipInterval_Throws(int intervalMs)
        {
            var options = new GossipOptions
            {
                GossipInterval = TimeSpan.FromMilliseconds(intervalMs)
            };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Theory]
        [InlineData(0)] // Invalid: must be at least 1
        [InlineData(-1)] // Invalid: must be at least 1
        public void GossipOptions_Validate_InvalidFanout_Throws(int fanout)
        {
            var options = new GossipOptions
            {
                GossipInterval = TimeSpan.FromSeconds(1),
                Fanout = fanout
            };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Theory]
        [InlineData(0)] // Invalid: must be at least 1
        [InlineData(-1)] // Invalid: must be at least 1
        public void GossipOptions_Validate_InvalidMaxNodesPerMessage_Throws(int maxNodes)
        {
            var options = new GossipOptions
            {
                GossipInterval = TimeSpan.FromSeconds(1),
                MaxNodesPerMessage = maxNodes
            };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Fact]
        public void GossipProtocol_Constructor_ValidParameters_CreatesInstance()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            Assert.NotNull(protocol);
        }

        [Fact]
        public void GossipProtocol_Constructor_NullClusterManager_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new GossipProtocol(null!, _configuration, _options));
        }

        [Fact]
        public void GossipProtocol_Constructor_NullConfiguration_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new GossipProtocol(_mockClusterManager.Object, null!, _options));
        }

        [Fact]
        public async Task GossipProtocol_StartAsync_StartsSuccessfully()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();

            var stats = protocol.GetStats();
            Assert.True(stats.StartedAt > DateTime.MinValue);

            protocol.Dispose();
        }

        [Fact]
        public async Task GossipProtocol_StartAsync_CalledTwice_DoesNotThrow()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();
            await protocol.StartAsync(); // Second call should be idempotent

            protocol.Dispose();
        }

        [Fact]
        public async Task GossipProtocol_StopAsync_StopsSuccessfully()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();
            await protocol.StopAsync();

            // Should be able to dispose after stop
            protocol.Dispose();
        }

        [Fact]
        public async Task GossipProtocol_StopAsync_CalledTwice_DoesNotThrow()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();
            await protocol.StopAsync();
            await protocol.StopAsync(); // Second call should be idempotent

            protocol.Dispose();
        }

        [Fact]
        public async Task GossipProtocol_UpdateLocalStateAsync_AddsLocalNode()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();
            await protocol.UpdateLocalStateAsync(NodeState.Active);

            var state = protocol.GetClusterState();
            Assert.Single(state);
            Assert.True(state.ContainsKey(_configuration.NodeId));
            Assert.Equal(NodeState.Active, state[_configuration.NodeId].State);

            protocol.Dispose();
        }

        [Fact]
        public async Task GossipProtocol_UpdateLocalStateAsync_MultipleUpdates_IncrementsVersion()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();

            await protocol.UpdateLocalStateAsync(NodeState.Active);
            var version1 = protocol.GetClusterState()[_configuration.NodeId].Version;

            await protocol.UpdateLocalStateAsync(NodeState.Active);
            var version2 = protocol.GetClusterState()[_configuration.NodeId].Version;

            Assert.True(version2 > version1);

            protocol.Dispose();
        }

        [Fact]
        public void GossipMessage_Create_ValidParameters_CreatesMessage()
        {
            var message = new GossipMessage
            {
                SenderId = "sender-123",
                Generation = 1,
                Version = 1,
                NodeStates = new Dictionary<string, NodeState>(),
                Heartbeats = new Dictionary<string, long>()
            };

            Assert.NotNull(message);
            Assert.Equal("sender-123", message.SenderId);
            Assert.Equal(1, message.Generation);
            Assert.Equal(1, message.Version);
            Assert.Equal(P2PMessageType.Gossip, message.MessageType);
            Assert.NotNull(message.NodeStates);
            Assert.NotNull(message.Heartbeats);
            Assert.True(message.Timestamp > DateTime.MinValue);
        }

        [Fact]
        public async Task GossipProtocol_ProcessGossipMessageAsync_NewerState_UpdatesState()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();
            await protocol.UpdateLocalStateAsync(NodeState.Active);

            var message = new GossipMessage
            {
                SenderId = "sender-node",
                Generation = 1,
                Version = 1,
                NodeStates = new Dictionary<string, NodeState>
                {
                    { "other-node", NodeState.Active }
                },
                Heartbeats = new Dictionary<string, long>
                {
                    { "other-node", 1 }
                }
            };

            var stateUpdatedEvent = new TaskCompletionSource<GossipStateUpdatedEventArgs>();
            protocol.StateUpdated += (s, e) => stateUpdatedEvent.TrySetResult(e);

            await protocol.ProcessGossipMessageAsync(message, "sender-node");

            var state = protocol.GetClusterState();
            Assert.True(state.ContainsKey("other-node"));
            Assert.Equal(NodeState.Active, state["other-node"].State);

            protocol.Dispose();
        }

        [Fact]
        public async Task GossipProtocol_ProcessGossipMessageAsync_OlderState_DoesNotUpdate()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();
            await protocol.UpdateLocalStateAsync(NodeState.Active);

            // First, add a node with generation 2
            var message1 = new GossipMessage
            {
                SenderId = "sender-node",
                Generation = 2,
                Version = 1,
                NodeStates = new Dictionary<string, NodeState>
                {
                    { "other-node", NodeState.Active }
                }
            };

            await protocol.ProcessGossipMessageAsync(message1, "sender-node");

            // Now try to update with older generation
            var message2 = new GossipMessage
            {
                SenderId = "sender-node",
                Generation = 1, // Older generation
                Version = 100, // Higher version but older generation
                NodeStates = new Dictionary<string, NodeState>
                {
                    { "other-node", NodeState.Leaving } // Different state
                }
            };

            await protocol.ProcessGossipMessageAsync(message2, "sender-node");

            // State should still be Active (from generation 2), not Leaving
            var state = protocol.GetClusterState();
            Assert.Equal(NodeState.Active, state["other-node"].State);

            protocol.Dispose();
        }

        [Fact]
        public async Task GossipProtocol_ProcessGossipMessageAsync_SameGenerationHigherVersion_Updates()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();
            await protocol.UpdateLocalStateAsync(NodeState.Active);

            // First state
            var message1 = new GossipMessage
            {
                SenderId = "sender",
                Generation = 1,
                Version = 1,
                NodeStates = new Dictionary<string, NodeState>
                {
                    { "other-node", NodeState.Active }
                }
            };

            await protocol.ProcessGossipMessageAsync(message1, "sender");

            // Updated state (same generation, higher version)
            var message2 = new GossipMessage
            {
                SenderId = "sender",
                Generation = 1,
                Version = 2,
                NodeStates = new Dictionary<string, NodeState>
                {
                    { "other-node", NodeState.Leaving }
                }
            };

            await protocol.ProcessGossipMessageAsync(message2, "sender");

            var state = protocol.GetClusterState();
            Assert.Equal(NodeState.Leaving, state["other-node"].State);

            protocol.Dispose();
        }

        [Fact]
        public async Task GossipProtocol_GetStats_InitialState_ReturnsZeroStats()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            var stats = protocol.GetStats();

            Assert.Equal(0, stats.MessagesSent);
            Assert.Equal(0, stats.MessagesReceived);
            Assert.Equal(0, stats.StatesPropagated);
            Assert.Equal(0, stats.GossipRounds);
            Assert.Equal(0, stats.CurrentNodeCount);
        }

        [Fact]
        public async Task GossipProtocol_CreateGossipMessage_IncludesLocalNode()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();
            await protocol.UpdateLocalStateAsync(NodeState.Active);

            var message = protocol.CreateGossipMessage();

            Assert.NotNull(message);
            Assert.Equal(_configuration.NodeId, message.SenderId);
            Assert.NotEmpty(message.NodeStates);
            Assert.True(message.NodeStates.ContainsKey(_configuration.NodeId));

            protocol.Dispose();
        }

        [Fact]
        public async Task GossipProtocol_CreateGossipMessage_RespectsMaxNodesPerMessage()
        {
            var options = new GossipOptions
            {
                MaxNodesPerMessage = 3
            };

            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                options);

            await protocol.StartAsync();
            await protocol.UpdateLocalStateAsync(NodeState.Active);

            // Add more nodes via gossip messages
            for (int i = 0; i < 5; i++)
            {
                var message = new GossipMessage
                {
                    SenderId = "sender",
                    Generation = 1,
                    Version = i + 1,
                    NodeStates = new Dictionary<string, NodeState>
                    {
                        { $"node-{i}", NodeState.Active }
                    }
                };

                await protocol.ProcessGossipMessageAsync(message, "sender");
            }

            var gossipMessage = protocol.CreateGossipMessage();

            Assert.True(gossipMessage.NodeStates.Count <= options.MaxNodesPerMessage,
                $"Expected at most {options.MaxNodesPerMessage} nodes, got {gossipMessage.NodeStates.Count}");

            protocol.Dispose();
        }

        [Fact]
        public async Task GossipProtocol_TriggerGossipRoundAsync_IncrementsRoundCounter()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();

            var initialRounds = protocol.GetStats().GossipRounds;

            await protocol.TriggerGossipRoundAsync();

            var stats = protocol.GetStats();
            Assert.True(stats.GossipRounds > initialRounds);

            protocol.Dispose();
        }

        [Fact]
        public void NodeStatusInfo_Equality_SameValues_AreEqual()
        {
            var now = DateTime.UtcNow;
            var node = new NodeInfo { NodeId = "node-1", Host = "127.0.0.1", P2PPort = 9092 };

            var info1 = new NodeStateInfo
            {
                Node = node,
                State = NodeState.Active,
                Generation = 1,
                Version = 2,
                LastUpdated = now,
                HeartbeatSequence = 5
            };

            var info2 = new NodeStateInfo
            {
                Node = node,
                State = NodeState.Active,
                Generation = 1,
                Version = 2,
                LastUpdated = now,
                HeartbeatSequence = 5
            };

            Assert.Equal(info1.State, info2.State);
            Assert.Equal(info1.Generation, info2.Generation);
            Assert.Equal(info1.Version, info2.Version);
        }

        [Fact]
        public void NodeStatus_EnumValues_AreDefined()
        {
            Assert.Equal(0, (int)NodeStatus.Alive);
            Assert.Equal(1, (int)NodeStatus.Suspected);
            Assert.Equal(2, (int)NodeStatus.Failed);
            Assert.Equal(3, (int)NodeStatus.Unknown);
        }

        [Fact]
        public void NodeStatus_Unknown_IsLastValue()
        {
            // NodeStatus.Unknown is defined in our interface, not in NodeState enum
            // This test ensures the values align
            var values = Enum.GetValues<NodeStatus>();
            Assert.Equal(4, values.Length);
        }

        [Fact]
        public void GossipStats_Properties_CanBeSet()
        {
            var stats = new GossipStats
            {
                MessagesSent = 100,
                MessagesReceived = 95,
                StatesPropagated = 200,
                GossipRounds = 50,
                AverageMessageSizeBytes = 1024.5,
                CurrentNodeCount = 10
            };

            Assert.Equal(100, stats.MessagesSent);
            Assert.Equal(95, stats.MessagesReceived);
            Assert.Equal(200, stats.StatesPropagated);
            Assert.Equal(50, stats.GossipRounds);
            Assert.Equal(1024.5, stats.AverageMessageSizeBytes);
            Assert.Equal(10, stats.CurrentNodeCount);
        }

        [Fact]
        public void FailureDetectorStats_Properties_CanBeSet()
        {
            var stats = new FailureDetectorStats
            {
                TotalNodes = 10,
                AliveNodes = 8,
                SuspectedNodes = 1,
                FailedNodes = 1,
                TotalHeartbeatsReceived = 1000,
                TotalFailuresDetected = 2,
                TotalSuspicionsRaised = 3
            };

            Assert.Equal(10, stats.TotalNodes);
            Assert.Equal(8, stats.AliveNodes);
            Assert.Equal(1, stats.SuspectedNodes);
            Assert.Equal(1, stats.FailedNodes);
            Assert.Equal(1000, stats.TotalHeartbeatsReceived);
            Assert.Equal(2, stats.TotalFailuresDetected);
            Assert.Equal(3, stats.TotalSuspicionsRaised);
        }

        [Fact]
        public async Task GossipProtocol_SkipsOwnNodeId_WhenProcessingMessages()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();
            await protocol.UpdateLocalStateAsync(NodeState.Active);

            // Try to process a message claiming to update our own state
            var message = new GossipMessage
            {
                SenderId = "other-node",
                Generation = 999, // Higher generation
                Version = 1,
                NodeStates = new Dictionary<string, NodeState>
                {
                    { _configuration.NodeId, NodeState.Dead } // Try to set our state to Dead
                }
            };

            await protocol.ProcessGossipMessageAsync(message, "other-node");

            // Our state should remain Active, not Dead
            var state = protocol.GetClusterState();
            Assert.Equal(NodeState.Active, state[_configuration.NodeId].State);

            protocol.Dispose();
        }

        [Fact]
        public void GossipReceivedEventArgs_Properties_CanBeSet()
        {
            var node = new NodeInfo { NodeId = "sender", Host = "127.0.0.1", P2PPort = 9092 };
            var args = new GossipReceivedEventArgs
            {
                Sender = node,
                StateCount = 5
            };

            Assert.Equal("sender", args.Sender.NodeId);
            Assert.Equal(5, args.StateCount);
        }

        [Fact]
        public void GossipStateUpdatedEventArgs_Properties_CanBeSet()
        {
            var node = new NodeInfo { NodeId = "node-1", Host = "localhost", P2PPort = 9091 };
            var args = new GossipStateUpdatedEventArgs
            {
                Node = node,
                PreviousState = NodeState.Joining,
                NewState = NodeState.Active,
                SourceNodeId = "source-node"
            };

            Assert.Equal("node-1", args.Node.NodeId);
            Assert.Equal(NodeState.Joining, args.PreviousState);
            Assert.Equal(NodeState.Active, args.NewState);
            Assert.Equal("source-node", args.SourceNodeId);
        }

        [Fact]
        public async Task GossipProtocol_ProcessGossipMessageAsync_NullNodeStates_HandlesGracefully()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();

            var message = new GossipMessage
            {
                SenderId = "sender-node",
                Generation = 1,
                Version = 1,
                NodeStates = null!, // Null node states
                Heartbeats = new Dictionary<string, long>()
            };

            // Should not throw
            await protocol.ProcessGossipMessageAsync(message, "sender-node");

            protocol.Dispose();
        }

        [Fact]
        public async Task GossipProtocol_ProcessGossipMessageAsync_NullHeartbeats_HandlesGracefully()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();

            var message = new GossipMessage
            {
                SenderId = "sender-node",
                Generation = 1,
                Version = 1,
                NodeStates = new Dictionary<string, NodeState>(),
                Heartbeats = null! // Null heartbeats
            };

            // Should not throw
            await protocol.ProcessGossipMessageAsync(message, "sender-node");

            protocol.Dispose();
        }

        [Fact]
        public void GossipOptions_DefaultValues_AreReasonable()
        {
            var options = new GossipOptions();

            Assert.Equal(TimeSpan.FromMilliseconds(500), options.GossipInterval);
            Assert.Equal(3, options.Fanout);
            Assert.Equal(10, options.MaxNodesPerMessage);
            Assert.True(options.UsePushPull);
            Assert.Equal(TimeSpan.FromSeconds(3), options.GossipTimeout);
            Assert.Equal(4, options.SuspicionMultiplier);
            Assert.Equal(TimeSpan.FromSeconds(60), options.MaxSuspicionTimeout);
            Assert.False(options.EnableCompression);
        }

        [Fact]
        public void GossipOptions_Validate_MaxSuspicionTimeout_CanBeCustom()
        {
            var options = new GossipOptions
            {
                MaxSuspicionTimeout = TimeSpan.FromMinutes(5)
            };

            options.Validate();

            Assert.Equal(TimeSpan.FromMinutes(5), options.MaxSuspicionTimeout);
        }

        [Fact]
        public void GossipOptions_Validate_MinSuspicionMultiplier_IsOne()
        {
            var options = new GossipOptions
            {
                SuspicionMultiplier = 0
            };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Fact]
        public async Task GossipProtocol_Dispose_CanBeCalledMultipleTimes()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();

            protocol.Dispose();
            protocol.Dispose(); // Should not throw
        }

        [Fact]
        public async Task GossipProtocol_MultipleNodes_TrackedCorrectly()
        {
            var protocol = new GossipProtocol(
                _mockClusterManager.Object,
                _configuration,
                _options);

            await protocol.StartAsync();
            await protocol.UpdateLocalStateAsync(NodeState.Active);

            // Add multiple nodes
            for (int i = 0; i < 3; i++)
            {
                var message = new GossipMessage
                {
                    SenderId = "sender",
                    Generation = 1,
                    Version = i + 1,
                    NodeStates = new Dictionary<string, NodeState>
                    {
                        { $"node-{i}", NodeState.Active }
                    }
                };

                await protocol.ProcessGossipMessageAsync(message, "sender");
            }

            var state = protocol.GetClusterState();
            Assert.Equal(4, state.Count); // Local + 3 remote nodes

            protocol.Dispose();
        }
    }
}
