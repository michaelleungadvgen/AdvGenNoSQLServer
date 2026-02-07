// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Query.Aggregation.Stages;

/// <summary>
/// Represents an aggregation operator for $group stage
/// </summary>
public enum GroupOperator
{
    /// <summary>
    /// Sum of values
    /// </summary>
    Sum,

    /// <summary>
    /// Average of values
    /// </summary>
    Avg,

    /// <summary>
    /// Minimum value
    /// </summary>
    Min,

    /// <summary>
    /// Maximum value
    /// </summary>
    Max,

    /// <summary>
    /// Count of documents
    /// </summary>
    Count,

    /// <summary>
    /// First value in group
    /// </summary>
    First,

    /// <summary>
    /// Last value in group
    /// </summary>
    Last,

    /// <summary>
    /// Add to array
    /// </summary>
    Push,

    /// <summary>
    /// Add unique values to set
    /// </summary>
    AddToSet
}

/// <summary>
/// Represents a field aggregation specification in a $group stage
/// </summary>
public class GroupFieldSpec
{
    /// <summary>
    /// The operator to apply
    /// </summary>
    public GroupOperator Operator { get; set; }

    /// <summary>
    /// The field path to aggregate (e.g., "$quantity" or "$price")
    /// </summary>
    public string? FieldPath { get; set; }

    /// <summary>
    /// Creates a new GroupFieldSpec
    /// </summary>
    public GroupFieldSpec(GroupOperator op, string? fieldPath = null)
    {
        Operator = op;
        FieldPath = fieldPath;
    }
}

/// <summary>
/// $group stage - Groups documents by a field and applies aggregations
/// </summary>
public class GroupStage : IAggregationStage
{
    private readonly string? _groupByField;
    private readonly Dictionary<string, GroupFieldSpec> _aggregations;

    /// <inheritdoc />
    public string StageType => "$group";

    /// <summary>
    /// The field to group by (null for grouping all documents)
    /// </summary>
    public string? GroupByField => _groupByField;

    /// <summary>
    /// The aggregation specifications
    /// </summary>
    public IReadOnlyDictionary<string, GroupFieldSpec> Aggregations => _aggregations;

    /// <summary>
    /// Creates a new GroupStage that groups all documents together
    /// </summary>
    /// <param name="aggregations">Field name to aggregation spec mapping</param>
    public GroupStage(Dictionary<string, GroupFieldSpec> aggregations)
    {
        _groupByField = null;
        _aggregations = new Dictionary<string, GroupFieldSpec>(aggregations);
    }

    /// <summary>
    /// Creates a new GroupStage that groups by a specific field
    /// </summary>
    /// <param name="groupByField">The field path to group by (e.g., "$category")</param>
    /// <param name="aggregations">Field name to aggregation spec mapping</param>
    public GroupStage(string groupByField, Dictionary<string, GroupFieldSpec> aggregations)
    {
        _groupByField = groupByField?.TrimStart('$') ?? throw new ArgumentNullException(nameof(groupByField));
        _aggregations = new Dictionary<string, GroupFieldSpec>(aggregations);
    }

    /// <inheritdoc />
    public IEnumerable<Document> Execute(IEnumerable<Document> documents)
    {
        try
        {
            // Use a custom grouping approach that handles null keys
            var groups = new List<(object? Key, List<Document> Documents)>();

            // Group documents
            foreach (var doc in documents)
            {
                object? key = null;
                
                if (_groupByField != null)
                {
                    key = GetFieldValue(doc, _groupByField);
                }

                // Find existing group or create new one
                var existingGroup = groups.FirstOrDefault(g => 
                    (g.Key == null && key == null) || (g.Key?.Equals(key) == true));
                
                if (existingGroup.Documents == null)
                {
                    groups.Add((key, new List<Document> { doc }));
                }
                else
                {
                    existingGroup.Documents.Add(doc);
                }
            }

            // Apply aggregations to each group
            var results = new List<Document>();
            int groupId = 0;

            foreach (var group in groups)
            {
                var resultDoc = new Document
                {
                    Id = $"group_{groupId++}",
                    Data = new Dictionary<string, object?>()
                };

                // Add the _id field (group key)
                resultDoc.Data["_id"] = group.Key;

                // Apply each aggregation
                foreach (var agg in _aggregations)
                {
                    var fieldName = agg.Key;
                    var spec = agg.Value;
                    var value = ApplyAggregation(group.Documents, spec);
                    resultDoc.Data[fieldName] = value;
                }

                results.Add(resultDoc);
            }

            return results;
        }
        catch (AggregationStageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AggregationStageException(StageType, $"Failed to execute group stage: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> ExecuteAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Execute(documents));
    }

    private object? ApplyAggregation(List<Document> groupDocuments, GroupFieldSpec spec)
    {
        switch (spec.Operator)
        {
            case GroupOperator.Count:
                return groupDocuments.Count;

            case GroupOperator.Sum:
                if (string.IsNullOrEmpty(spec.FieldPath))
                    throw new AggregationStageException(StageType, "Sum operator requires a field path");
                return CalculateSum(groupDocuments, spec.FieldPath.TrimStart('$'));

            case GroupOperator.Avg:
                if (string.IsNullOrEmpty(spec.FieldPath))
                    throw new AggregationStageException(StageType, "Avg operator requires a field path");
                return CalculateAverage(groupDocuments, spec.FieldPath.TrimStart('$'));

            case GroupOperator.Min:
                if (string.IsNullOrEmpty(spec.FieldPath))
                    throw new AggregationStageException(StageType, "Min operator requires a field path");
                return CalculateMin(groupDocuments, spec.FieldPath.TrimStart('$'));

            case GroupOperator.Max:
                if (string.IsNullOrEmpty(spec.FieldPath))
                    throw new AggregationStageException(StageType, "Max operator requires a field path");
                return CalculateMax(groupDocuments, spec.FieldPath.TrimStart('$'));

            case GroupOperator.First:
                if (string.IsNullOrEmpty(spec.FieldPath))
                    throw new AggregationStageException(StageType, "First operator requires a field path");
                return GetFieldValue(groupDocuments.First(), spec.FieldPath.TrimStart('$'));

            case GroupOperator.Last:
                if (string.IsNullOrEmpty(spec.FieldPath))
                    throw new AggregationStageException(StageType, "Last operator requires a field path");
                return GetFieldValue(groupDocuments.Last(), spec.FieldPath.TrimStart('$'));

            case GroupOperator.Push:
                if (string.IsNullOrEmpty(spec.FieldPath))
                    throw new AggregationStageException(StageType, "Push operator requires a field path");
                return groupDocuments.Select(d => GetFieldValue(d, spec.FieldPath.TrimStart('$'))).ToList();

            case GroupOperator.AddToSet:
                if (string.IsNullOrEmpty(spec.FieldPath))
                    throw new AggregationStageException(StageType, "AddToSet operator requires a field path");
                var values = groupDocuments.Select(d => GetFieldValue(d, spec.FieldPath.TrimStart('$'))).ToList();
                // Use a HashSet to get unique values, then convert back to list
                var uniqueValues = new HashSet<object?>(values, new ObjectEqualityComparer());
                return uniqueValues.ToList();

            default:
                throw new AggregationStageException(StageType, $"Unknown operator: {spec.Operator}");
        }
    }

    private double CalculateSum(List<Document> documents, string fieldPath)
    {
        double sum = 0;
        foreach (var doc in documents)
        {
            var value = GetFieldValue(doc, fieldPath);
            if (value is IConvertible conv)
            {
                sum += conv.ToDouble(null);
            }
        }
        return sum;
    }

    private double CalculateAverage(List<Document> documents, string fieldPath)
    {
        if (documents.Count == 0) return 0;
        return CalculateSum(documents, fieldPath) / documents.Count;
    }

    private object? CalculateMin(List<Document> documents, string fieldPath)
    {
        object? min = null;
        foreach (var doc in documents)
        {
            var value = GetFieldValue(doc, fieldPath);
            if (value is IComparable comparable)
            {
                if (min == null || comparable.CompareTo(min) < 0)
                {
                    min = value;
                }
            }
        }
        return min;
    }

    private object? CalculateMax(List<Document> documents, string fieldPath)
    {
        object? max = null;
        foreach (var doc in documents)
        {
            var value = GetFieldValue(doc, fieldPath);
            if (value is IComparable comparable)
            {
                if (max == null || comparable.CompareTo(max) > 0)
                {
                    max = value;
                }
            }
        }
        return max;
    }

    private object? GetFieldValue(Document document, string fieldPath)
    {
        if (document.Data == null) return null;

        // Handle nested paths like "address.city"
        var parts = fieldPath.Split('.');
        object? current = document.Data;

        foreach (var part in parts)
        {
            if (current is Dictionary<string, object?> dict && dict.TryGetValue(part, out var value))
            {
                current = value;
            }
            else
            {
                return null;
            }
        }

        return current;
    }
}

/// <summary>
/// Equality comparer for objects that handles nulls and type conversions
/// </summary>
internal class ObjectEqualityComparer : IEqualityComparer<object?>
{
    public new bool Equals(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        
        // Try numeric comparison
        if (x is IConvertible cx && y is IConvertible cy)
        {
            try
            {
                return cx.ToDouble(null) == cy.ToDouble(null);
            }
            catch { }
        }

        return x.Equals(y);
    }

    public int GetHashCode(object? obj)
    {
        return obj?.GetHashCode() ?? 0;
    }
}
