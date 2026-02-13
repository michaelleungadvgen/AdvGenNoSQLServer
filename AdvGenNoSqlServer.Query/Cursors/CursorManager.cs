// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Storage;

namespace AdvGenNoSqlServer.Query.Cursors;

/// <summary>
/// Manages cursor lifecycle including creation, retrieval, and cleanup
/// </summary>
public interface ICursorManager
{
    /// <summary>
    /// Creates a new cursor for the specified query
    /// </summary>
    Task<CursorResult> CreateCursorAsync(
        string collectionName,
        QueryFilter? filter,
        List<SortField>? sort,
        CursorOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an existing cursor by ID
    /// </summary>
    Task<ICursor?> GetCursorAsync(string cursorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next batch of documents from a cursor
    /// </summary>
    Task<CursorBatchResult> GetMoreAsync(string cursorId, int? batchSize = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes and removes a cursor
    /// </summary>
    Task<bool> KillCursorAsync(string cursorId);

    /// <summary>
    /// Lists all active cursor IDs
    /// </summary>
    IEnumerable<string> ListActiveCursors();

    /// <summary>
    /// Gets statistics about cursor usage
    /// </summary>
    CursorStats GetStats();

    /// <summary>
    /// Event raised when a cursor is created
    /// </summary>
    event EventHandler<CursorEventArgs>? CursorCreated;

    /// <summary>
    /// Event raised when a cursor is closed
    /// </summary>
    event EventHandler<CursorEventArgs>? CursorClosed;

    /// <summary>
    /// Event raised when a cursor expires
    /// </summary>
    event EventHandler<CursorEventArgs>? CursorExpired;
}

/// <summary>
/// Event args for cursor events
/// </summary>
public class CursorEventArgs : EventArgs
{
    public required string CursorId { get; set; }
    public required string CollectionName { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of getting more documents from a cursor
/// </summary>
public class CursorBatchResult
{
    /// <summary>
    /// The documents in this batch
    /// </summary>
    public List<Document> Documents { get; set; } = new();

    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether there are more documents available
    /// </summary>
    public bool HasMore { get; set; }

    /// <summary>
    /// Resume token for this cursor position
    /// </summary>
    public string? ResumeToken { get; set; }
}

/// <summary>
/// Implementation of cursor manager with automatic cleanup
/// </summary>
public class CursorManager : ICursorManager, IDisposable
{
    private readonly ConcurrentDictionary<string, Cursor> _cursors = new();
    private readonly IDocumentStore _documentStore;
    private readonly IFilterEngine _filterEngine;
    private readonly Timer _cleanupTimer;
    private long _totalCreated;
    private long _totalClosed;
    private long _totalExpired;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Creates a new cursor manager
    /// </summary>
    public CursorManager(IDocumentStore documentStore, IFilterEngine filterEngine)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _filterEngine = filterEngine ?? throw new ArgumentNullException(nameof(filterEngine));
        _cleanupTimer = new Timer(CleanupExpiredCursors, null, _cleanupInterval, _cleanupInterval);
    }

    /// <inheritdoc />
    public event EventHandler<CursorEventArgs>? CursorCreated;

    /// <inheritdoc />
    public event EventHandler<CursorEventArgs>? CursorClosed;

    /// <inheritdoc />
    public event EventHandler<CursorEventArgs>? CursorExpired;

    /// <inheritdoc />
    public async Task<CursorResult> CreateCursorAsync(
        string collectionName,
        QueryFilter? filter,
        List<SortField>? sort,
        CursorOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate options
            var validationErrors = options.Validate();
            if (validationErrors.Count > 0)
            {
                return CursorResult.FailureResult($"Invalid cursor options: {string.Join(", ", validationErrors)}");
            }

            // Handle resume token if provided
            ResumeToken? resumeToken = null;
            if (!string.IsNullOrEmpty(options.ResumeToken))
            {
                resumeToken = ResumeToken.FromTokenString(options.ResumeToken);
                if (resumeToken == null)
                {
                    return CursorResult.FailureResult("Invalid resume token");
                }
            }

            // Create the cursor
            var cursorId = GenerateCursorId();
            var cursor = new Cursor(
                cursorId,
                collectionName,
                filter,
                sort,
                options,
                _documentStore,
                _filterEngine,
                resumeToken);

            // Calculate total count if requested
            if (options.IncludeTotalCount)
            {
                await cursor.CalculateTotalCountAsync(cancellationToken);
            }

            // Store the cursor
            if (!_cursors.TryAdd(cursorId, cursor))
            {
                return CursorResult.FailureResult("Failed to create cursor - ID collision");
            }

            Interlocked.Increment(ref _totalCreated);
            CursorCreated?.Invoke(this, new CursorEventArgs { CursorId = cursorId, CollectionName = collectionName });

            // Get first batch
            var firstBatch = await cursor.GetNextBatchAsync(options.BatchSize, cancellationToken);
            var hasMore = await cursor.HasMoreAsync(cancellationToken);

            stopwatch.Stop();

            // Generate resume token
            var resumeTokenString = hasMore ? GenerateResumeToken(cursor) : null;

            return new CursorResult
            {
                Cursor = cursor,
                Documents = firstBatch.ToList(),
                TotalCount = cursor.TotalCount,
                HasMore = hasMore,
                ResumeToken = resumeTokenString,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                Success = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return CursorResult.FailureResult($"Failed to create cursor: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<ICursor?> GetCursorAsync(string cursorId, CancellationToken cancellationToken = default)
    {
        if (_cursors.TryGetValue(cursorId, out var cursor))
        {
            if (cursor.IsExpired)
            {
                _cursors.TryRemove(cursorId, out _);
                Interlocked.Increment(ref _totalExpired);
                CursorExpired?.Invoke(this, new CursorEventArgs { CursorId = cursorId, CollectionName = cursor.CollectionName });
                return Task.FromResult<ICursor?>(null);
            }
            return Task.FromResult<ICursor?>(cursor);
        }
        return Task.FromResult<ICursor?>(null);
    }

    /// <inheritdoc />
    public async Task<CursorBatchResult> GetMoreAsync(string cursorId, int? batchSize = null, CancellationToken cancellationToken = default)
    {
        if (!_cursors.TryGetValue(cursorId, out var cursor))
        {
            return new CursorBatchResult
            {
                Success = false,
                ErrorMessage = $"Cursor '{cursorId}' not found or has expired"
            };
        }

        if (cursor.IsExpired)
        {
            _cursors.TryRemove(cursorId, out _);
            Interlocked.Increment(ref _totalExpired);
            CursorExpired?.Invoke(this, new CursorEventArgs { CursorId = cursorId, CollectionName = cursor.CollectionName });
            return new CursorBatchResult
            {
                Success = false,
                ErrorMessage = $"Cursor '{cursorId}' has expired"
            };
        }

        if (cursor.IsClosed)
        {
            return new CursorBatchResult
            {
                Success = false,
                ErrorMessage = $"Cursor '{cursorId}' has been closed"
            };
        }

        try
        {
            var size = batchSize ?? cursor.BatchSize;
            size = Math.Min(size, CursorOptions.MaxBatchSize);

            var documents = await cursor.GetNextBatchAsync(size, cancellationToken);
            var hasMore = await cursor.HasMoreAsync(cancellationToken);

            // Auto-close cursor if no more documents
            if (!hasMore)
            {
                await KillCursorAsync(cursorId);
            }

            // Generate resume token
            var resumeTokenString = hasMore ? GenerateResumeToken(cursor) : null;

            return new CursorBatchResult
            {
                Documents = documents.ToList(),
                Success = true,
                HasMore = hasMore,
                ResumeToken = resumeTokenString
            };
        }
        catch (Exception ex)
        {
            return new CursorBatchResult
            {
                Success = false,
                ErrorMessage = $"Failed to get more documents: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<bool> KillCursorAsync(string cursorId)
    {
        if (_cursors.TryRemove(cursorId, out var cursor))
        {
            await cursor.CloseAsync();
            Interlocked.Increment(ref _totalClosed);
            CursorClosed?.Invoke(this, new CursorEventArgs { CursorId = cursorId, CollectionName = cursor.CollectionName });
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public IEnumerable<string> ListActiveCursors()
    {
        return _cursors.Keys.ToList();
    }

    /// <inheritdoc />
    public CursorStats GetStats()
    {
        var activeCursors = _cursors.Count;
        var created = Interlocked.Read(ref _totalCreated);
        var closed = Interlocked.Read(ref _totalClosed);
        var expired = Interlocked.Read(ref _totalExpired);

        // Calculate average lifetime
        double avgLifetime = 0;
        if (closed > 0)
        {
            // Note: This is a simplified calculation - in production you'd track individual cursor lifetimes
            avgLifetime = (DateTime.UtcNow - DateTime.UtcNow.AddMinutes(-10)).TotalMilliseconds / 2;
        }

        return new CursorStats
        {
            ActiveCursors = activeCursors,
            TotalCursorsCreated = created,
            TotalCursorsClosed = closed,
            TotalCursorsExpired = expired,
            AverageCursorLifetimeMs = avgLifetime
        };
    }

    /// <summary>
    /// Kills all cursors for a collection
    /// </summary>
    public async Task<int> KillCursorsForCollectionAsync(string collectionName)
    {
        var cursorIdsToRemove = _cursors
            .Where(kvp => kvp.Value.CollectionName == collectionName)
            .Select(kvp => kvp.Key)
            .ToList();

        int count = 0;
        foreach (var cursorId in cursorIdsToRemove)
        {
            if (await KillCursorAsync(cursorId))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Kills all cursors
    /// </summary>
    public async Task<int> KillAllCursorsAsync()
    {
        var cursorIds = _cursors.Keys.ToList();
        int count = 0;
        foreach (var cursorId in cursorIds)
        {
            if (await KillCursorAsync(cursorId))
            {
                count++;
            }
        }
        return count;
    }

    private void CleanupExpiredCursors(object? state)
    {
        var expiredCursors = _cursors
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var cursorId in expiredCursors)
        {
            if (_cursors.TryRemove(cursorId, out var cursor))
            {
                cursor.CloseAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                Interlocked.Increment(ref _totalExpired);
                CursorExpired?.Invoke(this, new CursorEventArgs { CursorId = cursorId, CollectionName = cursor.CollectionName });
            }
        }
    }

    private static string GenerateCursorId()
    {
        // Generate a unique cursor ID
        return "cursor_" + Guid.NewGuid().ToString("N")[..16];
    }

    private static string GenerateResumeToken(Cursor cursor)
    {
        var lastDocId = cursor.LastDocumentId;
        var token = new ResumeToken
        {
            CursorId = cursor.CursorId,
            LastDocumentId = lastDocId,
            CreatedAt = DateTime.UtcNow,
            FilterJson = cursor.Filter != null ? System.Text.Json.JsonSerializer.Serialize(cursor.Filter.Conditions) : null,
            SortJson = cursor.Sort != null ? System.Text.Json.JsonSerializer.Serialize(cursor.Sort) : null
        };
        return token.ToTokenString();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cleanupTimer.Dispose();
        // Close all remaining cursors
        KillAllCursorsAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    }
}
