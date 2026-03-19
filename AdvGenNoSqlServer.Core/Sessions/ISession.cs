// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Transactions;

namespace AdvGenNoSqlServer.Core.Sessions;

/// <summary>
/// Represents the state of a session
/// </summary>
public enum SessionState
{
    /// <summary>
    /// Session is open but no transaction has been started
    /// </summary>
    Open,

    /// <summary>
    /// Session has an active transaction in progress
    /// </summary>
    Active,

    /// <summary>
    /// Changes have been committed
    /// </summary>
    Committed,

    /// <summary>
    /// Changes have been rolled back
    /// </summary>
    RolledBack,

    /// <summary>
    /// Session has been disposed
    /// </summary>
    Disposed
}

/// <summary>
/// Configuration options for a session
/// </summary>
public class SessionOptions
{
    /// <summary>
    /// The default isolation level for transactions in this session
    /// </summary>
    public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    /// <summary>
    /// Timeout for transactions in milliseconds (0 = no timeout)
    /// </summary>
    public int TransactionTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Automatically begin a transaction when the session is created
    /// </summary>
    public bool AutoBeginTransaction { get; set; } = true;

    /// <summary>
    /// Automatically commit changes when the session is disposed (if not already committed/rolled back)
    /// </summary>
    public bool AutoCommitOnDispose { get; set; } = false;

    /// <summary>
    /// Enable automatic change tracking for documents loaded through this session
    /// </summary>
    public bool EnableChangeTracking { get; set; } = true;

    /// <summary>
    /// Throw an exception if a document with the same key is already being tracked
    /// </summary>
    public bool ThrowOnDuplicateTracking { get; set; } = false;

    /// <summary>
    /// Default session options with conservative settings
    /// </summary>
    public static SessionOptions Default => new();

    /// <summary>
    /// Session options optimized for read-only operations
    /// </summary>
    public static SessionOptions ReadOnly => new()
    {
        AutoBeginTransaction = false,
        EnableChangeTracking = false
    };

    /// <summary>
    /// Session options with auto-commit enabled
    /// </summary>
    public static SessionOptions AutoCommit => new()
    {
        AutoCommitOnDispose = true
    };
}

/// <summary>
/// Represents a session with the database following the Unit of Work pattern.
/// Sessions provide change tracking and transaction management for database operations.
/// </summary>
public interface ISession : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// The unique identifier for this session
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// The current state of the session
    /// </summary>
    SessionState State { get; }

    /// <summary>
    /// The options used to configure this session
    /// </summary>
    SessionOptions Options { get; }

    /// <summary>
    /// The change tracker for this session
    /// </summary>
    IChangeTracker ChangeTracker { get; }

    /// <summary>
    /// Gets the current transaction ID if a transaction is active, null otherwise
    /// </summary>
    string? CurrentTransactionId { get; }

    /// <summary>
    /// Event raised when the session state changes
    /// </summary>
    event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Begins a new transaction for this session
    /// </summary>
    /// <param name="isolationLevel">Optional isolation level (uses session default if not specified)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The transaction ID</returns>
    /// <exception cref="InvalidOperationException">Thrown if a transaction is already active</exception>
    Task<string> BeginTransactionAsync(IsolationLevel? isolationLevel = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits all changes in the current transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="InvalidOperationException">Thrown if no transaction is active</exception>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back all changes in the current transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="InvalidOperationException">Thrown if no transaction is active</exception>
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a document by ID from the specified collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The document if found, null otherwise</returns>
    Task<Document?> GetAsync(string collectionName, string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple documents by their IDs from the specified collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentIds">The document IDs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Enumerable of found documents</returns>
    Task<IEnumerable<Document>> GetManyAsync(string collectionName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all documents from the specified collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Enumerable of all documents</returns>
    Task<IEnumerable<Document>> GetAllAsync(string collectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new document into the specified collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="document">The document to insert</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The inserted document</returns>
    Task<Document> InsertAsync(string collectionName, Document document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing document in the specified collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="document">The document to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated document</returns>
    Task<Document> UpdateAsync(string collectionName, Document document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document by ID from the specified collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a document exists in the specified collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if exists, false otherwise</returns>
    Task<bool> ExistsAsync(string collectionName, string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves all tracked changes to the database
    /// Detects modified documents and applies changes within the current transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of documents affected</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the change tracker, removing all tracked entities
    /// </summary>
    void ClearChangeTracker();
}

/// <summary>
/// Event arguments for session state changes
/// </summary>
public class SessionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// The previous session state
    /// </summary>
    public SessionState OldState { get; }

    /// <summary>
    /// The new session state
    /// </summary>
    public SessionState NewState { get; }

    /// <summary>
    /// The time when the state change occurred
    /// </summary>
    public DateTime ChangedAt { get; }

    /// <summary>
    /// Optional reason for the state change
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Creates a new session state changed event args
    /// </summary>
    public SessionStateChangedEventArgs(SessionState oldState, SessionState newState, string? reason = null)
    {
        OldState = oldState;
        NewState = newState;
        ChangedAt = DateTime.UtcNow;
        Reason = reason;
    }
}
