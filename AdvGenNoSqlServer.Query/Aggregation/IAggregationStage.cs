// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Query.Aggregation;

/// <summary>
/// Interface for a single aggregation pipeline stage
/// </summary>
public interface IAggregationStage
{
    /// <summary>
    /// The type of aggregation stage (e.g., "$match", "$group", "$project")
    /// </summary>
    string StageType { get; }

    /// <summary>
    /// Executes this stage on a sequence of documents
    /// </summary>
    /// <param name="documents">Input documents</param>
    /// <returns>Transformed documents</returns>
    IEnumerable<Document> Execute(IEnumerable<Document> documents);

    /// <summary>
    /// Executes this stage on a sequence of documents asynchronously
    /// </summary>
    /// <param name="documents">Input documents</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transformed documents</returns>
    Task<IEnumerable<Document>> ExecuteAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default);
}

/// <summary>
/// Exception thrown when an aggregation stage encounters an error
/// </summary>
public class AggregationStageException : Exception
{
    /// <summary>
    /// The type of stage that threw the exception
    /// </summary>
    public string StageType { get; }

    /// <summary>
    /// Creates a new AggregationStageException
    /// </summary>
    public AggregationStageException(string stageType, string message) : base(message)
    {
        StageType = stageType;
    }

    /// <summary>
    /// Creates a new AggregationStageException with an inner exception
    /// </summary>
    public AggregationStageException(string stageType, string message, Exception innerException) 
        : base(message, innerException)
    {
        StageType = stageType;
    }
}

/// <summary>
/// Exception thrown when an aggregation pipeline encounters an error
/// </summary>
public class AggregationPipelineException : Exception
{
    /// <summary>
    /// Creates a new AggregationPipelineException
    /// </summary>
    public AggregationPipelineException(string message) : base(message) { }

    /// <summary>
    /// Creates a new AggregationPipelineException with an inner exception
    /// </summary>
    public AggregationPipelineException(string message, Exception innerException) 
        : base(message, innerException) { }
}
