// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Manages locks for transaction concurrency control with deadlock detection
/// </summary>
public class LockManager : ILockManager, IDisposable
{
    // ResourceId -> List of locks held on this resource
    private readonly ConcurrentDictionary<string, List<LockInfo>> _resourceLocks = new();

    // TransactionId -> Set of resources locked by this transaction
    private readonly ConcurrentDictionary<string, HashSet<string>> _transactionLocks = new();

    // ResourceId -> Queue of waiting lock requests
    private readonly ConcurrentDictionary<string, Queue<LockRequest>> _waitingQueues = new();

    // Lock for synchronizing access to internal structures
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);

    // Background task for deadlock detection
    private readonly Timer? _deadlockDetectionTimer;
    private readonly TimeSpan _deadlockDetectionInterval;

    /// <inheritdoc />
    public int ActiveLockCount
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _resourceLocks.Values.Sum(list => list.Count);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <inheritdoc />
    public int WaitingRequestCount
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _waitingQueues.Values.Sum(queue => queue.Count);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<DeadlockEventArgs>? DeadlockDetected;

    /// <summary>
    /// Creates a new LockManager
    /// </summary>
    /// <param name="enableDeadlockDetection">Whether to enable automatic deadlock detection</param>
    /// <param name="deadlockDetectionInterval">Interval for deadlock detection scans</param>
    public LockManager(bool enableDeadlockDetection = true, TimeSpan? deadlockDetectionInterval = null)
    {
        _deadlockDetectionInterval = deadlockDetectionInterval ?? TimeSpan.FromSeconds(5);

        if (enableDeadlockDetection)
        {
            _deadlockDetectionTimer = new Timer(
                _ => DetectAndResolveDeadlocks(),
                null,
                _deadlockDetectionInterval,
                _deadlockDetectionInterval);
        }
    }

    /// <inheritdoc />
    public Task<LockResult> AcquireLockAsync(string transactionId, string resourceId, LockType lockType, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(transactionId, nameof(transactionId));
        ArgumentException.ThrowIfNullOrEmpty(resourceId, nameof(resourceId));

        // Fast path: try to acquire lock immediately
        if (TryAcquireLock(transactionId, resourceId, lockType))
        {
            return Task.FromResult(LockResult.Granted);
        }

        // Check for deadlock before waiting
        if (WouldCauseDeadlock(transactionId, resourceId))
        {
            return Task.FromResult(LockResult.DeadlockDetected);
        }

        // Slow path: need to wait for the lock
        return WaitForLockAsync(transactionId, resourceId, lockType, timeout);
    }

    /// <inheritdoc />
    public LockResult AcquireLock(string transactionId, string resourceId, LockType lockType, TimeSpan? timeout = null)
    {
        return AcquireLockAsync(transactionId, resourceId, lockType, timeout).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public bool ReleaseLock(string transactionId, string resourceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(transactionId, nameof(transactionId));
        ArgumentException.ThrowIfNullOrEmpty(resourceId, nameof(resourceId));

        _lock.EnterWriteLock();
        try
        {
            // Remove from resource locks
            if (!_resourceLocks.TryGetValue(resourceId, out var locks))
            {
                return false;
            }

            var lockToRemove = locks.FirstOrDefault(l => l.TransactionId == transactionId);
            if (lockToRemove == null)
            {
                return false;
            }

            locks.Remove(lockToRemove);

            // Clean up empty lock lists
            if (locks.Count == 0)
            {
                _resourceLocks.TryRemove(resourceId, out _);
            }

            // Remove from transaction locks
            if (_transactionLocks.TryGetValue(transactionId, out var resources))
            {
                resources.Remove(resourceId);
                if (resources.Count == 0)
                {
                    _transactionLocks.TryRemove(transactionId, out _);
                }
            }

            // Notify waiting requests (before releasing the write lock)
            GrantWaitingLocks(resourceId);

            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public int ReleaseAllLocks(string transactionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(transactionId, nameof(transactionId));

        _lock.EnterWriteLock();
        try
        {
            if (!_transactionLocks.TryGetValue(transactionId, out var resources))
            {
                return 0;
            }

            var releasedCount = 0;
            var resourcesCopy = resources.ToList();

            foreach (var resourceId in resourcesCopy)
            {
                if (_resourceLocks.TryGetValue(resourceId, out var locks))
                {
                    var removed = locks.RemoveAll(l => l.TransactionId == transactionId);
                    releasedCount += removed;

                    if (locks.Count == 0)
                    {
                        _resourceLocks.TryRemove(resourceId, out _);
                    }
                }
            }

            _transactionLocks.TryRemove(transactionId, out _);

            // Notify waiting requests for all released resources (while still holding the lock)
            foreach (var resourceId in resourcesCopy)
            {
                GrantWaitingLocks(resourceId);
            }

            return releasedCount;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public Task<LockResult> UpgradeLockAsync(string transactionId, string resourceId, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(transactionId, nameof(transactionId));
        ArgumentException.ThrowIfNullOrEmpty(resourceId, nameof(resourceId));

        _lock.EnterUpgradeableReadLock();
        try
        {
            // Check if transaction has a shared lock
            if (!TryGetLockInfo(transactionId, resourceId, out var lockInfo) || lockInfo.LockType != LockType.Shared)
            {
                return Task.FromResult(LockResult.Denied);
            }

            // Check if upgrade is possible (no other locks on this resource)
            if (_resourceLocks.TryGetValue(resourceId, out var locks) && locks.Count > 1)
            {
                // Need to wait for other locks to be released
                return WaitForUpgradeAsync(transactionId, resourceId, timeout);
            }

            // Can upgrade immediately
            _lock.EnterWriteLock();
            try
            {
                lockInfo.LockType = LockType.Exclusive;
                return Task.FromResult(LockResult.Granted);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }

    /// <inheritdoc />
    public bool HasLock(string transactionId, string resourceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(transactionId, nameof(transactionId));
        ArgumentException.ThrowIfNullOrEmpty(resourceId, nameof(resourceId));

        return TryGetLockInfo(transactionId, resourceId, out _);
    }

    /// <inheritdoc />
    public LockType? GetLockType(string transactionId, string resourceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(transactionId, nameof(transactionId));
        ArgumentException.ThrowIfNullOrEmpty(resourceId, nameof(resourceId));

        if (TryGetLockInfo(transactionId, resourceId, out var lockInfo))
        {
            return lockInfo.LockType;
        }

        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<LockInfo> GetTransactionLocks(string transactionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(transactionId, nameof(transactionId));

        _lock.EnterReadLock();
        try
        {
            var result = new List<LockInfo>();

            if (_transactionLocks.TryGetValue(transactionId, out var resources))
            {
                foreach (var resourceId in resources)
                {
                    if (_resourceLocks.TryGetValue(resourceId, out var locks))
                    {
                        var lockInfo = locks.FirstOrDefault(l => l.TransactionId == transactionId);
                        if (lockInfo != null)
                        {
                            result.Add(new LockInfo
                            {
                                TransactionId = lockInfo.TransactionId,
                                ResourceId = lockInfo.ResourceId,
                                LockType = lockInfo.LockType,
                                AcquiredAt = lockInfo.AcquiredAt,
                                ExpiresAt = lockInfo.ExpiresAt
                            });
                        }
                    }
                }
            }

            return result.AsReadOnly();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<LockInfo> GetResourceLocks(string resourceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceId, nameof(resourceId));

        _lock.EnterReadLock();
        try
        {
            if (!_resourceLocks.TryGetValue(resourceId, out var locks))
            {
                return new List<LockInfo>().AsReadOnly();
            }

            return locks.Select(l => new LockInfo
            {
                TransactionId = l.TransactionId,
                ResourceId = l.ResourceId,
                LockType = l.LockType,
                AcquiredAt = l.AcquiredAt,
                ExpiresAt = l.ExpiresAt
            }).ToList().AsReadOnly();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Disposes the lock manager and stops deadlock detection
    /// </summary>
    public void Dispose()
    {
        _deadlockDetectionTimer?.Dispose();
        _lock.Dispose();
    }

    #region Private Methods

    private bool TryAcquireLock(string transactionId, string resourceId, LockType lockType)
    {
        _lock.EnterWriteLock();
        try
        {
            // Check if transaction already has a lock on this resource
            if (TryGetLockInfo(transactionId, resourceId, out var existingLock))
            {
                // Already have the requested lock type or better
                if (existingLock.LockType == LockType.Exclusive || lockType == LockType.Shared)
                {
                    return true;
                }
                // Have shared, want exclusive - need to check if upgrade is possible
                return false;
            }

            // Check if any locks exist on this resource
            if (!_resourceLocks.TryGetValue(resourceId, out var existingLocks) || existingLocks.Count == 0)
            {
                // No locks exist, can acquire
                GrantLock(transactionId, resourceId, lockType);
                return true;
            }

            // Check compatibility with existing locks
            if (lockType == LockType.Shared)
            {
                // Shared locks are compatible with other shared locks
                if (existingLocks.All(l => l.LockType == LockType.Shared))
                {
                    GrantLock(transactionId, resourceId, lockType);
                    return true;
                }
            }

            // Exclusive lock or mixed with exclusive - cannot acquire
            return false;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void GrantLock(string transactionId, string resourceId, LockType lockType)
    {
        var lockInfo = new LockInfo
        {
            TransactionId = transactionId,
            ResourceId = resourceId,
            LockType = lockType,
            AcquiredAt = DateTime.UtcNow
        };

        var locks = _resourceLocks.GetOrAdd(resourceId, _ => new List<LockInfo>());
        locks.Add(lockInfo);

        var resources = _transactionLocks.GetOrAdd(transactionId, _ => new HashSet<string>());
        resources.Add(resourceId);
    }

    private async Task<LockResult> WaitForLockAsync(string transactionId, string resourceId, LockType lockType, TimeSpan? timeout)
    {
        var request = new LockRequest
        {
            TransactionId = transactionId,
            ResourceId = resourceId,
            LockType = lockType,
            RequestedAt = DateTime.UtcNow,
            Timeout = timeout,
            CompletionSource = new TaskCompletionSource<LockResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };

        _lock.EnterWriteLock();
        try
        {
            var queue = _waitingQueues.GetOrAdd(resourceId, _ => new Queue<LockRequest>());
            queue.Enqueue(request);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        // Set up timeout if specified
        if (timeout.HasValue)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(timeout.Value);
                request.CompletionSource.TrySetResult(LockResult.Timeout);
            });
        }

        return await request.CompletionSource.Task;
    }

    private async Task<LockResult> WaitForUpgradeAsync(string transactionId, string resourceId, TimeSpan? timeout)
    {
        // Similar to WaitForLockAsync but for lock upgrades
        // For simplicity, release shared lock and re-acquire as exclusive
        ReleaseLock(transactionId, resourceId);
        return await AcquireLockAsync(transactionId, resourceId, LockType.Exclusive, timeout);
    }

    /// <summary>
    /// Grants locks to waiting requests for a specific resource.
    /// Must be called while holding the write lock.
    /// </summary>
    private void GrantWaitingLocks(string resourceId)
    {
        if (!_waitingQueues.TryGetValue(resourceId, out var queue) || queue.Count == 0)
        {
            return;
        }

        // Process queue in order
        while (queue.Count > 0)
        {
            var request = queue.Peek();

            // Check if request has timed out or been cancelled
            if (request.CompletionSource.Task.IsCompleted)
            {
                queue.Dequeue();
                continue;
            }

            // Try to grant the lock (using the internal grant method that doesn't need the lock)
            if (CanGrantLock(request.TransactionId, resourceId, request.LockType))
            {
                queue.Dequeue();
                GrantLock(request.TransactionId, resourceId, request.LockType);
                request.CompletionSource.TrySetResult(LockResult.Granted);
            }
            else
            {
                // Cannot grant this request, stop processing
                // (queue ordering preserves fairness)
                break;
            }
        }
    }

    /// <summary>
    /// Checks if a lock can be granted without acquiring the write lock.
    /// Must be called while holding the write lock.
    /// </summary>
    private bool CanGrantLock(string transactionId, string resourceId, LockType lockType)
    {
        // Check if transaction already has a lock on this resource
        if (TryGetLockInfo(transactionId, resourceId, out var existingLock))
        {
            // Already have the requested lock type or better
            if (existingLock.LockType == LockType.Exclusive || lockType == LockType.Shared)
            {
                return true;
            }
            // Have shared, want exclusive - need to check if upgrade is possible
            return false;
        }

        // Check if any locks exist on this resource
        if (!_resourceLocks.TryGetValue(resourceId, out var existingLocks) || existingLocks.Count == 0)
        {
            // No locks exist, can acquire
            return true;
        }

        // Check compatibility with existing locks
        if (lockType == LockType.Shared)
        {
            // Shared locks are compatible with other shared locks
            if (existingLocks.All(l => l.LockType == LockType.Shared))
            {
                return true;
            }
        }

        // Exclusive lock or mixed with exclusive - cannot acquire
        return false;
    }

    private bool TryGetLockInfo(string transactionId, string resourceId, out LockInfo lockInfo)
    {
        lockInfo = null!;

        if (!_resourceLocks.TryGetValue(resourceId, out var locks))
        {
            return false;
        }

        lockInfo = locks.FirstOrDefault(l => l.TransactionId == transactionId)!;
        return lockInfo != null;
    }

    /// <summary>
    /// Checks if granting a lock would cause a deadlock using wait-for graph analysis
    /// </summary>
    private bool WouldCauseDeadlock(string transactionId, string resourceId)
    {
        _lock.EnterReadLock();
        try
        {
            // Build wait-for graph
            // Transaction A waits for Transaction B if:
            // - A is requesting a lock on resource R
            // - B holds a lock on resource R that conflicts with A's request

            var waitForGraph = new Dictionary<string, HashSet<string>>();

            // Add current request as a node
            if (!waitForGraph.ContainsKey(transactionId))
            {
                waitForGraph[transactionId] = new HashSet<string>();
            }

            // Find all transactions that hold locks on the requested resource
            if (_resourceLocks.TryGetValue(resourceId, out var holders))
            {
                foreach (var holder in holders)
                {
                    if (holder.TransactionId != transactionId)
                    {
                        waitForGraph[transactionId].Add(holder.TransactionId);
                    }
                }
            }

            // Add edges from waiting transactions
            foreach (var kvp in _waitingQueues)
            {
                var resId = kvp.Key;
                var waitingQueue = kvp.Value;

                foreach (var waitingRequest in waitingQueue)
                {
                    if (!waitForGraph.ContainsKey(waitingRequest.TransactionId))
                    {
                        waitForGraph[waitingRequest.TransactionId] = new HashSet<string>();
                    }

                    // This waiting transaction waits for holders of that resource
                    if (_resourceLocks.TryGetValue(resId, out var resourceHolders))
                    {
                        foreach (var holder in resourceHolders)
                        {
                            if (holder.TransactionId != waitingRequest.TransactionId)
                            {
                                waitForGraph[waitingRequest.TransactionId].Add(holder.TransactionId);
                            }
                        }
                    }
                }
            }

            // Check for cycle using DFS
            return HasCycle(waitForGraph, transactionId, new HashSet<string>(), new HashSet<string>());
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private bool HasCycle(Dictionary<string, HashSet<string>> graph, string node, HashSet<string> visited, HashSet<string> recursionStack)
    {
        visited.Add(node);
        recursionStack.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    if (HasCycle(graph, neighbor, visited, recursionStack))
                    {
                        return true;
                    }
                }
                else if (recursionStack.Contains(neighbor))
                {
                    return true;
                }
            }
        }

        recursionStack.Remove(node);
        return false;
    }

    /// <summary>
    /// Detects and resolves deadlocks by aborting a victim transaction
    /// </summary>
    private void DetectAndResolveDeadlocks()
    {
        _lock.EnterWriteLock();
        try
        {
            var waitForGraph = BuildWaitForGraph();
            var cycle = FindDeadlockCycle(waitForGraph);

            if (cycle.Count > 0)
            {
                // Choose victim (simple heuristic: youngest transaction)
                var victim = cycle
                    .Select(t => new { TransactionId = t, Info = GetTransactionInfo(t) })
                    .OrderByDescending(t => t.Info?.CreatedAt ?? DateTime.MinValue)
                    .First();

                // Release all locks held by victim
                ReleaseAllLocks(victim.TransactionId);

                // Notify waiting requests
                OnDeadlockDetected(new DeadlockEventArgs
                {
                    VictimTransactionId = victim.TransactionId,
                    InvolvedTransactions = cycle.AsReadOnly(),
                    ResourceId = string.Empty,
                    DetectedAt = DateTime.UtcNow
                });
            }
        }
        catch (Exception)
        {
            // Don't let deadlock detection crash the system
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private Dictionary<string, HashSet<string>> BuildWaitForGraph()
    {
        var graph = new Dictionary<string, HashSet<string>>();

        // Add edges from waiting transactions to lock holders
        foreach (var kvp in _waitingQueues)
        {
            var resourceId = kvp.Key;
            var queue = kvp.Value;

            foreach (var request in queue)
            {
                if (!graph.ContainsKey(request.TransactionId))
                {
                    graph[request.TransactionId] = new HashSet<string>();
                }

                // Find holders of this resource
                if (_resourceLocks.TryGetValue(resourceId, out var holders))
                {
                    foreach (var holder in holders)
                    {
                        if (holder.TransactionId != request.TransactionId &&
                            LocksConflict(request.LockType, holder.LockType))
                        {
                            graph[request.TransactionId].Add(holder.TransactionId);
                        }
                    }
                }
            }
        }

        return graph;
    }

    private bool LocksConflict(LockType requested, LockType held)
    {
        // Shared locks don't conflict with each other
        // All other combinations conflict
        return !(requested == LockType.Shared && held == LockType.Shared);
    }

    private List<string> FindDeadlockCycle(Dictionary<string, HashSet<string>> graph)
    {
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var path = new List<string>();

        foreach (var node in graph.Keys)
        {
            if (!visited.Contains(node))
            {
                var cycle = FindCycleFromNode(graph, node, visited, recursionStack, path, new HashSet<string>());
                if (cycle.Count > 0)
                {
                    return cycle;
                }
            }
        }

        return new List<string>();
    }

    private List<string> FindCycleFromNode(
        Dictionary<string, HashSet<string>> graph,
        string node,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> path,
        HashSet<string> pathSet)
    {
        visited.Add(node);
        recursionStack.Add(node);
        path.Add(node);
        pathSet.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    var cycle = FindCycleFromNode(graph, neighbor, visited, recursionStack, path, pathSet);
                    if (cycle.Count > 0)
                    {
                        return cycle;
                    }
                }
                else if (recursionStack.Contains(neighbor))
                {
                    // Found a cycle - extract it from the path
                    var cycleStart = path.IndexOf(neighbor);
                    return path.Skip(cycleStart).ToList();
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        pathSet.Remove(node);
        recursionStack.Remove(node);
        return new List<string>();
    }

    private TransactionInfo? GetTransactionInfo(string transactionId)
    {
        // This would normally query the transaction manager
        // For now, return null (victim selection will use other heuristics)
        return null;
    }

    private void OnDeadlockDetected(DeadlockEventArgs e)
    {
        DeadlockDetected?.Invoke(this, e);
    }

    private class TransactionInfo
    {
        public DateTime CreatedAt { get; set; }
    }

    #endregion
}
