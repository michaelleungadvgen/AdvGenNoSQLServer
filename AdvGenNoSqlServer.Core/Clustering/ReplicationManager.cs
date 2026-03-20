// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Manages data replication across the cluster.
    /// </summary>
    public class ReplicationManager : IReplicationManager, IDisposable
    {
        private readonly IClusterManager _clusterManager;
        private readonly ReplicationConfiguration _configuration;
        private readonly ConcurrentDictionary<string, int> _collectionReplicationFactors = new();
        private readonly ConcurrentDictionary<string, PendingReplication> _pendingReplications = new();
        private readonly ConcurrentDictionary<string, NodeReplicationState> _nodeStates = new();
        private readonly System.Timers.Timer _statsTimer;
        private readonly System.Timers.Timer _cleanupTimer;
        private long _totalEventsSent;
        private long _totalEventsAcknowledged;
        private long _totalFailures;
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;
        private bool _started;

        /// <inheritdoc/>
        public ReplicationConfiguration Configuration => _configuration;

        /// <inheritdoc/>
        public ReplicationStatistics Statistics => CalculateStatistics();

        /// <inheritdoc/>
        public event EventHandler<ReplicationAck>? ReplicationAcknowledged;

        /// <inheritdoc/>
        public event EventHandler<ReplicationEvent>? ReplicationFailed;

        /// <summary>
        /// Callback for applying replication events to the local store.
        /// </summary>
        public Func<ReplicationEvent, CancellationToken, Task>? ApplyEventCallback { get; set; }

        /// <summary>
        /// Creates a new replication manager.
        /// </summary>
        public ReplicationManager(
            IClusterManager clusterManager,
            ReplicationConfiguration? configuration = null)
        {
            _clusterManager = clusterManager ?? throw new ArgumentNullException(nameof(clusterManager));
            _configuration = configuration ?? new ReplicationConfiguration();

            // Validate configuration
            ValidateConfiguration();

            // Stats timer - calculate statistics every 10 seconds
            _statsTimer = new System.Timers.Timer(10000);
            _statsTimer.Elapsed += (s, e) => CalculateStatistics();

            // Cleanup timer - clean up old pending replications every 30 seconds
            _cleanupTimer = new System.Timers.Timer(30000);
            _cleanupTimer.Elapsed += (s, e) => CleanupPendingReplications();
        }

        private void ValidateConfiguration()
        {
            if (_configuration.ReplicationFactor < 1)
                throw new ArgumentException("Replication factor must be at least 1", nameof(_configuration));

            if (_configuration.WriteQuorum < 1)
                throw new ArgumentException("Write quorum must be at least 1", nameof(_configuration));

            if (_configuration.WriteQuorum > _configuration.ReplicationFactor)
                throw new ArgumentException("Write quorum cannot exceed replication factor", nameof(_configuration));
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken ct = default)
        {
            if (_started)
                return Task.CompletedTask;

            _started = true;
            _statsTimer.Start();
            _cleanupTimer.Start();

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken ct = default)
        {
            if (!_started)
                return Task.CompletedTask;

            _started = false;
            _statsTimer.Stop();
            _cleanupTimer.Stop();
            _cts.Cancel();

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task SetReplicationFactorAsync(string collection, int factor, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(collection))
                throw new ArgumentException("Collection name cannot be empty", nameof(collection));

            if (factor < 1)
                throw new ArgumentException("Replication factor must be at least 1", nameof(factor));

            _collectionReplicationFactors[collection] = factor;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<int> GetReplicationFactorAsync(string collection, CancellationToken ct = default)
        {
            if (_collectionReplicationFactors.TryGetValue(collection, out var factor))
                return Task.FromResult(factor);

            return Task.FromResult(_configuration.ReplicationFactor);
        }

        /// <inheritdoc/>
        public async Task<ReplicationResult> ReplicateWriteAsync(ReplicationEvent evt, CancellationToken ct = default)
        {
            EnsureStarted();

            var stopwatch = Stopwatch.StartNew();
            var nodes = await GetReplicationTargetsAsync();
            var factor = await GetReplicationFactorAsync(evt.Collection, ct);
            var requiredAcks = CalculateQuorum(factor);

            // For asynchronous replication, don't wait
            if (_configuration.Strategy.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase))
            {
                _ = Task.Run(async () => await ReplicateToNodesAsync(evt, nodes), ct);
                return ReplicationResult.SuccessResult(0, 0, new List<string>(), stopwatch.Elapsed);
            }

            // Create pending replication tracking
            var pending = new PendingReplication
            {
                OperationId = evt.OperationId,
                Event = evt,
                TargetNodes = nodes.Select(n => n.NodeId).ToList(),
                RequiredAcks = requiredAcks,
                StartTime = DateTime.UtcNow
            };
            _pendingReplications[evt.OperationId] = pending;

            // Send to all target nodes
            var sendTasks = nodes.Select(node => SendToNodeAsync(evt, node)).ToList();
            await Task.WhenAll(sendTasks);

            Interlocked.Increment(ref _totalEventsSent);

            // Wait for acknowledgments
            var result = await WaitForAcksAsync(evt.OperationId, requiredAcks, _configuration.SyncTimeout, ct);
            result.ReplicationTime = stopwatch.Elapsed;

            // Clean up
            _pendingReplications.TryRemove(evt.OperationId, out _);

            return result;
        }

        /// <inheritdoc/>
        public async Task<ReplicationResult> ReplicateBatchAsync(IEnumerable<ReplicationEvent> events, CancellationToken ct = default)
        {
            EnsureStarted();

            var eventList = events.ToList();
            if (eventList.Count == 0)
            {
                return ReplicationResult.SuccessResult(0, 0, new List<string>(), TimeSpan.Zero);
            }

            var stopwatch = Stopwatch.StartNew();
            var results = new List<ReplicationResult>();

            foreach (var evt in eventList)
            {
                var result = await ReplicateWriteAsync(evt, ct);
                results.Add(result);
            }

            // Aggregate results
            var successCount = results.Count(r => r.Success);
            var totalAcks = results.Sum(r => r.AcknowledgedCount);
            var allAckNodes = results.SelectMany(r => r.AcknowledgingNodes).Distinct().ToList();

            return new ReplicationResult
            {
                Success = successCount == eventList.Count,
                AcknowledgedCount = totalAcks,
                RequiredQuorum = results.First().RequiredQuorum * eventList.Count,
                AcknowledgingNodes = allAckNodes,
                ReplicationTime = stopwatch.Elapsed
            };
        }

        /// <inheritdoc/>
        public async Task<ReplicationResult> WaitForAcksAsync(string operationId, int requiredAcks, TimeSpan timeout, CancellationToken ct = default)
        {
            if (!_pendingReplications.TryGetValue(operationId, out var pending))
            {
                return ReplicationResult.FailureResult(0, requiredAcks, "Operation not found", new Dictionary<string, string>(), TimeSpan.Zero);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            cts.CancelAfter(timeout);

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var currentAcks = pending.Acknowledgments.Count(a => a.Value.Success);

                    if (currentAcks >= requiredAcks)
                    {
                        return ReplicationResult.SuccessResult(
                            currentAcks,
                            requiredAcks,
                            pending.Acknowledgments.Where(a => a.Value.Success).Select(a => a.Key).ToList(),
                            DateTime.UtcNow - pending.StartTime);
                    }

                    await Task.Delay(10, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout or cancellation
            }

            // Timeout - return partial result
            var acks = pending.Acknowledgments.Count(a => a.Value.Success);
            var failedNodes = pending.TargetNodes
                .Where(n => !pending.Acknowledgments.ContainsKey(n) || !pending.Acknowledgments[n].Success)
                .ToDictionary(
                    n => n,
                    n => pending.Acknowledgments.TryGetValue(n, out var ack) && !ack.Success
                        ? ack.ErrorMessage ?? "Unknown error"
                        : "No acknowledgment received");

            return ReplicationResult.FailureResult(
                acks,
                requiredAcks,
                $"Timeout waiting for acknowledgments. Got {acks}/{requiredAcks}",
                failedNodes,
                DateTime.UtcNow - pending.StartTime);
        }

        /// <inheritdoc/>
        public Task ProcessAckAsync(ReplicationAck ack, CancellationToken ct = default)
        {
            if (_pendingReplications.TryGetValue(ack.OperationId, out var pending))
            {
                pending.Acknowledgments[ack.NodeId] = ack;

                if (ack.Success)
                {
                    Interlocked.Increment(ref _totalEventsAcknowledged);
                }
                else
                {
                    Interlocked.Increment(ref _totalFailures);
                }

                ReplicationAcknowledged?.Invoke(this, ack);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<SyncStatus> GetSyncStatusAsync(string nodeId, CancellationToken ct = default)
        {
            if (!_nodeStates.TryGetValue(nodeId, out var state))
            {
                return Task.FromResult(new SyncStatus
                {
                    NodeId = nodeId,
                    IsSynchronized = false,
                    LastSequenceNumber = 0,
                    PendingEvents = 0
                });
            }

            var status = new SyncStatus
            {
                NodeId = nodeId,
                IsSynchronized = state.IsSynchronized,
                LastSequenceNumber = state.LastSequenceNumber,
                PendingEvents = state.PendingEvents,
                LastReplicationTime = state.LastReplicationTime,
                ReplicationLagMs = state.ReplicationLagMs
            };

            return Task.FromResult(status);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<SyncStatus>> GetAllSyncStatusAsync(CancellationToken ct = default)
        {
            var statuses = _nodeStates.Values.Select(s => new SyncStatus
            {
                NodeId = s.NodeId,
                IsSynchronized = s.IsSynchronized,
                LastSequenceNumber = s.LastSequenceNumber,
                PendingEvents = s.PendingEvents,
                LastReplicationTime = s.LastReplicationTime,
                ReplicationLagMs = s.ReplicationLagMs
            }).ToList();

            return Task.FromResult<IReadOnlyList<SyncStatus>>(statuses);
        }

        /// <inheritdoc/>
        public async Task RequestFullSyncAsync(string nodeId, CancellationToken ct = default)
        {
            // This would trigger a full synchronization from the leader
            // Implementation depends on having access to the full document store
            // For now, we just mark the node as needing sync

            var state = _nodeStates.GetOrAdd(nodeId, _ => new NodeReplicationState { NodeId = nodeId });
            state.IsSynchronized = false;
            state.PendingEvents = long.MaxValue; // Unknown, needs full sync

            // TODO: Implement actual full sync using snapshot + WAL replay
            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task ApplyReplicationEventAsync(ReplicationEvent evt, CancellationToken ct = default)
        {
            // Apply the event to the local document store via callback
            // This is called when receiving a replication event from another node

            if (ApplyEventCallback != null)
            {
                await ApplyEventCallback(evt, ct);
            }
        }

        private async Task<List<NodeInfo>> GetReplicationTargetsAsync()
        {
            var allNodes = await _clusterManager.GetNodesAsync();
            var localNodeId = _clusterManager.LocalNode.NodeId;

            // Exclude self from replication targets
            return allNodes.Where(n => n.NodeId != localNodeId).ToList();
        }

        private int CalculateQuorum(int factor)
        {
            var strategy = _configuration.Strategy;
            
            if (strategy.Equals("Synchronous", StringComparison.OrdinalIgnoreCase))
                return factor;
            
            if (strategy.Equals("Asynchronous", StringComparison.OrdinalIgnoreCase))
                return 1;
            
            // SemiSynchronous (default) - majority
            return (factor / 2) + 1;
        }

        private async Task SendToNodeAsync(ReplicationEvent evt, NodeInfo node)
        {
            // In a real implementation, this would send over the network
            // For now, we simulate the send by updating node state

            var nodeState = _nodeStates.GetOrAdd(node.NodeId, _ => new NodeReplicationState
            {
                NodeId = node.NodeId,
                LastReplicationTime = DateTime.UtcNow
            });

            nodeState.EventsSent++;

            // Simulate async network call
            await Task.Delay(1);
        }

        private async Task ReplicateToNodesAsync(ReplicationEvent evt, List<NodeInfo> nodes)
        {
            // Background replication for asynchronous mode
            var sendTasks = nodes.Select(node => SendToNodeAsync(evt, node)).ToList();
            await Task.WhenAll(sendTasks);
        }

        private ReplicationStatistics CalculateStatistics()
        {
            var stats = new ReplicationStatistics
            {
                TotalEventsSent = Interlocked.Read(ref _totalEventsSent),
                TotalEventsAcknowledged = Interlocked.Read(ref _totalEventsAcknowledged),
                TotalFailures = Interlocked.Read(ref _totalFailures),
                PendingEvents = _pendingReplications.Count
            };

            // Calculate average latency
            var latencies = _pendingReplications.Values
                .Where(p => p.Acknowledgments.Any())
                .Select(p => (DateTime.UtcNow - p.StartTime).TotalMilliseconds)
                .ToList();

            if (latencies.Any())
            {
                stats.AverageLatencyMs = latencies.Average();
            }

            // Per-node stats
            foreach (var nodeState in _nodeStates.Values)
            {
                stats.PerNodeStats[nodeState.NodeId] = new NodeReplicationStats
                {
                    NodeId = nodeState.NodeId,
                    EventsSent = nodeState.EventsSent,
                    EventsAcknowledged = nodeState.EventsAcknowledged,
                    Failures = nodeState.Failures,
                    AverageLatencyMs = nodeState.AverageLatencyMs,
                    LastSeen = nodeState.LastReplicationTime
                };
            }

            return stats;
        }

        private void CleanupPendingReplications()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            var toRemove = _pendingReplications
                .Where(p => p.Value.StartTime < cutoff)
                .Select(p => p.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _pendingReplications.TryRemove(key, out _);
            }
        }

        private void EnsureStarted()
        {
            if (!_started)
                throw new InvalidOperationException("Replication manager not started. Call StartAsync first.");
        }

        /// <summary>
        /// Disposes the replication manager.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cts.Cancel();
            _statsTimer.Dispose();
            _cleanupTimer.Dispose();
            _cts.Dispose();
        }

        /// <summary>
        /// Tracks pending replication state.
        /// </summary>
        private class PendingReplication
        {
            public required string OperationId { get; init; }
            public required ReplicationEvent Event { get; init; }
            public required List<string> TargetNodes { get; init; }
            public required int RequiredAcks { get; init; }
            public required DateTime StartTime { get; init; }
            public ConcurrentDictionary<string, ReplicationAck> Acknowledgments { get; } = new();
        }

        /// <summary>
        /// Tracks per-node replication state.
        /// </summary>
        private class NodeReplicationState
        {
            public required string NodeId { get; init; }
            public bool IsSynchronized { get; set; }
            public long LastSequenceNumber { get; set; }
            public long PendingEvents { get; set; }
            public DateTime LastReplicationTime { get; set; }
            public double ReplicationLagMs { get; set; }
            public long EventsSent { get; set; }
            public long EventsAcknowledged { get; set; }
            public long Failures { get; set; }
            public double AverageLatencyMs { get; set; }
        }
    }
}
