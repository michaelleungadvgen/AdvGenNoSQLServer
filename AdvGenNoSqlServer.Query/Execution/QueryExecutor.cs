// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Indexing;

namespace AdvGenNoSqlServer.Query.Execution;

/// <summary>
/// Implementation of the query executor that runs queries against the document store
/// </summary>
public class QueryExecutor : IQueryExecutor
{
    private readonly IDocumentStore _documentStore;
    private readonly IFilterEngine _filterEngine;
    private readonly IndexManager? _indexManager;

    /// <summary>
    /// Creates a new QueryExecutor
    /// </summary>
    public QueryExecutor(IDocumentStore documentStore, IFilterEngine filterEngine, IndexManager? indexManager = null)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _filterEngine = filterEngine ?? throw new ArgumentNullException(nameof(filterEngine));
        _indexManager = indexManager;
    }

    /// <inheritdoc />
    public async Task<QueryResult> ExecuteAsync(Query.Models.Query query, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Get candidate documents using index if available
            var candidateIds = await GetCandidateDocumentIdsAsync(query);

            // Fetch documents
            IEnumerable<Document> documents;
            if (candidateIds != null)
            {
                // Use index results
                var docs = new List<Document>();
                foreach (var id in candidateIds)
                {
                    var doc = await _documentStore.GetAsync(query.CollectionName, id);
                    if (doc != null)
                        docs.Add(doc);
                }
                documents = docs;
            }
            else
            {
                // Full collection scan
                documents = await _documentStore.GetAllAsync(query.CollectionName);
            }

            // Apply filters
            var filtered = _filterEngine.Filter(documents, query.Filter).ToList();
            var totalCount = filtered.Count;

            // Apply sorting
            if (query.Sort != null && query.Sort.Count > 0)
            {
                filtered = ApplySorting(filtered, query.Sort);
            }

            // Apply pagination
            var skip = query.Options?.Skip ?? 0;
            var limit = query.Options?.Limit;

            if (skip > 0)
            {
                filtered = filtered.Skip(skip).ToList();
            }

            if (limit.HasValue)
            {
                filtered = filtered.Take(limit.Value).ToList();
            }

            // Apply projection if specified
            if (query.Projection != null && query.Projection.Count > 0)
            {
                filtered = ApplyProjection(filtered, query.Projection);
            }

            stopwatch.Stop();

            return new QueryResult
            {
                Documents = filtered,
                TotalCount = query.Options?.IncludeTotalCount == true ? totalCount : filtered.Count,
                Skipped = skip,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                Success = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var failureResult = QueryResult.FailureResult(ex.Message);
            failureResult.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            return failureResult;
        }
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(Query.Models.Query query, CancellationToken cancellationToken = default)
    {
        // Get candidate documents using index if available
        var candidateIds = await GetCandidateDocumentIdsAsync(query);

        // Fetch documents
        IEnumerable<Document> documents;
        if (candidateIds != null)
        {
            var docs = new List<Document>();
            foreach (var id in candidateIds)
            {
                var doc = await _documentStore.GetAsync(query.CollectionName, id);
                if (doc != null)
                    docs.Add(doc);
            }
            documents = docs;
        }
        else
        {
            documents = await _documentStore.GetAllAsync(query.CollectionName);
        }

        // Apply filters and count
        return _filterEngine.Filter(documents, query.Filter).LongCount();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(Query.Models.Query query, CancellationToken cancellationToken = default)
    {
        var count = await CountAsync(query, cancellationToken);
        return count > 0;
    }

    /// <inheritdoc />
    public Task<QueryStats> ExplainAsync(Query.Models.Query query)
    {
        var stats = new QueryStats();

        // Determine if an index can be used
        var indexInfo = GetIndexUsageInfo(query);
        stats.IndexUsed = indexInfo.CanUseIndex;
        stats.IndexName = indexInfo.IndexName;

        var stages = new List<QueryPlanStage>();

        // Collection scan or index scan stage
        if (indexInfo.CanUseIndex)
        {
            stages.Add(new QueryPlanStage
            {
                StageName = "IndexScan",
                Details = new Dictionary<string, object>
                {
                    ["IndexName"] = indexInfo.IndexName!,
                    ["Field"] = indexInfo.Field!
                }
            });
        }
        else
        {
            stages.Add(new QueryPlanStage
            {
                StageName = "CollectionScan",
                Details = new Dictionary<string, object>
                {
                    ["Collection"] = query.CollectionName
                }
            });
        }

        // Filter stage if there are non-indexed conditions
        if (query.Filter != null && query.Filter.Conditions.Count > 0)
        {
            stages.Add(new QueryPlanStage
            {
                StageName = "Filter",
                Details = new Dictionary<string, object>
                {
                    ["FilterConditions"] = query.Filter.Conditions.Count
                }
            });
        }

        // Sort stage
        if (query.Sort != null && query.Sort.Count > 0)
        {
            stages.Add(new QueryPlanStage
            {
                StageName = "Sort",
                Details = new Dictionary<string, object>
                {
                    ["SortFields"] = query.Sort.Select(s => s.FieldName).ToList()
                }
            });
        }

        // Skip stage
        if (query.Options?.Skip > 0)
        {
            stages.Add(new QueryPlanStage
            {
                StageName = "Skip",
                Details = new Dictionary<string, object>
                {
                    ["Skip"] = query.Options.Skip.Value
                }
            });
        }

        // Limit stage
        if (query.Options?.Limit.HasValue == true)
        {
            stages.Add(new QueryPlanStage
            {
                StageName = "Limit",
                Details = new Dictionary<string, object>
                {
                    ["Limit"] = query.Options.Limit.Value
                }
            });
        }

        stats.ExecutionPlan = stages;
        return Task.FromResult(stats);
    }

    private async Task<List<string>?> GetCandidateDocumentIdsAsync(Query.Models.Query query)
    {
        if (_indexManager == null || query.Filter == null)
            return null;

        // Try to use index for equality conditions
        foreach (var condition in query.Filter.Conditions)
        {
            if (condition.Key.StartsWith('$'))
                continue; // Logical operators not supported for index lookup yet

            // Check if it's a simple equality condition
            if (condition.Value is not Dictionary<string, object>)
            {
                // Simple equality - check for index
                var index = FindIndexForField(query.CollectionName, condition.Key);
                if (index != null)
                {
                    return await QueryIndexAsync(index, condition.Value);
                }
            }
            else if (condition.Value is Dictionary<string, object> operators)
            {
                // Check for operators that can use index
                if (operators.ContainsKey("$eq"))
                {
                    var index = FindIndexForField(query.CollectionName, condition.Key);
                    if (index != null)
                    {
                        return await QueryIndexAsync(index, operators["$eq"]);
                    }
                }
            }
        }

        return null;
    }

    private object? FindIndexForField(string collectionName, string fieldName)
    {
        if (_indexManager == null)
            return null;

        // Try different key types - start with string
        var stringIndex = _indexManager.GetIndex<string>(collectionName, fieldName);
        if (stringIndex != null)
            return stringIndex;

        var intIndex = _indexManager.GetIndex<int>(collectionName, fieldName);
        if (intIndex != null)
            return intIndex;

        var longIndex = _indexManager.GetIndex<long>(collectionName, fieldName);
        if (longIndex != null)
            return longIndex;

        var doubleIndex = _indexManager.GetIndex<double>(collectionName, fieldName);
        if (doubleIndex != null)
            return doubleIndex;

        var dateTimeIndex = _indexManager.GetIndex<DateTime>(collectionName, fieldName);
        if (dateTimeIndex != null)
            return dateTimeIndex;

        return null;
    }

    private static Task<List<string>> QueryIndexAsync(object index, object value)
    {
        var documentIds = new List<string>();

        // Use reflection to call the appropriate generic method
        var indexType = index.GetType();
        var keyType = indexType.GetGenericArguments()[0];

        try
        {
            var convertedValue = Convert.ChangeType(value, keyType);
            var tryGetValueMethod = indexType.GetMethod("TryGetValue", new[] { keyType, typeof(string).MakeByRefType() });
            
            if (tryGetValueMethod != null)
            {
                var parameters = new object?[] { convertedValue, null };
                var result = (bool)tryGetValueMethod.Invoke(index, parameters)!;
                
                if (result && parameters[1] != null)
                {
                    documentIds.Add((string)parameters[1]!);
                }
            }

            // Also try GetValues for non-unique indexes
            var getValuesMethod = indexType.GetMethod("GetValues", new[] { keyType });
            if (getValuesMethod != null)
            {
                var values = getValuesMethod.Invoke(index, new[] { convertedValue }) as IEnumerable<string>;
                if (values != null)
                {
                    documentIds.AddRange(values);
                }
            }
        }
        catch
        {
            // Type conversion failed, ignore this index
        }

        return Task.FromResult(documentIds);
    }

    private (bool CanUseIndex, string? IndexName, string? Field) GetIndexUsageInfo(Query.Models.Query query)
    {
        if (_indexManager == null || query.Filter == null)
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

    private List<Document> ApplySorting(List<Document> documents, List<SortField> sortFields)
    {
        IOrderedEnumerable<Document>? ordered = null;

        for (int i = 0; i < sortFields.Count; i++)
        {
            var sortField = sortFields[i];
            var fieldName = sortField.FieldName;
            var direction = sortField.Direction;

            if (i == 0)
            {
                ordered = direction == SortDirection.Ascending
                    ? documents.OrderBy(d => GetFieldForSorting(d, fieldName))
                    : documents.OrderByDescending(d => GetFieldForSorting(d, fieldName));
            }
            else
            {
                ordered = direction == SortDirection.Ascending
                    ? ordered!.ThenBy(d => GetFieldForSorting(d, fieldName))
                    : ordered!.ThenByDescending(d => GetFieldForSorting(d, fieldName));
            }
        }

        return ordered?.ToList() ?? documents;
    }

    private object? GetFieldForSorting(Document document, string fieldName)
    {
        var value = _filterEngine.GetFieldValue(document, fieldName);
        return value ?? string.Empty; // Null values sort to the end
    }

    private List<Document> ApplyProjection(List<Document> documents, Dictionary<string, bool> projection)
    {
        // Determine if this is an inclusion or exclusion projection
        var isInclusion = projection.Values.Any(v => v);

        if (!isInclusion)
        {
            // Exclusion projection - exclude specified fields
            var fieldsToExclude = projection.Where(p => !p.Value).Select(p => p.Key).ToList();
            foreach (var doc in documents)
            {
                if (doc.Data != null)
                {
                    foreach (var field in fieldsToExclude)
                    {
                        doc.Data.Remove(field);
                    }
                }
            }
        }
        else
        {
            // Inclusion projection - include only specified fields + _id
            var fieldsToInclude = projection.Where(p => p.Value).Select(p => p.Key).ToList();
            fieldsToInclude.Add("_id");

            foreach (var doc in documents)
            {
                if (doc.Data != null)
                {
                    var keysToRemove = doc.Data.Keys.Where(k => !fieldsToInclude.Contains(k)).ToList();
                    foreach (var key in keysToRemove)
                    {
                        doc.Data.Remove(key);
                    }
                }
            }
        }

        return documents;
    }
}
