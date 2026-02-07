// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Represents the type of lock
/// </summary>
public enum LockType
{
    /// <summary>
    /// Shared lock for read operations - multiple readers can hold simultaneously
    /// </summary>
    Shared,

    /// <summary>
    /// Exclusive lock for write operations - only one writer can hold, blocks all other locks
    /// </summary>
    Exclusive
}

/// <summary>
/// Represents the result of a lock acquisition attempt
/// </summary>
public enum LockResult
{
    /// <summary>
    /// Lock was acquired successfully
    /// </summary>
    Granted,

    /// <summary>
    /// Lock request timed out
    /// </summary>
    Timeout,

    /// <summary>
    /// Lock request was denied due to deadlock detection
    /// </summary>
    DeadlockDetected,

    /// <summary>
    /// Lock request was denied for other reasons
    /// </summary>
    Denied
}

/// <summary>
/// Represents information about a held lock
/// </summary>
public class LockInfo
{
    /// <summary>
    /// The transaction ID that holds this lock
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// The resource being locked (e.g., "collection:documentId")
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// The type of lock
    /// </summary>
    public LockType LockType { get; set; }

    /// <summary>
    /// When the lock was acquired
    /// </summary>
    public DateTime AcquiredAt { get; set; }

    /// <summary>
    /// When the lock will expire (null if no timeout)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Represents information about a waiting lock request
/// </summary>
public class LockRequest
{
    /// <summary>
    /// The transaction ID requesting the lock
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// The resource being requested
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// The type of lock requested
    /// </summary>
    public LockType LockType { get; set; }

    /// <summary>
    /// When the request was made
    /// </summary>
    public DateTime RequestedAt { get; set; }

    /// <summary>
    /// Maximum time to wait for the lock
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Task completion source for async wait
    /// </summary>
    internal TaskCompletionSource<LockResult> CompletionSource { get; set; } = new();
}

/// <summary>
/// Exception thrown when a deadlock is detected
/// </summary>
public class DeadlockException : Exception
{
    public string TransactionId { get; }
    public string ResourceId { get; }

    public DeadlockException(string message, string transactionId, string resourceId)
        : base(message)
    {
        TransactionId = transactionId;
        ResourceId = resourceId;
    }
}

/// <summary>
/// Interface for managing locks in the transaction system
/// </summary>
public interface ILockManager
{
    /// <summary>
    /// Attempts to acquire a lock on a resource
    /// </summary>
    /// <param name="transactionId">The transaction requesting the lock</param>
    /// <param name="resourceId">The resource to lock (e.g., "collection:documentId")</param>
    /// <param name="lockType">The type of lock (Shared or Exclusive)</param>
    /// <param name="timeout">Optional timeout for the lock acquisition attempt</param>
    /// <returns>A task that resolves to the lock result</returns>
    Task<LockResult> AcquireLockAsync(string transactionId, string resourceId, LockType lockType, TimeSpan? timeout = null);

    /// <summary>
    /// Attempts to acquire a lock on a resource synchronously
    /// </summary>
    /// <param name="transactionId">The transaction requesting the lock</param>
    /// <param name="resourceId">The resource to lock</param>
    /// <param name="lockType">The type of lock</param>
    /// <param name="timeout">Optional timeout</param>
    /// <returns>The lock result</returns>
    LockResult AcquireLock(string transactionId, string resourceId, LockType lockType, TimeSpan? timeout = null);

    /// <summary>
    /// Releases a lock held by a transaction
    /// </summary>
    /// <param name="transactionId">The transaction holding the lock</param>
    /// <param name="resourceId">The resource to unlock</param>
    /// <returns>True if the lock was released, false if not found</returns>
    bool ReleaseLock(string transactionId, string resourceId);

    /// <summary>
    /// Releases all locks held by a transaction
    /// </summary>
    /// <param name="transactionId">The transaction to release all locks for</param>
    /// <returns>The number of locks released</returns>
    int ReleaseAllLocks(string transactionId);

    /// <summary>
    /// Attempts to upgrade a shared lock to an exclusive lock
    /// </summary>
    /// <param name="transactionId">The transaction holding the shared lock</param>
    /// <param name="resourceId">The resource to upgrade</param>
    /// <param name="timeout">Optional timeout for the upgrade attempt</param>
    /// <returns>The lock result</returns>
    Task<LockResult> UpgradeLockAsync(string transactionId, string resourceId, TimeSpan? timeout = null);

    /// <summary>
    /// Checks if a transaction holds a lock on a resource
    /// </summary>
    /// <param name="transactionId">The transaction to check</param>
    /// <param name="resourceId">The resource to check</param>
    /// <returns>True if the transaction holds a lock on the resource</returns>
    bool HasLock(string transactionId, string resourceId);

    /// <summary>
    /// Gets the type of lock held by a transaction on a resource
    /// </summary>
    /// <param name="transactionId">The transaction to check</param>
    /// <param name="resourceId">The resource to check</param>
    /// <returns>The lock type, or null if no lock is held</returns>
    LockType? GetLockType(string transactionId, string resourceId);

    /// <summary>
    /// Gets all locks held by a transaction
    /// </summary>
    /// <param name="transactionId">The transaction to get locks for</param>
    /// <returns>A read-only list of lock information</returns>
    IReadOnlyList<LockInfo> GetTransactionLocks(string transactionId);

    /// <summary>
    /// Gets all locks held on a resource
    /// </summary>
    /// <param name="resourceId">The resource to get locks for</param>
    /// <returns>A read-only list of lock information</returns>
    IReadOnlyList<LockInfo> GetResourceLocks(string resourceId);

    /// <summary>
    /// Gets the current number of active locks
    /// </summary>
    int ActiveLockCount { get; }

    /// <summary>
    /// Gets the current number of waiting lock requests
    /// </summary>
    int WaitingRequestCount { get; }

    /// <summary>
    /// Event raised when a deadlock is detected and resolved
    /// </summary>
    event EventHandler<DeadlockEventArgs>? DeadlockDetected;
}

/// <summary>
/// Event arguments for deadlock detection events
/// </summary>
public class DeadlockEventArgs : EventArgs
{
    /// <summary>
    /// The transaction that was chosen as the victim
    /// </summary>
    public string VictimTransactionId { get; set; } = string.Empty;

    /// <summary>
    /// All transactions involved in the deadlock cycle
    /// </summary>
    public IReadOnlyList<string> InvolvedTransactions { get; set; } = new List<string>();

    /// <summary>
    /// The resource that caused the deadlock detection
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// When the deadlock was detected
    /// </summary>
    public DateTime DetectedAt { get; set; }
}
