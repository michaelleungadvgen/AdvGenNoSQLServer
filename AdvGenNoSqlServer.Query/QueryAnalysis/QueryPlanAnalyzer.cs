// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using System.Text.Json;
using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Indexing;

namespace AdvGenNoSqlServer.Query.QueryAnalysis;

/// <summary>
/// Implementation of query plan analysis service
/// </summary>
public class QueryPlanAnalyzer : IQueryPlanAnalyzer
{
    private readonly IDocumentStore _documentStore;
    private readonly IndexManager? _indexManager;

    /// <summary>
    /// Creates a new QueryPlanAnalyzer
    /// </summary>
    public QueryPlanAnalyzer(IDocumentStore documentStore, IndexManager? indexManager = null)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _indexManager = indexManager;
    }

    /// <inheritdoc />
    public async Task<QueryAnalysisResult> AnalyzeAsync(
        Query.Models.Query query,
        ExplainVerbosity verbosity = ExplainVerbosity.ExecutionStats,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Build query info
        var queryInfo = BuildQueryInfo(query);

        // Analyze execution plan
        var executionPlan = await BuildExecutionPlanAsync(query, cancellationToken);

        // Calculate summary statistics
        var summary = BuildExecutionSummary(query, executionPlan);

        // Generate recommendations
        var indexRecommendations = await GetIndexRecommendationsAsync(query, cancellationToken);
        var suggestions = await GetOptimizationSuggestionsAsync(query, cancellationToken);

        // Calculate complexity
        var complexityScore = CalculateComplexityScore(query);

        // Build alternative plans for high verbosity
        var alternativePlans = verbosity == ExplainVerbosity.AllPlansExecution
            ? BuildAlternativePlans(query, executionPlan)
            : new List<AlternativePlan>();

        stopwatch.Stop();

        return new QueryAnalysisResult
        {
            Query = queryInfo,
            ExecutionPlan = executionPlan,
            Summary = summary,
            IndexRecommendations = indexRecommendations,
            Suggestions = suggestions,
            AlternativePlans = alternativePlans,
            ComplexityScore = complexityScore,
            IsSlowQuery = summary.EstimatedExecutionTimeMs > 100,
            AnalyzedAt = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public Task<List<IndexRecommendation>> GetIndexRecommendationsAsync(
        Query.Models.Query query,
        CancellationToken cancellationToken = default)
    {
        var recommendations = new List<IndexRecommendation>();

        // Check if query has filter conditions
        if (query.Filter?.Conditions != null && query.Filter.Conditions.Count > 0)
        {
            foreach (var condition in query.Filter.Conditions)
            {
                // Skip logical operators
                if (condition.Key.StartsWith('$'))
                    continue;

                // Check if this field has an index
                var hasIndex = _indexManager?.HasIndex(query.CollectionName, condition.Key) ?? false;

                if (!hasIndex)
                {
                    var currentImpact = EstimateCurrentImpact(query, condition.Key);
                    var improvement = EstimateImprovement(query, condition.Key);

                    recommendations.Add(new IndexRecommendation
                    {
                        Priority = "High",
                        Fields = new List<string> { condition.Key },
                        IsUnique = false,
                        CurrentImpact = currentImpact,
                        PerformanceImprovement = improvement,
                        CreateIndexCommand = $"CREATE_INDEX {query.CollectionName} {{\"field\": \"{condition.Key}\", \"unique\": false}}",
                        Explanation = $"Adding an index on '{condition.Key}' would avoid a collection scan for queries filtering on this field."
                    });
                }
            }
        }

        // Check for compound index recommendations
        var compoundRecommendation = AnalyzeForCompoundIndex(query);
        if (compoundRecommendation != null)
        {
            recommendations.Add(compoundRecommendation);
        }

        // Check for sort optimization
        var sortRecommendation = AnalyzeSortOptimization(query);
        if (sortRecommendation != null)
        {
            recommendations.Add(sortRecommendation);
        }

        return Task.FromResult(recommendations);
    }

    /// <inheritdoc />
    public Task<List<OptimizationSuggestion>> GetOptimizationSuggestionsAsync(
        Query.Models.Query query,
        CancellationToken cancellationToken = default)
    {
        var suggestions = new List<OptimizationSuggestion>();

        // Check for unbounded queries
        if ((query.Options?.Limit == null) && (query.Filter?.Conditions == null || query.Filter.Conditions.Count == 0))
        {
            suggestions.Add(new OptimizationSuggestion
            {
                Type = "Pagination",
                Severity = "High",
                Title = "Unbounded Query Detected",
                Description = "This query has no filter and no limit, which will scan the entire collection.",
                CurrentImplementation = "No LIMIT specified",
                SuggestedImprovement = "Add a limit (e.g., LIMIT 100) or use pagination",
                ExpectedBenefit = "Significant reduction in memory usage and execution time"
            });
        }

        // Check for expensive skip operations
        if (query.Options?.Skip > 1000)
        {
            suggestions.Add(new OptimizationSuggestion
            {
                Type = "Pagination",
                Severity = "Medium",
                Title = "Large Skip Value",
                Description = $"Skip value of {query.Options.Skip} requires scanning and discarding many documents.",
                CurrentImplementation = $"SKIP {query.Options.Skip}",
                SuggestedImprovement = "Consider using cursor-based pagination instead",
                ExpectedBenefit = "Better performance for deep pagination"
            });
        }

        // Check for filter on non-indexed fields
        if (query.Filter?.Conditions != null)
        {
            var nonIndexedFilters = query.Filter.Conditions
                .Where(c => !c.Key.StartsWith('$') && !(_indexManager?.HasIndex(query.CollectionName, c.Key) ?? false))
                .Select(c => c.Key)
                .ToList();

            if (nonIndexedFilters.Any())
            {
                suggestions.Add(new OptimizationSuggestion
                {
                    Type = "FilterOptimization",
                    Severity = "Medium",
                    Title = "Filters on Non-Indexed Fields",
                    Description = $"Query filters on non-indexed fields: {string.Join(", ", nonIndexedFilters)}",
                    CurrentImplementation = "Collection scan required",
                    SuggestedImprovement = $"Create indexes on: {string.Join(", ", nonIndexedFilters)}",
                    ExpectedBenefit = "Index-based lookups instead of collection scan"
                });
            }
        }

        // Check for in-memory sorting
        if (query.Sort?.Count > 0)
        {
            var sortFields = query.Sort.Select(s => s.FieldName).ToList();
            var indexedSortFields = sortFields
                .Where(f => _indexManager?.HasIndex(query.CollectionName, f) ?? false)
                .ToList();

            if (indexedSortFields.Count == 0)
            {
                suggestions.Add(new OptimizationSuggestion
                {
                    Type = "SortOptimization",
                    Severity = "Medium",
                    Title = "In-Memory Sort",
                    Description = $"Sorting on fields without indexes: {string.Join(", ", sortFields)}",
                    CurrentImplementation = "In-memory sort after fetching documents",
                    SuggestedImprovement = $"Create an index on: {string.Join(", ", sortFields)}",
                    ExpectedBenefit = "Index-based sorting avoids in-memory sort"
                });
            }
        }

        // Check for complex filters
        if (query.Filter?.Conditions != null && query.Filter.Conditions.Count > 5)
        {
            suggestions.Add(new OptimizationSuggestion
            {
                Type = "FilterOptimization",
                Severity = "Low",
                Title = "Complex Filter",
                Description = $"Query has {query.Filter.Conditions.Count} filter conditions, which may be slow to evaluate.",
                CurrentImplementation = "Multiple filter conditions",
                SuggestedImprovement = "Consider simplifying filter or using compound indexes",
                ExpectedBenefit = "Faster filter evaluation"
            });
        }

        // Check for projection optimization
        if (query.Projection == null || query.Projection.Count == 0)
        {
            suggestions.Add(new OptimizationSuggestion
            {
                Type = "Projection",
                Severity = "Low",
                Title = "No Field Projection",
                Description = "Query returns all fields. Consider projecting only needed fields.",
                CurrentImplementation = "Returning all fields",
                SuggestedImprovement = "Use projection to return only required fields",
                ExpectedBenefit = "Reduced memory usage and network transfer"
            });
        }

        return Task.FromResult(suggestions);
    }

    /// <inheritdoc />
    public int CalculateComplexityScore(Query.Models.Query query)
    {
        int score = 0;

        // Base complexity
        score += 10;

        // Filter complexity
        if (query.Filter?.Conditions != null)
        {
            score += query.Filter.Conditions.Count * 5;

            // Complex operators
            foreach (var condition in query.Filter.Conditions)
            {
                if (condition.Value is Dictionary<string, object> operators)
                {
                    // Operators like $regex, $where are expensive
                    if (operators.ContainsKey("$regex")) score += 15;
                    if (operators.ContainsKey("$where")) score += 20;
                    if (operators.ContainsKey("$text")) score += 10;
                }
            }
        }

        // Sort complexity
        if (query.Sort?.Count > 0)
        {
            score += query.Sort.Count * 5;
            // In-memory sorting is expensive
            var hasSortIndex = query.Sort.Any(s =>
                _indexManager?.HasIndex(query.CollectionName, s.FieldName) ?? false);
            if (!hasSortIndex) score += 10;
        }

        // Pagination complexity
        if (query.Options?.Skip > 0)
        {
            score += Math.Min((int)(query.Options.Skip / 100), 20);
        }

        // Collection scan penalty
        if (!WillUseIndex(query))
        {
            score += 20;
        }

        // Cap at 100
        return Math.Min(score, 100);
    }

    /// <inheritdoc />
    public async Task<long> EstimateDocumentCountAsync(
        Query.Models.Query query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var totalCount = await _documentStore.CountAsync(query.CollectionName);

            // If using an index, estimate based on selectivity
            if (WillUseIndex(query) && query.Filter?.Conditions != null)
            {
                // Rough estimate: assume 10% selectivity for indexed queries
                return Math.Max(1, totalCount / 10);
            }

            // Full collection scan
            return totalCount;
        }
        catch
        {
            return -1; // Unknown
        }
    }

    private QueryInfo BuildQueryInfo(Query.Models.Query query)
    {
        return new QueryInfo
        {
            Collection = query.CollectionName,
            Filter = query.Filter?.Conditions?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value),
            SortFields = query.Sort?.Select(s => new SortFieldInfo
            {
                Field = s.FieldName,
                Direction = s.Direction == SortDirection.Ascending ? "ascending" : "descending"
            }).ToList(),
            Projection = query.Projection,
            Pagination = query.Options != null
                ? new PaginationInfo
                {
                    Skip = query.Options.Skip ?? 0,
                    Limit = query.Options.Limit ?? 0
                }
                : null,
            QueryJson = query.Filter != null
                ? JsonSerializer.Serialize(query.Filter.Conditions)
                : null
        };
    }

    private async Task<List<PlanStage>> BuildExecutionPlanAsync(
        Query.Models.Query query,
        CancellationToken cancellationToken)
    {
        var stages = new List<PlanStage>();
        int stageOrder = 1;

        // Get collection stats
        var collectionCount = await _documentStore.CountAsync(query.CollectionName);

        // Stage 1: Document retrieval (IndexScan or CollectionScan)
        var indexInfo = GetIndexUsageInfo(query);
        if (indexInfo.CanUseIndex)
        {
            stages.Add(new PlanStage
            {
                StageName = "IndexScan",
                StageOrder = stageOrder++,
                EstimatedCost = 10,
                EstimatedInputDocuments = collectionCount,
                EstimatedOutputDocuments = Math.Max(1, collectionCount / 10),
                EstimatedTimeMs = 5,
                Details = new Dictionary<string, object>
                {
                    ["IndexName"] = indexInfo.IndexName!,
                    ["Field"] = indexInfo.Field!,
                    ["IndexType"] = "B-Tree",
                    ["ScanType"] = "Exact Match"
                }
            });
        }
        else
        {
            stages.Add(new PlanStage
            {
                StageName = "CollectionScan",
                StageOrder = stageOrder++,
                EstimatedCost = collectionCount * 0.1,
                EstimatedInputDocuments = collectionCount,
                EstimatedOutputDocuments = collectionCount,
                EstimatedTimeMs = collectionCount / 100,
                Details = new Dictionary<string, object>
                {
                    ["Collection"] = query.CollectionName,
                    ["ScanType"] = "Full Collection Scan",
                    ["DocumentsInCollection"] = collectionCount
                }
            });
        }

        // Stage 2: Filter (if there are non-indexed conditions)
        if (query.Filter?.Conditions != null && query.Filter.Conditions.Count > 0)
        {
            var hasComplexFilters = query.Filter.Conditions.Any(c =>
                c.Key.StartsWith('$') ||
                (c.Value is Dictionary<string, object> dict && dict.Count > 0));

            if (hasComplexFilters || !indexInfo.CanUseIndex)
            {
                var inputDocs = stages.Last().EstimatedOutputDocuments;
                stages.Add(new PlanStage
                {
                    StageName = "Filter",
                    StageOrder = stageOrder++,
                    EstimatedCost = inputDocs * 0.05,
                    EstimatedInputDocuments = inputDocs,
                    EstimatedOutputDocuments = Math.Max(1, inputDocs / 3),
                    EstimatedTimeMs = inputDocs / 200,
                    Details = new Dictionary<string, object>
                    {
                        ["FilterConditions"] = query.Filter.Conditions.Count,
                        ["ComplexOperators"] = hasComplexFilters
                    }
                });
            }
        }

        // Stage 3: Sort (if needed and not using index)
        if (query.Sort?.Count > 0 && !indexInfo.CanUseIndex)
        {
            var inputDocs = stages.Last().EstimatedOutputDocuments;
            stages.Add(new PlanStage
            {
                StageName = "Sort",
                StageOrder = stageOrder++,
                EstimatedCost = inputDocs * Math.Log(inputDocs + 1) * 0.01,
                EstimatedInputDocuments = inputDocs,
                EstimatedOutputDocuments = inputDocs,
                EstimatedTimeMs = inputDocs * (long)Math.Log(inputDocs + 1) / 50,
                Details = new Dictionary<string, object>
                {
                    ["SortFields"] = query.Sort.Select(s => s.FieldName).ToList(),
                    ["Algorithm"] = "In-Memory QuickSort",
                    ["MemoryRequired"] = $"{inputDocs * 100} bytes estimated"
                }
            });
        }

        // Stage 4: Skip
        var skipValue = query.Options?.Skip ?? 0;
        if (skipValue > 0)
        {
            var inputDocs = stages.Last().EstimatedOutputDocuments;
            stages.Add(new PlanStage
            {
                StageName = "Skip",
                StageOrder = stageOrder++,
                EstimatedCost = Math.Min(skipValue * 0.01, 100),
                EstimatedInputDocuments = inputDocs,
                EstimatedOutputDocuments = Math.Max(0, inputDocs - skipValue),
                EstimatedTimeMs = skipValue / 100,
                Details = new Dictionary<string, object>
                {
                    ["Skip"] = skipValue
                }
            });
        }

        // Stage 5: Limit
        if (query.Options?.Limit.HasValue == true)
        {
            var inputDocs = stages.Last().EstimatedOutputDocuments;
            stages.Add(new PlanStage
            {
                StageName = "Limit",
                StageOrder = stageOrder++,
                EstimatedCost = 1,
                EstimatedInputDocuments = inputDocs,
                EstimatedOutputDocuments = Math.Min(inputDocs, query.Options.Limit.Value),
                EstimatedTimeMs = 1,
                Details = new Dictionary<string, object>
                {
                    ["Limit"] = query.Options.Limit.Value
                }
            });
        }

        // Stage 6: Projection
        if (query.Projection?.Count > 0)
        {
            var inputDocs = stages.Last().EstimatedOutputDocuments;
            stages.Add(new PlanStage
            {
                StageName = "Project",
                StageOrder = stageOrder++,
                EstimatedCost = inputDocs * 0.01,
                EstimatedInputDocuments = inputDocs,
                EstimatedOutputDocuments = inputDocs,
                EstimatedTimeMs = inputDocs / 100,
                Details = new Dictionary<string, object>
                {
                    ["ProjectionFields"] = query.Projection.Keys.ToList(),
                    ["ProjectionType"] = query.Projection.Values.Any(v => v) ? "Inclusion" : "Exclusion"
                }
            });
        }

        return stages;
    }

    private ExecutionSummary BuildExecutionSummary(Query.Models.Query query, List<PlanStage> stages)
    {
        var indexInfo = GetIndexUsageInfo(query);
        var totalCost = stages.Sum(s => s.EstimatedCost);
        var totalTime = stages.Sum(s => s.EstimatedTimeMs);
        var inputDocs = stages.FirstOrDefault()?.EstimatedInputDocuments ?? 0;
        var outputDocs = stages.LastOrDefault()?.EstimatedOutputDocuments ?? 0;

        // Determine execution strategy
        string strategy;
        if (indexInfo.CanUseIndex)
        {
            strategy = query.Sort?.Count > 0 && indexInfo.Field == query.Sort[0].FieldName
                ? "IndexScan with IndexSort"
                : "IndexScan";
        }
        else if (query.Sort?.Count > 0)
        {
            strategy = "CollectionScan with InMemorySort";
        }
        else
        {
            strategy = "CollectionScan";
        }

        return new ExecutionSummary
        {
            EstimatedExecutionTimeMs = totalTime,
            EstimatedDocumentsToScan = inputDocs,
            EstimatedDocumentsToReturn = outputDocs,
            WillUseIndex = indexInfo.CanUseIndex,
            IndexName = indexInfo.IndexName,
            IndexUsageType = indexInfo.CanUseIndex ? "Exact Match" : null,
            IsIndexCovered = CanBeIndexCovered(query, indexInfo),
            ExecutionStrategy = strategy,
            TotalCost = totalCost
        };
    }

    private (bool CanUseIndex, string? IndexName, string? Field) GetIndexUsageInfo(Query.Models.Query query)
    {
        if (_indexManager == null || query.Filter?.Conditions == null)
            return (false, null, null);

        foreach (var condition in query.Filter.Conditions)
        {
            if (condition.Key.StartsWith('$'))
                continue;

            if (_indexManager.HasIndex(query.CollectionName, condition.Key))
            {
                return (true, $"{query.CollectionName}_{condition.Key}_idx", condition.Key);
            }
        }

        return (false, null, null);
    }

    private bool WillUseIndex(Query.Models.Query query)
    {
        return GetIndexUsageInfo(query).CanUseIndex;
    }

    private bool CanBeIndexCovered(Query.Models.Query query, (bool CanUseIndex, string? IndexName, string? Field) indexInfo)
    {
        if (!indexInfo.CanUseIndex)
            return false;

        // Check if all required fields are in the index
        // For now, assume simple single-field indexes
        if (query.Projection?.Count > 0)
        {
            var requiredFields = query.Projection.Keys.ToList();
            // If projecting only the indexed field + _id, it can be covered
            return requiredFields.All(f => f == indexInfo.Field || f == "_id");
        }

        return false;
    }

    private IndexRecommendation? AnalyzeForCompoundIndex(Query.Models.Query query)
    {
        if (query.Filter?.Conditions == null || query.Filter.Conditions.Count < 2)
            return null;

        // Find multiple filter fields without indexes
        var filterFields = query.Filter.Conditions
            .Where(c => !c.Key.StartsWith('$'))
            .Select(c => c.Key)
            .ToList();

        if (filterFields.Count >= 2)
        {
            var hasIndividualIndexes = filterFields.All(f =>
                _indexManager?.HasIndex(query.CollectionName, f) ?? false);

            if (!hasIndividualIndexes)
            {
                return new IndexRecommendation
                {
                    Priority = "Medium",
                    Fields = filterFields,
                    IsUnique = false,
                    CurrentImpact = "Multiple index lookups or collection scan",
                    PerformanceImprovement = "Single index lookup for all filter conditions",
                    CreateIndexCommand = $"CREATE_INDEX {query.CollectionName} {{\"fields\": [{string.Join(", ", filterFields.Select(f => $"\"{f}\""))}], \"unique\": false}}",
                    Explanation = $"A compound index on ({string.Join(", ", filterFields)}) would optimize queries filtering on all these fields together."
                };
            }
        }

        return null;
    }

    private IndexRecommendation? AnalyzeSortOptimization(Query.Models.Query query)
    {
        if (query.Sort?.Count != 1)
            return null;

        var sortField = query.Sort[0].FieldName;
        var hasSortIndex = _indexManager?.HasIndex(query.CollectionName, sortField) ?? false;

        if (!hasSortIndex)
        {
            // Check if there's a filter on the same field
            var filterField = query.Filter?.Conditions?.FirstOrDefault(c =>
                !c.Key.StartsWith('$')).Key;

            if (filterField != null && filterField == sortField)
            {
                return new IndexRecommendation
                {
                    Priority = "High",
                    Fields = new List<string> { sortField },
                    IsUnique = false,
                    CurrentImpact = "In-memory sort required",
                    PerformanceImprovement = "Index-based sort (no in-memory sorting)",
                    CreateIndexCommand = $"CREATE_INDEX {query.CollectionName} {{\"field\": \"{sortField}\", \"unique\": false}}",
                    Explanation = $"An index on '{sortField}' would eliminate in-memory sorting when filtering and sorting on the same field."
                };
            }
        }

        return null;
    }

    private List<AlternativePlan> BuildAlternativePlans(Query.Models.Query query, List<PlanStage> chosenPlan)
    {
        var alternatives = new List<AlternativePlan>();
        var indexInfo = GetIndexUsageInfo(query);

        // Alternative: Compound index
        if (query.Filter?.Conditions != null && query.Filter.Conditions.Count >= 2)
        {
            alternatives.Add(new AlternativePlan
            {
                PlanName = "CompoundIndex",
                Description = "Use a compound index for multiple filter conditions",
                EstimatedCost = chosenPlan.Sum(s => s.EstimatedCost) * 0.5,
                IsRejected = !indexInfo.CanUseIndex,
                RejectionReason = indexInfo.CanUseIndex ? null : "No compound index available"
            });
        }

        // Alternative: Index-only scan
        if (indexInfo.CanUseIndex && query.Projection?.Count > 0)
        {
            alternatives.Add(new AlternativePlan
            {
                PlanName = "IndexOnlyScan",
                Description = "Return data directly from index without fetching documents",
                EstimatedCost = chosenPlan.Sum(s => s.EstimatedCost) * 0.3,
                IsRejected = !CanBeIndexCovered(query, indexInfo),
                RejectionReason = "Projection requires fields not in index"
            });
        }

        return alternatives;
    }

    private string EstimateCurrentImpact(Query.Models.Query query, string fieldName)
    {
        try
        {
            var count = _documentStore.CountAsync(query.CollectionName).GetAwaiter().GetResult();
            if (count > 10000)
                return "High impact - collection scan on large collection";
            if (count > 1000)
                return "Medium impact - collection scan on medium collection";
            return "Low impact - small collection, scan is acceptable";
        }
        catch
        {
            return "Unknown impact - cannot determine collection size";
        }
    }

    private string EstimateImprovement(Query.Models.Query query, string fieldName)
    {
        try
        {
            var count = _documentStore.CountAsync(query.CollectionName).GetAwaiter().GetResult();
            if (count > 10000)
                return "10x-100x faster for selective queries";
            if (count > 1000)
                return "5x-10x faster for selective queries";
            return "2x-5x faster, minimal for small collections";
        }
        catch
        {
            return "Significant improvement expected";
        }
    }
}
