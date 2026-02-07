// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Coordinates transactions across the system using Two-Phase Commit protocol
/// </summary>
public class TransactionCoordinator : ITransactionCoordinator
{
    private readonly IWriteAheadLog _writeAheadLog;
    private readonly ILockManager _lockManager;
    private readonly ConcurrentDictionary<string, TransactionContext> _activeTransactions;
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
    /// Creates a new transaction coordinator
    /// </summary>
    public TransactionCoordinator(IWriteAheadLog writeAheadLog, ILockManager lockManager)
    {
        _writeAheadLog = writeAheadLog ?? throw new ArgumentNullException(nameof(writeAheadLog));
        _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
        _activeTransactions = new ConcurrentDictionary<string, TransactionContext>();
        _transactionSequence = 0;

        // Setup cleanup timer for timed-out transactions
        _cleanupTimer = new Timer(
            CleanupTimedOutTransactions,
            null,
            _cleanupInterval,
            _cleanupInterval);

        // Subscribe to deadlock events
        _lockManager.DeadlockDetected += OnDeadlockDetected;
    }

    /// <summary>
    /// Begins a new transaction
    /// </summary>
    public async Task<ITransactionContext> BeginTransactionAsync(
        TransactionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        options ??= new TransactionOptions();

        // Generate transaction ID
        var transactionId = GenerateTransactionId();

        // Write begin transaction to WAL
        await _writeAheadLog.AppendBeginTransactionAsync(transactionId);

        // Create transaction context
        var context = new TransactionContext(
            transactionId,
            options,
            this,
            _writeAheadLog,
            _lockManager);

        // Register active transaction
        if (!_activeTransactions.TryAdd(transactionId, context))
        {
            throw new InvalidOperationException($"Transaction {transactionId} already exists");
        }

        return context;
    }

    /// <summary>
    /// Commits a transaction
    /// </summary>
    public async Task<bool> CommitAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_activeTransactions.TryGetValue(transactionId, out var context))
        {
            return false;
        }

        var startTime = context.StartedAt;
        var operationCount = context.OperationCount;
        var isolationLevel = context.IsolationLevel;

        try
        {
            // Perform two-phase commit
            var committed = await context.CommitAsync(cancellationToken);

            if (committed)
            {
                // Remove from active transactions
                _activeTransactions.TryRemove(transactionId, out _);

                // Raise event
                OnTransactionCommitted(new TransactionEventArgs
                {
                    TransactionId = transactionId,
                    IsolationLevel = isolationLevel,
                    Duration = DateTime.UtcNow - startTime,
                    OperationCount = operationCount,
                    Timestamp = DateTime.UtcNow
                });
            }

            return committed;
        }
        catch (Exception)
        {
            // Ensure transaction is removed on error
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
        {
            return false; // Transaction not found or already completed
        }

        var startTime = context.StartedAt;
        var operationCount = context.OperationCount;
        var isolationLevel = context.IsolationLevel;

        try
        {
            var rolledBack = await context.RollbackAsync(cancellationToken);

            if (rolledBack)
            {
                // Remove from active transactions
                _activeTransactions.TryRemove(transactionId, out _);

                // Raise event
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
            // Ensure transaction is removed on error
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
        {
            return null;
        }

        return new TransactionInfo
        {
            TransactionId = context.TransactionId,
            State = context.State,
            IsolationLevel = context.IsolationLevel,
            StartedAt = context.StartedAt,
            ExpiresAt = context.ExpiresAt,
            OperationCount = context.OperationCount,
            LockedResources = context.GetLockedResources().ToList()
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
            LockedResources = context.GetLockedResources().ToList()
        }).ToList();
    }

    /// <summary>
    /// Aborts a transaction (used for deadlock resolution)
    /// </summary>
    public async Task<bool> AbortAsync(string transactionId, string reason)
    {
        ThrowIfDisposed();

        if (!_activeTransactions.TryGetValue(transactionId, out var context))
        {
            return false;
        }

        var startTime = context.StartedAt;
        var operationCount = context.OperationCount;
        var isolationLevel = context.IsolationLevel;

        try
        {
            await context.AbortAsync(reason);

            // Remove from active transactions
            _activeTransactions.TryRemove(transactionId, out _);

            // Raise event
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
            // Ensure transaction is removed on error
            _activeTransactions.TryRemove(transactionId, out _);
            throw;
        }
    }

    /// <summary>
    /// Generates a unique transaction ID
    /// </summary>
    private string GenerateTransactionId()
    {
        var sequence = Interlocked.Increment(ref _transactionSequence);
        return $"txn_{DateTime.UtcNow:yyyyMMddHHmmss}_{sequence:D8}_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Handles deadlock detection events from the lock manager
    /// </summary>
    private async void OnDeadlockDetected(object? sender, DeadlockEventArgs e)
    {
        // Abort the victim transaction
        if (!string.IsNullOrEmpty(e.VictimTransactionId))
        {
            await AbortAsync(e.VictimTransactionId, $"Deadlock detected, victim selected");
        }
    }

    /// <summary>
    /// Cleans up timed-out transactions
    /// </summary>
    private async void CleanupTimedOutTransactions(object? state)
    {
        var timedOutTransactions = _activeTransactions
            .Where(t => t.Value.IsTimedOut())
            .Select(t => t.Key)
            .ToList();

        foreach (var transactionId in timedOutTransactions)
        {
            try
            {
                await AbortAsync(transactionId, "Transaction timed out");
            }
            catch (Exception)
            {
                // Ignore errors during cleanup
            }
        }
    }

    /// <summary>
    /// Raises the TransactionCommitted event
    /// </summary>
    protected virtual void OnTransactionCommitted(TransactionEventArgs e)
    {
        TransactionCommitted?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the TransactionRolledBack event
    /// </summary>
    protected virtual void OnTransactionRolledBack(TransactionEventArgs e)
    {
        TransactionRolledBack?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the TransactionAborted event
    /// </summary>
    protected virtual void OnTransactionAborted(TransactionAbortedEventArgs e)
    {
        TransactionAborted?.Invoke(this, e);
    }

    /// <summary>
    /// Disposes the transaction coordinator
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
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TransactionCoordinator));
        }
    }
}
