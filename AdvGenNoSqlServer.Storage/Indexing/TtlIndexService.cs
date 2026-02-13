// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Indexing;

/// <summary>
/// Service that manages TTL (Time-To-Live) indexes for automatic document expiration
/// Uses a priority queue (min-heap) to efficiently track and expire documents
/// </summary>
public class TtlIndexService : ITtlIndexService
{
    private readonly ConcurrentDictionary<string, TtlIndexConfiguration> _ttlIndexes = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTime>> _documentExpirationTimes = new();
    private readonly ConcurrentDictionary<string, PriorityQueue<string, DateTime>> _expirationQueues = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _collectionLocks = new();
    private readonly Func<string, string, Task<bool>>? _deleteDocumentCallback;
    private readonly Timer? _cleanupTimer;
    private readonly TimeSpan _defaultCleanupInterval;
    private long _documentsExpired;
    private long _cleanupRuns;
    private long _totalCleanupTimeMs;
    private DateTime _lastCleanupTime = DateTime.MinValue;
    private bool _isDisposed;
    private bool _isRunning;

    /// <summary>
    /// Event raised when documents are expired and removed
    /// </summary>
    public event EventHandler<DocumentsExpiredEventArgs>? DocumentsExpired;

    /// <summary>
    /// Creates a new TTL index service
    /// </summary>
    /// <param name="deleteDocumentCallback">Optional callback for deleting expired documents</param>
    /// <param name="cleanupInterval">Default interval for cleanup operations</param>
    public TtlIndexService(Func<string, string, Task<bool>>? deleteDocumentCallback = null, TimeSpan? cleanupInterval = null)
    {
        _deleteDocumentCallback = deleteDocumentCallback;
        _defaultCleanupInterval = cleanupInterval ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Creates a TTL index on a collection field
    /// </summary>
    /// <param name="configuration">The TTL index configuration</param>
    public void CreateTtlIndex(TtlIndexConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrEmpty(configuration.CollectionName);
        ArgumentException.ThrowIfNullOrEmpty(configuration.ExpireAfterField);

        _ttlIndexes[configuration.CollectionName] = configuration;
        _documentExpirationTimes[configuration.CollectionName] = new ConcurrentDictionary<string, DateTime>();
        _expirationQueues[configuration.CollectionName] = new PriorityQueue<string, DateTime>();
        _collectionLocks[configuration.CollectionName] = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Drops a TTL index from a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>True if index was found and removed, false otherwise</returns>
    public bool DropTtlIndex(string collectionName)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);

        var removed = _ttlIndexes.TryRemove(collectionName, out _);
        _documentExpirationTimes.TryRemove(collectionName, out _);
        _expirationQueues.TryRemove(collectionName, out _);
        _collectionLocks.TryRemove(collectionName, out _);

        return removed;
    }

    /// <summary>
    /// Checks if a TTL index exists for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>True if TTL index exists, false otherwise</returns>
    public bool HasTtlIndex(string collectionName)
    {
        return _ttlIndexes.ContainsKey(collectionName);
    }

    /// <summary>
    /// Gets the TTL index configuration for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>The TTL index configuration if found, null otherwise</returns>
    public TtlIndexConfiguration? GetTtlIndexConfiguration(string collectionName)
    {
        _ttlIndexes.TryGetValue(collectionName, out var config);
        return config;
    }

    /// <summary>
    /// Registers a document for expiration tracking
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="document">The document to track</param>
    public void RegisterDocument(string collectionName, Document document)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentNullException.ThrowIfNull(document);

        if (!_ttlIndexes.TryGetValue(collectionName, out var config))
            return;

        if (!_documentExpirationTimes.TryGetValue(collectionName, out var expirationDict))
            return;

        DateTime? expirationTime = ExtractExpirationTime(document, config);
        if (!expirationTime.HasValue)
            return;

        // Remove old expiration time if exists
        expirationDict.TryRemove(document.Id, out _);

        // Add new expiration time
        expirationDict[document.Id] = expirationTime.Value;

        // Add to priority queue
        if (_expirationQueues.TryGetValue(collectionName, out var queue))
        {
            queue.Enqueue(document.Id, expirationTime.Value);
        }
    }

    /// <summary>
    /// Unregisters a document from expiration tracking
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    public void UnregisterDocument(string collectionName, string documentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        if (_documentExpirationTimes.TryGetValue(collectionName, out var expirationDict))
        {
            expirationDict.TryRemove(documentId, out _);
        }
    }

    /// <summary>
    /// Manually triggers cleanup of expired documents
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of documents removed</returns>
    public async Task<int> CleanupExpiredDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        int totalExpired = 0;
        var now = DateTime.UtcNow;

        foreach (var collectionName in _ttlIndexes.Keys)
        {
            if (!_collectionLocks.TryGetValue(collectionName, out var lockObj))
                continue;

            await lockObj.WaitAsync(cancellationToken);
            try
            {
                var expired = await CleanupCollectionExpiredDocumentsAsync(collectionName, now, cancellationToken);
                totalExpired += expired;
            }
            finally
            {
                lockObj.Release();
            }
        }

        stopwatch.Stop();

        // Update statistics
        Interlocked.Add(ref _documentsExpired, totalExpired);
        Interlocked.Increment(ref _cleanupRuns);
        Interlocked.Add(ref _totalCleanupTimeMs, stopwatch.ElapsedMilliseconds);
        _lastCleanupTime = now;

        return totalExpired;
    }

    private async Task<int> CleanupCollectionExpiredDocumentsAsync(string collectionName, DateTime now, CancellationToken cancellationToken)
    {
        if (!_expirationQueues.TryGetValue(collectionName, out var queue))
            return 0;

        if (!_documentExpirationTimes.TryGetValue(collectionName, out var expirationDict))
            return 0;

        var expiredIds = new List<string>();

        // Peek at documents with earliest expiration times
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!queue.TryPeek(out var documentId, out var expirationTime))
                break;

            // Check if this document has been updated with a later expiration
            if (expirationDict.TryGetValue(documentId, out var actualExpiration))
            {
                if (actualExpiration > expirationTime)
                {
                    // Re-enqueue with correct time and continue
                    queue.Enqueue(documentId, actualExpiration);
                    queue.Dequeue(); // Remove old entry
                    continue;
                }
                expirationTime = actualExpiration;
            }

            // If not expired yet, stop (queue is ordered by expiration time)
            if (expirationTime > now)
                break;

            // Dequeue the expired document
            queue.Dequeue();
            expirationDict.TryRemove(documentId, out _);
            expiredIds.Add(documentId);

            // Delete the document if callback is provided
            if (_deleteDocumentCallback != null)
            {
                await _deleteDocumentCallback(collectionName, documentId);
            }
        }

        // Raise event if documents were expired
        if (expiredIds.Count > 0)
        {
            DocumentsExpired?.Invoke(this, new DocumentsExpiredEventArgs
            {
                CollectionName = collectionName,
                DocumentIds = expiredIds,
                ExpirationTime = now
            });
        }

        return expiredIds.Count;
    }

    /// <summary>
    /// Gets statistics for TTL index operations
    /// </summary>
    /// <returns>The current statistics</returns>
    public TtlIndexStatistics GetStatistics()
    {
        var runs = Interlocked.Read(ref _cleanupRuns);
        return new TtlIndexStatistics
        {
            DocumentsExpired = Interlocked.Read(ref _documentsExpired),
            DocumentsTracked = _documentExpirationTimes.Values.Sum(d => d.Count),
            LastCleanupTime = _lastCleanupTime,
            CleanupRuns = runs,
            AverageCleanupTimeMs = runs > 0 ? (double)Interlocked.Read(ref _totalCleanupTimeMs) / runs : 0
        };
    }

    /// <summary>
    /// Extracts the expiration time from a document based on TTL configuration
    /// </summary>
    private static DateTime? ExtractExpirationTime(Document document, TtlIndexConfiguration config)
    {
        // Document.Data is a Dictionary<string, object>
        var data = document.Data;
        
        if (data != null && data.TryGetValue(config.ExpireAfterField, out var expireValue))
        {
            // Try to parse as DateTime
            if (expireValue is DateTime dateTime)
            {
                return dateTime;
            }
            else if (expireValue is DateTimeOffset dateTimeOffset)
            {
                return dateTimeOffset.UtcDateTime;
            }
            else if (expireValue is string dateString)
            {
                if (DateTime.TryParse(dateString, out var parsedDateTime))
                {
                    return parsedDateTime;
                }
            }
            else if (expireValue is long timestamp)
            {
                // Unix timestamp (milliseconds)
                return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
            }
            else if (expireValue is int intTimestamp)
            {
                // Unix timestamp (seconds) - convert to milliseconds
                return DateTimeOffset.FromUnixTimeSeconds(intTimestamp).UtcDateTime;
            }
        }
        
        if (config.DefaultExpireAfter.HasValue)
        {
            // Use default expiration from document creation time
            if (document.CreatedAt != default)
            {
                return document.CreatedAt.Add(config.DefaultExpireAfter.Value);
            }
            // Fallback to current time + default expiration
            return DateTime.UtcNow.Add(config.DefaultExpireAfter.Value);
        }

        return null;
    }

    /// <summary>
    /// Starts the background cleanup service
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return Task.CompletedTask;

        _isRunning = true;

        // Start the cleanup timer with the shortest interval from all TTL indexes
        var shortestInterval = _ttlIndexes.Values.Count > 0
            ? _ttlIndexes.Values.Min(c => c.CleanupInterval)
            : _defaultCleanupInterval;

        // Use a simple background task approach instead of timer for async operations
        _ = Task.Run(async () =>
        {
            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(shortestInterval, cancellationToken);
                    if (_isRunning)
                    {
                        await CleanupExpiredDocumentsAsync(cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Ignore errors in background cleanup
                }
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the background cleanup service
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes the TTL index service
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _isRunning = false;

        _cleanupTimer?.Dispose();

        foreach (var lockObj in _collectionLocks.Values)
        {
            lockObj.Dispose();
        }
    }
}
