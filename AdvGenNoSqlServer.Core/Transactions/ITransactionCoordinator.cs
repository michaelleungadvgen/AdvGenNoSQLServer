// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Represents the isolation level for transactions
/// </summary>
public enum IsolationLevel
{
    /// <summary>
    /// Read uncommitted - allows dirty reads
    /// </summary>
    ReadUncommitted,

    /// <summary>
    /// Read committed - prevents dirty reads
    /// </summary>
    ReadCommitted,

    /// <summary>
    /// Repeatable read - prevents non-repeatable reads
    /// </summary>
    RepeatableRead,

    /// <summary>
    /// Serializable - prevents phantom reads, strictest isolation
    /// </summary>
    Serializable
}

/// <summary>
/// Represents the state of a transaction
/// </summary>
public enum TransactionState
{
    /// <summary>
    /// Transaction is active and can perform operations
    /// </summary>
    Active,

    /// <summary>
    /// Transaction is preparing to commit (two-phase commit)
    /// </summary>
    Preparing,

    /// <summary>
    /// Transaction has been committed
    /// </summary>
    Committed,

    /// <summary>
    /// Transaction is rolling back
    /// </summary>
    RollingBack,

    /// <summary>
    /// Transaction has been rolled back
    /// </summary>
    RolledBack,

    /// <summary>
    /// Transaction was aborted due to error or deadlock
    /// </summary>
    Aborted,

    /// <summary>
    /// Transaction failed due to error
    /// </summary>
    Failed
}

/// <summary>
/// Represents information about a transaction
/// </summary>
public class TransactionInfo
{
    /// <summary>
    /// The unique transaction ID
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// The current state of the transaction
    /// </summary>
    public TransactionState State { get; set; }

    /// <summary>
    /// The isolation level of the transaction
    /// </summary>
    public IsolationLevel IsolationLevel { get; set; }

    /// <summary>
    /// When the transaction was started
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// When the transaction will timeout (null if no timeout)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Number of operations performed in this transaction
    /// </summary>
    public int OperationCount { get; set; }

    /// <summary>
    /// The IDs of resources locked by this transaction
    /// </summary>
    public List<string> LockedResources { get; set; } = new();
}

/// <summary>
/// Represents a savepoint within a transaction for partial rollback
/// </summary>
public class Savepoint
{
    /// <summary>
    /// The name of the savepoint
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The LSN at which the savepoint was created
    /// </summary>
    public long Lsn { get; set; }

    /// <summary>
    /// The operation count at savepoint creation
    /// </summary>
    public int OperationCount { get; set; }

    /// <summary>
    /// When the savepoint was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Options for beginning a new transaction
/// </summary>
public class TransactionOptions
{
    /// <summary>
    /// The isolation level (default: ReadCommitted)
    /// </summary>
    public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    /// <summary>
    /// Transaction timeout (default: 30 seconds)
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to automatically rollback on dispose if not committed
    /// </summary>
    public bool AutoRollbackOnDispose { get; set; } = true;
}

/// <summary>
/// Interface for transaction context - represents an active transaction
/// </summary>
public interface ITransactionContext : IDisposable
{
    /// <summary>
    /// The unique transaction ID
    /// </summary>
    string TransactionId { get; }

    /// <summary>
    /// The current state of the transaction
    /// </summary>
    TransactionState State { get; }

    /// <summary>
    /// The isolation level of the transaction
    /// </summary>
    IsolationLevel IsolationLevel { get; }

    /// <summary>
    /// When the transaction was started
    /// </summary>
    DateTime StartedAt { get; }

    /// <summary>
    /// The number of operations performed in this transaction
    /// </summary>
    int OperationCount { get; }

    /// <summary>
    /// Whether to automatically rollback on dispose if not committed
    /// </summary>
    bool AutoRollbackOnDispose { get; }

    /// <summary>
    /// Commits the transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if committed successfully</returns>
    Task<bool> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the entire transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if rolled back successfully</returns>
    Task<bool> RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a savepoint for partial rollback
    /// </summary>
    /// <param name="name">The savepoint name</param>
    /// <returns>True if savepoint was created</returns>
    Task<bool> SavepointAsync(string name);

    /// <summary>
    /// Rolls back to a savepoint
    /// </summary>
    /// <param name="name">The savepoint name</param>
    /// <returns>True if rolled back to savepoint</returns>
    Task<bool> RollbackToSavepointAsync(string name);

    /// <summary>
    /// Records a read operation for conflict detection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    Task RecordReadAsync(string collectionName, string documentId);

    /// <summary>
    /// Records a write operation for conflict detection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="beforeImage">Document before modification</param>
    /// <param name="afterImage">Document after modification</param>
    Task RecordWriteAsync(string collectionName, string documentId, Document? beforeImage, Document? afterImage);
}

/// <summary>
/// Interface for coordinating transactions across the system
/// </summary>
public interface ITransactionCoordinator : IDisposable
{
    /// <summary>
    /// Gets the number of active transactions
    /// </summary>
    int ActiveTransactionCount { get; }

    /// <summary>
    /// Begins a new transaction
    /// </summary>
    /// <param name="options">Transaction options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A transaction context</returns>
    Task<ITransactionContext> BeginTransactionAsync(TransactionOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a transaction
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if committed successfully</returns>
    Task<bool> CommitAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back a transaction
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if rolled back successfully</returns>
    Task<bool> RollbackAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a transaction
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <returns>Transaction information, or null if not found</returns>
    TransactionInfo? GetTransactionInfo(string transactionId);

    /// <summary>
    /// Gets all active transactions
    /// </summary>
    /// <returns>List of active transaction information</returns>
    IReadOnlyList<TransactionInfo> GetActiveTransactions();

    /// <summary>
    /// Aborts a transaction (used for deadlock resolution)
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <param name="reason">The reason for abort</param>
    /// <returns>True if aborted successfully</returns>
    Task<bool> AbortAsync(string transactionId, string reason);

    /// <summary>
    /// Event raised when a transaction is committed
    /// </summary>
    event EventHandler<TransactionEventArgs>? TransactionCommitted;

    /// <summary>
    /// Event raised when a transaction is rolled back
    /// </summary>
    event EventHandler<TransactionEventArgs>? TransactionRolledBack;

    /// <summary>
    /// Event raised when a transaction is aborted
    /// </summary>
    event EventHandler<TransactionAbortedEventArgs>? TransactionAborted;
}

/// <summary>
/// Event args for transaction events
/// </summary>
public class TransactionEventArgs : EventArgs
{
    /// <summary>
    /// The transaction ID
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// The isolation level
    /// </summary>
    public IsolationLevel IsolationLevel { get; set; }

    /// <summary>
    /// How long the transaction was active
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Number of operations in the transaction
    /// </summary>
    public int OperationCount { get; set; }

    /// <summary>
    /// When the event occurred
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Event args for transaction aborted events
/// </summary>
public class TransactionAbortedEventArgs : TransactionEventArgs
{
    /// <summary>
    /// The reason for abort
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
