// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Embedded;

/// <summary>
/// Serializes <see cref="Document"/> to/from the compact JSON stored in record bodies.
/// Data values are re-materialized as plain CLR primitives (long/double/string/bool/list/dict)
/// so the reused query filter engine compares them correctly.
/// </summary>
internal static class DocumentSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static byte[] Serialize(Document document)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("id", document.Id);
            writer.WriteString("createdAt", document.CreatedAt);
            writer.WriteString("updatedAt", document.UpdatedAt);
            writer.WriteNumber("version", document.Version);
            writer.WritePropertyName("data");
            JsonSerializer.Serialize(writer, document.Data ?? new Dictionary<string, object>(), Options);
            writer.WriteEndObject();
        }
        return ms.ToArray();
    }

    public static Document Deserialize(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        var data = new Dictionary<string, object>();
        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in dataEl.EnumerateObject())
                data[prop.Name] = ToClr(prop.Value)!;
        }
        return new Document
        {
            Id = root.GetProperty("id").GetString()!,
            Data = data,
            CreatedAt = root.GetProperty("createdAt").GetDateTime(),
            UpdatedAt = root.GetProperty("updatedAt").GetDateTime(),
            Version = root.GetProperty("version").GetInt64(),
        };
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

    // Preserve integral vs floating type. A ternary here would unify to double and lose longs.
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

    // Kept for symmetry with callers that already have UTF-8 text.
    public static string ToJsonString(Document document) => Encoding.UTF8.GetString(Serialize(document));
}
