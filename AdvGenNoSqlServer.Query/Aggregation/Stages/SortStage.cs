// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Query.Aggregation.Stages;

/// <summary>
/// Represents a sort specification
/// </summary>
public class SortSpec
{
    /// <summary>
    /// The field path to sort by
    /// </summary>
    public string FieldPath { get; set; } = string.Empty;

    /// <summary>
    /// True for ascending, false for descending
    /// </summary>
    public bool Ascending { get; set; } = true;

    /// <summary>
    /// Creates a new SortSpec
    /// </summary>
    public SortSpec() { }

    /// <summary>
    /// Creates a new SortSpec with the specified field and direction
    /// </summary>
    public SortSpec(string fieldPath, bool ascending = true)
    {
        FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
        Ascending = ascending;
    }

    /// <summary>
    /// Creates an ascending sort spec
    /// </summary>
    public static SortSpec Asc(string fieldPath) => new(fieldPath, true);

    /// <summary>
    /// Creates a descending sort spec
    /// </summary>
    public static SortSpec Desc(string fieldPath) => new(fieldPath, false);
}

/// <summary>
/// $sort stage - Sorts documents by specified fields
/// </summary>
public class SortStage : IAggregationStage
{
    private readonly List<SortSpec> _sortSpecs;

    /// <inheritdoc />
    public string StageType => "$sort";

    /// <summary>
    /// The sort specifications
    /// </summary>
    public IReadOnlyList<SortSpec> SortSpecs => _sortSpecs.AsReadOnly();

    /// <summary>
    /// Creates a new SortStage with a single sort field
    /// </summary>
    public SortStage(string fieldPath, bool ascending = true)
    {
        _sortSpecs = new List<SortSpec> { new(fieldPath, ascending) };
    }

    /// <summary>
    /// Creates a new SortStage with multiple sort fields
    /// </summary>
    public SortStage(IEnumerable<SortSpec> sortSpecs)
    {
        _sortSpecs = new List<SortSpec>(sortSpecs ?? throw new ArgumentNullException(nameof(sortSpecs)));
    }

    /// <summary>
    /// Creates a new SortStage from a dictionary (field path -> direction)
    /// </summary>
    public SortStage(Dictionary<string, int> sortSpecs)
    {
        _sortSpecs = new List<SortSpec>();
        foreach (var spec in sortSpecs)
        {
            _sortSpecs.Add(new SortSpec(spec.Key.TrimStart('$'), spec.Value >= 0));
        }
    }

    /// <inheritdoc />
    public IEnumerable<Document> Execute(IEnumerable<Document> documents)
    {
        try
        {
            if (!_sortSpecs.Any())
                return documents;

            var sorted = documents.OrderBy(d => GetFieldValue(d, _sortSpecs[0].FieldPath), new ObjectComparer(_sortSpecs[0].Ascending));

            for (int i = 1; i < _sortSpecs.Count; i++)
            {
                var spec = _sortSpecs[i];
                var index = i; // Capture for closure
                sorted = sorted.ThenBy(d => GetFieldValue(d, spec.FieldPath), new ObjectComparer(spec.Ascending));
            }

            return sorted.ToList();
        }
        catch (Exception ex)
        {
            throw new AggregationStageException(StageType, $"Failed to execute sort stage: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> ExecuteAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Execute(documents));
    }

    private object? GetFieldValue(Document document, string fieldPath)
    {
        if (document.Data == null) return null;

        // Handle _id specially
        if (fieldPath == "_id")
            return document.Id;

        // Handle nested paths
        var parts = fieldPath.TrimStart('$').Split('.');
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
/// Comparer for objects that handles nulls and type conversions
/// </summary>
internal class ObjectComparer : IComparer<object?>
{
    private readonly bool _ascending;

    public ObjectComparer(bool ascending = true)
    {
        _ascending = ascending;
    }

    public int Compare(object? x, object? y)
    {
        int result;

        if (x is null && y is null)
            result = 0;
        else if (x is null)
            result = 1; // Nulls sort to the end
        else if (y is null)
            result = -1;
        else if (x is IComparable comparableX && y is IComparable comparableY && x.GetType() == y.GetType())
            result = comparableX.CompareTo(comparableY);
        else if (x is IConvertible cx && y is IConvertible cy)
        {
            try
            {
                result = cx.ToDouble(null).CompareTo(cy.ToDouble(null));
            }
            catch
            {
                result = string.Compare(x.ToString(), y.ToString(), StringComparison.Ordinal);
            }
        }
        else
            result = string.Compare(x.ToString(), y.ToString(), StringComparison.Ordinal);

        return _ascending ? result : -result;
    }
}
