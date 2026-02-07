// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Represents a deleted document record (tombstone) for garbage collection
/// </summary>
public class Tombstone
{
    /// <summary>
    /// Gets the ID of the deleted document
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Gets the name of the collection the document was in
    /// </summary>
    public required string CollectionName { get; init; }

    /// <summary>
    /// Gets the timestamp when the document was deleted
    /// </summary>
    public required DateTime DeletedAt { get; init; }

    /// <summary>
    /// Gets the version of the document at the time of deletion
    /// </summary>
    public required long DocumentVersion { get; init; }

    /// <summary>
    /// Gets the transaction ID that deleted the document (if applicable)
    /// </summary>
    public string? TransactionId { get; init; }

    /// <summary>
    /// Gets the file path of the deleted document (for persistent stores)
    /// </summary>
    public string? FilePath { get; init; }
}

/// <summary>
/// Statistics for the garbage collector
/// </summary>
public class GarbageCollectorStats
{
    /// <summary>
    /// Gets the total number of tombstones tracked
    /// </summary>
    public long TotalTombstones { get; set; }

    /// <summary>
    /// Gets the number of tombstones that have been cleaned up
    /// </summary>
    public long CleanedTombstones { get; set; }

    /// <summary>
    /// Gets the number of documents physically deleted
    /// </summary>
    public long DocumentsPhysicallyDeleted { get; set; }

    /// <summary>
    /// Gets the total bytes freed by garbage collection
    /// </summary>
    public long BytesFreed { get; set; }

    /// <summary>
    /// Gets the timestamp of the last garbage collection run
    /// </summary>
    public DateTime? LastCollectionRun { get; set; }

    /// <summary>
    /// Gets the number of failed cleanup operations
    /// </summary>
    public long FailedCleanups { get; set; }
}

/// <summary>
/// Options for configuring the garbage collector
/// </summary>
public class GarbageCollectorOptions
{
    /// <summary>
    /// Gets or sets whether garbage collection is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the retention period for tombstones before physical deletion
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets the interval between automatic garbage collection runs
    /// </summary>
    public TimeSpan CollectionInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the maximum number of tombstones to process per collection run
    /// </summary>
    public int MaxTombstonesPerRun { get; set; } = 1000;

    /// <summary>
    /// Gets or sets whether to enable automatic background collection
    /// </summary>
    public bool EnableBackgroundCollection { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum free disk space percentage to trigger aggressive cleanup
    /// </summary>
    public double MinFreeDiskSpacePercent { get; set; } = 10.0;
}

/// <summary>
/// Interface for garbage collection of deleted documents
/// </summary>
public interface IGarbageCollector
{
    /// <summary>
    /// Records a document deletion (creates a tombstone)
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="documentVersion">The document version</param>
    /// <param name="filePath">The file path (for persistent stores)</param>
    /// <param name="transactionId">The transaction ID (if applicable)</param>
    void RecordDeletion(string collectionName, string documentId, long documentVersion, string? filePath = null, string? transactionId = null);

    /// <summary>
    /// Runs garbage collection to physically delete expired tombstones
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of documents cleaned up</returns>
    Task<int> CollectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current garbage collector statistics
    /// </summary>
    /// <returns>Statistics snapshot</returns>
    GarbageCollectorStats GetStatistics();

    /// <summary>
    /// Gets all active tombstones
    /// </summary>
    /// <returns>Enumerable of tombstones</returns>
    IEnumerable<Tombstone> GetTombstones();

    /// <summary>
    /// Gets tombstones for a specific collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>Enumerable of tombstones</returns>
    IEnumerable<Tombstone> GetTombstones(string collectionName);

    /// <summary>
    /// Manually removes a tombstone by document ID
    /// </summary>
    /// <param name="documentId">The document ID</param>
    /// <returns>True if tombstone was removed</returns>
    bool RemoveTombstone(string documentId);

    /// <summary>
    /// Clears all tombstones (use with caution)
    /// </summary>
    void ClearAllTombstones();

    /// <summary>
    /// Checks if a document ID has a tombstone (was recently deleted)
    /// </summary>
    /// <param name="documentId">The document ID</param>
    /// <returns>True if a tombstone exists</returns>
    bool HasTombstone(string documentId);
}

/// <summary>
/// Garbage collector for managing deleted documents and reclaiming storage space
/// </summary>
public class GarbageCollector : IGarbageCollector, IDisposable
{
    private readonly ConcurrentDictionary<string, Tombstone> _tombstones;
    private readonly GarbageCollectorOptions _options;
    private readonly Timer? _backgroundTimer;
    private readonly ReaderWriterLockSlim _statsLock;
    private GarbageCollectorStats _stats;
    private bool _disposed;

    /// <summary>
    /// Creates a new GarbageCollector instance
    /// </summary>
    /// <param name="options">The garbage collector options</param>
    public GarbageCollector(GarbageCollectorOptions? options = null)
    {
        _options = options ?? new GarbageCollectorOptions();
        _tombstones = new ConcurrentDictionary<string, Tombstone>();
        _statsLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        _stats = new GarbageCollectorStats();
        _disposed = false;

        // Start background collection if enabled
        if (_options.EnableBackgroundCollection && _options.Enabled)
        {
            _backgroundTimer = new Timer(
                async _ => await RunBackgroundCollectionAsync(),
                null,
                _options.CollectionInterval,
                _options.CollectionInterval);
        }
    }

    /// <inheritdoc />
    public void RecordDeletion(string collectionName, string documentId, long documentVersion, string? filePath = null, string? transactionId = null)
    {
        if (!_options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        var tombstone = new Tombstone
        {
            DocumentId = documentId,
            CollectionName = collectionName,
            DeletedAt = DateTime.UtcNow,
            DocumentVersion = documentVersion,
            FilePath = filePath,
            TransactionId = transactionId
        };

        _tombstones.AddOrUpdate(documentId, tombstone, (_, __) => tombstone);

        _statsLock.EnterWriteLock();
        try
        {
            _stats.TotalTombstones = _tombstones.Count;
        }
        finally
        {
            _statsLock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public async Task<int> CollectAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return 0;

        var cleanedCount = 0;
        var bytesFreed = 0L;
        var cutoffTime = DateTime.UtcNow.Subtract(_options.RetentionPeriod);
        var tombstonesToProcess = _tombstones.Values
            .Where(t => t.DeletedAt < cutoffTime)
            .Take(_options.MaxTombstonesPerRun)
            .ToList();

        foreach (var tombstone in tombstonesToProcess)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // Try to physically delete the file if it exists
                if (!string.IsNullOrEmpty(tombstone.FilePath) && File.Exists(tombstone.FilePath))
                {
                    var fileInfo = new FileInfo(tombstone.FilePath);
                    var fileSize = fileInfo.Length;

                    await Task.Run(() => File.Delete(tombstone.FilePath), cancellationToken);
                    bytesFreed += fileSize;
                }

                // Remove the tombstone
                if (_tombstones.TryRemove(tombstone.DocumentId, out _))
                {
                    cleanedCount++;
                }
            }
            catch (Exception)
            {
                _statsLock.EnterWriteLock();
                try
                {
                    _stats.FailedCleanups++;
                }
                finally
                {
                    _statsLock.ExitWriteLock();
                }
            }
        }

        // Update statistics
        _statsLock.EnterWriteLock();
        try
        {
            _stats.CleanedTombstones += cleanedCount;
            _stats.DocumentsPhysicallyDeleted += cleanedCount;
            _stats.BytesFreed += bytesFreed;
            _stats.LastCollectionRun = DateTime.UtcNow;
            _stats.TotalTombstones = _tombstones.Count;
        }
        finally
        {
            _statsLock.ExitWriteLock();
        }

        return cleanedCount;
    }

    /// <inheritdoc />
    public GarbageCollectorStats GetStatistics()
    {
        _statsLock.EnterReadLock();
        try
        {
            return new GarbageCollectorStats
            {
                TotalTombstones = _stats.TotalTombstones,
                CleanedTombstones = _stats.CleanedTombstones,
                DocumentsPhysicallyDeleted = _stats.DocumentsPhysicallyDeleted,
                BytesFreed = _stats.BytesFreed,
                LastCollectionRun = _stats.LastCollectionRun,
                FailedCleanups = _stats.FailedCleanups
            };
        }
        finally
        {
            _statsLock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public IEnumerable<Tombstone> GetTombstones()
    {
        return _tombstones.Values.ToList();
    }

    /// <inheritdoc />
    public IEnumerable<Tombstone> GetTombstones(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            return Enumerable.Empty<Tombstone>();

        return _tombstones.Values
            .Where(t => t.CollectionName.Equals(collectionName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <inheritdoc />
    public bool RemoveTombstone(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return false;

        var removed = _tombstones.TryRemove(documentId, out _);
        
        if (removed)
        {
            _statsLock.EnterWriteLock();
            try
            {
                _stats.TotalTombstones = _tombstones.Count;
            }
            finally
            {
                _statsLock.ExitWriteLock();
            }
        }

        return removed;
    }

    /// <inheritdoc />
    public void ClearAllTombstones()
    {
        _tombstones.Clear();
        
        _statsLock.EnterWriteLock();
        try
        {
            _stats.TotalTombstones = 0;
        }
        finally
        {
            _statsLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Checks if a document ID has a tombstone (was recently deleted)
    /// </summary>
    /// <param name="documentId">The document ID</param>
    /// <returns>True if a tombstone exists</returns>
    public bool HasTombstone(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return false;

        return _tombstones.ContainsKey(documentId);
    }

    /// <summary>
    /// Gets a tombstone by document ID
    /// </summary>
    /// <param name="documentId">The document ID</param>
    /// <returns>The tombstone if found, null otherwise</returns>
    public Tombstone? GetTombstone(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return null;

        _tombstones.TryGetValue(documentId, out var tombstone);
        return tombstone;
    }

    private async Task RunBackgroundCollectionAsync()
    {
        try
        {
            await CollectAsync(CancellationToken.None);
        }
        catch
        {
            // Background collection errors are suppressed
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _backgroundTimer?.Dispose();
        _statsLock.Dispose();
        _disposed = true;
    }
}
