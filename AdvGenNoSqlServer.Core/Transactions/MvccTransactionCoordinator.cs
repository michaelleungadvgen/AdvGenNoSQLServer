// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Transaction coordinator that uses MVCC for Serializable isolation.
/// Falls back to locking-based coordinator for lower isolation levels.
/// </summary>
public class MvccTransactionCoordinator : ITransactionCoordinator, IDisposable
{
    private readonly IMvccStore _mvccStore;
    private readonly IWriteAheadLog _writeAheadLog;
    private readonly ConcurrentDictionary<string, MvccTransactionContext> _activeTransactions;
    private readonly VersionGarbageCollector _garbageCollector;
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(30);
    private int _transactionSequence;
    private bool _disposed;

    /// <summary>
    /// Gets the number of active transactions
    /// </summary>
    public int ActiveTransactionCount => _activeTransactions.Count;

    /// <summary>
    /// Event raised when a transaction is committed
    /// </summary>
    public event EventHandler<TransactionEventArgs>? TransactionCommitted;

    /// <summary>
    /// Event raised when a transaction is rolled back
    /// </summary>
    public event EventHandler<TransactionEventArgs>? TransactionRolledBack;

    /// <summary>
    /// Event raised when a transaction is aborted
    /// </summary>
    public event EventHandler<TransactionAbortedEventArgs>? TransactionAborted;

    /// <summary>
    /// Creates a new MVCC transaction coordinator
    /// </summary>
    public MvccTransactionCoordinator(IMvccStore mvccStore, IWriteAheadLog writeAheadLog)
    {
        _mvccStore = mvccStore ?? throw new ArgumentNullException(nameof(mvccStore));
        _writeAheadLog = writeAheadLog ?? throw new ArgumentNullException(nameof(writeAheadLog));
        _activeTransactions = new ConcurrentDictionary<string, MvccTransactionContext>();
        _garbageCollector = new VersionGarbageCollector(mvccStore);

        // Setup cleanup timer for timed-out transactions
        _cleanupTimer = new Timer(
            CleanupTimedOutTransactions,
            null,
            _cleanupInterval,
            _cleanupInterval);
    }

    /// <summary>
    /// Begins a new transaction with MVCC support
    /// </summary>
    public Task<ITransactionContext> BeginTransactionAsync(
        TransactionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        options ??= new TransactionOptions();

        // Generate transaction ID
        var transactionId = GenerateTransactionId();

        // Get list of active transactions for snapshot
        var activeTransactionIds = _activeTransactions.Keys.ToList();

        // Create MVCC context
        var context = new MvccTransactionContext(
            transactionId,
            options,
            _mvccStore,
            _writeAheadLog,
            activeTransactionIds);

        // Register active transaction
        if (!_activeTransactions.TryAdd(transactionId, context))
        {
            throw new InvalidOperationException($"Transaction {transactionId} already exists");
        }

        // Write begin transaction to WAL
        return _writeAheadLog.AppendBeginTransactionAsync(transactionId)
            .ContinueWith(_ => (ITransactionContext)context, TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Commits a transaction
    /// </summary>
    public async Task<bool> CommitAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_activeTransactions.TryGetValue(transactionId, out var context))
            return false;

        var startTime = context.StartedAt;
        var operationCount = context.OperationCount;
        var isolationLevel = context.IsolationLevel;

        try
        {
            var committed = await context.CommitAsync(cancellationToken);

            if (committed)
            {
                _activeTransactions.TryRemove(transactionId, out _);

                OnTransactionCommitted(new TransactionEventArgs
                {
                    TransactionId = transactionId,
                    IsolationLevel = isolationLevel,
                    Duration = DateTime.UtcNow - startTime,
                    OperationCount = operationCount,
                    Timestamp = DateTime.UtcNow
                });

                // Trigger garbage collection
                _garbageCollector.Collect(GetOldestActiveTimestamp());
            }

            return committed;
        }
        catch (Exception)
        {
            _activeTransactions.TryRemove(transactionId, out _);
            throw;
        }
    }

    /// <summary>
    /// Rolls back a transaction
    /// </summary>
    public async Task<bool> RollbackAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_activeTransactions.TryGetValue(transactionId, out var context))
            return false;

        var startTime = context.StartedAt;
        var operationCount = context.OperationCount;
        var isolationLevel = context.IsolationLevel;

        try
        {
            var rolledBack = await context.RollbackAsync(cancellationToken);

            if (rolledBack)
            {
                _activeTransactions.TryRemove(transactionId, out _);

                OnTransactionRolledBack(new TransactionEventArgs
                {
                    TransactionId = transactionId,
                    IsolationLevel = isolationLevel,
                    Duration = DateTime.UtcNow - startTime,
                    OperationCount = operationCount,
                    Timestamp = DateTime.UtcNow
                });
            }

            return rolledBack;
        }
        catch (Exception)
        {
            _activeTransactions.TryRemove(transactionId, out _);
            throw;
        }
    }

    /// <summary>
    /// Gets information about a transaction
    /// </summary>
    public TransactionInfo? GetTransactionInfo(string transactionId)
    {
        ThrowIfDisposed();

        if (!_activeTransactions.TryGetValue(transactionId, out var context))
            return null;

        return new TransactionInfo
        {
            TransactionId = context.TransactionId,
            State = context.State,
            IsolationLevel = context.IsolationLevel,
            StartedAt = context.StartedAt,
            ExpiresAt = context.ExpiresAt,
            OperationCount = context.OperationCount,
            LockedResources = new List<string>() // MVCC doesn't use traditional locks
        };
    }

    /// <summary>
    /// Gets all active transactions
    /// </summary>
    public IReadOnlyList<TransactionInfo> GetActiveTransactions()
    {
        ThrowIfDisposed();

        return _activeTransactions.Values.Select(context => new TransactionInfo
        {
            TransactionId = context.TransactionId,
            State = context.State,
            IsolationLevel = context.IsolationLevel,
            StartedAt = context.StartedAt,
            ExpiresAt = context.ExpiresAt,
            OperationCount = context.OperationCount,
            LockedResources = new List<string>()
        }).ToList();
    }

    /// <summary>
    /// Aborts a transaction
    /// </summary>
    public async Task<bool> AbortAsync(string transactionId, string reason)
    {
        ThrowIfDisposed();

        if (!_activeTransactions.TryGetValue(transactionId, out var context))
            return false;

        var startTime = context.StartedAt;
        var operationCount = context.OperationCount;
        var isolationLevel = context.IsolationLevel;

        try
        {
            // Use reflection or cast to access internal method
            if (context is MvccTransactionContext mvccContext)
            {
                await mvccContext.RollbackAsync();
            }

            _activeTransactions.TryRemove(transactionId, out _);

            OnTransactionAborted(new TransactionAbortedEventArgs
            {
                TransactionId = transactionId,
                IsolationLevel = isolationLevel,
                Duration = DateTime.UtcNow - startTime,
                OperationCount = operationCount,
                Timestamp = DateTime.UtcNow,
                Reason = reason
            });

            return true;
        }
        catch (Exception)
        {
            _activeTransactions.TryRemove(transactionId, out _);
            throw;
        }
    }

    /// <summary>
    /// Gets MVCC statistics
    /// </summary>
    public MvccStatistics GetStatistics()
    {
        var oldestTimestamp = GetOldestActiveTimestamp();
        var gcStats = _garbageCollector.GetStatistics();

        return new MvccStatistics
        {
            ActiveTransactionCount = _activeTransactions.Count,
            OldestActiveTransactionTimestamp = oldestTimestamp,
            TotalVersionsCollected = gcStats.TotalVersionsCollected,
            LastCollectionTime = gcStats.LastCollectionTime
        };
    }

    private long GetOldestActiveTimestamp()
    {
        if (_activeTransactions.IsEmpty)
            return MvccTimestamp.Current();

        return _activeTransactions.Values
            .Min(t => t.Snapshot.ReadTimestamp);
    }

    private string GenerateTransactionId()
    {
        var sequence = Interlocked.Increment(ref _transactionSequence);
        return $"mvcc_txn_{DateTime.UtcNow:yyyyMMddHHmmss}_{sequence:D8}_{Guid.NewGuid():N}";
    }

    private void CleanupTimedOutTransactions(object? state)
    {
        var timedOutTransactions = _activeTransactions
            .Where(t => t.Value.IsTimedOut())
            .Select(t => t.Key)
            .ToList();

        foreach (var transactionId in timedOutTransactions)
        {
            try
            {
                _ = AbortAsync(transactionId, "Transaction timed out");
            }
            catch (Exception)
            {
                // Ignore errors during cleanup
            }
        }
    }

    private void OnTransactionCommitted(TransactionEventArgs e)
    {
        TransactionCommitted?.Invoke(this, e);
    }

    private void OnTransactionRolledBack(TransactionEventArgs e)
    {
        TransactionRolledBack?.Invoke(this, e);
    }

    private void OnTransactionAborted(TransactionAbortedEventArgs e)
    {
        TransactionAborted?.Invoke(this, e);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MvccTransactionCoordinator));
    }

    /// <summary>
    /// Disposes the coordinator
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _cleanupTimer?.Dispose();

        // Abort all active transactions
        foreach (var transactionId in _activeTransactions.Keys.ToList())
        {
            try
            {
                AbortAsync(transactionId, "Coordinator shutting down").Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // Ignore errors during disposal
            }
        }

        _activeTransactions.Clear();
        _garbageCollector?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Statistics for MVCC operations
/// </summary>
public class MvccStatistics
{
    public int ActiveTransactionCount { get; set; }
    public long OldestActiveTransactionTimestamp { get; set; }
    public int TotalVersionsCollected { get; set; }
    public DateTime? LastCollectionTime { get; set; }
}
