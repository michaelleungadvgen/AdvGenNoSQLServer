// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Clustering;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for Raft consensus implementation.
    /// </summary>
    public class RaftConsensusTests : IDisposable
    {
        private readonly MockClusterManager _clusterManager;
        private readonly MockRaftRpcClient _rpcClient;
        private readonly RaftConfiguration _config;

        /// <summary>
        /// Creates a new test instance.
        /// </summary>
        public RaftConsensusTests()
        {
            _clusterManager = new MockClusterManager();
            _rpcClient = new MockRaftRpcClient();
            _config = new RaftConfiguration
            {
                ElectionTimeoutMinMs = 100,
                ElectionTimeoutMaxMs = 200,
                HeartbeatIntervalMs = 50,
                MaxEntriesPerAppend = 10
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _clusterManager.Dispose();
        }

        /// <summary>
        /// Creates a new Raft consensus instance for testing.
        /// </summary>
        private RaftConsensus CreateRaft(string nodeId)
        {
            return new RaftConsensus(nodeId, _config, _clusterManager, _rpcClient);
        }

        [Fact]
        public void Constructor_WithValidParameters_CreatesInstance()
        {
            // Act
            var raft = CreateRaft("node1");

            // Assert
            Assert.NotNull(raft);
            Assert.Equal("node1", raft.LocalNodeId);
            Assert.Equal(RaftRole.Follower, raft.CurrentRole);
            Assert.Equal(0L, raft.CurrentTerm);
            Assert.Null(raft.CurrentLeaderId);
        }

        [Fact]
        public void Constructor_WithNullNodeId_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new RaftConsensus(null!, _config, _clusterManager, _rpcClient));
        }

        [Fact]
        public void Constructor_WithNullConfig_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new RaftConsensus("node1", null!, _clusterManager, _rpcClient));
        }

        [Fact]
        public void Constructor_WithNullClusterManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new RaftConsensus("node1", _config, null!, _rpcClient));
        }

        [Fact]
        public void Constructor_WithNullRpcClient_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new RaftConsensus("node1", _config, _clusterManager, null!));
        }

        [Fact]
        public async Task StartAsync_StartsAsFollower()
        {
            // Arrange
            var raft = CreateRaft("node1");

            // Act
            await raft.StartAsync();

            // Assert
            Assert.Equal(RaftRole.Follower, raft.CurrentRole);

            // Cleanup
            await raft.StopAsync();
        }

        [Fact]
        public async Task StartAsync_TwiceThrowsInvalidOperationException()
        {
            // Arrange
            var raft = CreateRaft("node1");
            await raft.StartAsync();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => raft.StartAsync());

            // Cleanup
            await raft.StopAsync();
        }

        [Fact]
        public async Task StopAsync_CanBeCalledMultipleTimes()
        {
            // Arrange
            var raft = CreateRaft("node1");
            await raft.StartAsync();
            await raft.StopAsync();

            // Act & Assert - Should not throw
            await raft.StopAsync();
        }

        [Fact]
        public async Task ProposeAsync_WhenNotLeader_ReturnsFailure()
        {
            // Arrange
            var raft = CreateRaft("node1");
            await raft.StartAsync();
            var entry = new RaftLogEntry { Data = new byte[] { 1, 2, 3 } };

            // Act
            var result = await raft.ProposeAsync(entry);

            // Assert
            Assert.False(result.Accepted);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("Not the leader", result.ErrorMessage);

            // Cleanup
            await raft.StopAsync();
        }

        [Fact]
        public async Task HandleVoteRequest_LowerTerm_ReturnsFalse()
        {
            // Arrange
            var raft = CreateRaft("node1");
            await raft.StartAsync();
            
            // Simulate higher term
            await ForceTerm(raft, 5);

            var request = new VoteRequest
            {
                Term = 3, // Lower term
                CandidateId = "node2",
                LastLogIndex = 0,
                LastLogTerm = 0
            };

            // Act
            var response = await raft.HandleVoteRequestAsync(request);

            // Assert
            Assert.False(response.VoteGranted);
            Assert.Equal(5, response.Term);

            // Cleanup
            await raft.StopAsync();
        }

        [Fact]
        public async Task HandleVoteRequest_HigherTerm_UpdatesTermAndBecomesFollower()
        {
            // Arrange
            var raft = CreateRaft("node1");
            await raft.StartAsync();

            var request = new VoteRequest
            {
                Term = 5, // Higher term
                CandidateId = "node2",
                LastLogIndex = 0,
                LastLogTerm = 0
            };

            // Act
            var response = await raft.HandleVoteRequestAsync(request);

            // Assert
            Assert.Equal(5, raft.CurrentTerm);
            Assert.Equal(RaftRole.Follower, raft.CurrentRole);

            // Cleanup
            await raft.StopAsync();
        }

        [Fact]
        public async Task HandleVoteRequest_ValidCandidate_GrantsVote()
        {
            // Arrange
            var raft = CreateRaft("node1");
            await raft.StartAsync();

            var request = new VoteRequest
            {
                Term = 1,
                CandidateId = "node2",
                LastLogIndex = 0,
                LastLogTerm = 0
            };

            // Act
            var response = await raft.HandleVoteRequestAsync(request);

            // Assert
            Assert.True(response.VoteGranted);
            Assert.Equal(1, response.Term);

            // Cleanup
            await raft.StopAsync();
        }

        [Fact]
        public async Task HandleVoteRequest_AlreadyVoted_DeniesSecondVote()
        {
            // Arrange
            var raft = CreateRaft("node1");
            await raft.StartAsync();

            var request1 = new VoteRequest
            {
                Term = 1,
                CandidateId = "node2",
                LastLogIndex = 0,
                LastLogTerm = 0
            };

            var request2 = new VoteRequest
            {
                Term = 1,
                CandidateId = "node3",
                LastLogIndex = 0,
                LastLogTerm = 0
            };

            // Act
            var response1 = await raft.HandleVoteRequestAsync(request1);
            var response2 = await raft.HandleVoteRequestAsync(request2);

            // Assert
            Assert.True(response1.VoteGranted);
            Assert.False(response2.VoteGranted);

            // Cleanup
            await raft.StopAsync();
        }

        [Fact]
        public async Task HandleVoteRequest_SameCandidateTwice_GrantsBoth()
        {
            // Arrange
            var raft = CreateRaft("node1");
            await raft.StartAsync();

            var request = new VoteRequest
            {
                Term = 1,
                CandidateId = "node2",
                LastLogIndex = 0,
                LastLogTerm = 0
            };

            // Act
            var response1 = await raft.HandleVoteRequestAsync(request);
            var response2 = await raft.HandleVoteRequestAsync(request);

            // Assert
            Assert.True(response1.VoteGranted);
            Assert.True(response2.VoteGranted);

            // Cleanup
            await raft.StopAsync();
        }

        [Fact]
        public async Task HandleAppendEntries_LowerTerm_ReturnsFalse()
        {
            // Arrange
            var raft = CreateRaft("node1");
            await raft.StartAsync();
            await ForceTerm(raft, 5);

            var request = new AppendRequest
            {
                Term = 3, // Lower term
                LeaderId = "node2",
                PrevLogIndex = 0,
                PrevLogTerm = 0,
                Entries = new List<RaftLogEntry>(),
                LeaderCommit = 0
            };

            // Act
            var response = await raft.HandleAppendEntriesAsync(request);

            // Assert
            Assert.False(response.Success);
            Assert.Equal(5, response.Term);

            // Cleanup
            await raft.StopAsync();
        }

        [Fact]
        public async Task HandleAppendEntries_HigherTerm_BecomesFollower()
        {
            // Arrange
            var raft = CreateRaft("node1");
            await raft.StartAsync();

            var request = new AppendRequest
            {
                Term = 5, // Higher term
                LeaderId = "node2",
                PrevLogIndex = 0,
                PrevLogTerm = 0,
                Entries = new List<RaftLogEntry>(),
                LeaderCommit = 0
            };

            // Act
            var response = await raft.HandleAppendEntriesAsync(request);

            // Assert
            Assert.Equal(5, raft.CurrentTerm);
            Assert.Equal(RaftRole.Follower, raft.CurrentRole);
            Assert.Equal("node2", raft.CurrentLeaderId);

            // Cleanup
            await raft.StopAsync();
        }

        [Fact]
        public async Task HandleAppendEntries_ValidHeartbeat_ReturnsTrue()
        {
            // Arrange
            var raft = CreateRaft("node1");
            await raft.StartAsync();

            var request = new AppendRequest
            {
                Term = 1,
                LeaderId = "node2",
                PrevLogIndex = 0,
                PrevLogTerm = 0,
                Entries = new List<RaftLogEntry>(),
                LeaderCommit = 0
            };

            // Act
            var response = await raft.HandleAppendEntriesAsync(request);

            // Assert
            Assert.True(response.Success);

            // Cleanup
            await raft.StopAsync();
        }

        [Fact]
        public async Task HandleAppendEntries_WithEntries_AppendsToLog()
        {
            // Arrange
            var raft = CreateRaft("node1");
            await raft.StartAsync();

            var entries = new List<RaftLogEntry>
            {
                new RaftLogEntry { Term = 1, Index = 1, Data = new byte[] { 1 } }
            };

            var request = new AppendRequest
            {
                Term = 1,
                LeaderId = "node2",
                PrevLogIndex = 0,
                PrevLogTerm = 0,
                Entries = entries,
                LeaderCommit = 0
            };

            // Act
            var response = await raft.HandleAppendEntriesAsync(request);

            // Assert
            Assert.True(response.Success);
            var log = raft.GetLogEntries();
            Assert.True(log.Count > 0);

            // Cleanup
            await raft.StopAsync();
        }

        [Fact]
        public void GetStatistics_ReturnsCorrectValues()
        {
            // Arrange
            var raft = CreateRaft("node1");

            // Act
            var stats = raft.GetStatistics();

            // Assert
            Assert.Equal(RaftRole.Follower, stats.Role);
            Assert.Equal(0L, stats.CurrentTerm);
            Assert.Equal(0L, stats.LastLogIndex);
            Assert.Equal(0L, stats.CommitIndex);
            Assert.Equal(0L, stats.LastApplied);
        }

        [Fact]
        public void RaftLogEntry_Create_SetsProperties()
        {
            // Arrange
            var data = new byte[] { 1, 2, 3 };

            // Act
            var entry = RaftLogEntry.Create(5, 10, data, RaftOperationType.Command);

            // Assert
            Assert.Equal(5, entry.Term);
            Assert.Equal(10, entry.Index);
            Assert.Equal(data, entry.Data);
            Assert.Equal(RaftOperationType.Command, entry.OperationType);
        }

        [Fact]
        public void VoteResponse_Granted_ReturnsGrantedResponse()
        {
            // Act
            var response = VoteResponse.Granted(5, "node1");

            // Assert
            Assert.True(response.VoteGranted);
            Assert.Equal(5, response.Term);
            Assert.Equal("node1", response.VoterId);
        }

        [Fact]
        public void VoteResponse_Denied_ReturnsDeniedResponse()
        {
            // Act
            var response = VoteResponse.Denied(5, "node1");

            // Assert
            Assert.False(response.VoteGranted);
            Assert.Equal(5, response.Term);
            Assert.Equal("node1", response.VoterId);
        }

        [Fact]
        public void AppendResponse_CreateSuccess_ReturnsSuccess()
        {
            // Act
            var response = AppendResponse.CreateSuccess(5, "node1");

            // Assert
            Assert.True(response.Success);
            Assert.Equal(5, response.Term);
            Assert.Equal("node1", response.ResponderId);
        }

        [Fact]
        public void AppendResponse_CreateFailure_ReturnsFailure()
        {
            // Act
            var response = AppendResponse.CreateFailure(5, "node1", 3);

            // Assert
            Assert.False(response.Success);
            Assert.Equal(5, response.Term);
            Assert.Equal("node1", response.ResponderId);
            Assert.Equal(3, response.ConflictIndex);
        }

        [Fact]
        public void ProposeResult_Success_ReturnsSuccess()
        {
            // Act
            var result = ProposeResult.Success(10, 5);

            // Assert
            Assert.True(result.Accepted);
            Assert.Equal(10, result.LogIndex);
            Assert.Equal(5, result.Term);
        }

        [Fact]
        public void ProposeResult_Failure_ReturnsFailure()
        {
            // Act
            var result = ProposeResult.Failure("test error");

            // Assert
            Assert.False(result.Accepted);
            Assert.Equal("test error", result.ErrorMessage);
        }

        [Fact]
        public void RaftConfiguration_GetElectionTimeout_ReturnsValueInRange()
        {
            // Arrange
            var config = new RaftConfiguration
            {
                ElectionTimeoutMinMs = 100,
                ElectionTimeoutMaxMs = 200
            };

            // Act
            var timeout = config.GetElectionTimeout();

            // Assert
            Assert.True(timeout >= TimeSpan.FromMilliseconds(100));
            Assert.True(timeout <= TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public void RoleChangedEventArgs_ContainsCorrectData()
        {
            // Arrange & Act
            var args = new RoleChangedEventArgs
            {
                PreviousRole = RaftRole.Follower,
                NewRole = RaftRole.Leader,
                Term = 5
            };

            // Assert
            Assert.Equal(RaftRole.Follower, args.PreviousRole);
            Assert.Equal(RaftRole.Leader, args.NewRole);
            Assert.Equal(5, args.Term);
            Assert.True(args.Timestamp <= DateTime.UtcNow);
        }

        [Fact]
        public void LogCommittedEventArgs_ContainsCorrectData()
        {
            // Arrange
            var entry = new RaftLogEntry { Term = 1, Index = 5, Data = new byte[] { 1 } };

            // Act
            var args = new LogCommittedEventArgs
            {
                Entry = entry,
                CommitIndex = 5
            };

            // Assert
            Assert.Equal(entry, args.Entry);
            Assert.Equal(5, args.CommitIndex);
            Assert.True(args.Timestamp <= DateTime.UtcNow);
        }

        [Fact]
        public void AppendRequest_IsHeartbeat_WhenNoEntries()
        {
            // Arrange
            var request = new AppendRequest
            {
                Term = 1,
                LeaderId = "node1",
                Entries = new List<RaftLogEntry>()
            };

            // Act & Assert
            Assert.True(request.IsHeartbeat);
        }

        [Fact]
        public void AppendRequest_IsNotHeartbeat_WhenHasEntries()
        {
            // Arrange
            var request = new AppendRequest
            {
                Term = 1,
                LeaderId = "node1",
                Entries = new List<RaftLogEntry> { new RaftLogEntry() }
            };

            // Act & Assert
            Assert.False(request.IsHeartbeat);
        }

        /// <summary>
        /// Helper method to force a Raft instance to a specific term.
        /// </summary>
        private async Task ForceTerm(RaftConsensus raft, long term)
        {
            // Send an append request with higher term to force term update
            var request = new AppendRequest
            {
                Term = term,
                LeaderId = "other",
                PrevLogIndex = 0,
                PrevLogTerm = 0,
                Entries = new List<RaftLogEntry>(),
                LeaderCommit = 0
            };
            await raft.HandleAppendEntriesAsync(request);
        }
    }

    /// <summary>
    /// Mock implementation of IClusterManager for testing.
    /// </summary>
    internal class MockClusterManager : IClusterManager
    {
        private readonly List<NodeInfo> _nodes = new();
        private NodeIdentity? _localNode;
        private bool _isLeader;

        public NodeIdentity LocalNode => _localNode ?? NodeIdentity.Create("test", "localhost", 9090, 9091);
        public bool IsClusterMember => true;
        public bool IsLeader => _isLeader;

        public void AddNode(NodeInfo node) => _nodes.Add(node);
        public void SetLeader(bool isLeader) => _isLeader = isLeader;

        public Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new ClusterInfo
            {
                ClusterId = "test",
                ClusterName = "Test Cluster",
                Nodes = _nodes.ToList(),
                Leader = _isLeader ? new NodeInfo { NodeId = LocalNode.NodeId, Host = "localhost", P2PPort = 9091 } : null
            });
        }

        public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken ct = default)
        {
            var result = new List<NodeInfo>(_nodes);
            if (!result.Any(n => n.NodeId == LocalNode.NodeId))
            {
                result.Add(NodeInfo.FromIdentity(LocalNode));
            }
            return Task.FromResult<IReadOnlyList<NodeInfo>>(result);
        }

        public Task<NodeInfo?> GetNodeAsync(string nodeId, CancellationToken ct = default)
        {
            return Task.FromResult(_nodes.FirstOrDefault(n => n.NodeId == nodeId));
        }

        public Task<JoinResult> JoinClusterAsync(string seedNode, JoinOptions options, CancellationToken ct = default)
        {
            return Task.FromResult(JoinResult.SuccessResult(new ClusterInfo
            {
                ClusterId = "test",
                ClusterName = "Test Cluster",
                Nodes = new List<NodeInfo>(),
                Leader = null
            }));
        }

        public Task<JoinResult> CreateClusterAsync(string clusterName, CancellationToken ct = default)
        {
            return Task.FromResult(JoinResult.SuccessResult(new ClusterInfo
            {
                ClusterId = "test",
                ClusterName = clusterName,
                Nodes = new List<NodeInfo>(),
                Leader = null
            }));
        }

        public Task<LeaveResult> LeaveClusterAsync(LeaveOptions options, CancellationToken ct = default)
        {
            return Task.FromResult(LeaveResult.SuccessResult());
        }

        public Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default)
        {
            _nodes.RemoveAll(n => n.NodeId == nodeId);
            return Task.FromResult(true);
        }

        public Task<NodeInfo?> GetLeaderAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_nodes.FirstOrDefault(n => n.IsLeader));
        }

        public Task<bool> RequestLeaderElectionAsync(CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> UpdateNodeStateAsync(NodeState newState, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public event EventHandler<NodeJoinedEventArgs>? NodeJoined;
        public event EventHandler<NodeLeftEventArgs>? NodeLeft;
        public event EventHandler<LeaderChangedEventArgs>? LeaderChanged;
        public event EventHandler<NodeStateChangedEventArgs>? NodeStateChanged;

        public void Dispose() { }
    }

    /// <summary>
    /// Mock implementation of IRaftRpcClient for testing.
    /// </summary>
    internal class MockRaftRpcClient : IRaftRpcClient
    {
        private readonly Dictionary<string, VoteResponse> _voteResponses = new();
        private readonly Dictionary<string, AppendResponse> _appendResponses = new();

        /// <summary>
        /// Sets the response for a specific node.
        /// </summary>
        public void SetVoteResponse(string nodeId, VoteResponse response)
        {
            _voteResponses[nodeId] = response;
        }

        /// <summary>
        /// Sets the response for a specific node.
        /// </summary>
        public void SetAppendResponse(string nodeId, AppendResponse response)
        {
            _appendResponses[nodeId] = response;
        }

        /// <inheritdoc/>
        public Task<VoteResponse> RequestVoteAsync(string targetNodeId, VoteRequest request, CancellationToken ct = default)
        {
            if (_voteResponses.TryGetValue(targetNodeId, out var response))
                return Task.FromResult(response);
            
            return Task.FromResult(VoteResponse.Denied(request.Term, targetNodeId));
        }

        /// <inheritdoc/>
        public Task<AppendResponse> AppendEntriesAsync(string targetNodeId, AppendRequest request, CancellationToken ct = default)
        {
            if (_appendResponses.TryGetValue(targetNodeId, out var response))
                return Task.FromResult(response);
            
            return Task.FromResult(AppendResponse.CreateFailure(request.Term, targetNodeId));
        }
    }
}
