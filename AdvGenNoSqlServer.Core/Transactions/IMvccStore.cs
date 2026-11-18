// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Interface for MVCC (Multi-Version Concurrency Control) document storage.
/// </summary>
public interface IMvccStore
{
    /// <summary>
    /// Gets a document version visible to the given snapshot
    /// </summary>
    Task<DocumentVersion?> GetVisibleVersionAsync(
        string collectionName,
        string documentId,
        MvccSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all document versions visible to the given snapshot
    /// </summary>
    Task<IReadOnlyList<DocumentVersion>> GetVisibleVersionsAsync(
        string collectionName,
        MvccSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new document version
    /// </summary>
    Task<DocumentVersion> InsertVersionAsync(
        string collectionName,
        Document document,
        string transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a document by creating a new version
    /// </summary>
    Task<DocumentVersion?> UpdateVersionAsync(
        string collectionName,
        string documentId,
        Document newDocument,
        string transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a document as deleted (creates a tombstone version)
    /// </summary>
    Task<bool> DeleteVersionAsync(
        string collectionName,
        string documentId,
        string transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a document exists and is visible to the snapshot
    /// </summary>
    Task<bool> ExistsAsync(
        string collectionName,
        string documentId,
        MvccSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of visible documents in a collection
    /// </summary>
    Task<long> CountAsync(
        string collectionName,
        MvccSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new collection
    /// </summary>
    Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a collection and all its versions
    /// </summary>
    Task<bool> DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all collection names
    /// </summary>
    Task<IReadOnlyList<string>> GetCollectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all versions from a collection
    /// </summary>
    Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a transaction as active so its uncommitted writes are invisible to other snapshots.
    /// </summary>
    void RegisterTransaction(string transactionId);

    /// <summary>
    /// Marks a transaction committed, making its writes visible to later snapshots.
    /// </summary>
    void MarkTransactionCommitted(string transactionId);

    /// <summary>
    /// Marks a transaction rolled back.
    /// </summary>
    void MarkTransactionRolledBack(string transactionId);

    /// <summary>
    /// Gets the commit-state record of a registered transaction, or null if unregistered.
    /// </summary>
    MvccTransactionRecord? GetTransactionRecord(string transactionId);

    /// <summary>
    /// Gets the latest version of a document regardless of snapshot visibility.
    /// </summary>
    Task<DocumentVersion?> GetLatestVersionAsync(
        string collectionName,
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes a single write during rollback.
    /// </summary>
    Task<bool> UndoVersionAsync(
        string collectionName,
        string documentId,
        DocumentVersion version,
        MvccUndoKind kind,
        CancellationToken cancellationToken = default);
}
