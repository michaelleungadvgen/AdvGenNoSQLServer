// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Implementation of transaction context for active transactions
/// </summary>
public class TransactionContext : ITransactionContext
{
    private readonly TransactionCoordinator _coordinator;
    private readonly IWriteAheadLog _writeAheadLog;
    private readonly ILockManager _lockManager;
    private readonly ConcurrentDictionary<string, Savepoint> _savepoints;
    private readonly ConcurrentDictionary<string, Document> _readSet;
    private readonly ConcurrentDictionary<string, Document> _writeSet;
    private readonly object _stateLock = new();
    private int _operationCount;
    private bool _isCommittedOrRolledBack;
    private bool _disposed;

    /// <summary>
    /// The unique transaction ID
    /// </summary>
    public string TransactionId { get; }

    /// <summary>
    /// The current state of the transaction
    /// </summary>
    public TransactionState State { get; private set; }

    /// <summary>
    /// The isolation level of the transaction
    /// </summary>
    public IsolationLevel IsolationLevel { get; }

    /// <summary>
    /// When the transaction was started
    /// </summary>
    public DateTime StartedAt { get; }

    /// <summary>
    /// When the transaction will timeout
    /// </summary>
    public DateTime? ExpiresAt { get; }

    /// <summary>
    /// Whether to automatically rollback on dispose
    /// </summary>
    public bool AutoRollbackOnDispose { get; }

    /// <summary>
    /// The number of operations performed
    /// </summary>
    public int OperationCount => _operationCount;

    /// <summary>
    /// Creates a new transaction context
    /// </summary>
    internal TransactionContext(
        string transactionId,
        TransactionOptions options,
        TransactionCoordinator coordinator,
        IWriteAheadLog writeAheadLog,
        ILockManager lockManager)
    {
        TransactionId = transactionId;
        IsolationLevel = options.IsolationLevel;
        AutoRollbackOnDispose = options.AutoRollbackOnDispose;
        StartedAt = DateTime.UtcNow;
        ExpiresAt = options.Timeout > TimeSpan.Zero ? StartedAt.Add(options.Timeout) : null;
        State = TransactionState.Active;

        _coordinator = coordinator;
        _writeAheadLog = writeAheadLog;
        _lockManager = lockManager;
        _savepoints = new ConcurrentDictionary<string, Savepoint>();
        _readSet = new ConcurrentDictionary<string, Document>();
        _writeSet = new ConcurrentDictionary<string, Document>();
        _operationCount = 0;
    }

    /// <summary>
    /// Commits the transaction
    /// </summary>
    public async Task<bool> CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (State != TransactionState.Active)
            {
                throw new InvalidOperationException($"Cannot commit transaction in {State} state");
            }
            State = TransactionState.Preparing;
        }

        try
        {
            // Write commit record to WAL
            await _writeAheadLog.AppendCommitAsync(TransactionId);

            lock (_stateLock)
            {
                State = TransactionState.Committed;
                _isCommittedOrRolledBack = true;
            }

            // Release all locks
            ReleaseAllLocks();

            return true;
        }
        catch (Exception)
        {
            lock (_stateLock)
            {
                State = TransactionState.Failed;
            }
            throw;
        }
    }

    /// <summary>
    /// Rolls back the entire transaction
    /// </summary>
    public async Task<bool> RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (State != TransactionState.Active && State != TransactionState.Preparing)
            {
                return false; // Already committed or rolled back
            }
            State = TransactionState.RollingBack;
        }

        try
        {
            // Write rollback record to WAL
            await _writeAheadLog.AppendRollbackAsync(TransactionId);

            lock (_stateLock)
            {
                State = TransactionState.RolledBack;
                _isCommittedOrRolledBack = true;
            }

            // Release all locks
            ReleaseAllLocks();

            return true;
        }
        catch (Exception)
        {
            lock (_stateLock)
            {
                State = TransactionState.Failed;
            }
            throw;
        }
    }

    /// <summary>
    /// Creates a savepoint for partial rollback
    /// </summary>
    public Task<bool> SavepointAsync(string name)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Savepoint name cannot be empty", nameof(name));
        }

        lock (_stateLock)
        {
            if (State != TransactionState.Active)
            {
                throw new InvalidOperationException($"Cannot create savepoint in {State} state");
            }
        }

        var savepoint = new Savepoint
        {
            Name = name,
            Lsn = _writeAheadLog.CurrentLsn,
            OperationCount = _operationCount,
            CreatedAt = DateTime.UtcNow
        };

        _savepoints[name] = savepoint;
        return Task.FromResult(true);
    }

    /// <summary>
    /// Rolls back to a savepoint
    /// </summary>
    public Task<bool> RollbackToSavepointAsync(string name)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Savepoint name cannot be empty", nameof(name));
        }

        lock (_stateLock)
        {
            if (State != TransactionState.Active)
            {
                throw new InvalidOperationException($"Cannot rollback to savepoint in {State} state");
            }
        }

        if (!_savepoints.TryGetValue(name, out var savepoint))
        {
            throw new InvalidOperationException($"Savepoint '{name}' not found");
        }

        // Remove all savepoints created after this one
        var savepointsToRemove = _savepoints
            .Where(s => s.Value.CreatedAt > savepoint.CreatedAt)
            .Select(s => s.Key)
            .ToList();

        foreach (var key in savepointsToRemove)
        {
            _savepoints.TryRemove(key, out _);
        }

        // Reset operation count
        _operationCount = savepoint.OperationCount;

        return Task.FromResult(true);
    }

    /// <summary>
    /// Records a read operation for conflict detection
    /// </summary>
    public Task RecordReadAsync(string collectionName, string documentId)
    {
        ThrowIfDisposed();

        if (IsolationLevel >= IsolationLevel.RepeatableRead)
        {
            var key = $"{collectionName}:{documentId}";
            _readSet.TryAdd(key, new Document { Id = documentId });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Records a write operation for conflict detection
    /// </summary>
    public async Task RecordWriteAsync(string collectionName, string documentId, Document? beforeImage, Document? afterImage)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (State != TransactionState.Active)
            {
                throw new InvalidOperationException($"Cannot perform write in {State} state");
            }
        }

        var key = $"{collectionName}:{documentId}";

        // Acquire appropriate lock based on isolation level
        var lockType = IsolationLevel == IsolationLevel.Serializable ? LockType.Exclusive : LockType.Shared;
        var lockResult = await _lockManager.AcquireLockAsync(TransactionId, key, lockType);

        if (lockResult == LockResult.DeadlockDetected)
        {
            throw new DeadlockException($"Deadlock detected while accessing {key}", TransactionId, key);
        }

        if (lockResult != LockResult.Granted)
        {
            throw new TransactionTimeoutException($"Could not acquire lock on {key} within timeout");
        }

        // Record in write set
        if (afterImage != null)
        {
            _writeSet[key] = afterImage;
        }

        // Write to WAL
        if (beforeImage == null && afterImage != null)
        {
            // Insert
            await _writeAheadLog.AppendInsertAsync(TransactionId, collectionName, afterImage);
        }
        else if (beforeImage != null && afterImage != null)
        {
            // Update
            await _writeAheadLog.AppendUpdateAsync(TransactionId, collectionName, beforeImage, afterImage);
        }
        else if (beforeImage != null && afterImage == null)
        {
            // Delete
            await _writeAheadLog.AppendDeleteAsync(TransactionId, collectionName, beforeImage);
        }

        Interlocked.Increment(ref _operationCount);
    }

    /// <summary>
    /// Gets the locked resources
    /// </summary>
    internal IReadOnlyList<string> GetLockedResources()
    {
        return _lockManager.GetTransactionLocks(TransactionId)
            .Select(l => l.ResourceId)
            .ToList();
    }

    /// <summary>
    /// Aborts the transaction
    /// </summary>
    internal async Task AbortAsync(string reason)
    {
        lock (_stateLock)
        {
            if (State != TransactionState.Active && State != TransactionState.Preparing)
            {
                return;
            }
            State = TransactionState.Aborted;
        }

        try
        {
            await _writeAheadLog.AppendRollbackAsync(TransactionId);
        }
        finally
        {
            ReleaseAllLocks();
        }
    }

    /// <summary>
    /// Checks if the transaction has timed out
    /// </summary>
    internal bool IsTimedOut()
    {
        return ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    }

    /// <summary>
    /// Releases all locks held by this transaction
    /// </summary>
    private void ReleaseAllLocks()
    {
        _lockManager.ReleaseAllLocks(TransactionId);
    }

    /// <summary>
    /// Disposes the transaction context
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        if (AutoRollbackOnDispose && State == TransactionState.Active && !_isCommittedOrRolledBack)
        {
            // Fire and forget rollback - don't block dispose
            _ = RollbackAsync();
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TransactionContext));
        }
    }
}

/// <summary>
/// Exception thrown when a transaction times out
/// </summary>
public class TransactionTimeoutException : Exception
{
    public TransactionTimeoutException(string message) : base(message) { }
    public TransactionTimeoutException(string message, Exception innerException) : base(message, innerException) { }
}
