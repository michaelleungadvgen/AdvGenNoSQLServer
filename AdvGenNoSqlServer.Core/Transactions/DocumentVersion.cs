// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Represents a version of a document in the MVCC system.
/// </summary>
public sealed class DocumentVersion
{
    /// <summary>Gets the unique identifier of this version.</summary>
    public Guid VersionId { get; }

    /// <summary>Gets the document content captured by this version.</summary>
    public Document Document { get; }

    /// <summary>Gets the identifier of the transaction that created this version.</summary>
    public string CreatedByTransactionId { get; }

    /// <summary>Gets the creation time derived from the MVCC timestamp generator.</summary>
    public DateTime CreatedAt { get; }

    /// <summary>Gets the deletion time, or null if this version has not been deleted.</summary>
    public DateTime? DeletedAt { get; private set; }

    /// <summary>Gets the identifier of the transaction that deleted this version, or null.</summary>
    public string? DeletedByTransactionId { get; private set; }

    /// <summary>Gets a value indicating whether this version has been marked deleted.</summary>
    public bool IsDeleted => DeletedAt.HasValue;

    /// <summary>Gets the previous version in the chain, or null for the first version.</summary>
    public DocumentVersion? PreviousVersion { get; }

    /// <summary>
    /// Initializes a new document version with a fresh version identifier and MVCC creation timestamp
    /// </summary>
    /// <param name="document">The document content for this version</param>
    /// <param name="transactionId">The identifier of the creating transaction</param>
    /// <param name="previousVersion">The previous version in the chain, if any</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document"/> or <paramref name="transactionId"/> is null</exception>
    public DocumentVersion(Document document, string transactionId, DocumentVersion? previousVersion = null)
    {
        VersionId = Guid.NewGuid();
        Document = document ?? throw new ArgumentNullException(nameof(document));
        CreatedByTransactionId = transactionId ?? throw new ArgumentNullException(nameof(transactionId));
        CreatedAt = new DateTime(MvccTimestamp.Next());
        PreviousVersion = previousVersion;
    }

    /// <summary>
    /// Marks this version as deleted by the given transaction, stamping the deletion time
    /// </summary>
    /// <param name="transactionId">The identifier of the deleting transaction</param>
    /// <exception cref="InvalidOperationException">Thrown when the version is already deleted</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transactionId"/> is null</exception>
    public void MarkDeleted(string transactionId)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Version is already deleted");
        DeletedAt = new DateTime(MvccTimestamp.Next());
        DeletedByTransactionId = transactionId ?? throw new ArgumentNullException(nameof(transactionId));
    }

    /// <summary>
    /// Determines whether this version is visible at the given read timestamp: always visible to its
    /// creator, otherwise only if created at or before the read timestamp and not yet deleted then
    /// </summary>
    /// <param name="readTimestamp">The reader's snapshot timestamp in ticks</param>
    /// <param name="currentTransactionId">The reading transaction's identifier</param>
    /// <returns>True if the version is visible; otherwise false</returns>
    public bool IsVisibleTo(long readTimestamp, string currentTransactionId)
    {
        if (CreatedByTransactionId == currentTransactionId)
            return true;
        if (CreatedAt.Ticks > readTimestamp)
            return false;
        if (DeletedAt.HasValue && DeletedAt.Value.Ticks <= readTimestamp)
            return false;
        return true;
    }

    /// <summary>
    /// Snapshot-isolation visibility: a version is visible only if its creator committed
    /// before the snapshot's read timestamp (or is the snapshot's own transaction).
    /// Unregistered creators are treated as committed for legacy direct-write compatibility.
    /// </summary>
    public bool IsVisibleToSnapshot(MvccSnapshot snapshot, Func<string, MvccTransactionRecord?> resolveTransaction)
    {
        if (CreatedByTransactionId == snapshot.TransactionId)
            return !IsDeletionVisibleTo(snapshot, resolveTransaction);

        var creator = resolveTransaction(CreatedByTransactionId);
        if (creator == null)
        {
            if (CreatedAt.Ticks > snapshot.ReadTimestamp)
                return false;
        }
        else if (creator.State != TransactionState.Committed || creator.CommitTimestamp > snapshot.ReadTimestamp)
        {
            return false;
        }

        return !IsDeletionVisibleTo(snapshot, resolveTransaction);
    }

    internal bool IsDeletionVisibleTo(MvccSnapshot snapshot, Func<string, MvccTransactionRecord?> resolveTransaction)
    {
        if (!IsDeleted)
            return false;
        if (DeletedByTransactionId == snapshot.TransactionId)
            return true;

        var deleter = resolveTransaction(DeletedByTransactionId!);
        if (deleter == null)
            return DeletedAt!.Value.Ticks <= snapshot.ReadTimestamp;
        return deleter.State == TransactionState.Committed && deleter.CommitTimestamp <= snapshot.ReadTimestamp;
    }

    internal void UnmarkDeleted()
    {
        DeletedAt = null;
        DeletedByTransactionId = null;
    }
}

/// <summary>
/// Represents a chain of document versions for MVCC.
/// </summary>
public sealed class VersionChain
{
    private readonly object _lock = new();
    private DocumentVersion? _latestVersion;

    /// <summary>Gets the identifier of the document this chain tracks.</summary>
    public string DocumentId { get; }

    /// <summary>Gets the name of the collection containing the document.</summary>
    public string CollectionName { get; }

    /// <summary>Gets the most recent version in the chain, or null when empty.</summary>
    public DocumentVersion? LatestVersion
    {
        get { lock (_lock) { return _latestVersion; } }
    }

    /// <summary>Gets the number of versions currently in the chain.</summary>
    public int VersionCount { get; private set; }

    /// <summary>
    /// Initializes a new empty version chain for a document
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The document identifier</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="collectionName"/> or <paramref name="documentId"/> is null</exception>
    public VersionChain(string collectionName, string documentId)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        DocumentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
        VersionCount = 0;
    }

    /// <summary>
    /// Appends a new version, which must link to the current latest version
    /// </summary>
    /// <param name="version">The version to append</param>
    /// <exception cref="ArgumentException">Thrown when the version does not link to the latest version</exception>
    public void AddVersion(DocumentVersion version)
    {
        lock (_lock)
        {
            if (_latestVersion != null && version.PreviousVersion != _latestVersion)
            {
                throw new ArgumentException("New version must link to the latest version");
            }
            _latestVersion = version;
            VersionCount++;
        }
    }

    /// <summary>
    /// Walks the chain from newest to oldest and returns the first version visible at the read timestamp
    /// </summary>
    /// <param name="readTimestamp">The reader's snapshot timestamp in ticks</param>
    /// <param name="transactionId">The reading transaction's identifier</param>
    /// <returns>The visible version, or null if none is visible</returns>
    public DocumentVersion? GetVisibleVersion(long readTimestamp, string transactionId)
    {
        lock (_lock)
        {
            var current = _latestVersion;
            while (current != null)
            {
                if (current.IsVisibleTo(readTimestamp, transactionId))
                    return current;
                current = current.PreviousVersion;
            }
            return null;
        }
    }

    /// <summary>
    /// Walks the chain under snapshot isolation, returning null when a deletion visible at the snapshot
    /// makes the document logically gone
    /// </summary>
    /// <param name="snapshot">The snapshot defining read timestamp and owning transaction</param>
    /// <param name="resolveTransaction">Resolver mapping transaction identifiers to their records</param>
    /// <returns>The visible version, or null if none is visible or the document is deleted at the snapshot</returns>
    public DocumentVersion? GetVisibleVersion(MvccSnapshot snapshot, Func<string, MvccTransactionRecord?> resolveTransaction)
    {
        lock (_lock)
        {
            var current = _latestVersion;
            while (current != null)
            {
                // A deletion visible at this snapshot means the document is gone;
                // walking further would resurrect stale pre-delete versions.
                if (current.IsDeletionVisibleTo(snapshot, resolveTransaction))
                    return null;
                if (current.IsVisibleToSnapshot(snapshot, resolveTransaction))
                    return current;
                current = current.PreviousVersion;
            }
            return null;
        }
    }

    /// <summary>
    /// Removes the latest version if it is the given one (rollback undo); the previous
    /// version becomes latest again.
    /// </summary>
    public bool TryRemoveLatestVersion(DocumentVersion version)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_latestVersion, version))
                return false;
            _latestVersion = version.PreviousVersion;
            VersionCount--;
            return true;
        }
    }

    /// <summary>
    /// Marks the latest version as deleted by the given transaction
    /// </summary>
    /// <param name="transactionId">The identifier of the deleting transaction</param>
    /// <returns>True if a latest version existed and was marked; false when the chain is empty</returns>
    public bool MarkLatestDeleted(string transactionId)
    {
        lock (_lock)
        {
            if (_latestVersion == null)
                return false;
            _latestVersion.MarkDeleted(transactionId);
            return true;
        }
    }

    /// <summary>
    /// Gets all versions in the chain, newest first
    /// </summary>
    /// <returns>A snapshot list of the versions from latest to oldest</returns>
    public IReadOnlyList<DocumentVersion> GetAllVersions()
    {
        lock (_lock)
        {
            var versions = new List<DocumentVersion>();
            var current = _latestVersion;
            while (current != null)
            {
                versions.Add(current);
                current = current.PreviousVersion;
            }
            return versions;
        }
    }
}

/// <summary>
/// Global timestamp generator for MVCC.
/// </summary>
public static class MvccTimestamp
{
    private static long _currentTimestamp = DateTime.UtcNow.Ticks;

    /// <summary>
    /// Atomically advances the global timestamp and returns the new value
    /// </summary>
    /// <returns>The next monotonically increasing timestamp in ticks</returns>
    public static long Next()
    {
        return Interlocked.Increment(ref _currentTimestamp);
    }

    /// <summary>
    /// Gets the current global timestamp without advancing it
    /// </summary>
    /// <returns>The current timestamp in ticks</returns>
    public static long Current()
    {
        return Interlocked.Read(ref _currentTimestamp);
    }
}
