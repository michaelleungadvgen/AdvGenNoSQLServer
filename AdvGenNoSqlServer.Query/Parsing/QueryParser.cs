// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Query.Parsing;

/// <summary>
/// Implementation of the query parser supporting MongoDB-like syntax
/// </summary>
public class QueryParser : IQueryParser
{
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Creates a new QueryParser with default options
    /// </summary>
    public QueryParser()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
    }

    /// <inheritdoc />
    public Models.Query Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Parse(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new QueryParseException($"Invalid JSON: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Models.Query Parse(JsonElement jsonElement)
    {
        if (jsonElement.ValueKind != JsonValueKind.Object)
        {
            throw new QueryParseException("Query must be a JSON object");
        }

        var query = new Models.Query { CollectionName = string.Empty };

        foreach (var property in jsonElement.EnumerateObject())
        {
            switch (property.Name.ToLowerInvariant())
            {
                case "collection":
                case "collectionname":
                    query.CollectionName = property.Value.GetString() 
                        ?? throw new QueryParseException("Collection name cannot be null");
                    break;

                case "filter":
                    query.Filter = ParseFilter(property.Value);
                    break;

                case "sort":
                    query.Sort = ParseSort(property.Value);
                    break;

                case "options":
                    query.Options = ParseOptions(property.Value);
                    break;

                case "projection":
                    query.Projection = ParseProjection(property.Value);
                    break;

                default:
                    // If no explicit filter object, treat unknown properties as filter conditions
                    if (query.Filter == null)
                    {
                        query.Filter = new QueryFilter();
                    }
                    query.Filter.Conditions[property.Name] = JsonElementToObject(property.Value);
                    break;
            }
        }

        if (string.IsNullOrEmpty(query.CollectionName))
        {
            throw new QueryParseException("Collection name is required");
        }

        return query;
    }

    /// <inheritdoc />
    public QueryFilter ParseFilter(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return ParseFilter(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new QueryParseException($"Invalid JSON filter: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public bool TryParse(string json, out Models.Query? query, out string? error)
    {
        try
        {
            query = Parse(json);
            error = null;
            return true;
        }
        catch (QueryParseException ex)
        {
            query = null;
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            query = null;
            error = $"Unexpected error: {ex.Message}";
            return false;
        }
    }

    private QueryFilter ParseFilter(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new QueryParseException("Filter must be a JSON object");
        }

        var filter = new QueryFilter();
        foreach (var property in element.EnumerateObject())
        {
            filter.Conditions[property.Name] = JsonElementToObject(property.Value);
        }
        return filter;
    }

    private List<SortField> ParseSort(JsonElement element)
    {
        var sortFields = new List<SortField>();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var direction = property.Value.ValueKind switch
                {
                    JsonValueKind.Number => property.Value.GetInt32() >= 0 ? SortDirection.Ascending : SortDirection.Descending,
                    JsonValueKind.String => property.Value.GetString()?.ToLowerInvariant() switch
                    {
                        "asc" or "ascending" => SortDirection.Ascending,
                        "desc" or "descending" => SortDirection.Descending,
                        _ => SortDirection.Ascending
                    },
                    _ => SortDirection.Ascending
                };

                sortFields.Add(new SortField
                {
                    FieldName = property.Name,
                    Direction = direction
                });
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in item.EnumerateObject())
                    {
                        var direction = property.Value.ValueKind switch
                        {
                            JsonValueKind.Number => property.Value.GetInt32() >= 0 ? SortDirection.Ascending : SortDirection.Descending,
                            JsonValueKind.String => property.Value.GetString()?.ToLowerInvariant() switch
                            {
                                "asc" or "ascending" => SortDirection.Ascending,
                                "desc" or "descending" => SortDirection.Descending,
                                _ => SortDirection.Ascending
                            },
                            _ => SortDirection.Ascending
                        };

                        sortFields.Add(new SortField
                        {
                            FieldName = property.Name,
                            Direction = direction
                        });
                    }
                }
            }
        }

        return sortFields;
    }

    private QueryOptions ParseOptions(JsonElement element)
    {
        var options = new QueryOptions();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                switch (property.Name.ToLowerInvariant())
                {
                    case "limit":
                        options.Limit = property.Value.GetInt32();
                        break;
                    case "skip":
                        options.Skip = property.Value.GetInt32();
                        break;
                    case "includetotalcount":
                        options.IncludeTotalCount = property.Value.GetBoolean();
                        break;
                    case "timeoutms":
                    case "timeout":
                        options.TimeoutMs = property.Value.GetInt32();
                        break;
                }
            }
        }

        return options;
    }

    private Dictionary<string, bool> ParseProjection(JsonElement element)
    {
        var projection = new Dictionary<string, bool>();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                projection[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Number => property.Value.GetInt32() != 0,
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => true
                };
            }
        }

        return projection;
    }

    private static object JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonElementToDictionary(element),
            JsonValueKind.Array => JsonElementToList(element),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => TryGetNumber(element),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.ToString()!
        };
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

    private static List<object> JsonElementToList(JsonElement element)
    {
        var result = new List<object>();
        foreach (var item in element.EnumerateArray())
        {
            result.Add(JsonElementToObject(item));
        }
        return result;
    }

    private static object TryGetNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var longValue))
            return longValue;
        if (element.TryGetDouble(out var doubleValue))
            return doubleValue;
        return element.GetDecimal();
    }
}
