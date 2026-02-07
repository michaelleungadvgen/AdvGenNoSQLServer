// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Aggregation.Stages;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Query.Aggregation;

/// <summary>
/// Builder for creating aggregation pipelines with a fluent API
/// </summary>
public class AggregationPipelineBuilder
{
    private readonly List<IAggregationStage> _stages = new();
    private readonly IFilterEngine _filterEngine;

    /// <summary>
    /// Creates a new AggregationPipelineBuilder
    /// </summary>
    public AggregationPipelineBuilder(IFilterEngine filterEngine)
    {
        _filterEngine = filterEngine ?? throw new ArgumentNullException(nameof(filterEngine));
    }

    /// <summary>
    /// Adds a $match stage to filter documents
    /// </summary>
    public AggregationPipelineBuilder Match(QueryFilter filter)
    {
        _stages.Add(new MatchStage(_filterEngine, filter));
        return this;
    }

    /// <summary>
    /// Adds a $match stage to filter documents
    /// </summary>
    public AggregationPipelineBuilder Match(Dictionary<string, object> filterConditions)
    {
        _stages.Add(new MatchStage(_filterEngine, filterConditions));
        return this;
    }

    /// <summary>
    /// Adds a $group stage to group documents
    /// </summary>
    public AggregationPipelineBuilder Group(Dictionary<string, GroupFieldSpec> aggregations)
    {
        _stages.Add(new GroupStage(aggregations));
        return this;
    }

    /// <summary>
    /// Adds a $group stage to group documents by a field
    /// </summary>
    public AggregationPipelineBuilder Group(string groupByField, Dictionary<string, GroupFieldSpec> aggregations)
    {
        _stages.Add(new GroupStage(groupByField, aggregations));
        return this;
    }

    /// <summary>
    /// Adds a $project stage to reshape documents
    /// </summary>
    public AggregationPipelineBuilder Project(Dictionary<string, bool> projections)
    {
        _stages.Add(new ProjectStage(projections));
        return this;
    }

    /// <summary>
    /// Adds a $project stage with field mappings
    /// </summary>
    public AggregationPipelineBuilder Project(Dictionary<string, bool> projections, Dictionary<string, string> fieldMappings)
    {
        _stages.Add(new ProjectStage(projections, fieldMappings));
        return this;
    }

    /// <summary>
    /// Adds a $sort stage to sort documents
    /// </summary>
    public AggregationPipelineBuilder Sort(string fieldPath, bool ascending = true)
    {
        _stages.Add(new SortStage(fieldPath, ascending));
        return this;
    }

    /// <summary>
    /// Adds a $sort stage with multiple sort fields
    /// </summary>
    public AggregationPipelineBuilder Sort(IEnumerable<SortSpec> sortSpecs)
    {
        _stages.Add(new SortStage(sortSpecs));
        return this;
    }

    /// <summary>
    /// Adds a $sort stage from a dictionary
    /// </summary>
    public AggregationPipelineBuilder Sort(Dictionary<string, int> sortSpecs)
    {
        _stages.Add(new SortStage(sortSpecs));
        return this;
    }

    /// <summary>
    /// Adds a $limit stage to limit the number of documents
    /// </summary>
    public AggregationPipelineBuilder Limit(int limit)
    {
        _stages.Add(new LimitStage(limit));
        return this;
    }

    /// <summary>
    /// Adds a $skip stage to skip documents
    /// </summary>
    public AggregationPipelineBuilder Skip(int skip)
    {
        _stages.Add(new SkipStage(skip));
        return this;
    }

    /// <summary>
    /// Adds a custom stage to the pipeline
    /// </summary>
    public AggregationPipelineBuilder AddStage(IAggregationStage stage)
    {
        _stages.Add(stage ?? throw new ArgumentNullException(nameof(stage)));
        return this;
    }

    /// <summary>
    /// Builds the aggregation pipeline
    /// </summary>
    public AggregationPipeline Build()
    {
        return new AggregationPipeline(_stages);
    }

    /// <summary>
    /// Executes the pipeline on a sequence of documents
    /// </summary>
    public AggregationResult Execute(IEnumerable<Document> documents)
    {
        return Build().Execute(documents);
    }

    /// <summary>
    /// Executes the pipeline on a sequence of documents asynchronously
    /// </summary>
    public Task<AggregationResult> ExecuteAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        return Build().ExecuteAsync(documents, cancellationToken);
    }
}

/// <summary>
/// Static helper class for creating aggregation pipelines
/// </summary>
public static class Aggregation
{
    /// <summary>
    /// Creates a new pipeline builder
    /// </summary>
    public static AggregationPipelineBuilder New(IFilterEngine filterEngine)
    {
        return new AggregationPipelineBuilder(filterEngine);
    }

    /// <summary>
    /// Creates a sum aggregation spec
    /// </summary>
    public static GroupFieldSpec Sum(string fieldPath) => new(GroupOperator.Sum, fieldPath);

    /// <summary>
    /// Creates an average aggregation spec
    /// </summary>
    public static GroupFieldSpec Avg(string fieldPath) => new(GroupOperator.Avg, fieldPath);

    /// <summary>
    /// Creates a minimum aggregation spec
    /// </summary>
    public static GroupFieldSpec Min(string fieldPath) => new(GroupOperator.Min, fieldPath);

    /// <summary>
    /// Creates a maximum aggregation spec
    /// </summary>
    public static GroupFieldSpec Max(string fieldPath) => new(GroupOperator.Max, fieldPath);

    /// <summary>
    /// Creates a count aggregation spec
    /// </summary>
    public static GroupFieldSpec Count() => new(GroupOperator.Count);

    /// <summary>
    /// Creates a first value aggregation spec
    /// </summary>
    public static GroupFieldSpec First(string fieldPath) => new(GroupOperator.First, fieldPath);

    /// <summary>
    /// Creates a last value aggregation spec
    /// </summary>
    public static GroupFieldSpec Last(string fieldPath) => new(GroupOperator.Last, fieldPath);

    /// <summary>
    /// Creates a push to array aggregation spec
    /// </summary>
    public static GroupFieldSpec Push(string fieldPath) => new(GroupOperator.Push, fieldPath);

    /// <summary>
    /// Creates an add to set aggregation spec
    /// </summary>
    public static GroupFieldSpec AddToSet(string fieldPath) => new(GroupOperator.AddToSet, fieldPath);
}
