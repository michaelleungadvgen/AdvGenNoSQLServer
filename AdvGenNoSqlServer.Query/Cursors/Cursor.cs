// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Query.Cursors;

/// <summary>
/// Interface for cursor-based pagination of query results
/// </summary>
public interface ICursor : IAsyncDisposable
{
    /// <summary>
    /// Unique identifier for this cursor
    /// </summary>
    string CursorId { get; }

    /// <summary>
    /// The collection being queried
    /// </summary>
    string CollectionName { get; }

    /// <summary>
    /// The filter used for this query
    /// </summary>
    QueryFilter? Filter { get; }

    /// <summary>
    /// The sort specification
    /// </summary>
    List<SortField>? Sort { get; }

    /// <summary>
    /// Gets the next batch of documents from the cursor
    /// </summary>
    /// <param name="batchSize">Number of documents to return (max 1000)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of documents in the next batch</returns>
    Task<IReadOnlyList<Document>> GetNextBatchAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if there are more documents available
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if more documents are available, false otherwise</returns>
    Task<bool> HasMoreAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Total count of matching documents (if IncludeTotalCount was true when cursor was created)
    /// </summary>
    long? TotalCount { get; }

    /// <summary>
    /// Number of documents returned so far
    /// </summary>
    long DocumentsReturned { get; }

    /// <summary>
    /// When the cursor was created
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// When the cursor will expire
    /// </summary>
    DateTime ExpiresAt { get; }

    /// <summary>
    /// Whether the cursor has expired
    /// </summary>
    bool IsExpired => DateTime.UtcNow > ExpiresAt;

    /// <summary>
    /// Whether the cursor has been closed
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Closes the cursor and releases resources
    /// </summary>
    Task CloseAsync();
}

/// <summary>
/// Represents a resume token for cursor continuation after disconnect
/// </summary>
public class ResumeToken
{
    /// <summary>
    /// The cursor ID
    /// </summary>
    public required string CursorId { get; set; }

    /// <summary>
    /// The last document ID that was returned
    /// </summary>
    public string? LastDocumentId { get; set; }

    /// <summary>
    /// Timestamp of when the token was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The query filter (serialized)
    /// </summary>
    public string? FilterJson { get; set; }

    /// <summary>
    /// The sort specification (serialized)
    /// </summary>
    public string? SortJson { get; set; }

    /// <summary>
    /// Serializes the resume token to a string
    /// </summary>
    public string ToTokenString()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Deserializes a resume token from a string
    /// </summary>
    public static ResumeToken? FromTokenString(string tokenString)
    {
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(tokenString));
            return System.Text.Json.JsonSerializer.Deserialize<ResumeToken>(json);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Options for creating a cursor
/// </summary>
public class CursorOptions
{
    /// <summary>
    /// Default batch size if not specified
    /// </summary>
    public const int DefaultBatchSize = 101;

    /// <summary>
    /// Maximum allowed batch size
    /// </summary>
    public const int MaxBatchSize = 10000;

    /// <summary>
    /// Default cursor timeout in minutes
    /// </summary>
    public const int DefaultTimeoutMinutes = 10;

    /// <summary>
    /// Maximum allowed cursor timeout in minutes
    /// </summary>
    public const int MaxTimeoutMinutes = 60;

    /// <summary>
    /// Number of documents to return per batch
    /// </summary>
    public int BatchSize { get; set; } = DefaultBatchSize;

    /// <summary>
    /// Whether to include the total count of matching documents
    /// </summary>
    public bool IncludeTotalCount { get; set; }

    /// <summary>
    /// Cursor timeout in minutes (after which the cursor is automatically closed)
    /// </summary>
    public int TimeoutMinutes { get; set; } = DefaultTimeoutMinutes;

    /// <summary>
    /// Optional resume token for continuing a previous cursor
    /// </summary>
    public string? ResumeToken { get; set; }

    /// <summary>
    /// Validates the options and returns any errors
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (BatchSize < 1)
            errors.Add("BatchSize must be at least 1");

        if (BatchSize > MaxBatchSize)
            errors.Add($"BatchSize cannot exceed {MaxBatchSize}");

        if (TimeoutMinutes < 1)
            errors.Add("TimeoutMinutes must be at least 1");

        if (TimeoutMinutes > MaxTimeoutMinutes)
            errors.Add($"TimeoutMinutes cannot exceed {MaxTimeoutMinutes}");

        return errors;
    }
}

/// <summary>
/// Result of a cursor-based query
/// </summary>
public class CursorResult
{
    /// <summary>
    /// The cursor for retrieving more results
    /// </summary>
    public ICursor? Cursor { get; set; }

    /// <summary>
    /// The documents in the current batch
    /// </summary>
    public List<Document> Documents { get; set; } = new();

    /// <summary>
    /// Total count of matching documents (if requested)
    /// </summary>
    public long? TotalCount { get; set; }

    /// <summary>
    /// Whether there are more documents available
    /// </summary>
    public bool HasMore { get; set; }

    /// <summary>
    /// Resume token for continuing after disconnect
    /// </summary>
    public string? ResumeToken { get; set; }

    /// <summary>
    /// Query execution time in milliseconds
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Whether the query was successful
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message if the query failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a successful cursor result
    /// </summary>
    public static CursorResult SuccessResult(ICursor cursor, List<Document> documents, bool hasMore, long executionTimeMs)
    {
        return new CursorResult
        {
            Cursor = cursor,
            Documents = documents,
            TotalCount = cursor.TotalCount,
            HasMore = hasMore,
            ExecutionTimeMs = executionTimeMs,
            Success = true
        };
    }

    /// <summary>
    /// Creates a failed cursor result
    /// </summary>
    public static CursorResult FailureResult(string errorMessage)
    {
        return new CursorResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Documents = new List<Document>()
        };
    }
}

/// <summary>
/// Statistics for cursor management
/// </summary>
public class CursorStats
{
    /// <summary>
    /// Total number of active cursors
    /// </summary>
    public int ActiveCursors { get; set; }

    /// <summary>
    /// Total number of cursors created
    /// </summary>
    public long TotalCursorsCreated { get; set; }

    /// <summary>
    /// Total number of cursors closed
    /// </summary>
    public long TotalCursorsClosed { get; set; }

    /// <summary>
    /// Total number of cursors expired
    /// </summary>
    public long TotalCursorsExpired { get; set; }

    /// <summary>
    /// Average cursor lifetime in milliseconds
    /// </summary>
    public double AverageCursorLifetimeMs { get; set; }
}

/// <summary>
/// Exception thrown when cursor operations fail
/// </summary>
public class CursorException : Exception
{
    public CursorException(string message) : base(message) { }
    public CursorException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a cursor is not found
/// </summary>
public class CursorNotFoundException : CursorException
{
    public CursorNotFoundException(string cursorId) : base($"Cursor '{cursorId}' not found or has expired") { }
}

/// <summary>
/// Exception thrown when cursor options are invalid
/// </summary>
public class InvalidCursorOptionsException : CursorException
{
    public InvalidCursorOptionsException(string message) : base(message) { }
    public InvalidCursorOptionsException(IEnumerable<string> errors) : base($"Invalid cursor options: {string.Join(", ", errors)}") { }
}
