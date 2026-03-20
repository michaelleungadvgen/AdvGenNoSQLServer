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
    /// SWIM-style failure detector with suspicion mechanism.
    /// Detects node failures using heartbeat timeouts and confirmation from other nodes.
    /// </summary>
    public class NodeFailureDetector : IFailureDetector, IDisposable
    {
        private readonly IClusterManager _clusterManager;
        private readonly IGossipProtocol _gossipProtocol;
        private readonly P2PConfiguration _configuration;
        private readonly GossipOptions _gossipOptions;
        private readonly string _localNodeId;

        // Node tracking
        private readonly ConcurrentDictionary<string, NodeHealthInfo> _nodeHealth = new();

        // Suspicion tracking
        private readonly ConcurrentDictionary<string, SuspicionInfo> _suspicions = new();

        // Statistics
        private long _totalHeartbeatsReceived = 0;
        private long _totalFailuresDetected = 0;
        private long _totalSuspicionsRaised = 0;
        private DateTime _startedAt;

        // Runtime
        private Timer? _detectionTimer;
        private readonly CancellationTokenSource _cts = new();
        private bool _isRunning = false;
        private readonly object _stateLock = new();

        /// <inheritdoc />
        public event EventHandler<NodeSuspectedEventArgs>? NodeSuspected;

        /// <inheritdoc />
        public event EventHandler<NodeFailedEventArgs>? NodeFailed;

        /// <inheritdoc />
        public event EventHandler<NodeRecoveredEventArgs>? NodeRecovered;

        /// <summary>
        /// Creates a new node failure detector.
        /// </summary>
        public NodeFailureDetector(
            IClusterManager clusterManager,
            IGossipProtocol gossipProtocol,
            P2PConfiguration configuration,
            GossipOptions? gossipOptions = null)
        {
            _clusterManager = clusterManager ?? throw new ArgumentNullException(nameof(clusterManager));
            _gossipProtocol = gossipProtocol ?? throw new ArgumentNullException(nameof(gossipProtocol));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _gossipOptions = gossipOptions ?? new GossipOptions();

            _localNodeId = string.IsNullOrEmpty(configuration.NodeId)
                ? Guid.NewGuid().ToString("N")
                : configuration.NodeId;

            // Subscribe to gossip updates
            _gossipProtocol.StateUpdated += OnGossipStateUpdated;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                return Task.CompletedTask;

            lock (_stateLock)
            {
                if (_isRunning)
                    return Task.CompletedTask;

                _isRunning = true;
                _startedAt = DateTime.UtcNow;

                // Start detection timer (runs more frequently than gossip)
                var checkInterval = TimeSpan.FromMilliseconds(
                    Math.Max(100, _configuration.HeartbeatInterval.TotalMilliseconds / 2));

                _detectionTimer = new Timer(
                    OnDetectionTimer,
                    null,
                    checkInterval,
                    checkInterval);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!_isRunning)
                return Task.CompletedTask;

            lock (_stateLock)
            {
                if (!_isRunning)
                    return Task.CompletedTask;

                _isRunning = false;
                _cts.Cancel();
                _detectionTimer?.Dispose();
                _detectionTimer = null;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void RecordHeartbeat(string nodeId)
        {
            if (nodeId == _localNodeId)
                return;

            Interlocked.Increment(ref _totalHeartbeatsReceived);

            var health = _nodeHealth.GetOrAdd(nodeId, _ => new NodeHealthInfo { NodeId = nodeId });
            
            var previousStatus = health.Status;
            health.LastHeartbeat = DateTime.UtcNow;
            health.HeartbeatCount++;

            // Check if this is a recovery
            if (previousStatus == NodeStatus.Failed || previousStatus == NodeStatus.Suspected)
            {
                health.Status = NodeStatus.Alive;
                health.SuspicionStartedAt = null;
                health.SuspicionConfirmations.Clear();

                // Remove from suspicions
                _suspicions.TryRemove(nodeId, out _);

                // Raise recovery event
                NodeRecovered?.Invoke(this, new NodeRecoveredEventArgs
                {
                    Node = new NodeInfo { NodeId = nodeId, Host = "", P2PPort = 0 },
                    TimeFailed = DateTime.UtcNow - (health.LastStateChange ?? DateTime.UtcNow)
                });
            }

            health.LastStateChange = DateTime.UtcNow;
        }

        /// <inheritdoc />
        public NodeStatus GetNodeStatus(string nodeId)
        {
            if (nodeId == _localNodeId)
                return NodeStatus.Alive;

            var health = _nodeHealth.GetValueOrDefault(nodeId);
            return health?.Status ?? NodeStatus.Unknown;
        }

        /// <inheritdoc />
        public FailureDetectorStats GetStats()
        {
            var healthList = _nodeHealth.Values.ToList();

            return new FailureDetectorStats
            {
                TotalNodes = healthList.Count,
                AliveNodes = healthList.Count(h => h.Status == NodeStatus.Alive),
                SuspectedNodes = healthList.Count(h => h.Status == NodeStatus.Suspected),
                FailedNodes = healthList.Count(h => h.Status == NodeStatus.Failed),
                TotalHeartbeatsReceived = Interlocked.Read(ref _totalHeartbeatsReceived),
                TotalFailuresDetected = Interlocked.Read(ref _totalFailuresDetected),
                TotalSuspicionsRaised = Interlocked.Read(ref _totalSuspicionsRaised),
                StartedAt = _startedAt
            };
        }

        /// <summary>
        /// Confirms that a node is suspected to have failed.
        /// Called when another node reports suspicion.
        /// </summary>
        public void ConfirmSuspicion(string suspectedNodeId, string confirmingNodeId)
        {
            var health = _nodeHealth.GetValueOrDefault(suspectedNodeId);
            if (health == null)
                return;

            health.SuspicionConfirmations.Add(confirmingNodeId);

            // Check if we have enough confirmations to mark as failed
            CheckSuspicionConfirmations(suspectedNodeId, health);
        }

        private void OnGossipStateUpdated(object? sender, GossipStateUpdatedEventArgs e)
        {
            // Record heartbeat when we receive state updates
            RecordHeartbeat(e.Node.NodeId);
        }

        private void OnDetectionTimer(object? state)
        {
            if (!_isRunning || _cts.IsCancellationRequested)
                return;

            try
            {
                CheckForFailedNodes();
            }
            catch (Exception)
            {
                // Log error but continue detection
            }
        }

        private void CheckForFailedNodes()
        {
            var now = DateTime.UtcNow;
            var timeout = _configuration.DeadNodeTimeout;
            var suspicionTimeout = CalculateSuspicionTimeout();

            foreach (var (nodeId, health) in _nodeHealth)
            {
                if (nodeId == _localNodeId)
                    continue;

                var timeSinceLastHeartbeat = now - health.LastHeartbeat;

                switch (health.Status)
                {
                    case NodeStatus.Alive:
                        // Check if we should move to suspected
                        if (timeSinceLastHeartbeat > timeout)
                        {
                            TransitionToSuspected(nodeId, health, timeSinceLastHeartbeat);
                        }
                        break;

                    case NodeStatus.Suspected:
                        // Check suspicion confirmations and timeout
                        CheckSuspicionConfirmations(nodeId, health);

                        // Check if suspicion has timed out
                        if (health.SuspicionStartedAt.HasValue &&
                            now - health.SuspicionStartedAt.Value > suspicionTimeout)
                        {
                            TransitionToFailed(nodeId, health, timeSinceLastHeartbeat);
                        }
                        break;
                }
            }
        }

        private void TransitionToSuspected(string nodeId, NodeHealthInfo health, TimeSpan timeSinceLastHeartbeat)
        {
            health.Status = NodeStatus.Suspected;
            health.SuspicionStartedAt = DateTime.UtcNow;
            health.LastStateChange = DateTime.UtcNow;

            Interlocked.Increment(ref _totalSuspicionsRaised);

            var suspicionInfo = new SuspicionInfo
            {
                NodeId = nodeId,
                StartedAt = DateTime.UtcNow,
                StartedBy = _localNodeId
            };
            _suspicions[nodeId] = suspicionInfo;

            NodeSuspected?.Invoke(this, new NodeSuspectedEventArgs
            {
                Node = new NodeInfo { NodeId = nodeId, Host = "", P2PPort = 0 },
                ConfirmationCount = health.SuspicionConfirmations.Count
            });
        }

        private void CheckSuspicionConfirmations(string nodeId, NodeHealthInfo health)
        {
            if (health.Status != NodeStatus.Suspected)
                return;

            // Get active nodes for K calculation
            var activeNodes = _nodeHealth.Count(h => h.Value.Status == NodeStatus.Alive);
            var k = Math.Max(1, Math.Min(health.SuspicionConfirmations.Count, activeNodes - 1));

            // SWIM suspicion formula: timeout * log(n+1) / confirmations
            var requiredConfirmations = Math.Max(1, k / 2); // Simple majority of k

            if (health.SuspicionConfirmations.Count >= requiredConfirmations)
            {
                var timeSinceLastHeartbeat = DateTime.UtcNow - health.LastHeartbeat;
                TransitionToFailed(nodeId, health, timeSinceLastHeartbeat);
            }
        }

        private void TransitionToFailed(string nodeId, NodeHealthInfo health, TimeSpan timeSinceLastHeartbeat)
        {
            if (health.Status == NodeStatus.Failed)
                return;

            health.Status = NodeStatus.Failed;
            health.LastStateChange = DateTime.UtcNow;

            Interlocked.Increment(ref _totalFailuresDetected);
            _suspicions.TryRemove(nodeId, out _);

            NodeFailed?.Invoke(this, new NodeFailedEventArgs
            {
                Node = new NodeInfo { NodeId = nodeId, Host = "", P2PPort = 0 },
                TimeSinceLastSeen = timeSinceLastHeartbeat,
                Reason = health.SuspicionConfirmations.Count > 0
                    ? $"Confirmed by {health.SuspicionConfirmations.Count} nodes"
                    : "Heartbeat timeout"
            });
        }

        private TimeSpan CalculateSuspicionTimeout()
        {
            var nodeCount = Math.Max(1, _nodeHealth.Count);
            var multiplier = _gossipOptions.SuspicionMultiplier;

            // SWIM suspicion timeout: K * log(n+1) * protocol_period
            var logFactor = Math.Log(nodeCount + 1);
            var timeoutMs = _configuration.HeartbeatInterval.TotalMilliseconds *
                           multiplier * logFactor;

            return TimeSpan.FromMilliseconds(
                Math.Min(timeoutMs, _gossipOptions.MaxSuspicionTimeout.TotalMilliseconds));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            _gossipProtocol.StateUpdated -= OnGossipStateUpdated;
            _cts.Dispose();
            _detectionTimer?.Dispose();
        }

        /// <summary>
        /// Internal class for tracking node health information.
        /// </summary>
        private class NodeHealthInfo
        {
            public required string NodeId { get; set; }
            public NodeStatus Status { get; set; } = NodeStatus.Alive;
            public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
            public DateTime? LastStateChange { get; set; }
            public DateTime? SuspicionStartedAt { get; set; }
            public long HeartbeatCount { get; set; }
            public HashSet<string> SuspicionConfirmations { get; set; } = new();
        }

        /// <summary>
        /// Internal class for tracking suspicion information.
        /// </summary>
        private class SuspicionInfo
        {
            public required string NodeId { get; set; }
            public required DateTime StartedAt { get; set; }
            public required string StartedBy { get; set; }
            public HashSet<string> Confirmations { get; set; } = new();
        }
    }
}
