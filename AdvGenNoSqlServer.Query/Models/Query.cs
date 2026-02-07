// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;

namespace AdvGenNoSqlServer.Query.Models;

/// <summary>
/// Represents a query to be executed against a document collection
/// </summary>
public class Query
{
    /// <summary>
    /// The name of the collection to query
    /// </summary>
    public required string CollectionName { get; set; }

    /// <summary>
    /// The filter criteria for the query
    /// </summary>
    public QueryFilter? Filter { get; set; }

    /// <summary>
    /// Sort specifications for ordering results
    /// </summary>
    public List<SortField>? Sort { get; set; }

    /// <summary>
    /// Pagination and limit options
    /// </summary>
    public QueryOptions? Options { get; set; }

    /// <summary>
    /// The projection specification to select specific fields
    /// </summary>
    public Dictionary<string, bool>? Projection { get; set; }
}

/// <summary>
/// Represents filter criteria for querying documents
/// Supports MongoDB-like query syntax with operators
/// </summary>
public class QueryFilter
{
    /// <summary>
    /// The filter conditions as a dictionary
    /// Keys are field names or operators ($and, $or)
    /// Values are the filter values or nested conditions
    /// </summary>
    public Dictionary<string, object> Conditions { get; set; } = new();

    /// <summary>
    /// Creates an empty filter
    /// </summary>
    public QueryFilter() { }

    /// <summary>
    /// Creates a filter from a JSON element
    /// </summary>
    public QueryFilter(JsonElement jsonElement)
    {
        Conditions = JsonElementToDictionary(jsonElement);
    }

    /// <summary>
    /// Creates a filter with a single equality condition
    /// </summary>
    public static QueryFilter Eq(string field, object value)
    {
        return new QueryFilter { Conditions = { [field] = value } };
    }

    /// <summary>
    /// Creates a filter with a greater than condition
    /// </summary>
    public static QueryFilter Gt(string field, object value)
    {
        return new QueryFilter { Conditions = { [field] = new Dictionary<string, object> { ["$gt"] = value } } };
    }

    /// <summary>
    /// Creates a filter with a greater than or equal condition
    /// </summary>
    public static QueryFilter Gte(string field, object value)
    {
        return new QueryFilter { Conditions = { [field] = new Dictionary<string, object> { ["$gte"] = value } } };
    }

    /// <summary>
    /// Creates a filter with a less than condition
    /// </summary>
    public static QueryFilter Lt(string field, object value)
    {
        return new QueryFilter { Conditions = { [field] = new Dictionary<string, object> { ["$lt"] = value } } };
    }

    /// <summary>
    /// Creates a filter with a less than or equal condition
    /// </summary>
    public static QueryFilter Lte(string field, object value)
    {
        return new QueryFilter { Conditions = { [field] = new Dictionary<string, object> { ["$lte"] = value } } };
    }

    /// <summary>
    /// Creates a filter with a not equal condition
    /// </summary>
    public static QueryFilter Ne(string field, object value)
    {
        return new QueryFilter { Conditions = { [field] = new Dictionary<string, object> { ["$ne"] = value } } };
    }

    /// <summary>
    /// Creates a filter with an "in" condition
    /// </summary>
    public static QueryFilter In(string field, IEnumerable<object> values)
    {
        return new QueryFilter { Conditions = { [field] = new Dictionary<string, object> { ["$in"] = values.ToList() } } };
    }

    /// <summary>
    /// Creates a filter with a "not in" condition
    /// </summary>
    public static QueryFilter Nin(string field, IEnumerable<object> values)
    {
        return new QueryFilter { Conditions = { [field] = new Dictionary<string, object> { ["$nin"] = values.ToList() } } };
    }

    /// <summary>
    /// Combines this filter with another using AND logic
    /// </summary>
    public QueryFilter And(QueryFilter other)
    {
        var result = new QueryFilter();
        var andList = new List<object>();
        
        // Add current conditions as a dictionary
        andList.Add(Conditions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
        
        // Add other conditions as a dictionary
        andList.Add(other.Conditions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
        
        result.Conditions["$and"] = andList;
        return result;
    }

    /// <summary>
    /// Combines this filter with another using OR logic
    /// </summary>
    public QueryFilter Or(QueryFilter other)
    {
        var result = new QueryFilter();
        var orList = new List<object>();
        
        // Add current conditions as a dictionary
        orList.Add(Conditions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
        
        // Add other conditions as a dictionary
        orList.Add(other.Conditions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
        
        result.Conditions["$or"] = orList;
        return result;
    }

    private static Dictionary<string, object> JsonElementToDictionary(JsonElement element)
    {
        var result = new Dictionary<string, object>();
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = JsonElementToObject(property.Value);
        }
        return result;
    }

    private static object JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonElementToDictionary(element),
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.ToString()!
        };
    }
}

/// <summary>
/// Specifies a field to sort by and the sort direction
/// </summary>
public class SortField
{
    /// <summary>
    /// The field name to sort by
    /// </summary>
    public required string FieldName { get; set; }

    /// <summary>
    /// The sort direction (1 for ascending, -1 for descending)
    /// </summary>
    public SortDirection Direction { get; set; } = SortDirection.Ascending;

    /// <summary>
    /// Creates an ascending sort field
    /// </summary>
    public static SortField Ascending(string fieldName) => new() { FieldName = fieldName, Direction = SortDirection.Ascending };

    /// <summary>
    /// Creates a descending sort field
    /// </summary>
    public static SortField Descending(string fieldName) => new() { FieldName = fieldName, Direction = SortDirection.Descending };
}

/// <summary>
/// Sort direction enumeration
/// </summary>
public enum SortDirection
{
    /// <summary>
    /// Ascending order (A to Z, 0 to 9)
    /// </summary>
    Ascending = 1,

    /// <summary>
    /// Descending order (Z to A, 9 to 0)
    /// </summary>
    Descending = -1
}

/// <summary>
/// Options for query execution including pagination
/// </summary>
public class QueryOptions
{
    /// <summary>
    /// Maximum number of documents to return
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Number of documents to skip before returning results
    /// </summary>
    public int? Skip { get; set; }

    /// <summary>
    /// Whether to include the total count of matching documents
    /// </summary>
    public bool IncludeTotalCount { get; set; }

    /// <summary>
    /// Maximum time in milliseconds to wait for query execution
    /// </summary>
    public int? TimeoutMs { get; set; }
}
