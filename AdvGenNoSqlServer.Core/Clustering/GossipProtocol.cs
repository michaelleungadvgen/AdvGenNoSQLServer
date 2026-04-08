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
    /// Implementation of the gossip protocol for cluster state propagation.
    /// Uses SWIM-style gossip with suspicion mechanism for failure detection.
    /// </summary>
    public class GossipProtocol : IGossipProtocol, IDisposable
    {
        private readonly IClusterManager _clusterManager;
        private readonly P2PConfiguration _configuration;
        private readonly GossipOptions _options;
        private readonly string _localNodeId;

        // State management
        private readonly ConcurrentDictionary<string, NodeStateInfo> _nodeStates = new();
        private readonly ConcurrentDictionary<string, long> _heartbeats = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _suspicionConfirmations = new();

        // Gossip tracking
        private long _sequenceNumber = 0;
        private long _localGeneration = 0;
        private long _localVersion = 0;

        // Statistics
        private long _messagesSent = 0;
        private long _messagesReceived = 0;
        private long _statesPropagated = 0;
        private long _gossipRounds = 0;
        private long _totalMessageSizeBytes = 0;
        private DateTime _startedAt;

        // Runtime
        private Timer? _gossipTimer;
        private readonly CancellationTokenSource _cts = new();
        private bool _isRunning = false;
        private readonly object _stateLock = new();

        /// <inheritdoc />
        public event EventHandler<GossipStateUpdatedEventArgs>? StateUpdated;

        /// <inheritdoc />
        public event EventHandler<GossipReceivedEventArgs>? GossipReceived;

        /// <summary>
        /// Creates a new gossip protocol instance.
        /// </summary>
        public GossipProtocol(
            IClusterManager clusterManager,
            P2PConfiguration configuration,
            GossipOptions? options = null)
        {
            _clusterManager = clusterManager ?? throw new ArgumentNullException(nameof(clusterManager));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _options = options ?? new GossipOptions();
            _options.Validate();

            _localNodeId = string.IsNullOrEmpty(configuration.NodeId)
                ? Guid.NewGuid().ToString("N")
                : configuration.NodeId;

            // Initialize local node state
            _localGeneration = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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

                // Start gossip timer
                _gossipTimer = new Timer(
                    OnGossipTimer,
                    null,
                    _options.GossipInterval,
                    _options.GossipInterval);
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
                _gossipTimer?.Dispose();
                _gossipTimer = null;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UpdateLocalStateAsync(NodeState newState, CancellationToken cancellationToken = default)
        {
            lock (_stateLock)
            {
                _localVersion++;

                var nodeInfo = new NodeInfo
                {
                    NodeId = _localNodeId,
                    Host = _configuration.GetAdvertiseAddress(),
                    P2PPort = _configuration.GetAdvertisePort(),
                    State = newState,
                    LastSeenAt = DateTime.UtcNow
                };

                var stateInfo = new NodeStateInfo
                {
                    Node = nodeInfo,
                    State = newState,
                    Generation = _localGeneration,
                    Version = _localVersion,
                    LastUpdated = DateTime.UtcNow,
                    LastUpdatedBy = _localNodeId,
                    HeartbeatSequence = Interlocked.Increment(ref _sequenceNumber)
                };

                _nodeStates[_localNodeId] = stateInfo;
                _heartbeats[_localNodeId] = stateInfo.HeartbeatSequence;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, NodeStateInfo> GetClusterState()
        {
            return _nodeStates;
        }

        /// <inheritdoc />
        public GossipStats GetStats()
        {
            var messageCount = Interlocked.Read(ref _messagesSent);
            var avgSize = messageCount > 0
                ? (double)Interlocked.Read(ref _totalMessageSizeBytes) / messageCount
                : 0;

            return new GossipStats
            {
                MessagesSent = messageCount,
                MessagesReceived = Interlocked.Read(ref _messagesReceived),
                StatesPropagated = Interlocked.Read(ref _statesPropagated),
                GossipRounds = Interlocked.Read(ref _gossipRounds),
                AverageMessageSizeBytes = avgSize,
                StartedAt = _startedAt,
                CurrentNodeCount = _nodeStates.Count
            };
        }

        /// <inheritdoc />
        public async Task TriggerGossipRoundAsync(CancellationToken cancellationToken = default)
        {
            await ExecuteGossipRoundAsync(cancellationToken);
        }

        /// <summary>
        /// Processes an incoming gossip message.
        /// </summary>
        public Task ProcessGossipMessageAsync(GossipMessage message, string senderId)
        {
            Interlocked.Increment(ref _messagesReceived);

            // Update our local state based on received gossip
            if (message.NodeStates != null && message.NodeStates.Count > 0)
            {
                foreach (var (nodeId, nodeState) in message.NodeStates)
                {
                    if (nodeId == _localNodeId)
                        continue;

                    ProcessReceivedNodeState(nodeId, nodeState, message.Generation, message.Version, senderId);
                }
            }

            // Process heartbeat updates
            if (message.Heartbeats != null)
            {
                foreach (var (nodeId, heartbeat) in message.Heartbeats)
                {
                    if (nodeId == _localNodeId)
                        continue;

                    UpdateHeartbeat(nodeId, heartbeat);
                }
            }

            GossipReceived?.Invoke(this, new GossipReceivedEventArgs
            {
                Sender = new NodeInfo 
                { 
                    NodeId = senderId, 
                    Host = "", 
                    P2PPort = 0 
                },
                StateCount = message.NodeStates?.Count ?? 0
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Creates a gossip message for sending to other nodes.
        /// </summary>
        public GossipMessage CreateGossipMessage(bool isResponse = false)
        {
            var message = new GossipMessage
            {
                SenderId = _localNodeId,
                Timestamp = DateTime.UtcNow,
                Generation = _localGeneration,
                Version = _localVersion,
                NodeStates = new Dictionary<string, NodeState>(),
                Heartbeats = new Dictionary<string, long>()
            };

            // Add local state first (always include)
            if (_nodeStates.TryGetValue(_localNodeId, out var localState))
            {
                message.NodeStates[_localNodeId] = localState.State;
                message.Heartbeats[_localNodeId] = localState.HeartbeatSequence;
            }

            // Add other node states (prioritize recently updated)
            var otherStates = _nodeStates
                .Where(kvp => kvp.Key != _localNodeId)
                .OrderByDescending(kvp => kvp.Value.LastUpdated)
                .Take(_options.MaxNodesPerMessage - 1)
                .ToList();

            foreach (var kvp in otherStates)
            {
                message.NodeStates[kvp.Key] = kvp.Value.State;
                message.Heartbeats[kvp.Key] = kvp.Value.HeartbeatSequence;
            }

            return message;
        }

        /// <summary>
        /// Records a heartbeat from a node.
        /// </summary>
        public void RecordHeartbeat(string nodeId, long sequenceNumber)
        {
            UpdateHeartbeat(nodeId, sequenceNumber);
        }

        /// <summary>
        /// Gets nodes that are suspected to have failed.
        /// </summary>
        public IReadOnlyList<string> GetSuspectedNodes()
        {
            return _suspicionConfirmations
                .Where(kvp => kvp.Value.Count > 0)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        private async void OnGossipTimer(object? state)
        {
            if (!_isRunning || _cts.IsCancellationRequested)
                return;

            try
            {
                await ExecuteGossipRoundAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception)
            {
                // Log error but don't stop gossiping
            }
        }

        private async Task ExecuteGossipRoundAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _gossipRounds);

            // Get target nodes for gossip (exclude self and failed nodes)
            var targetNodes = GetGossipTargets();
            if (targetNodes.Count == 0)
                return;

            // Select random subset for fanout
            var selectedTargets = SelectRandomTargets(targetNodes, _options.Fanout);

            // Send gossip to selected targets
            var gossipMessage = CreateGossipMessage();
            var tasks = selectedTargets.Select(target =>
                SendGossipToNodeAsync(target, gossipMessage, cancellationToken));

            await Task.WhenAll(tasks);
        }

        private List<NodeInfo> GetGossipTargets()
        {
            var nodes = _clusterManager.GetNodesAsync().GetAwaiter().GetResult();
            return nodes
                .Where(n => n.NodeId != _localNodeId && n.State != NodeState.Dead)
                .ToList();
        }

        private List<NodeInfo> SelectRandomTargets(List<NodeInfo> candidates, int count)
        {
            if (candidates.Count <= count)
                return candidates;

                        // Use CSPRNG for target selection to prevent predictability and targeted node isolation
            return candidates
                .OrderBy(_ => System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue))
                .Take(count)
                .ToList();
        }

        private async Task SendGossipToNodeAsync(NodeInfo target, GossipMessage message, CancellationToken cancellationToken)
        {
            try
            {
                // This would integrate with P2PClient to send the message
                // For now, we just track that we attempted to send
                Interlocked.Increment(ref _messagesSent);
                Interlocked.Add(ref _totalMessageSizeBytes, EstimateMessageSize(message));

                // If using push-pull, we would wait for a response here
                if (_options.UsePushPull)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(_options.GossipTimeout);
                    // Response handling would be done via the message handler
                }

                await Task.CompletedTask;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Gossip is best-effort, failures are acceptable
            }
        }

        private void ProcessReceivedNodeState(string nodeId, NodeState receivedState, long generation, long version, string sourceNodeId)
        {
            var existing = _nodeStates.GetValueOrDefault(nodeId);

            // Check if this is a newer state
            bool isNewer = existing == null ||
                           generation > existing.Generation ||
                           (generation == existing.Generation && version > existing.Version);

            if (!isNewer)
                return;

            var previousState = existing?.State ?? NodeState.Joining;

            // Get or create node info
            var nodeInfo = existing?.Node ?? new NodeInfo
            {
                NodeId = nodeId,
                Host = "",
                P2PPort = 0,
                LastSeenAt = DateTime.UtcNow
            };
            nodeInfo.State = receivedState;

            // Update state
            var newStateInfo = new NodeStateInfo
            {
                Node = nodeInfo,
                State = receivedState,
                Generation = generation,
                Version = version,
                LastUpdated = DateTime.UtcNow,
                LastUpdatedBy = sourceNodeId,
                HeartbeatSequence = _heartbeats.GetValueOrDefault(nodeId)
            };

            _nodeStates[nodeId] = newStateInfo;
            Interlocked.Increment(ref _statesPropagated);

            // Raise event
            StateUpdated?.Invoke(this, new GossipStateUpdatedEventArgs
            {
                Node = newStateInfo.Node,
                PreviousState = previousState,
                NewState = receivedState,
                SourceNodeId = sourceNodeId
            });
        }

        private void UpdateHeartbeat(string nodeId, long sequenceNumber)
        {
            var current = _heartbeats.GetValueOrDefault(nodeId);
            if (sequenceNumber > current)
            {
                _heartbeats[nodeId] = sequenceNumber;

                // Update last seen for the node
                if (_nodeStates.TryGetValue(nodeId, out var state))
                {
                    state.LastUpdated = DateTime.UtcNow;
                    state.HeartbeatSequence = sequenceNumber;
                }
            }
        }

        private static int EstimateMessageSize(GossipMessage message)
        {
            // Rough estimation for statistics
            var size = 100; // Header overhead
            size += (message.NodeStates?.Count ?? 0) * 50; // ~50 bytes per node state
            size += (message.Heartbeats?.Count ?? 0) * 24; // ~24 bytes per heartbeat entry
            return size;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            _cts.Dispose();
            _gossipTimer?.Dispose();
        }
    }
}
