// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Query.Models;

/// <summary>
/// Represents the result of a query execution
/// </summary>
public class QueryResult
{
    /// <summary>
    /// The documents matching the query
    /// </summary>
    public List<Document> Documents { get; set; } = new();

    /// <summary>
    /// Total number of documents matching the query (before pagination)
    /// </summary>
    public long TotalCount { get; set; }

    /// <summary>
    /// Number of documents skipped
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    /// Number of documents returned
    /// </summary>
    public int ReturnedCount => Documents.Count;

    /// <summary>
    /// Whether there are more results available
    /// </summary>
    public bool HasMore => Skipped + ReturnedCount < TotalCount;

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
    /// Creates a successful result with documents
    /// </summary>
    public static QueryResult SuccessResult(List<Document> documents, long totalCount, int skipped, long executionTimeMs)
    {
        return new QueryResult
        {
            Documents = documents,
            TotalCount = totalCount,
            Skipped = skipped,
            ExecutionTimeMs = executionTimeMs,
            Success = true
        };
    }

    /// <summary>
    /// Creates a failed result with an error message
    /// </summary>
    public static QueryResult FailureResult(string errorMessage)
    {
        return new QueryResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Documents = new List<Document>(),
            TotalCount = 0
        };
    }

    /// <summary>
    /// Creates an empty result
    /// </summary>
    public static QueryResult EmptyResult()
    {
        return new QueryResult
        {
            Documents = new List<Document>(),
            TotalCount = 0,
            Success = true
        };
    }
}

/// <summary>
/// Represents statistics about a query execution
/// </summary>
public class QueryStats
{
    /// <summary>
    /// Query execution time in milliseconds
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Number of documents scanned
    /// </summary>
    public long DocumentsScanned { get; set; }

    /// <summary>
    /// Number of documents returned
    /// </summary>
    public long DocumentsReturned { get; set; }

    /// <summary>
    /// Whether an index was used for the query
    /// </summary>
    public bool IndexUsed { get; set; }

    /// <summary>
    /// Name of the index used (if any)
    /// </summary>
    public string? IndexName { get; set; }

    /// <summary>
    /// Stage-by-stage execution plan
    /// </summary>
    public List<QueryPlanStage>? ExecutionPlan { get; set; }
}

/// <summary>
/// Represents a stage in the query execution plan
/// </summary>
public class QueryPlanStage
{
    /// <summary>
    /// Name of the stage (e.g., "CollectionScan", "IndexScan", "Filter", "Sort")
    /// </summary>
    public required string StageName { get; set; }

    /// <summary>
    /// Time spent in this stage (milliseconds)
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Number of documents processed in this stage
    /// </summary>
    public long DocumentsProcessed { get; set; }

    /// <summary>
    /// Stage-specific details
    /// </summary>
    public Dictionary<string, object>? Details { get; set; }
}
