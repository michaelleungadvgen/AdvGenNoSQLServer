// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Indexing;

/// <summary>
/// Represents a TTL (Time-To-Live) index configuration for automatic document expiration
/// </summary>
public class TtlIndexConfiguration
{
    /// <summary>
    /// The name of the collection
    /// </summary>
    public required string CollectionName { get; set; }

    /// <summary>
    /// The field name containing the expiration timestamp (DateTime)
    /// </summary>
    public required string ExpireAfterField { get; set; }

    /// <summary>
    /// Default expiration time span for documents (if field is not present)
    /// </summary>
    public TimeSpan? DefaultExpireAfter { get; set; }

    /// <summary>
    /// Whether to delete documents immediately when they expire or mark them for background cleanup
    /// </summary>
    public bool ImmediateDeletion { get; set; } = false;

    /// <summary>
    /// The interval at which expired documents are checked and removed
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Statistics for TTL index operations
/// </summary>
public class TtlIndexStatistics
{
    /// <summary>
    /// Total number of documents expired and removed
    /// </summary>
    public long DocumentsExpired { get; set; }

    /// <summary>
    /// Number of documents currently tracked for expiration
    /// </summary>
    public long DocumentsTracked { get; set; }

    /// <summary>
    /// Timestamp of the last cleanup run
    /// </summary>
    public DateTime LastCleanupTime { get; set; }

    /// <summary>
    /// Total number of cleanup runs performed
    /// </summary>
    public long CleanupRuns { get; set; }

    /// <summary>
    /// Average time (in milliseconds) for cleanup operations
    /// </summary>
    public double AverageCleanupTimeMs { get; set; }
}

/// <summary>
/// Interface for TTL (Time-To-Live) index service that manages automatic document expiration
/// </summary>
public interface ITtlIndexService : IDisposable
{
    /// <summary>
    /// Creates a TTL index on a collection field
    /// </summary>
    /// <param name="configuration">The TTL index configuration</param>
    void CreateTtlIndex(TtlIndexConfiguration configuration);

    /// <summary>
    /// Drops a TTL index from a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>True if index was found and removed, false otherwise</returns>
    bool DropTtlIndex(string collectionName);

    /// <summary>
    /// Checks if a TTL index exists for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>True if TTL index exists, false otherwise</returns>
    bool HasTtlIndex(string collectionName);

    /// <summary>
    /// Gets the TTL index configuration for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>The TTL index configuration if found, null otherwise</returns>
    TtlIndexConfiguration? GetTtlIndexConfiguration(string collectionName);

    /// <summary>
    /// Registers a document for expiration tracking
    /// Called automatically when documents are inserted or updated
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="document">The document to track</param>
    void RegisterDocument(string collectionName, Document document);

    /// <summary>
    /// Unregisters a document from expiration tracking
    /// Called automatically when documents are deleted
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    void UnregisterDocument(string collectionName, string documentId);

    /// <summary>
    /// Manually triggers cleanup of expired documents
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of documents removed</returns>
    Task<int> CleanupExpiredDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics for TTL index operations
    /// </summary>
    /// <returns>The current statistics</returns>
    TtlIndexStatistics GetStatistics();

    /// <summary>
    /// Event raised when documents are expired and removed
    /// </summary>
    event EventHandler<DocumentsExpiredEventArgs>? DocumentsExpired;

    /// <summary>
    /// Starts the background cleanup service
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the background cleanup service
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Event arguments for document expiration events
/// </summary>
public class DocumentsExpiredEventArgs : EventArgs
{
    /// <summary>
    /// The name of the collection
    /// </summary>
    public required string CollectionName { get; set; }

    /// <summary>
    /// The IDs of documents that were expired
    /// </summary>
    public required IReadOnlyList<string> DocumentIds { get; set; }

    /// <summary>
    /// The number of documents expired
    /// </summary>
    public int Count => DocumentIds.Count;

    /// <summary>
    /// The time when the expiration occurred
    /// </summary>
    public DateTime ExpirationTime { get; set; }
}
