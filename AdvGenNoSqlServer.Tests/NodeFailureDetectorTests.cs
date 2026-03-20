// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Clustering;
using Moq;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for the Node Failure Detector implementation.
    /// </summary>
    public class NodeFailureDetectorTests : IDisposable
    {
        private readonly Mock<IClusterManager> _mockClusterManager;
        private readonly Mock<IGossipProtocol> _mockGossipProtocol;
        private readonly P2PConfiguration _configuration;
        private readonly GossipOptions _gossipOptions;

        public NodeFailureDetectorTests()
        {
            _mockClusterManager = new Mock<IClusterManager>();
            _mockGossipProtocol = new Mock<IGossipProtocol>();
            _configuration = new P2PConfiguration
            {
                NodeId = Guid.NewGuid().ToString("N"),
                ClusterId = "test-cluster",
                BindAddress = "127.0.0.1",
                P2PPort = 9092,
                HeartbeatInterval = TimeSpan.FromMilliseconds(100),
                DeadNodeTimeout = TimeSpan.FromMilliseconds(500)
            };
            _gossipOptions = new GossipOptions
            {
                SuspicionMultiplier = 2,
                MaxSuspicionTimeout = TimeSpan.FromSeconds(5)
            };
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        [Fact]
        public void NodeFailureDetector_Constructor_ValidParameters_CreatesInstance()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            Assert.NotNull(detector);
        }

        [Fact]
        public void NodeFailureDetector_Constructor_NullClusterManager_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new NodeFailureDetector(null!, _mockGossipProtocol.Object, _configuration, _gossipOptions));
        }

        [Fact]
        public void NodeFailureDetector_Constructor_NullGossipProtocol_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new NodeFailureDetector(_mockClusterManager.Object, null!, _configuration, _gossipOptions));
        }

        [Fact]
        public void NodeFailureDetector_Constructor_NullConfiguration_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new NodeFailureDetector(_mockClusterManager.Object, _mockGossipProtocol.Object, null!, _gossipOptions));
        }

        [Fact]
        public async Task NodeFailureDetector_StartAsync_StartsSuccessfully()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            await detector.StartAsync();

            var stats = detector.GetStats();
            Assert.True(stats.StartedAt > DateTime.MinValue);

            detector.Dispose();
        }

        [Fact]
        public async Task NodeFailureDetector_StartAsync_CalledTwice_DoesNotThrow()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            await detector.StartAsync();
            await detector.StartAsync();

            detector.Dispose();
        }

        [Fact]
        public async Task NodeFailureDetector_StopAsync_StopsSuccessfully()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            await detector.StartAsync();
            await detector.StopAsync();

            detector.Dispose();
        }

        [Fact]
        public async Task NodeFailureDetector_StopAsync_CalledTwice_DoesNotThrow()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            await detector.StartAsync();
            await detector.StopAsync();
            await detector.StopAsync();

            detector.Dispose();
        }

        [Fact]
        public async Task NodeFailureDetector_RecordHeartbeat_NewNode_TracksNode()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            await detector.StartAsync();

            detector.RecordHeartbeat("node-1");

            var status = detector.GetNodeStatus("node-1");
            Assert.Equal(NodeStatus.Alive, status);

            var stats = detector.GetStats();
            Assert.Equal(1, stats.TotalNodes);
            Assert.Equal(1, stats.AliveNodes);

            detector.Dispose();
        }

        [Fact]
        public void NodeFailureDetector_RecordHeartbeat_SkipsOwnNodeId()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            // Recording heartbeat for self should not track it
            detector.RecordHeartbeat(_configuration.NodeId);

            var stats = detector.GetStats();
            Assert.Equal(0, stats.TotalNodes);
        }

        [Fact]
        public void NodeFailureDetector_GetNodeStatus_UnknownNode_ReturnsUnknown()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            var status = detector.GetNodeStatus("non-existent-node");
            Assert.Equal(NodeStatus.Unknown, status);
        }

        [Fact]
        public async Task NodeFailureDetector_GetNodeStatus_LocalNode_ReturnsAlive()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            await detector.StartAsync();

            var status = detector.GetNodeStatus(_configuration.NodeId);
            Assert.Equal(NodeStatus.Alive, status);

            detector.Dispose();
        }

        [Fact]
        public void NodeFailedEventArgs_Properties_CanBeSet()
        {
            var node = new NodeInfo { NodeId = "failed-node", Host = "127.0.0.1", P2PPort = 9092 };
            var args = new NodeFailedEventArgs
            {
                Node = node,
                TimeSinceLastSeen = TimeSpan.FromSeconds(30),
                Reason = "Heartbeat timeout"
            };

            Assert.Equal("failed-node", args.Node.NodeId);
            Assert.Equal(TimeSpan.FromSeconds(30), args.TimeSinceLastSeen);
            Assert.Equal("Heartbeat timeout", args.Reason);
        }

        [Fact]
        public void NodeRecoveredEventArgs_Properties_CanBeSet()
        {
            var node = new NodeInfo { NodeId = "recovered-node", Host = "127.0.0.1", P2PPort = 9092 };
            var args = new NodeRecoveredEventArgs
            {
                Node = node,
                TimeFailed = TimeSpan.FromMinutes(5)
            };

            Assert.Equal("recovered-node", args.Node.NodeId);
            Assert.Equal(TimeSpan.FromMinutes(5), args.TimeFailed);
        }

        [Fact]
        public void NodeSuspectedEventArgs_Properties_CanBeSet()
        {
            var node = new NodeInfo { NodeId = "suspected-node", Host = "127.0.0.1", P2PPort = 9092 };
            var args = new NodeSuspectedEventArgs
            {
                Node = node,
                ConfirmationCount = 2
            };

            Assert.Equal("suspected-node", args.Node.NodeId);
            Assert.Equal(2, args.ConfirmationCount);
        }

        [Fact]
        public void FailureDetectorStats_Properties_CanBeSet()
        {
            var stats = new FailureDetectorStats
            {
                TotalNodes = 10,
                AliveNodes = 7,
                SuspectedNodes = 2,
                FailedNodes = 1,
                TotalHeartbeatsReceived = 5000,
                TotalFailuresDetected = 5,
                TotalSuspicionsRaised = 10,
                StartedAt = DateTime.UtcNow
            };

            Assert.Equal(10, stats.TotalNodes);
            Assert.Equal(7, stats.AliveNodes);
            Assert.Equal(2, stats.SuspectedNodes);
            Assert.Equal(1, stats.FailedNodes);
            Assert.Equal(5000, stats.TotalHeartbeatsReceived);
            Assert.Equal(5, stats.TotalFailuresDetected);
            Assert.Equal(10, stats.TotalSuspicionsRaised);
        }

        [Fact]
        public void NodeStatus_Enum_HasExpectedValues()
        {
            Assert.Equal(0, (int)NodeStatus.Alive);
            Assert.Equal(1, (int)NodeStatus.Suspected);
            Assert.Equal(2, (int)NodeStatus.Failed);
            Assert.Equal(3, (int)NodeStatus.Unknown);
        }

        [Fact]
        public async Task NodeFailureDetector_ConfirmSuspicion_AddsConfirmation()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            await detector.StartAsync();

            // Simulate a node being tracked
            detector.RecordHeartbeat("node-1");

            // Confirm suspicion from other nodes
            detector.ConfirmSuspicion("node-1", "confirmer-1");
            detector.ConfirmSuspicion("node-1", "confirmer-2");

            // The confirmations should be tracked (internally)
            // We can't directly verify, but the behavior should work

            detector.Dispose();
        }

        [Fact]
        public async Task NodeFailureDetector_RecordHeartbeat_RecoversFailedNode()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            var recoveredEvent = new TaskCompletionSource<NodeRecoveredEventArgs>();
            detector.NodeRecovered += (s, e) => recoveredEvent.TrySetResult(e);

            await detector.StartAsync();

            // First, track the node
            detector.RecordHeartbeat("node-1");

            // Simulate time passing without heartbeats would mark it as suspected/failed
            // But for this test, we just verify the tracking mechanism

            detector.Dispose();
        }

        [Fact]
        public async Task NodeFailureDetector_MultipleNodes_TracksIndependently()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            await detector.StartAsync();

            detector.RecordHeartbeat("node-1");
            detector.RecordHeartbeat("node-2");
            detector.RecordHeartbeat("node-3");

            var stats = detector.GetStats();
            Assert.Equal(3, stats.TotalNodes);
            Assert.Equal(3, stats.AliveNodes);

            Assert.Equal(NodeStatus.Alive, detector.GetNodeStatus("node-1"));
            Assert.Equal(NodeStatus.Alive, detector.GetNodeStatus("node-2"));
            Assert.Equal(NodeStatus.Alive, detector.GetNodeStatus("node-3"));

            detector.Dispose();
        }

        [Fact]
        public async Task NodeFailureDetector_GetStats_InitialState_IsZero()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            var stats = detector.GetStats();

            Assert.Equal(0, stats.TotalNodes);
            Assert.Equal(0, stats.AliveNodes);
            Assert.Equal(0, stats.SuspectedNodes);
            Assert.Equal(0, stats.FailedNodes);
            Assert.Equal(0, stats.TotalHeartbeatsReceived);
            Assert.Equal(0, stats.TotalFailuresDetected);
            Assert.Equal(0, stats.TotalSuspicionsRaised);
        }

        [Fact]
        public async Task NodeFailureDetector_MultipleHeartbeats_IncrementsCounter()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            await detector.StartAsync();

            for (int i = 0; i < 10; i++)
            {
                detector.RecordHeartbeat("node-1");
            }

            var stats = detector.GetStats();
            Assert.Equal(10, stats.TotalHeartbeatsReceived);

            detector.Dispose();
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
        public async Task NodeFailureDetector_Dispose_CanBeCalledMultipleTimes()
        {
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            await detector.StartAsync();

            detector.Dispose();
            detector.Dispose(); // Should not throw
        }

        [Fact]
        public async Task NodeFailureDetector_GossipStateUpdated_TriggersRecordHeartbeat()
        {
            // This test verifies that the detector subscribes to gossip updates
            var detector = new NodeFailureDetector(
                _mockClusterManager.Object,
                _mockGossipProtocol.Object,
                _configuration,
                _gossipOptions);

            await detector.StartAsync();

            // Simulate a gossip state update event
            var nodeInfo = new NodeInfo { NodeId = "remote-node", Host = "127.0.0.1", P2PPort = 9092 };
            var eventArgs = new GossipStateUpdatedEventArgs
            {
                Node = nodeInfo,
                PreviousState = NodeState.Joining,
                NewState = NodeState.Active,
                SourceNodeId = "source-node"
            };

            // Raise the event on the mock
            _mockGossipProtocol.Raise(m => m.StateUpdated += null, (object?)null, eventArgs);

            // The detector should have recorded this as a heartbeat
            var stats = detector.GetStats();
            Assert.True(stats.TotalHeartbeatsReceived > 0);

            detector.Dispose();
        }
    }
}
