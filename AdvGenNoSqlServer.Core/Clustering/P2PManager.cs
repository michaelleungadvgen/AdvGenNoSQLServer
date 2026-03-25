// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Central coordinator for all P2P clustering components.
    /// Manages the lifecycle of cluster membership, gossip protocol,
    /// Raft consensus, data replication, and conflict resolution.
    /// </summary>
    public class P2PManager : IP2PManager
    {
        private readonly P2PConfiguration _configuration;
        private readonly P2PManagerOptions _options;
        private readonly IClusterManager _clusterManager;
        private readonly IGossipProtocol? _gossipProtocol;
        private readonly IReplicationManager? _replicationManager;
        private readonly IConflictResolver? _conflictResolver;
        private readonly ConcurrentDictionary<string, P2PNodeStatistics> _nodeStatistics;
        private readonly object _stateLock = new();
        
        private P2PManagerState _state = P2PManagerState.Stopped;
        private DateTime? _startedAt;
        private long _totalErrors;
        private long _successfulJoins;
        private long _failedJoins;
        private Timer? _statisticsTimer;
        private bool _disposed;

        /// <summary>
        /// Creates a new P2P manager with all required components.
        /// </summary>
        public P2PManager(
            P2PConfiguration configuration,
            P2PManagerOptions options,
            IClusterManager clusterManager,
            IGossipProtocol? gossipProtocol = null,
            IReplicationManager? replicationManager = null,
            IConflictResolver? conflictResolver = null)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clusterManager = clusterManager ?? throw new ArgumentNullException(nameof(clusterManager));
            _gossipProtocol = gossipProtocol;
            _replicationManager = replicationManager;
            _conflictResolver = conflictResolver;
            _nodeStatistics = new ConcurrentDictionary<string, P2PNodeStatistics>();

            options.Validate();
            var errors = new List<string>();
            configuration.Validate(out errors);

            // Subscribe to cluster manager events
            _clusterManager.NodeJoined += OnNodeJoined;
            _clusterManager.NodeLeft += OnNodeLeft;
            _clusterManager.LeaderChanged += OnLeaderChanged;
            _clusterManager.NodeStateChanged += OnNodeStateChanged;

            // Subscribe to gossip protocol events
            if (_gossipProtocol != null)
            {
                _gossipProtocol.StateUpdated += OnGossipStateUpdated;
                _gossipProtocol.GossipReceived += OnGossipReceived;
            }

            // Subscribe to replication manager events
            if (_replicationManager != null)
            {
                _replicationManager.ReplicationAcknowledged += OnReplicationAcknowledged;
                _replicationManager.ReplicationFailed += OnReplicationFailed;
            }
        }

        /// <inheritdoc />
        public P2PManagerState State
        {
            get
            {
                lock (_stateLock)
                {
                    return _state;
                }
            }
            private set
            {
                P2PManagerState oldState;
                lock (_stateLock)
                {
                    oldState = _state;
                    _state = value;
                }
                
                if (oldState != value)
                {
                    OnStateChanged(new P2PManagerStateChangedEventArgs
                    {
                        PreviousState = oldState,
                        NewState = value
                    });
                }
            }
        }

        /// <inheritdoc />
        public NodeIdentity LocalNode => _clusterManager.LocalNode;

        /// <inheritdoc />
        public ClusterInfo? ClusterInfo => _clusterManager.IsClusterMember ? 
            _clusterManager.GetClusterInfoAsync().GetAwaiter().GetResult() : null;

        /// <inheritdoc />
        public bool IsClusterConnected => _clusterManager.IsClusterMember;

        /// <inheritdoc />
        public bool IsLeader => _clusterManager.IsLeader;

        /// <inheritdoc />
        public P2PConfiguration Configuration => _configuration;

        /// <inheritdoc />
        public P2PManagerOptions Options => _options;

        /// <inheritdoc />
        public IClusterManager ClusterManager => _clusterManager;

        /// <inheritdoc />
        public IGossipProtocol? GossipProtocol => _gossipProtocol;

        /// <inheritdoc />
        public IReplicationManager? ReplicationManager => _replicationManager;

        /// <inheritdoc />
        public async Task InitializeAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            
            if (State != P2PManagerState.Stopped)
            {
                throw new InvalidOperationException($"Cannot initialize from state {State}");
            }

            try
            {
                State = P2PManagerState.Initializing;

                // Initialize cluster manager
                // Note: ClusterManager doesn't have InitializeAsync, it's ready to use

                // Initialize gossip protocol if enabled
                if (_options.EnableGossip && _gossipProtocol != null)
                {
                    // Gossip protocol is initialized on StartAsync
                }

                // Initialize replication manager if enabled
                if (_options.EnableReplication && _replicationManager != null)
                {
                    await _replicationManager.StartAsync(ct);
                }

                // Start statistics collection timer
                _statisticsTimer = new Timer(OnStatisticsTimer, null, _options.StatisticsInterval, _options.StatisticsInterval);
            }
            catch (Exception ex)
            {
                State = P2PManagerState.Failed;
                OnErrorOccurred(new P2PErrorEventArgs
                {
                    ErrorCode = "INITIALIZATION_FAILED",
                    ErrorMessage = $"Failed to initialize P2P manager: {ex.Message}",
                    Exception = ex,
                    Component = nameof(P2PManager)
                });
                throw;
            }
        }

        /// <inheritdoc />
        public async Task StartAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (State != P2PManagerState.Initializing && State != P2PManagerState.Stopped)
            {
                throw new InvalidOperationException($"Cannot start from state {State}");
            }

            try
            {
                // Initialize if not already done
                if (State == P2PManagerState.Stopped)
                {
                    await InitializeAsync(ct);
                }

                State = P2PManagerState.Running;
                _startedAt = DateTime.UtcNow;

                // Start gossip protocol if enabled
                if (_options.EnableGossip && _gossipProtocol != null)
                {
                    await _gossipProtocol.StartAsync(ct);
                }

                // Auto-join cluster if enabled
                if (_options.EnableAutoJoin)
                {
                    var joinResult = await JoinClusterAsync(ct);
                    if (!joinResult.Success && _configuration.Discovery?.Seeds?.Length > 0)
                    {
                        // If join failed but we have seeds, create a new cluster
                        await CreateClusterAsync(_configuration.ClusterName ?? "default", ct);
                    }
                }
            }
            catch (Exception ex)
            {
                State = P2PManagerState.Failed;
                OnErrorOccurred(new P2PErrorEventArgs
                {
                    ErrorCode = "START_FAILED",
                    ErrorMessage = $"Failed to start P2P manager: {ex.Message}",
                    Exception = ex,
                    Component = nameof(P2PManager)
                });
                throw;
            }
        }

        /// <inheritdoc />
        public async Task StopAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (State != P2PManagerState.Running)
            {
                return;
            }

            State = P2PManagerState.Stopping;

            try
            {
                // Stop gossip protocol
                if (_gossipProtocol != null)
                {
                    await _gossipProtocol.StopAsync(ct);
                }

                // Stop replication manager
                if (_replicationManager != null)
                {
                    await _replicationManager.StopAsync(ct);
                }

                // Leave cluster gracefully
                if (_clusterManager.IsClusterMember)
                {
                    await _clusterManager.LeaveClusterAsync(new LeaveOptions(), ct);
                }

                // Stop statistics timer
                _statisticsTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                State = P2PManagerState.Stopped;
                _startedAt = null;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new P2PErrorEventArgs
                {
                    ErrorCode = "STOP_FAILED",
                    ErrorMessage = $"Error during P2P manager shutdown: {ex.Message}",
                    Exception = ex,
                    Component = nameof(P2PManager)
                });
                State = P2PManagerState.Failed;
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<JoinResult> JoinClusterAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            
            if (State != P2PManagerState.Running)
            {
                return JoinResult.FailureResult("P2P manager is not running");
            }

            if (_clusterManager.IsClusterMember)
            {
                return JoinResult.FailureResult("Already a member of a cluster");
            }

            try
            {
                // Use seed discovery if seeds are configured
                if (_configuration.Discovery?.Seeds?.Length > 0)
                {
                    foreach (var seed in _configuration.Discovery.Seeds)
                    {
                        var result = await _clusterManager.JoinClusterAsync(seed, new JoinOptions { SeedNode = seed }, ct);
                        if (result.Success)
                        {
                            Interlocked.Increment(ref _successfulJoins);
                            return result;
                        }
                    }
                }

                Interlocked.Increment(ref _failedJoins);
                return JoinResult.FailureResult("Failed to join cluster using any configured seed");
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedJoins);
                Interlocked.Increment(ref _totalErrors);
                OnErrorOccurred(new P2PErrorEventArgs
                {
                    ErrorCode = "JOIN_FAILED",
                    ErrorMessage = $"Failed to join cluster: {ex.Message}",
                    Exception = ex,
                    Component = nameof(P2PManager)
                });
                return JoinResult.FailureResult(ex.Message);
            }
        }

        /// <inheritdoc />
        public async Task<JoinResult> CreateClusterAsync(string clusterName, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            
            if (State != P2PManagerState.Running)
            {
                return JoinResult.FailureResult("P2P manager is not running");
            }

            try
            {
                var result = await _clusterManager.CreateClusterAsync(clusterName, ct);
                if (result.Success)
                {
                    Interlocked.Increment(ref _successfulJoins);
                }
                else
                {
                    Interlocked.Increment(ref _failedJoins);
                }
                return result;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedJoins);
                Interlocked.Increment(ref _totalErrors);
                OnErrorOccurred(new P2PErrorEventArgs
                {
                    ErrorCode = "CREATE_CLUSTER_FAILED",
                    ErrorMessage = $"Failed to create cluster: {ex.Message}",
                    Exception = ex,
                    Component = nameof(P2PManager)
                });
                return JoinResult.FailureResult(ex.Message);
            }
        }

        /// <inheritdoc />
        public async Task<LeaveResult> LeaveClusterAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (!_clusterManager.IsClusterMember)
            {
                return LeaveResult.SuccessResult();
            }

            try
            {
                return await _clusterManager.LeaveClusterAsync(new LeaveOptions(), ct);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalErrors);
                OnErrorOccurred(new P2PErrorEventArgs
                {
                    ErrorCode = "LEAVE_FAILED",
                    ErrorMessage = $"Failed to leave cluster: {ex.Message}",
                    Exception = ex,
                    Component = nameof(P2PManager)
                });
                return LeaveResult.FailureResult(ex.Message);
            }
        }

        /// <inheritdoc />
        public async Task<P2PManagerStatistics> GetStatisticsAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            ClusterInfo? clusterInfo = null;
            IReadOnlyList<NodeInfo> nodes = new List<NodeInfo>();
            
            try
            {
                clusterInfo = await _clusterManager.GetClusterInfoAsync(ct);
                nodes = await _clusterManager.GetNodesAsync(ct);
            }
            catch
            {
                // Cluster may not be available yet
            }

            var stats = new P2PManagerStatistics
            {
                State = State,
                StartedAt = _startedAt,
                TotalNodes = nodes.Count + 1, // +1 for local node
                ConnectedNodes = nodes.Count(n => n.State == NodeState.Active),
                NodeStatistics = _nodeStatistics.Values.ToList(),
                GossipStatistics = _gossipProtocol?.GetStats(),
                ReplicationStatistics = _replicationManager?.Statistics,
                TotalErrors = Interlocked.Read(ref _totalErrors),
                SuccessfulJoins = Interlocked.Read(ref _successfulJoins),
                FailedJoins = Interlocked.Read(ref _failedJoins),
                CalculatedAt = DateTime.UtcNow
            };

            // Count nodes by state
            stats.NodesByState[NodeState.Joining] = nodes.Count(n => n.State == NodeState.Joining);
            stats.NodesByState[NodeState.Syncing] = nodes.Count(n => n.State == NodeState.Syncing);
            stats.NodesByState[NodeState.Active] = nodes.Count(n => n.State == NodeState.Active);
            stats.NodesByState[NodeState.Leaving] = nodes.Count(n => n.State == NodeState.Leaving);
            stats.NodesByState[NodeState.Dead] = nodes.Count(n => n.State == NodeState.Dead);

            return stats;
        }

        /// <inheritdoc />
        public Task<NodeInfo?> GetLeaderAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            return _clusterManager.GetLeaderAsync(ct);
        }

        /// <inheritdoc />
        public Task<bool> RequestLeaderElectionAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            return _clusterManager.RequestLeaderElectionAsync(ct);
        }

        /// <inheritdoc />
        public async Task<ReplicationResult> ReplicateWriteAsync(ReplicationEvent evt, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (_replicationManager == null)
            {
                return ReplicationResult.FailureResult(0, 0, "Replication manager not available", 
                    new Dictionary<string, string>(), TimeSpan.Zero);
            }

            try
            {
                return await _replicationManager.ReplicateWriteAsync(evt, ct);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalErrors);
                OnErrorOccurred(new P2PErrorEventArgs
                {
                    ErrorCode = "REPLICATION_FAILED",
                    ErrorMessage = $"Replication failed: {ex.Message}",
                    Exception = ex,
                    Component = nameof(P2PManager)
                });
                return ReplicationResult.FailureResult(0, 0, ex.Message, 
                    new Dictionary<string, string>(), TimeSpan.Zero);
            }
        }

        /// <inheritdoc />
        public async Task<P2PHealthStatus> GetHealthStatusAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var status = new P2PHealthStatus
            {
                Status = HealthStatus.Healthy,
                IsHealthy = true,
                ComponentHealth = new Dictionary<string, ComponentHealth>()
            };

            // Check cluster manager health
            var clusterInfo = await _clusterManager.GetClusterInfoAsync(ct);
            status.ComponentHealth["ClusterManager"] = new ComponentHealth
            {
                Name = "ClusterManager",
                IsHealthy = _clusterManager.IsClusterMember || !_options.EnableAutoJoin,
                Message = clusterInfo?.Health.ToString() ?? "Not connected"
            };

            // Check gossip protocol health
            if (_options.EnableGossip && _gossipProtocol != null)
            {
                var gossipStats = _gossipProtocol.GetStats();
                status.ComponentHealth["GossipProtocol"] = new ComponentHealth
                {
                    Name = "GossipProtocol",
                    IsHealthy = gossipStats.MessagesReceived > 0 || gossipStats.MessagesSent == 0,
                    Message = $"Sent: {gossipStats.MessagesSent}, Received: {gossipStats.MessagesReceived}"
                };
            }

            // Check replication manager health
            if (_options.EnableReplication && _replicationManager != null)
            {
                var replStats = _replicationManager.Statistics;
                status.ComponentHealth["ReplicationManager"] = new ComponentHealth
                {
                    Name = "ReplicationManager",
                    IsHealthy = replStats.TotalFailures < replStats.TotalEventsAcknowledged * 0.1, // < 10% failure rate
                    Message = $"Events: {replStats.TotalEventsAcknowledged}, Failures: {replStats.TotalFailures}"
                };
            }

            // Determine overall health
            if (status.ComponentHealth.Values.Any(c => !c.IsHealthy))
            {
                var unhealthyCount = status.ComponentHealth.Values.Count(c => !c.IsHealthy);
                status.Status = unhealthyCount > 1 ? HealthStatus.Unhealthy : HealthStatus.Degraded;
                status.IsHealthy = status.Status != HealthStatus.Unhealthy;
                status.Message = $"{unhealthyCount} component(s) unhealthy";
            }
            else
            {
                status.Message = "All components healthy";
            }

            return status;
        }

        /// <inheritdoc />
        public event EventHandler<P2PManagerStateChangedEventArgs>? StateChanged;

        /// <inheritdoc />
        public event EventHandler<PeerConnectionEventArgs>? PeerConnectionChanged;

        /// <inheritdoc />
        public event EventHandler<ClusterTopologyChangedEventArgs>? ClusterTopologyChanged;

        /// <inheritdoc />
        public event EventHandler<ReplicationStatusEventArgs>? ReplicationStatusChanged;

        /// <inheritdoc />
        public event EventHandler<P2PErrorEventArgs>? ErrorOccurred;

        /// <summary>
        /// Disposes the P2P manager and all components.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // Stop components
            _statisticsTimer?.Dispose();
            
            // Unsubscribe from events
            _clusterManager.NodeJoined -= OnNodeJoined;
            _clusterManager.NodeLeft -= OnNodeLeft;
            _clusterManager.LeaderChanged -= OnLeaderChanged;
            _clusterManager.NodeStateChanged -= OnNodeStateChanged;

            if (_gossipProtocol != null)
            {
                _gossipProtocol.StateUpdated -= OnGossipStateUpdated;
                _gossipProtocol.GossipReceived -= OnGossipReceived;
            }

            if (_replicationManager != null)
            {
                _replicationManager.ReplicationAcknowledged -= OnReplicationAcknowledged;
                _replicationManager.ReplicationFailed -= OnReplicationFailed;
            }

            _clusterManager.Dispose();
        }

        private void OnNodeJoined(object? sender, NodeJoinedEventArgs e)
        {
            // Update or create node statistics
            _nodeStatistics.AddOrUpdate(e.Node.NodeId, 
                _ => new P2PNodeStatistics 
                { 
                    NodeId = e.Node.NodeId,
                    IsConnected = true,
                    State = NodeState.Joining
                },
                (_, existing) =>
                {
                    existing.IsConnected = true;
                    return existing;
                });

            OnClusterTopologyChanged(new ClusterTopologyChangedEventArgs
            {
                PreviousNodeCount = _nodeStatistics.Count - 1,
                CurrentNodeCount = _nodeStatistics.Count,
                ChangedNode = e.Node,
                NodeJoined = true
            });

            OnPeerConnectionChanged(new PeerConnectionEventArgs
            {
                Node = e.Node,
                IsConnected = true
            });
        }

        private void OnNodeLeft(object? sender, NodeLeftEventArgs e)
        {
            if (_nodeStatistics.TryRemove(e.Node.NodeId, out _))
            {
                OnClusterTopologyChanged(new ClusterTopologyChangedEventArgs
                {
                    PreviousNodeCount = _nodeStatistics.Count + 1,
                    CurrentNodeCount = _nodeStatistics.Count,
                    ChangedNode = e.Node,
                    NodeJoined = false
                });

                OnPeerConnectionChanged(new PeerConnectionEventArgs
                {
                    Node = e.Node,
                    IsConnected = false
                });
            }
        }

        private void OnLeaderChanged(object? sender, LeaderChangedEventArgs e)
        {
            // Leader change handled via events to subscribers
        }

        private void OnNodeStateChanged(object? sender, NodeStateChangedEventArgs e)
        {
            _nodeStatistics.AddOrUpdate(e.Node.NodeId,
                _ => new P2PNodeStatistics
                {
                    NodeId = e.Node.NodeId,
                    State = e.NewState
                },
                (_, existing) =>
                {
                    existing.State = e.NewState;
                    return existing;
                });
        }

        private void OnGossipStateUpdated(object? sender, GossipStateUpdatedEventArgs e)
        {
            _nodeStatistics.AddOrUpdate(e.Node.NodeId,
                _ => new P2PNodeStatistics
                {
                    NodeId = e.Node.NodeId,
                    State = e.NewState
                },
                (_, existing) =>
                {
                    existing.State = e.NewState;
                    existing.LastMessageReceived = DateTime.UtcNow;
                    return existing;
                });
        }

        private void OnGossipReceived(object? sender, GossipReceivedEventArgs e)
        {
            // Track gossip message statistics
        }

        private void OnReplicationAcknowledged(object? sender, ReplicationAck e)
        {
            _nodeStatistics.AddOrUpdate(e.NodeId,
                _ => new P2PNodeStatistics
                {
                    NodeId = e.NodeId,
                    ReplicationEventsAcked = 1
                },
                (_, existing) =>
                {
                    existing.ReplicationEventsAcked++;
                    return existing;
                });

            OnReplicationStatusChanged(new ReplicationStatusEventArgs
            {
                SourceNodeId = e.NodeId,
                Success = true
            });
        }

        private void OnReplicationFailed(object? sender, ReplicationEvent e)
        {
            Interlocked.Increment(ref _totalErrors);

            OnReplicationStatusChanged(new ReplicationStatusEventArgs
            {
                SourceNodeId = e.SourceNodeId,
                OperationType = e.Type,
                Collection = e.Collection,
                Success = false,
                ErrorMessage = "Replication failed"
            });
        }

        private void OnStatisticsTimer(object? state)
        {
            // Periodic statistics update
            if (State != P2PManagerState.Running) return;

            try
            {
                // Update node statistics from gossip
                if (_gossipProtocol != null)
                {
                    var clusterState = _gossipProtocol.GetClusterState();
                    foreach (var kvp in clusterState)
                    {
                        _nodeStatistics.AddOrUpdate(kvp.Key,
                            _ => new P2PNodeStatistics
                            {
                                NodeId = kvp.Key,
                                State = kvp.Value.State,
                                LastMessageReceived = kvp.Value.LastUpdated
                            },
                            (_, existing) =>
                            {
                                existing.State = kvp.Value.State;
                                existing.LastMessageReceived = kvp.Value.LastUpdated;
                                return existing;
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalErrors);
                OnErrorOccurred(new P2PErrorEventArgs
                {
                    ErrorCode = "STATISTICS_UPDATE_FAILED",
                    ErrorMessage = $"Failed to update statistics: {ex.Message}",
                    Exception = ex
                });
            }
        }

        private void OnStateChanged(P2PManagerStateChangedEventArgs e)
        {
            StateChanged?.Invoke(this, e);
        }

        private void OnPeerConnectionChanged(PeerConnectionEventArgs e)
        {
            PeerConnectionChanged?.Invoke(this, e);
        }

        private void OnClusterTopologyChanged(ClusterTopologyChangedEventArgs e)
        {
            ClusterTopologyChanged?.Invoke(this, e);
        }

        private void OnReplicationStatusChanged(ReplicationStatusEventArgs e)
        {
            ReplicationStatusChanged?.Invoke(this, e);
        }

        private void OnErrorOccurred(P2PErrorEventArgs e)
        {
            ErrorOccurred?.Invoke(this, e);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(P2PManager));
            }
        }
    }
}
