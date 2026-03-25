// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// MVCC-enabled transaction context that provides snapshot isolation.
/// Non-blocking reads are achieved by reading from a consistent snapshot.
/// </summary>
public class MvccTransactionContext : ITransactionContext
{
    private readonly IMvccStore _mvccStore;
    private readonly IWriteAheadLog _writeAheadLog;
    private readonly List<string> _lockedResources;
    private readonly HashSet<string> _writtenDocuments;
    private readonly object _stateLock = new();
    private bool _isCommittedOrRolledBack;
    private bool _disposed;

    /// <summary>
    /// The unique transaction ID
    /// </summary>
    public string TransactionId { get; }

    /// <summary>
    /// The read snapshot for this transaction
    /// </summary>
    public MvccSnapshot Snapshot { get; }

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
    public int OperationCount { get; private set; }

    /// <summary>
    /// Creates a new MVCC transaction context
    /// </summary>
    public MvccTransactionContext(
        string transactionId,
        TransactionOptions options,
        IMvccStore mvccStore,
        IWriteAheadLog writeAheadLog,
        IEnumerable<string> activeTransactions)
    {
        TransactionId = transactionId ?? throw new ArgumentNullException(nameof(transactionId));
        _mvccStore = mvccStore ?? throw new ArgumentNullException(nameof(mvccStore));
        _writeAheadLog = writeAheadLog ?? throw new ArgumentNullException(nameof(writeAheadLog));

        IsolationLevel = options.IsolationLevel;
        AutoRollbackOnDispose = options.AutoRollbackOnDispose;
        StartedAt = DateTime.UtcNow;
        ExpiresAt = options.Timeout > TimeSpan.Zero ? StartedAt.Add(options.Timeout) : null;
        State = TransactionState.Active;

        // Create snapshot at transaction start
        var readTimestamp = MvccTimestamp.Next();
        Snapshot = new MvccSnapshot(readTimestamp, transactionId, activeTransactions);

        _lockedResources = new List<string>();
        _writtenDocuments = new HashSet<string>();
        OperationCount = 0;
    }

    /// <summary>
    /// Gets a document visible to this transaction's snapshot
    /// </summary>
    public async Task<Document?> GetAsync(string collectionName, string documentId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureActive();

        var version = await _mvccStore.GetVisibleVersionAsync(collectionName, documentId, Snapshot, ct);

        if (version == null || version.IsDeleted)
            return null;

        return version.Document;
    }

    /// <summary>
    /// Gets all documents visible to this transaction's snapshot
    /// </summary>
    public async Task<IReadOnlyList<Document>> GetAllAsync(string collectionName, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureActive();

        var versions = await _mvccStore.GetVisibleVersionsAsync(collectionName, Snapshot, ct);
        return versions.Where(v => !v.IsDeleted).Select(v => v.Document).ToList();
    }

    /// <summary>
    /// Inserts a new document
    /// </summary>
    public async Task<Document> InsertAsync(string collectionName, Document document, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureActive();

        // Check for write-write conflict
        var existingVersion = await _mvccStore.GetVisibleVersionAsync(
            collectionName, document.Id, Snapshot, ct);

        if (existingVersion != null && !existingVersion.IsDeleted)
        {
            throw new InvalidOperationException(
                $"Document {document.Id} already exists in collection {collectionName}");
        }

        var resourceKey = $"{collectionName}:{document.Id}";
        LockResource(resourceKey);

        var version = await _mvccStore.InsertVersionAsync(collectionName, document, TransactionId, ct);
        _writtenDocuments.Add(resourceKey);

        await _writeAheadLog.AppendInsertAsync(TransactionId, collectionName, document);

        OperationCount++;
        return document;
    }

    /// <summary>
    /// Updates an existing document
    /// </summary>
    public async Task<Document?> UpdateAsync(string collectionName, Document document, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureActive();

        // For Serializable isolation, we need to ensure no one else modified
        // the document since our snapshot
        if (IsolationLevel == IsolationLevel.Serializable)
        {
            var currentVersion = await _mvccStore.GetVisibleVersionAsync(
                collectionName, document.Id, Snapshot, ct);

            if (currentVersion == null || currentVersion.IsDeleted)
                return null;

            // Check if another transaction modified it after our snapshot
            if (currentVersion.CreatedByTransactionId != TransactionId &&
                currentVersion.CreatedAt.Ticks > Snapshot.ReadTimestamp)
            {
                throw new TransactionConflictException(
                    $"Write-write conflict on document {document.Id}. Another transaction modified it.");
            }
        }

        var resourceKey = $"{collectionName}:{document.Id}";
        LockResource(resourceKey);

        var version = await _mvccStore.UpdateVersionAsync(
            collectionName, document.Id, document, TransactionId, ct);

        if (version == null)
            return null;

        _writtenDocuments.Add(resourceKey);

        await _writeAheadLog.AppendUpdateAsync(TransactionId, collectionName, document, document);

        OperationCount++;
        return document;
    }

    /// <summary>
    /// Deletes a document
    /// </summary>
    public async Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureActive();

        // For Serializable isolation, check for conflicts
        if (IsolationLevel == IsolationLevel.Serializable)
        {
            var currentVersion = await _mvccStore.GetVisibleVersionAsync(
                collectionName, documentId, Snapshot, ct);

            if (currentVersion == null || currentVersion.IsDeleted)
                return false;

            if (currentVersion.CreatedByTransactionId != TransactionId &&
                currentVersion.CreatedAt.Ticks > Snapshot.ReadTimestamp)
            {
                throw new TransactionConflictException(
                    $"Write-write conflict on document {documentId}. Another transaction modified it.");
            }
        }

        var resourceKey = $"{collectionName}:{documentId}";
        LockResource(resourceKey);

        var result = await _mvccStore.DeleteVersionAsync(collectionName, documentId, TransactionId, ct);

        if (result)
        {
            _writtenDocuments.Add(resourceKey);

            // Create a tombstone document for WAL
            var tombstone = new Document { Id = documentId };
            await _writeAheadLog.AppendDeleteAsync(TransactionId, collectionName, tombstone);

            OperationCount++;
        }

        return result;
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
                throw new InvalidOperationException($"Cannot commit transaction in {State} state");

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
    /// Rolls back the transaction
    /// </summary>
    public async Task<bool> RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (State != TransactionState.Active && State != TransactionState.Preparing)
                return false;

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
    /// Creates a savepoint
    /// </summary>
    public Task<bool> SavepointAsync(string name)
    {
        ThrowIfDisposed();
        EnsureActive();

        // MVCC doesn't support true savepoints easily, but we can log the position
        return Task.FromResult(true);
    }

    /// <summary>
    /// Rolls back to a savepoint
    /// </summary>
    public Task<bool> RollbackToSavepointAsync(string name)
    {
        ThrowIfDisposed();
        EnsureActive();

        // MVCC doesn't support rollback to savepoint without complex undo logic
        throw new NotSupportedException("Rollback to savepoint is not supported in MVCC mode");
    }

    /// <summary>
    /// Records a read operation (no-op for MVCC - reads are from snapshot)
    /// </summary>
    public Task RecordReadAsync(string collectionName, string documentId)
    {
        // In MVCC, reads don't need to be recorded - they use the snapshot
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records a write operation (version is already created)
    /// </summary>
    public Task RecordWriteAsync(string collectionName, string documentId, Document? beforeImage, Document? afterImage)
    {
        // Write is already recorded in the version chain
        OperationCount++;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks if the transaction has timed out
    /// </summary>
    public bool IsTimedOut()
    {
        return ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    }

    private void LockResource(string resourceId)
    {
        if (!_lockedResources.Contains(resourceId))
        {
            _lockedResources.Add(resourceId);
        }
    }

    private void ReleaseAllLocks()
    {
        _lockedResources.Clear();
    }

    private void EnsureActive()
    {
        lock (_stateLock)
        {
            if (State != TransactionState.Active)
                throw new InvalidOperationException($"Transaction is not active (current state: {State})");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MvccTransactionContext));
    }

    /// <summary>
    /// Disposes the transaction context
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        if (AutoRollbackOnDispose && State == TransactionState.Active && !_isCommittedOrRolledBack)
        {
            _ = RollbackAsync();
        }

        ReleaseAllLocks();
        _disposed = true;
    }
}

/// <summary>
/// Exception thrown when a transaction conflict is detected
/// </summary>
public class TransactionConflictException : Exception
{
    public TransactionConflictException(string message) : base(message) { }
    public TransactionConflictException(string message, Exception inner) : base(message, inner) { }
}
