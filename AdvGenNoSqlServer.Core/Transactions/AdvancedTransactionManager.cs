using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Advanced transaction manager with expiration and cleanup functionality
/// </summary>
public class AdvancedTransactionManager : ITransactionManager, IDisposable
{
    private readonly ConcurrentDictionary<string, TransactionInfo> _transactions;
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _transactionTimeout;
    private readonly object _lock = new object();
    private bool _disposed = false;

    public AdvancedTransactionManager(TimeSpan? transactionTimeout = null)
    {
        _transactions = new ConcurrentDictionary<string, TransactionInfo>();
        _transactionTimeout = transactionTimeout ?? TimeSpan.FromMinutes(30);
        _cleanupTimer = new Timer(CleanupExpiredTransactions, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public string BeginTransaction()
    {
        var transactionId = Guid.NewGuid().ToString();
        var transactionInfo = new TransactionInfo
        {
            Id = transactionId,
            Status = TransactionStatus.Active,
            Operations = new List<TransactionOperation>(),
            CreatedAt = DateTime.UtcNow
        };

        _transactions.TryAdd(transactionId, transactionInfo);
        return transactionId;
    }

    public bool CommitTransaction(string transactionId)
    {
        if (!_transactions.TryGetValue(transactionId, out var transactionInfo))
        {
            return false;
        }

        lock (_lock)
        {
            if (transactionInfo.Status != TransactionStatus.Active)
            {
                return false;
            }

            if (DateTime.UtcNow - transactionInfo.CreatedAt > _transactionTimeout)
            {
                transactionInfo.Status = TransactionStatus.Failed;
                return false;
            }

            // In a real implementation, we would actually commit the operations here
            // For now, we'll just mark the transaction as committed
            transactionInfo.Status = TransactionStatus.Committed;
            transactionInfo.CommittedAt = DateTime.UtcNow;
            return true;
        }
    }

    public bool RollbackTransaction(string transactionId)
    {
        if (!_transactions.TryGetValue(transactionId, out var transactionInfo))
        {
            return false;
        }

        lock (_lock)
        {
            if (transactionInfo.Status != TransactionStatus.Active)
            {
                return false;
            }

            // In a real implementation, we would actually rollback the operations here
            // For now, we'll just mark the transaction as rolled back
            transactionInfo.Status = TransactionStatus.RolledBack;
            transactionInfo.RolledBackAt = DateTime.UtcNow;
            return true;
        }
    }

    public TransactionStatus GetTransactionStatus(string transactionId)
    {
        if (!_transactions.TryGetValue(transactionId, out var transactionInfo))
        {
            return TransactionStatus.Failed;
        }

        // Check if transaction has expired
        if (transactionInfo.Status == TransactionStatus.Active &&
            DateTime.UtcNow - transactionInfo.CreatedAt > _transactionTimeout)
        {
            lock (_lock)
            {
                if (transactionInfo.Status == TransactionStatus.Active)
                {
                    transactionInfo.Status = TransactionStatus.Failed;
                }
            }
        }

        return transactionInfo.Status;
    }

    public void AddOperation(string transactionId, TransactionOperation operation)
    {
        if (!_transactions.TryGetValue(transactionId, out var transactionInfo))
        {
            throw new InvalidOperationException($"Transaction {transactionId} not found");
        }

        lock (_lock)
        {
            if (transactionInfo.Status != TransactionStatus.Active)
            {
                throw new InvalidOperationException($"Cannot add operations to a {transactionInfo.Status} transaction");
            }

            if (DateTime.UtcNow - transactionInfo.CreatedAt > _transactionTimeout)
            {
                transactionInfo.Status = TransactionStatus.Failed;
                throw new InvalidOperationException($"Transaction {transactionId} has expired");
            }

            transactionInfo.Operations.Add(operation);
        }
    }

    public IEnumerable<TransactionOperation> GetOperations(string transactionId)
    {
        if (!_transactions.TryGetValue(transactionId, out var transactionInfo))
        {
            return Enumerable.Empty<TransactionOperation>();
        }

        return transactionInfo.Operations.AsReadOnly();
    }

    private void CleanupExpiredTransactions(object? state)
    {
        var now = DateTime.UtcNow;
        var expiredTransactions = _transactions
            .Where(kvp => kvp.Value.Status == TransactionStatus.Active &&
                         now - kvp.Value.CreatedAt > _transactionTimeout)
            .ToList();

        foreach (var kvp in expiredTransactions)
        {
            lock (_lock)
            {
                if (kvp.Value.Status == TransactionStatus.Active)
                {
                    kvp.Value.Status = TransactionStatus.Failed;
                }
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _cleanupTimer?.Dispose();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Internal class to hold transaction information
    /// </summary>
    private class TransactionInfo
    {
        public string Id { get; set; } = string.Empty;
        public TransactionStatus Status { get; set; }
        public List<TransactionOperation> Operations { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime? CommittedAt { get; set; }
        public DateTime? RolledBackAt { get; set; }
    }
}