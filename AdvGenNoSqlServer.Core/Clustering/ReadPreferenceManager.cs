// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace AdvGenNoSqlServer.Core.Clustering;

/// <summary>
/// Implementation of the read preference manager that handles node selection
/// based on configured read preference modes and strategies.
/// </summary>
public class ReadPreferenceManager : IReadPreferenceManager, IDisposable
{
    private readonly IClusterManager? _clusterManager;
    private readonly ConcurrentDictionary<string, NodeReadInfo> _nodeInfo;
    private readonly ConcurrentDictionary<string, DateTime> _lastSelectionTime;
    private ReadPreferenceOptions _defaultOptions;
    private long _roundRobinIndex;
    private readonly ReaderWriterLockSlim _optionsLock;
    private bool _disposed;

    // Statistics fields (using long for thread-safe operations)
    private long _totalSelections;
    private long _successfulSelections;
    private long _failedSelections;
    private long _primarySelections;
    private long _secondarySelections;
    private long _totalSelectionLatencyMs;
    private DateTime? _lastSelectionTimeStats;

    /// <summary>
    /// Creates a new read preference manager with optional cluster manager integration.
    /// </summary>
    /// <param name="clusterManager">Optional cluster manager for node discovery</param>
    /// <param name="defaultOptions">Optional default read preference options</param>
    public ReadPreferenceManager(IClusterManager? clusterManager = null, ReadPreferenceOptions? defaultOptions = null)
    {
        _clusterManager = clusterManager;
        _defaultOptions = defaultOptions ?? ReadPreferenceOptions.Primary;
        _nodeInfo = new ConcurrentDictionary<string, NodeReadInfo>();
        _lastSelectionTime = new ConcurrentDictionary<string, DateTime>();
        _optionsLock = new ReaderWriterLockSlim();
        _roundRobinIndex = 0;
    }

    /// <inheritdoc />
    public async Task<ReadPreferenceResult> SelectNodeAsync(ReadPreferenceOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var stopwatch = Stopwatch.StartNew();
        Interlocked.Increment(ref _totalSelections);

        try
        {
            var availableNodes = await GetAvailableNodesAsync(!options.EnableHealthChecks, cancellationToken);
            var eligibleNodes = FilterEligibleNodes(availableNodes, options);
            var nodesConsidered = availableNodes.Count;
            var nodesExcluded = nodesConsidered - eligibleNodes.Count;

            if (eligibleNodes.Count == 0)
            {
                // Try fallback based on mode
                var fallbackResult = TryFallback(availableNodes, options, nodesConsidered, nodesExcluded);
                if (fallbackResult != null)
                {
                    RecordSelection(fallbackResult, stopwatch.ElapsedMilliseconds);
                    return fallbackResult;
                }

                Interlocked.Increment(ref _failedSelections);
                var errorResult = ReadPreferenceResult.FailureResult(
                    options.Mode,
                    $"No eligible nodes found for read preference mode '{options.Mode}'",
                    nodesConsidered,
                    nodesExcluded
                );

                OnNodeSelectionFailed(new NodeSelectionFailedEventArgs
                {
                    Mode = options.Mode,
                    ErrorMessage = errorResult.ErrorMessage!,
                    NodesConsidered = nodesConsidered,
                    NodesExcluded = nodesExcluded
                });

                return errorResult;
            }

            var selectedNode = SelectNodeFromCandidates(eligibleNodes, options);
            if (selectedNode == null)
            {
                Interlocked.Increment(ref _failedSelections);
                return ReadPreferenceResult.FailureResult(
                    options.Mode,
                    "Node selection failed from eligible candidates",
                    nodesConsidered,
                    nodesExcluded
                );
            }

            var result = ReadPreferenceResult.SuccessResult(
                selectedNode.Node,
                options.Mode,
                nodesConsidered,
                nodesExcluded,
                selectedNode.LatencyMs
            );

            RecordSelection(result, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedSelections);
            return ReadPreferenceResult.FailureResult(options.Mode, $"Selection error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<ReadPreferenceResult> SelectNodeAsync(CancellationToken cancellationToken = default)
    {
        ReadPreferenceOptions options;
        _optionsLock.EnterReadLock();
        try
        {
            options = _defaultOptions;
        }
        finally
        {
            _optionsLock.ExitReadLock();
        }
        return SelectNodeAsync(options, cancellationToken);
    }

    /// <inheritdoc />
    public Task<NodeIdentity?> GetPrimaryNodeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var primary = _nodeInfo.Values.FirstOrDefault(n => n.IsPrimary && n.IsHealthy);
        return Task.FromResult(primary?.Node);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<NodeIdentity>> GetSecondaryNodesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var secondaries = _nodeInfo.Values
            .Where(n => !n.IsPrimary && n.IsHealthy)
            .Select(n => n.Node)
            .ToList();

        return Task.FromResult<IReadOnlyList<NodeIdentity>>(secondaries);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<NodeReadInfo>> GetAvailableNodesAsync(bool includeUnhealthy = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var nodes = _nodeInfo.Values
            .Where(n => includeUnhealthy || n.IsHealthy)
            .ToList();

        return Task.FromResult<IReadOnlyList<NodeReadInfo>>(nodes);
    }

    /// <inheritdoc />
    public void UpdateNodeLatency(string nodeId, double latencyMs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _nodeInfo.AddOrUpdate(
            nodeId,
            _ => new NodeReadInfo
            {
                Node = CreateMinimalNodeIdentity(nodeId),
                LatencyMs = latencyMs,
                IsHealthy = true
            },
            (_, existing) =>
            {
                existing.LatencyMs = latencyMs;
                return existing;
            }
        );
    }

    /// <inheritdoc />
    public void UpdateNodeHealth(string nodeId, bool isHealthy, long replicationLagMs = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _nodeInfo.AddOrUpdate(
            nodeId,
            _ => new NodeReadInfo
            {
                Node = CreateMinimalNodeIdentity(nodeId),
                IsHealthy = isHealthy,
                ReplicationLagMs = replicationLagMs,
                LastHealthCheck = DateTime.UtcNow
            },
            (_, existing) =>
            {
                existing.IsHealthy = isHealthy;
                existing.ReplicationLagMs = replicationLagMs;
                existing.LastHealthCheck = DateTime.UtcNow;
                return existing;
            }
        );
    }

    /// <inheritdoc />
    public ReadPreferenceStatistics GetStatistics()
    {
        var totalSelections = Interlocked.Read(ref _totalSelections);
        return new ReadPreferenceStatistics
        {
            TotalSelections = totalSelections,
            SuccessfulSelections = Interlocked.Read(ref _successfulSelections),
            FailedSelections = Interlocked.Read(ref _failedSelections),
            PrimarySelections = Interlocked.Read(ref _primarySelections),
            SecondarySelections = Interlocked.Read(ref _secondarySelections),
            AverageSelectionLatencyMs = totalSelections > 0
                ? (double)Interlocked.Read(ref _totalSelectionLatencyMs) / totalSelections
                : 0,
            LastSelectionTime = _lastSelectionTimeStats
        };
    }

    /// <inheritdoc />
    public void ResetStatistics()
    {
        Interlocked.Exchange(ref _totalSelections, 0);
        Interlocked.Exchange(ref _successfulSelections, 0);
        Interlocked.Exchange(ref _failedSelections, 0);
        Interlocked.Exchange(ref _primarySelections, 0);
        Interlocked.Exchange(ref _secondarySelections, 0);
        Interlocked.Exchange(ref _totalSelectionLatencyMs, 0);
        _lastSelectionTimeStats = null;
    }

    /// <inheritdoc />
    public void SetDefaultOptions(ReadPreferenceOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _optionsLock.EnterWriteLock();
        try
        {
            _defaultOptions = options ?? throw new ArgumentNullException(nameof(options));
        }
        finally
        {
            _optionsLock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public ReadPreferenceOptions GetDefaultOptions()
    {
        _optionsLock.EnterReadLock();
        try
        {
            return _defaultOptions;
        }
        finally
        {
            _optionsLock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public event EventHandler<NodeSelectedEventArgs>? NodeSelected;

    /// <inheritdoc />
    public event EventHandler<NodeSelectionFailedEventArgs>? NodeSelectionFailed;

    /// <summary>
    /// Registers a node with the read preference manager.
    /// </summary>
    /// <param name="nodeInfo">The node information</param>
    public void RegisterNode(NodeReadInfo nodeInfo)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(nodeInfo);

        _nodeInfo[nodeInfo.Node.NodeId.ToString()] = nodeInfo;
    }

    /// <summary>
    /// Unregisters a node from the read preference manager.
    /// </summary>
    /// <param name="nodeId">The node ID</param>
    /// <returns>True if the node was removed, false otherwise</returns>
    public bool UnregisterNode(string nodeId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _nodeInfo.TryRemove(nodeId, out _);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _optionsLock.Dispose();
    }

    private List<NodeReadInfo> FilterEligibleNodes(IReadOnlyList<NodeReadInfo> nodes, ReadPreferenceOptions options)
    {
        var eligible = nodes.ToList();

        // Filter by read preference mode
        // Primary and Secondary modes filter to only those node types
        // PrimaryPreferred and SecondaryPreferred order by preference but include all nodes
        eligible = options.Mode switch
        {
            ReadPreferenceMode.Primary => eligible.Where(n => n.IsPrimary).ToList(),
            ReadPreferenceMode.Secondary => eligible.Where(n => !n.IsPrimary).ToList(),
            ReadPreferenceMode.PrimaryPreferred => eligible.OrderByDescending(n => n.IsPrimary).ToList(),
            ReadPreferenceMode.SecondaryPreferred => eligible.OrderBy(n => n.IsPrimary).ToList(),
            ReadPreferenceMode.Nearest => eligible,
            _ => eligible
        };

        // Filter by tag sets if specified
        if (options.TagSets.Count > 0)
        {
            var tagMatchedNodes = eligible
                .Where(n => options.TagSets.Any(tagSet => tagSet.Matches(n.Tags)))
                .ToList();

            if (tagMatchedNodes.Count > 0)
            {
                eligible = tagMatchedNodes;
            }
        }

        // Filter by staleness
        if (options.MaxStalenessMs > 0)
        {
            eligible = eligible
                .Where(n => n.IsPrimary || n.ReplicationLagMs <= options.MaxStalenessMs)
                .ToList();
        }

        return eligible;
    }

    /// <summary>
    /// Selects a node from candidates based on the selection strategy.
    /// For preference modes, this ensures we pick the first preferred node type.
    /// </summary>
    private NodeReadInfo? SelectNodeFromCandidates(List<NodeReadInfo> candidates, ReadPreferenceOptions options)
    {
        if (candidates.Count == 0)
            return null;

        if (candidates.Count == 1)
            return candidates[0];

        // For preference modes with ordered lists, pick the first preferred node
        if (options.Mode == ReadPreferenceMode.PrimaryPreferred || options.Mode == ReadPreferenceMode.SecondaryPreferred)
        {
            // The list is already ordered by preference, so pick the first node
            // that matches the preferred type
            return candidates[0];
        }

        return options.SelectionStrategy switch
        {
            NodeSelectionStrategy.Random => SelectRandomNode(candidates),
            NodeSelectionStrategy.LatencyBased => SelectLowestLatencyNode(candidates),
            NodeSelectionStrategy.LoadBased => SelectLowestLoadNode(candidates),
            _ => SelectRoundRobinNode(candidates)
        };
    }

    private ReadPreferenceResult? TryFallback(
        IReadOnlyList<NodeReadInfo> availableNodes,
        ReadPreferenceOptions options,
        int nodesConsidered,
        int nodesExcluded)
    {
        List<NodeReadInfo> fallbackNodes = options.Mode switch
        {
            ReadPreferenceMode.PrimaryPreferred => availableNodes.Where(n => !n.IsPrimary).ToList(),
            ReadPreferenceMode.SecondaryPreferred => availableNodes.Where(n => n.IsPrimary).ToList(),
            _ => new List<NodeReadInfo>()
        };

        if (fallbackNodes.Count == 0)
            return null;

        var selectedNode = SelectNodeFromCandidates(fallbackNodes, options);
        if (selectedNode == null)
            return null;

        return ReadPreferenceResult.SuccessResult(
            selectedNode.Node,
            options.Mode,
            nodesConsidered,
            nodesExcluded,
            selectedNode.LatencyMs
        );
    }

    private NodeReadInfo SelectRandomNode(List<NodeReadInfo> candidates)
    {
        var index = Random.Shared.Next(candidates.Count);
        return candidates[index];
    }

    private NodeReadInfo SelectRoundRobinNode(List<NodeReadInfo> candidates)
    {
        var index = (int)(Interlocked.Increment(ref _roundRobinIndex) % (uint)candidates.Count);
        return candidates[index];
    }

    private NodeReadInfo SelectLowestLatencyNode(List<NodeReadInfo> candidates)
    {
        return candidates.OrderBy(n => n.LatencyMs).First();
    }

    private NodeReadInfo SelectLowestLoadNode(List<NodeReadInfo> candidates)
    {
        return candidates.OrderBy(n => n.LoadFactor).First();
    }

    private void RecordSelection(ReadPreferenceResult result, long elapsedMs)
    {
        Interlocked.Increment(ref _successfulSelections);
        Interlocked.Add(ref _totalSelectionLatencyMs, elapsedMs);
        _lastSelectionTimeStats = DateTime.UtcNow;

        if (result.SelectedNode != null)
        {
            var isPrimary = _nodeInfo.TryGetValue(result.SelectedNode.NodeId.ToString(), out var info) && info.IsPrimary;

            if (isPrimary)
                Interlocked.Increment(ref _primarySelections);
            else
                Interlocked.Increment(ref _secondarySelections);

            OnNodeSelected(new NodeSelectedEventArgs
            {
                Node = result.SelectedNode,
                Mode = result.Mode,
                SelectionTimeMs = elapsedMs,
                IsPrimary = isPrimary,
                LatencyMs = result.LatencyMs
            });
        }
    }

    protected virtual void OnNodeSelected(NodeSelectedEventArgs e)
    {
        NodeSelected?.Invoke(this, e);
    }

    protected virtual void OnNodeSelectionFailed(NodeSelectionFailedEventArgs e)
    {
        NodeSelectionFailed?.Invoke(this, e);
    }

    private static NodeIdentity CreateMinimalNodeIdentity(string nodeId)
    {
        // Validate the nodeId is a valid GUID string, otherwise generate a new one
        string id;
        try
        {
            // Verify it's a valid GUID format
            _ = Guid.Parse(nodeId);
            id = nodeId;
        }
        catch
        {
            id = Guid.NewGuid().ToString();
        }

        return new NodeIdentity
        {
            NodeId = id,
            ClusterId = "default",
            Host = "localhost",
            Port = 9090,
            P2PPort = 9091
        };
    }
}
