// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Query.Aggregation;

/// <summary>
/// Represents the result of an aggregation pipeline execution
/// </summary>
public class AggregationResult
{
    /// <summary>
    /// The documents produced by the aggregation pipeline
    /// </summary>
    public List<Document> Documents { get; set; } = new();

    /// <summary>
    /// The number of documents in the result
    /// </summary>
    public int Count => Documents.Count;

    /// <summary>
    /// Whether the aggregation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the aggregation failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Time taken to execute the aggregation in milliseconds
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Number of stages executed in the pipeline
    /// </summary>
    public int StagesExecuted { get; set; }

    /// <summary>
    /// Creates a successful aggregation result
    /// </summary>
    public static AggregationResult SuccessResult(List<Document> documents, int stagesExecuted, long executionTimeMs)
    {
        return new AggregationResult
        {
            Documents = documents,
            Success = true,
            StagesExecuted = stagesExecuted,
            ExecutionTimeMs = executionTimeMs
        };
    }

    /// <summary>
    /// Creates a failed aggregation result
    /// </summary>
    public static AggregationResult FailureResult(string errorMessage)
    {
        return new AggregationResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Documents = new List<Document>()
        };
    }
}
