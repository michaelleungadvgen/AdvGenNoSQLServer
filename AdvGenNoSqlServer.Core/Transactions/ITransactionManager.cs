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
    /// <summary>Transaction is in progress and accepting operations.</summary>
    Active,

    /// <summary>Transaction was committed successfully.</summary>
    Committed,

    /// <summary>Transaction was rolled back.</summary>
    RolledBack,

    /// <summary>Transaction failed or expired.</summary>
    Failed
}

/// <summary>
/// Represents a transaction operation
/// </summary>
public class TransactionOperation
{
    /// <summary>
    /// Gets or sets the operation type (e.g., INSERT, UPDATE, DELETE)
    /// </summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target collection name
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the document involved in the operation, if any
    /// </summary>
    public Document? Document { get; set; }

    /// <summary>
    /// Gets or sets the target document identifier, if any
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// Gets or sets additional data for the operation, if any
    /// </summary>
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