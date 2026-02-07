// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Query.Aggregation.Stages;

/// <summary>
/// $match stage - Filters documents based on query criteria
/// </summary>
public class MatchStage : IAggregationStage
{
    private readonly IFilterEngine _filterEngine;
    private readonly QueryFilter _filter;

    /// <inheritdoc />
    public string StageType => "$match";

    /// <summary>
    /// Creates a new MatchStage with a filter engine and filter criteria
    /// </summary>
    public MatchStage(IFilterEngine filterEngine, QueryFilter filter)
    {
        _filterEngine = filterEngine ?? throw new ArgumentNullException(nameof(filterEngine));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
    }

    /// <summary>
    /// Creates a new MatchStage with a filter engine and filter conditions
    /// </summary>
    public MatchStage(IFilterEngine filterEngine, Dictionary<string, object> filterConditions)
    {
        _filterEngine = filterEngine ?? throw new ArgumentNullException(nameof(filterEngine));
        _filter = new QueryFilter();
        _filter.Conditions = filterConditions ?? throw new ArgumentNullException(nameof(filterConditions));
    }

    /// <inheritdoc />
    public IEnumerable<Document> Execute(IEnumerable<Document> documents)
    {
        try
        {
            return _filterEngine.Filter(documents, _filter);
        }
        catch (Exception ex)
        {
            throw new AggregationStageException(StageType, $"Failed to execute match stage: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> ExecuteAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Execute(documents));
    }
}
