// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Query.Aggregation.Stages;

/// <summary>
/// $limit stage - Limits the number of documents passed to the next stage
/// </summary>
public class LimitStage : IAggregationStage
{
    private readonly int _limit;

    /// <inheritdoc />
    public string StageType => "$limit";

    /// <summary>
    /// The maximum number of documents to return
    /// </summary>
    public int Limit => _limit;

    /// <summary>
    /// Creates a new LimitStage
    /// </summary>
    /// <param name="limit">Maximum number of documents (must be positive)</param>
    public LimitStage(int limit)
    {
        if (limit < 0)
            throw new ArgumentException("Limit must be non-negative", nameof(limit));
        
        _limit = limit;
    }

    /// <inheritdoc />
    public IEnumerable<Document> Execute(IEnumerable<Document> documents)
    {
        try
        {
            return documents.Take(_limit).ToList();
        }
        catch (Exception ex)
        {
            throw new AggregationStageException(StageType, $"Failed to execute limit stage: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> ExecuteAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Execute(documents));
    }
}
