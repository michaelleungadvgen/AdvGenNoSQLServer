// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.WriteConcern;

/// <summary>
/// Represents the result of a write operation with write concern information.
/// </summary>
public class WriteConcernResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the write operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the document affected by the write operation.
    /// </summary>
    public Document? Document { get; set; }

    /// <summary>
    /// Gets or sets the write concern that was used for this operation.
    /// </summary>
    public WriteConcern WriteConcern { get; set; } = WriteConcern.Acknowledged;

    /// <summary>
    /// Gets or sets the number of documents affected by the write operation.
    /// </summary>
    public int AffectedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of nodes that acknowledged the write.
    /// </summary>
    public int AcknowledgedByNodes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the write was flushed to the journal.
    /// </summary>
    public bool WasJournaled { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the write was acknowledged.
    /// </summary>
    public DateTime AcknowledgedAt { get; set; }

    /// <summary>
    /// Gets or sets the execution time of the write operation.
    /// </summary>
    public TimeSpan ExecutionTime { get; set; }

    /// <summary>
    /// Gets or sets an error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the exception if the operation failed.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Gets a value indicating whether the operation was acknowledged.
    /// </summary>
    public bool IsAcknowledged => Success && WriteConcern.IsAcknowledged;

    /// <summary>
    /// Creates a successful result for an insert operation.
    /// </summary>
    public static WriteConcernResult SuccessResult(Document document, WriteConcern writeConcern)
    {
        return new WriteConcernResult
        {
            Success = true,
            Document = document,
            WriteConcern = writeConcern,
            AffectedCount = 1,
            AcknowledgedByNodes = 1, // In standalone mode, always 1
            WasJournaled = writeConcern.IsJournaled,
            AcknowledgedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a successful result for an update operation.
    /// </summary>
    public static WriteConcernResult UpdateResult(Document document, WriteConcern writeConcern)
    {
        return new WriteConcernResult
        {
            Success = true,
            Document = document,
            WriteConcern = writeConcern,
            AffectedCount = 1,
            AcknowledgedByNodes = 1,
            WasJournaled = writeConcern.IsJournaled,
            AcknowledgedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a successful result for a delete operation.
    /// </summary>
    public static WriteConcernResult DeleteResult(bool deleted, WriteConcern writeConcern)
    {
        return new WriteConcernResult
        {
            Success = true,
            Document = null,
            WriteConcern = writeConcern,
            AffectedCount = deleted ? 1 : 0,
            AcknowledgedByNodes = 1,
            WasJournaled = writeConcern.IsJournaled,
            AcknowledgedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a successful result for an unacknowledged operation.
    /// </summary>
    public static WriteConcernResult UnacknowledgedResult()
    {
        return new WriteConcernResult
        {
            Success = true,
            WriteConcern = WriteConcern.Unacknowledged,
            AffectedCount = 0,
            AcknowledgedByNodes = 0,
            WasJournaled = false,
            AcknowledgedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static WriteConcernResult FailureResult(string errorMessage, Exception? exception = null)
    {
        return new WriteConcernResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Exception = exception,
            WriteConcern = WriteConcern.Acknowledged,
            AcknowledgedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a failure result with write concern.
    /// </summary>
    public static WriteConcernResult FailureResult(WriteConcern writeConcern, string errorMessage, Exception? exception = null)
    {
        return new WriteConcernResult
        {
            Success = false,
            WriteConcern = writeConcern,
            ErrorMessage = errorMessage,
            Exception = exception,
            AcknowledgedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a result for a collection operation (create/drop/clear).
    /// </summary>
    public static WriteConcernResult CollectionOperationResult(bool success, string operation, WriteConcern writeConcern)
    {
        return new WriteConcernResult
        {
            Success = success,
            WriteConcern = writeConcern,
            AffectedCount = 0,
            AcknowledgedByNodes = 1,
            WasJournaled = writeConcern.IsJournaled,
            AcknowledgedAt = DateTime.UtcNow,
            ErrorMessage = success ? null : $"{operation} operation failed"
        };
    }
}

/// <summary>
/// Represents a batch of write concern results.
/// </summary>
public class WriteConcernBatchResult
{
    /// <summary>
    /// Gets or sets a value indicating whether all operations in the batch were successful.
    /// </summary>
    public bool AllSuccessful { get; set; }

    /// <summary>
    /// Gets or sets the individual results for each operation in the batch.
    /// </summary>
    public List<WriteConcernResult> Results { get; set; } = new();

    /// <summary>
    /// Gets or sets the total number of documents affected.
    /// </summary>
    public int TotalAffectedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of successful operations.
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Gets or sets the number of failed operations.
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Gets or sets the write concern used for the batch.
    /// </summary>
    public WriteConcern WriteConcern { get; set; } = WriteConcern.Acknowledged;

    /// <summary>
    /// Gets or sets the execution time for the entire batch.
    /// </summary>
    public TimeSpan ExecutionTime { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the batch was completed.
    /// </summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>
    /// Creates a result from a collection of individual results.
    /// </summary>
    public static WriteConcernBatchResult FromResults(IEnumerable<WriteConcernResult> results, WriteConcern writeConcern)
    {
        var resultList = results.ToList();
        var successCount = resultList.Count(r => r.Success);
        var failureCount = resultList.Count - successCount;

        return new WriteConcernBatchResult
        {
            Results = resultList,
            AllSuccessful = failureCount == 0,
            SuccessCount = successCount,
            FailureCount = failureCount,
            TotalAffectedCount = resultList.Sum(r => r.AffectedCount),
            WriteConcern = writeConcern,
            CompletedAt = DateTime.UtcNow
        };
    }
}
