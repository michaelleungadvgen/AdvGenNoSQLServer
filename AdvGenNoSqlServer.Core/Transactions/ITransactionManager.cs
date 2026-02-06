using AdvGenNoSqlServer.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Represents the status of a transaction
/// </summary>
public enum TransactionStatus
{
    Active,
    Committed,
    RolledBack,
    Failed
}

/// <summary>
/// Represents a transaction operation
/// </summary>
public class TransactionOperation
{
    public string OperationType { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public Document? Document { get; set; }
    public string? DocumentId { get; set; }
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// Interface for transaction management
/// </summary>
public interface ITransactionManager
{
    /// <summary>
    /// Begins a new transaction
    /// </summary>
    /// <returns>The transaction ID</returns>
    string BeginTransaction();

    /// <summary>
    /// Commits a transaction
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <returns>True if committed successfully, false otherwise</returns>
    bool CommitTransaction(string transactionId);

    /// <summary>
    /// Rolls back a transaction
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <returns>True if rolled back successfully, false otherwise</returns>
    bool RollbackTransaction(string transactionId);

    /// <summary>
    /// Gets the status of a transaction
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <returns>The transaction status</returns>
    TransactionStatus GetTransactionStatus(string transactionId);

    /// <summary>
    /// Adds an operation to a transaction
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <param name="operation">The operation to add</param>
    void AddOperation(string transactionId, TransactionOperation operation);

    /// <summary>
    /// Gets all operations for a transaction
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <returns>List of operations</returns>
    IEnumerable<TransactionOperation> GetOperations(string transactionId);
}