using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Implementation of the transaction manager
/// </summary>
public class TransactionManager : ITransactionManager
{
    private readonly ConcurrentDictionary<string, TransactionInfo> _transactions;
    private readonly object _lock = new object();

    public TransactionManager()
    {
        _transactions = new ConcurrentDictionary<string, TransactionInfo>();
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