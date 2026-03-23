// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Manages cluster membership and node coordination.
    /// </summary>
    public class ClusterManager : IClusterManager
    {
        private readonly P2PConfiguration _config;
        private readonly ConcurrentDictionary<string, NodeInfo> _nodes = new();
        private NodeIdentity? _localNode;
        private NodeInfo? _currentLeader;
        private long _currentTerm;
        private readonly ReaderWriterLockSlim _stateLock = new();
        private bool _disposed;

        /// <inheritdoc/>
        public NodeIdentity LocalNode
        {
            get
            {
                if (_localNode == null)
                    throw new InvalidOperationException("Cluster manager not initialized");
                return _localNode;
            }
        }

        /// <inheritdoc/>
        public bool IsClusterMember => _localNode?.State == NodeState.Active || _localNode?.State == NodeState.Syncing;

        /// <inheritdoc/>
        public bool IsLeader => _currentLeader?.NodeId == _localNode?.NodeId;

        /// <summary>
        /// Creates a new cluster manager.
        /// </summary>
        public ClusterManager(P2PConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Initializes the local node identity.
        /// </summary>
        public void InitializeLocalNode(string host, int clientPort)
        {
            var nodeId = string.IsNullOrEmpty(_config.NodeId)
                ? Guid.NewGuid().ToString("N")
                : _config.NodeId;

            _localNode = new NodeIdentity
            {
                NodeId = nodeId,
                ClusterId = _config.ClusterId,
                Host = _config.GetAdvertiseAddress(),
                Port = clientPort,
                P2PPort = _config.GetAdvertisePort(),
                State = NodeState.Joining,
                JoinedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };
        }

        /// <inheritdoc/>
        public Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default)
        {
            EnsureNotDisposed();

            var nodes = _nodes.Values.ToList();
            if (_localNode != null)
            {
                var localInfo = NodeInfo.FromIdentity(_localNode);
                localInfo.IsLeader = IsLeader;
                localInfo.Term = _currentTerm;
                nodes.Add(localInfo);
            }

            var health = CalculateClusterHealth(nodes);

            return Task.FromResult(new ClusterInfo
            {
                ClusterId = _config.ClusterId,
                ClusterName = _config.ClusterName,
                Leader = _currentLeader,
                Nodes = nodes,
                Health = health,
                CreatedAt = DateTime.UtcNow,
                Mode = _config.Mode
            });
        }

        /// <inheritdoc/>
        public Task<JoinResult> JoinClusterAsync(string seedNode, JoinOptions options, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            if (_localNode == null)
                return Task.FromResult(JoinResult.FailureResult("Local node not initialized"));

            if (IsClusterMember)
                return Task.FromResult(JoinResult.FailureResult("Already a member of a cluster"));

            // For foundation implementation, simulate successful join
            // Full implementation would connect to seed node via P2P protocol
            _localNode.State = NodeState.Active;
            _currentLeader = new NodeInfo
            {
                NodeId = "unknown",
                Host = seedNode.Split(':')[0],
                P2PPort = int.TryParse(seedNode.Split(':')[1], out var port) ? port : 9092,
                State = NodeState.Active,
                IsLeader = true
            };

            var clusterInfo = new ClusterInfo
            {
                ClusterId = _config.ClusterId,
                ClusterName = _config.ClusterName,
                Leader = _currentLeader,
                Nodes = new List<NodeInfo>(),
                Health = ClusterHealth.Healthy,
                Mode = _config.Mode
            };

            OnNodeJoined(new NodeJoinedEventArgs { Node = NodeInfo.FromIdentity(_localNode) });

            return Task.FromResult(JoinResult.SuccessResult(clusterInfo));
        }

        /// <inheritdoc/>
        public Task<JoinResult> CreateClusterAsync(string clusterName, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            if (_localNode == null)
                return Task.FromResult(JoinResult.FailureResult("Local node not initialized"));

            if (IsClusterMember)
                return Task.FromResult(JoinResult.FailureResult("Already a member of a cluster"));

            _config.ClusterName = clusterName;
            _localNode.State = NodeState.Active;
            _currentTerm = 1;
            _currentLeader = NodeInfo.FromIdentity(_localNode);
            _currentLeader.IsLeader = true;
            _currentLeader.Term = _currentTerm;

            var clusterInfo = new ClusterInfo
            {
                ClusterId = _config.ClusterId,
                ClusterName = clusterName,
                Leader = _currentLeader,
                Nodes = new List<NodeInfo> { _currentLeader },
                Health = ClusterHealth.Healthy,
                Mode = _config.Mode
            };

            return Task.FromResult(JoinResult.SuccessResult(clusterInfo));
        }

        /// <inheritdoc/>
        public Task<LeaveResult> LeaveClusterAsync(LeaveOptions options, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            if (_localNode == null || !IsClusterMember)
                return Task.FromResult(LeaveResult.SuccessResult());

            var previousState = _localNode.State;
            _localNode.State = NodeState.Leaving;

            OnNodeLeft(new NodeLeftEventArgs
            {
                Node = NodeInfo.FromIdentity(_localNode),
                Graceful = true
            });

            _localNode.State = NodeState.Dead;
            _currentLeader = null;
            _nodes.Clear();

            return Task.FromResult(LeaveResult.SuccessResult());
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken ct = default)
        {
            EnsureNotDisposed();
            return Task.FromResult<IReadOnlyList<NodeInfo>>(_nodes.Values.ToList());
        }

        /// <inheritdoc/>
        public Task<NodeInfo?> GetNodeAsync(string nodeId, CancellationToken ct = default)
        {
            EnsureNotDisposed();
            _nodes.TryGetValue(nodeId, out var node);
            return Task.FromResult(node);
        }

        /// <inheritdoc/>
        public Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            if (nodeId == _localNode?.NodeId)
                return Task.FromResult(false);

            var removed = _nodes.TryRemove(nodeId, out var node);
            if (removed && node != null)
            {
                OnNodeLeft(new NodeLeftEventArgs { Node = node, Graceful = false });
            }

            return Task.FromResult(removed);
        }

        /// <inheritdoc/>
        public Task<NodeInfo?> GetLeaderAsync(CancellationToken ct = default)
        {
            EnsureNotDisposed();
            return Task.FromResult(_currentLeader);
        }

        /// <inheritdoc/>
        public Task<bool> RequestLeaderElectionAsync(CancellationToken ct = default)
        {
            EnsureNotDisposed();

            if (!IsClusterMember)
                return Task.FromResult(false);

            // For foundation, just increment term locally
            // Full implementation would use Raft consensus
            _currentTerm++;

            var previousLeader = _currentLeader;
            _currentLeader = NodeInfo.FromIdentity(_localNode!);
            _currentLeader.IsLeader = true;
            _currentLeader.Term = _currentTerm;

            OnLeaderChanged(new LeaderChangedEventArgs
            {
                PreviousLeader = previousLeader,
                NewLeader = _currentLeader,
                Term = _currentTerm
            });

            return Task.FromResult(true);
        }

        /// <inheritdoc/>
        public Task<bool> UpdateNodeStateAsync(NodeState newState, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            if (_localNode == null)
                return Task.FromResult(false);

            var previousState = _localNode.State;
            if (previousState == newState)
                return Task.FromResult(true);

            _localNode.State = newState;
            _localNode.LastSeenAt = DateTime.UtcNow;

            OnNodeStateChanged(new NodeStateChangedEventArgs
            {
                Node = NodeInfo.FromIdentity(_localNode),
                PreviousState = previousState,
                NewState = newState
            });

            return Task.FromResult(true);
        }

        /// <summary>
        /// Adds or updates a node in the cluster.
        /// </summary>
        public Task<bool> AddOrUpdateNodeAsync(NodeInfo node, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            if (node.NodeId == _localNode?.NodeId)
                return Task.FromResult(false);

            var added = !_nodes.ContainsKey(node.NodeId);
            _nodes[node.NodeId] = node;

            if (added)
            {
                OnNodeJoined(new NodeJoinedEventArgs { Node = node });
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// Updates the leader information.
        /// </summary>
        public Task UpdateLeaderAsync(NodeInfo? newLeader, long term, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            var previousLeader = _currentLeader;
            _currentLeader = newLeader;
            _currentTerm = term;

            if (previousLeader?.NodeId != newLeader?.NodeId)
            {
                OnLeaderChanged(new LeaderChangedEventArgs
                {
                    PreviousLeader = previousLeader,
                    NewLeader = newLeader,
                    Term = term
                });
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public event EventHandler<NodeJoinedEventArgs>? NodeJoined;

        /// <inheritdoc/>
        public event EventHandler<NodeLeftEventArgs>? NodeLeft;

        /// <inheritdoc/>
        public event EventHandler<LeaderChangedEventArgs>? LeaderChanged;

        /// <inheritdoc/>
        public event EventHandler<NodeStateChangedEventArgs>? NodeStateChanged;

        /// <summary>
        /// Raises the NodeJoined event.
        /// </summary>
        protected virtual void OnNodeJoined(NodeJoinedEventArgs e)
        {
            NodeJoined?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the NodeLeft event.
        /// </summary>
        protected virtual void OnNodeLeft(NodeLeftEventArgs e)
        {
            NodeLeft?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the LeaderChanged event.
        /// </summary>
        protected virtual void OnLeaderChanged(LeaderChangedEventArgs e)
        {
            LeaderChanged?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the NodeStateChanged event.
        /// </summary>
        protected virtual void OnNodeStateChanged(NodeStateChangedEventArgs e)
        {
            NodeStateChanged?.Invoke(this, e);
        }

        private ClusterHealth CalculateClusterHealth(IReadOnlyList<NodeInfo> nodes)
        {
            if (nodes.Count == 0)
                return ClusterHealth.Unknown;

            var activeCount = nodes.Count(n => n.State == NodeState.Active);
            var totalCount = nodes.Count;

            if (activeCount == totalCount)
                return ClusterHealth.Healthy;

            if (activeCount > totalCount / 2)
                return ClusterHealth.Degraded;

            return ClusterHealth.Unhealthy;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ClusterManager));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;

            _stateLock.Dispose();
            _disposed = true;
        }
    }
}
