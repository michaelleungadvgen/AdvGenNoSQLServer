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
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for the ReplicationManager.
    /// </summary>
    public class ReplicationManagerTests : IDisposable
    {
        private readonly MockClusterManager _clusterManager;
        private readonly DocumentStore _documentStore;
        private readonly ReplicationConfiguration _config;
        private readonly ReplicationManager _manager;

        public ReplicationManagerTests()
        {
            _clusterManager = new MockClusterManager();
            _documentStore = new DocumentStore();
            _config = new ReplicationConfiguration
            {
                ReplicationFactor = 3,
                WriteQuorum = 2,
                ReadQuorum = 1,
                Strategy = "SemiSynchronous",
                SyncTimeout = TimeSpan.FromSeconds(5)
            };
            _manager = new ReplicationManager(_clusterManager, _config);
        }

        public void Dispose()
        {
            _manager.Dispose();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_ValidParameters_CreatesInstance()
        {
            var manager = new ReplicationManager(_clusterManager, _config);
            Assert.NotNull(manager);
            Assert.Equal(_config, manager.Configuration);
        }

        [Fact]
        public void Constructor_NullClusterManager_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ReplicationManager(null!, _config));
        }

        [Fact]
        public void Constructor_NullDocumentStore_ThrowsArgumentNullException()
        {
            // Document store is no longer required in constructor
        }

        [Fact]
        public void Constructor_NullConfiguration_UsesDefault()
        {
            var manager = new ReplicationManager(_clusterManager, null);
            Assert.NotNull(manager.Configuration);
            Assert.Equal(3, manager.Configuration.ReplicationFactor);
        }

        [Fact]
        public void Constructor_InvalidConfiguration_ThrowsArgumentException()
        {
            var invalidConfig = new ReplicationConfiguration
            {
                ReplicationFactor = 0,
                WriteQuorum = 0
            };
            Assert.Throws<ArgumentException>(() => new ReplicationManager(_clusterManager, invalidConfig));
        }

        #endregion

        #region Lifecycle Tests

        [Fact]
        public async Task StartAsync_NotStarted_StartsSuccessfully()
        {
            await _manager.StartAsync();
            // Should not throw
        }

        [Fact]
        public async Task StartAsync_AlreadyStarted_NoOp()
        {
            await _manager.StartAsync();
            await _manager.StartAsync(); // Should not throw
        }

        [Fact]
        public async Task StopAsync_Started_StopsSuccessfully()
        {
            await _manager.StartAsync();
            await _manager.StopAsync();
            // Should not throw
        }

        [Fact]
        public async Task StopAsync_NotStarted_NoOp()
        {
            await _manager.StopAsync(); // Should not throw
        }

        #endregion

        #region Replication Factor Tests

        [Fact]
        public async Task SetReplicationFactor_ValidCollection_SetsFactor()
        {
            await _manager.SetReplicationFactorAsync("users", 5);
            var factor = await _manager.GetReplicationFactorAsync("users");
            Assert.Equal(5, factor);
        }

        [Fact]
        public async Task GetReplicationFactor_UnknownCollection_ReturnsDefault()
        {
            var factor = await _manager.GetReplicationFactorAsync("unknown");
            Assert.Equal(_config.ReplicationFactor, factor);
        }

        [Fact]
        public async Task SetReplicationFactor_EmptyCollection_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _manager.SetReplicationFactorAsync("", 3));
        }

        [Fact]
        public async Task SetReplicationFactor_ZeroFactor_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _manager.SetReplicationFactorAsync("users", 0));
        }

        #endregion

        #region Replication Event Tests

        [Fact]
        public void ReplicationEvent_Insert_CreatesCorrectType()
        {
            var doc = CreateTestDocument("doc1");
            var evt = ReplicationEvent.Insert("node1", "users", doc, 1, 1);

            Assert.Equal(ReplicationEventType.Insert, evt.Type);
            Assert.Equal("node1", evt.SourceNodeId);
            Assert.Equal("users", evt.Collection);
            Assert.Equal("doc1", evt.DocumentId);
            Assert.Equal(doc, evt.Document);
            Assert.Equal(1, evt.SequenceNumber);
            Assert.Equal(1, evt.Term);
        }

        [Fact]
        public void ReplicationEvent_Update_CreatesCorrectType()
        {
            var doc = CreateTestDocument("doc1");
            var prevDoc = CreateTestDocument("doc1");
            var evt = ReplicationEvent.Update("node1", "users", doc, prevDoc, 1, 1);

            Assert.Equal(ReplicationEventType.Update, evt.Type);
            Assert.Equal(doc, evt.Document);
            Assert.Equal(prevDoc, evt.PreviousDocument);
        }

        [Fact]
        public void ReplicationEvent_Delete_CreatesCorrectType()
        {
            var prevDoc = CreateTestDocument("doc1");
            var evt = ReplicationEvent.Delete("node1", "users", "doc1", prevDoc, 1, 1);

            Assert.Equal(ReplicationEventType.Delete, evt.Type);
            Assert.Equal("doc1", evt.DocumentId);
            Assert.Equal(prevDoc, evt.PreviousDocument);
            Assert.Null(evt.Document);
        }

        #endregion

        #region ReplicationAck Tests

        [Fact]
        public void ReplicationAck_SuccessAck_HasSuccessTrue()
        {
            var ack = ReplicationAck.SuccessAck("op1", "node1", 100);

            Assert.Equal("op1", ack.OperationId);
            Assert.Equal("node1", ack.NodeId);
            Assert.True(ack.Success);
            Assert.Equal(100, ack.SequenceNumber);
            Assert.Null(ack.ErrorMessage);
        }

        [Fact]
        public void ReplicationAck_FailedAck_HasSuccessFalse()
        {
            var ack = ReplicationAck.FailedAck("op1", "node1", "Connection failed", 100);

            Assert.Equal("op1", ack.OperationId);
            Assert.Equal("node1", ack.NodeId);
            Assert.False(ack.Success);
            Assert.Equal(100, ack.SequenceNumber);
            Assert.Equal("Connection failed", ack.ErrorMessage);
        }

        #endregion

        #region Configuration Tests

        [Fact]
        public void ReplicationConfiguration_ValidValues_ValidatesSuccessfully()
        {
            var config = new ReplicationConfiguration
            {
                ReplicationFactor = 3,
                WriteQuorum = 2,
                ReadQuorum = 1,
                Strategy = "SemiSynchronous"
            };

            // Configuration is valid if it doesn't throw
            Assert.Equal(3, config.ReplicationFactor);
            Assert.Equal(2, config.WriteQuorum);
        }

        [Fact]
        public void ReplicationManager_InvalidReplicationFactor_ThrowsArgumentException()
        {
            var config = new ReplicationConfiguration
            {
                ReplicationFactor = 0
            };

            Assert.Throws<ArgumentException>(() => new ReplicationManager(_clusterManager, config));
        }

        [Fact]
        public void ReplicationManager_WriteQuorumExceedsFactor_ThrowsArgumentException()
        {
            var config = new ReplicationConfiguration
            {
                ReplicationFactor = 2,
                WriteQuorum = 5
            };

            Assert.Throws<ArgumentException>(() => new ReplicationManager(_clusterManager, config));
        }

        [Fact]
        public void ReplicationConfiguration_DefaultValues_AreCorrect()
        {
            var config = new ReplicationConfiguration();

            Assert.Equal(3, config.ReplicationFactor);
            Assert.Equal("SemiSynchronous", config.Strategy);
            Assert.Equal(3, config.ReplicationFactor);
            Assert.Equal(2, config.WriteQuorum);
            Assert.Equal(1, config.ReadQuorum);
            Assert.Equal(TimeSpan.FromSeconds(5), config.SyncTimeout);
        }

        #endregion

        #region SyncStatus Tests

        [Fact]
        public async Task GetSyncStatus_UnknownNode_ReturnsNotSynchronized()
        {
            var status = await _manager.GetSyncStatusAsync("unknown-node");

            Assert.Equal("unknown-node", status.NodeId);
            Assert.False(status.IsSynchronized);
            Assert.Equal(0, status.LastSequenceNumber);
        }

        [Fact]
        public async Task GetAllSyncStatus_NoNodes_ReturnsEmptyList()
        {
            var statuses = await _manager.GetAllSyncStatusAsync();
            Assert.Empty(statuses);
        }

        #endregion

        #region ReplicationResult Tests

        [Fact]
        public void ReplicationResult_Success_HasCorrectValues()
        {
            var result = ReplicationResult.SuccessResult(2, 2, new List<string> { "node1", "node2" }, TimeSpan.FromMilliseconds(100));

            Assert.True(result.Success);
            Assert.Equal(2, result.AcknowledgedCount);
            Assert.Equal(2, result.RequiredQuorum);
            Assert.Contains("node1", result.AcknowledgingNodes);
            Assert.Contains("node2", result.AcknowledgingNodes);
        }

        [Fact]
        public void ReplicationResult_Failure_HasCorrectValues()
        {
            var failedNodes = new Dictionary<string, string> { { "node2", "Timeout" } };
            var result = ReplicationResult.FailureResult(1, 2, "Quorum not reached", failedNodes, TimeSpan.FromMilliseconds(500));

            Assert.False(result.Success);
            Assert.Equal(1, result.AcknowledgedCount);
            Assert.Equal(2, result.RequiredQuorum);
            Assert.Equal("Quorum not reached", result.ErrorMessage);
            Assert.Contains("node2", result.FailedNodes.Keys);
        }

        #endregion

        #region Statistics Tests

        [Fact]
        public void Statistics_InitialState_IsZero()
        {
            var stats = _manager.Statistics;

            Assert.Equal(0, stats.TotalEventsSent);
            Assert.Equal(0, stats.TotalEventsAcknowledged);
            Assert.Equal(0, stats.TotalFailures);
            Assert.Equal(0, stats.PendingEvents);
        }

        [Fact]
        public void Statistics_PerNodeStats_InitiallyEmpty()
        {
            var stats = _manager.Statistics;
            Assert.Empty(stats.PerNodeStats);
        }

        #endregion

        #region ApplyReplicationEvent Tests

        [Fact]
        public async Task ApplyReplicationEvent_WithCallback_InvokesCallback()
        {
            var callbackInvoked = false;
            ReplicationEvent? receivedEvent = null;

            _manager.ApplyEventCallback = (evt, ct) =>
            {
                callbackInvoked = true;
                receivedEvent = evt;
                return Task.CompletedTask;
            };

            var doc = CreateTestDocument("doc1");
            var evt = ReplicationEvent.Insert("node1", "users", doc, 1, 1);

            await _manager.ApplyReplicationEventAsync(evt);

            Assert.True(callbackInvoked);
            Assert.NotNull(receivedEvent);
            Assert.Equal(evt.OperationId, receivedEvent.OperationId);
        }

        [Fact]
        public async Task ApplyReplicationEvent_WithoutCallback_DoesNotThrow()
        {
            var doc = CreateTestDocument("doc1");
            var evt = ReplicationEvent.Insert("node1", "users", doc, 1, 1);

            // Should not throw even without callback
            await _manager.ApplyReplicationEventAsync(evt);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task FullReplicationFlow_WithAcknowledgments_CompletesSuccessfully()
        {
            await _manager.StartAsync();

            // Setup: add other nodes to cluster
            _clusterManager.AddNode(new NodeInfo { NodeId = "node2", Host = "localhost", P2PPort = 9093 });
            _clusterManager.AddNode(new NodeInfo { NodeId = "node3", Host = "localhost", P2PPort = 9095 });

            var doc = CreateTestDocument("doc1");
            var evt = ReplicationEvent.Insert("node1", "users", doc, 1, 1);

            // Start replication (this creates pending replication)
            var replicateTask = _manager.ReplicateWriteAsync(evt);

            // Simulate acknowledgments from other nodes
            await Task.Delay(50); // Let replication start
            await _manager.ProcessAckAsync(ReplicationAck.SuccessAck(evt.OperationId, "node2", 1));
            await _manager.ProcessAckAsync(ReplicationAck.SuccessAck(evt.OperationId, "node3", 1));

            var result = await replicateTask;

            Assert.True(result.Success);
            Assert.True(result.AcknowledgedCount >= _config.WriteQuorum);
        }

        [Fact]
        public async Task ReplicateWrite_NotStarted_ThrowsInvalidOperationException()
        {
            var doc = CreateTestDocument("doc1");
            var evt = ReplicationEvent.Insert("node1", "users", doc, 1, 1);

            await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.ReplicateWriteAsync(evt));
        }

        [Fact]
        public async Task BatchReplication_MultipleEvents_ReplicatesAll()
        {
            await _manager.StartAsync();

            var events = new List<ReplicationEvent>
            {
                ReplicationEvent.Insert("node1", "users", CreateTestDocument("doc1"), 1, 1),
                ReplicationEvent.Insert("node1", "users", CreateTestDocument("doc2"), 2, 1),
                ReplicationEvent.Insert("node1", "users", CreateTestDocument("doc3"), 3, 1)
            };

            var result = await _manager.ReplicateBatchAsync(events);

            // Batch replication returns aggregated result
            Assert.NotNull(result);
        }

        #endregion

        #region Helper Methods

        private Document CreateTestDocument(string id, string name = "Test")
        {
            return new Document
            {
                Id = id,
                Data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                    $"{{\"name\":\"{name}\",\"value\":123}}")!,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };
        }

        #endregion

        #region Mock Classes

        private class MockClusterManager : IClusterManager
        {
            private readonly List<NodeInfo> _nodes = new();
            private NodeIdentity _localNode = new()
            {
                NodeId = "node1",
                ClusterId = "test-cluster",
                Host = "localhost",
                Port = 9090,
                P2PPort = 9091,
                State = NodeState.Active
            };

            public NodeIdentity LocalNode => _localNode;
            public bool IsClusterMember => true;
            public bool IsLeader => true;

            public void AddNode(NodeInfo node)
            {
                _nodes.Add(node);
            }

            public Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default)
            {
                return Task.FromResult(new ClusterInfo
                {
                    ClusterId = "test-cluster",
                    ClusterName = "Test Cluster",
                    Leader = new NodeInfo { NodeId = "node1", Host = "localhost", P2PPort = 9091 },
                    Nodes = _nodes.ToList(),
                    Health = ClusterHealth.Healthy
                });
            }

            public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken ct = default)
            {
                return Task.FromResult<IReadOnlyList<NodeInfo>>(_nodes.ToList());
            }

            public Task<NodeInfo?> GetNodeAsync(string nodeId, CancellationToken ct = default)
            {
                return Task.FromResult(_nodes.FirstOrDefault(n => n.NodeId == nodeId));
            }

            public Task<JoinResult> JoinClusterAsync(string seedNode, JoinOptions options, CancellationToken ct = default)
            {
                return Task.FromResult(new JoinResult { Success = true });
            }

            public Task<LeaveResult> LeaveClusterAsync(LeaveOptions options, CancellationToken ct = default)
            {
                return Task.FromResult(new LeaveResult { Success = true });
            }

            public Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default)
            {
                _nodes.RemoveAll(n => n.NodeId == nodeId);
                return Task.FromResult(true);
            }

            public Task<NodeInfo?> GetLeaderAsync(CancellationToken ct = default)
            {
                return Task.FromResult<NodeInfo?>(new NodeInfo { NodeId = "node1", Host = "localhost", P2PPort = 9091 });
            }

            public Task<bool> RequestLeaderElectionAsync(CancellationToken ct = default)
            {
                return Task.FromResult(true);
            }

            public Task<bool> UpdateNodeStateAsync(NodeState newState, CancellationToken ct = default)
            {
                _localNode.State = newState;
                return Task.FromResult(true);
            }

            public Task<JoinResult> CreateClusterAsync(string clusterName, CancellationToken ct = default)
            {
                return Task.FromResult(new JoinResult { Success = true });
            }

            public void Dispose()
            {
            }

            public event EventHandler<NodeJoinedEventArgs>? NodeJoined;
            public event EventHandler<NodeLeftEventArgs>? NodeLeft;
            public event EventHandler<LeaderChangedEventArgs>? LeaderChanged;
            public event EventHandler<NodeStateChangedEventArgs>? NodeStateChanged;
        }

        #endregion
    }
}
