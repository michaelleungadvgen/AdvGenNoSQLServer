// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Reflection;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Embedded.Typed;

/// <summary>Marks the property that carries a POCO's document id when it is not named <c>Id</c>.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EmbeddedIdAttribute : Attribute { }

/// <summary>
/// Maps POCOs to and from <see cref="Document"/>. The id comes from a public string property
/// named <c>Id</c> (or one carrying <see cref="EmbeddedIdAttribute"/>). All other properties
/// serialize into <see cref="Document.Data"/> via System.Text.Json and are re-materialized as
/// plain CLR values so the query filter engine compares them correctly.
/// </summary>
internal sealed class DocumentMapper<T> where T : class
{
    private readonly JsonSerializerOptions _options;
    private readonly PropertyInfo _idProperty;

    public DocumentMapper(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions();
        _idProperty = ResolveIdProperty();
    }

    /// <summary>The property used as the document id.</summary>
    public PropertyInfo IdProperty => _idProperty;

    /// <summary>Reads the id value from an entity (may be null/empty).</summary>
    public string? GetId(T entity) => _idProperty.GetValue(entity) as string;

    /// <summary>Writes an id back onto an entity (e.g. after insert assigns one).</summary>
    public void SetId(T entity, string id) => _idProperty.SetValue(entity, id);

    /// <summary>Converts an entity to a document.</summary>
    public Document ToDocument(T entity)
    {
        var id = GetId(entity) ?? string.Empty;
        // Serialize the whole entity, then drop the id property from Data.
        var json = JsonSerializer.SerializeToElement(entity, _options);
        var data = new Dictionary<string, object>();
        foreach (var prop in json.EnumerateObject())
        {
            if (string.Equals(prop.Name, _idProperty.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            data[prop.Name] = ToClr(prop.Value)!;
        }
        return new Document { Id = id, Data = data };
    }

    /// <summary>Converts a document back to an entity.</summary>
    public T ToEntity(Document doc)
    {
        // Rebuild a JSON object combining the id property and the data fields, then deserialize.
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WritePropertyName(_idProperty.Name);
            writer.WriteStringValue(doc.Id);
            if (doc.Data != null)
            {
                foreach (var kv in doc.Data)
                {
                    if (string.Equals(kv.Key, _idProperty.Name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    writer.WritePropertyName(kv.Key);
                    JsonSerializer.Serialize(writer, kv.Value, _options);
                }
            }
            writer.WriteEndObject();
        }
        var entity = JsonSerializer.Deserialize<T>(ms.ToArray(), _options)
            ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name}");
        return entity;
    }

    private static PropertyInfo ResolveIdProperty()
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var attributed = props.FirstOrDefault(p => p.GetCustomAttribute<EmbeddedIdAttribute>() != null);
        if (attributed != null)
        {
            if (attributed.PropertyType != typeof(string))
                throw new InvalidOperationException($"[EmbeddedId] property '{attributed.Name}' on {typeof(T).Name} must be a string");
            return attributed;
        }
        var byName = props.FirstOrDefault(p => p.Name == "Id" && p.PropertyType == typeof(string));
        if (byName != null) return byName;

        throw new InvalidOperationException(
            $"Type {typeof(T).Name} needs a public string 'Id' property or a property marked [EmbeddedId].");
    }

    private static object? ToClr(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ToDictionary(element),
        JsonValueKind.Array => element.EnumerateArray().Select(ToClr).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => ReadNumber(element),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.ToString()
    };

    private static object ReadNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var l)) return l;
        return element.GetDouble();
    }

    private static Dictionary<string, object> ToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in element.EnumerateObject())
            dict[prop.Name] = ToClr(prop.Value)!;
        return dict;
    }
}
