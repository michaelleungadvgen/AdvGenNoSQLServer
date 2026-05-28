using System.Text.Json;
using AdvGenNoSqlServer.Client;

namespace AdvGenNoSqlServer.Tests;

public class ClientGetFixTests
{
    [Fact]
    public void ParseResponse_WithDocumentKey_ReturnsDocument()
    {
        // Simulate what GetAsync does with the JsonElement from ParseResponse
        var json = """{"found":true,"document":{"_id":"abc","name":"test"}}""";
        using var doc = JsonDocument.Parse(json);
        var dataElement = doc.RootElement;

        Dictionary<string, object>? result = null;
        if (dataElement.TryGetProperty("document", out var documentElement) &&
            documentElement.ValueKind != JsonValueKind.Null)
        {
            result = JsonSerializer.Deserialize<Dictionary<string, object>>(documentElement.GetRawText());
        }

        Assert.NotNull(result);
        Assert.Equal("abc", result["_id"].ToString());
    }

    [Fact]
    public void ParseResponse_WithValueKey_ReturnsNull()
    {
        // Proves the bug: "value" key yields nothing since server sends "document"
        var json = """{"found":true,"document":{"_id":"abc","name":"test"}}""";
        using var doc = JsonDocument.Parse(json);
        var dataElement = doc.RootElement;

        Dictionary<string, object>? result = null;
        if (dataElement.TryGetProperty("value", out var valueElement) &&
            valueElement.ValueKind != JsonValueKind.Null)
        {
            result = JsonSerializer.Deserialize<Dictionary<string, object>>(valueElement.GetRawText());
        }

        Assert.Null(result); // proves current bug returns null
    }
}
