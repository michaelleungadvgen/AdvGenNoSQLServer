// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Query.Aggregation.Stages;

/// <summary>
/// $skip stage - Skips a specified number of documents
/// </summary>
public class SkipStage : IAggregationStage
{
    private readonly int _skip;

    /// <inheritdoc />
    public string StageType => "$skip";

    /// <summary>
    /// The number of documents to skip
    /// </summary>
    public int Skip => _skip;

    /// <summary>
    /// Creates a new SkipStage
    /// </summary>
    /// <param name="skip">Number of documents to skip (must be non-negative)</param>
    public SkipStage(int skip)
    {
        if (skip < 0)
            throw new ArgumentException("Skip must be non-negative", nameof(skip));
        
        _skip = skip;
    }

    /// <inheritdoc />
    public IEnumerable<Document> Execute(IEnumerable<Document> documents)
    {
        try
        {
            return documents.Skip(_skip).ToList();
        }
        catch (Exception ex)
        {
            throw new AggregationStageException(StageType, $"Failed to execute skip stage: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> ExecuteAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Execute(documents));
    }
}
