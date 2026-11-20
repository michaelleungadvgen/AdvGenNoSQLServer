// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;

namespace AdvGenNoSqlServer.Mcp.E2ETests;

/// <summary>
/// Whole-system MCP tests: real Host + real MCP server over stdio, driven by an MCP client.
/// </summary>
public class McpServerE2ETests : IClassFixture<McpE2EFixture>
{
    private readonly McpE2EFixture _fixture;

    public McpServerE2ETests(McpE2EFixture fixture)
    {
        _fixture = fixture;
    }

    private static string NewCollection() => "mcp_" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task ToolsList_ContainsExpectedTools()
    {
        var tools = await _fixture.Client.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToHashSet();

        foreach (var expected in new[]
        {
            "list_collections", "create_collection", "drop_collection",
            "get_document", "set_document", "delete_document", "document_exists",
            "count_documents", "list_documents", "server_stats",
            "list_users", "create_user", "delete_user", "set_user_role", "set_user_password",
            "list_attachments", "attachment_info", "upload_attachment", "download_attachment",
            "delete_attachment", "total_storage"
        })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public async Task DocumentRoundTrip_SetGetExistsCountDelete()
    {
        var col = NewCollection();
        await _fixture.CallAsync("create_collection", new() { ["name"] = col });

        var set = JsonDocument.Parse(await _fixture.CallAsync("set_document", new()
        {
            ["collection"] = col,
            ["documentJson"] = "{\"name\":\"Milk\",\"price\":3.5}",
            ["id"] = "m1"
        }));
        Assert.Equal("m1", set.RootElement.GetProperty("id").GetString());

        var get = JsonDocument.Parse(await _fixture.CallAsync("get_document", new()
        {
            ["collection"] = col, ["id"] = "m1"
        }));
        Assert.True(get.RootElement.GetProperty("found").GetBoolean());
        Assert.Equal("Milk", get.RootElement.GetProperty("document").GetProperty("name").GetString());
        Assert.Equal(3.5, get.RootElement.GetProperty("document").GetProperty("price").GetDouble());

        var exists = JsonDocument.Parse(await _fixture.CallAsync("document_exists", new()
        {
            ["collection"] = col, ["id"] = "m1"
        }));
        Assert.True(exists.RootElement.GetProperty("exists").GetBoolean());

        var count = JsonDocument.Parse(await _fixture.CallAsync("count_documents", new()
        {
            ["collection"] = col
        }));
        Assert.Equal(1, count.RootElement.GetProperty("count").GetInt32());

        var delete = JsonDocument.Parse(await _fixture.CallAsync("delete_document", new()
        {
            ["collection"] = col, ["id"] = "m1"
        }));
        Assert.True(delete.RootElement.GetProperty("deleted").GetBoolean());

        var getAfter = JsonDocument.Parse(await _fixture.CallAsync("get_document", new()
        {
            ["collection"] = col, ["id"] = "m1"
        }));
        Assert.False(getAfter.RootElement.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task SetDocument_WithoutId_GeneratesId()
    {
        var col = NewCollection();
        var set = JsonDocument.Parse(await _fixture.CallAsync("set_document", new()
        {
            ["collection"] = col,
            ["documentJson"] = "{\"name\":\"AutoId\"}"
        }));
        var id = set.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id));

        var get = JsonDocument.Parse(await _fixture.CallAsync("get_document", new()
        {
            ["collection"] = col, ["id"] = id
        }));
        Assert.True(get.RootElement.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task ListDocuments_SkipTake_ReturnsPage()
    {
        var col = NewCollection();
        for (var i = 0; i < 5; i++)
        {
            await _fixture.CallAsync("set_document", new()
            {
                ["collection"] = col,
                ["documentJson"] = $"{{\"n\":{i}}}",
                ["id"] = $"d{i}"
            });
        }

        var page = JsonDocument.Parse(await _fixture.CallAsync("list_documents", new()
        {
            ["collection"] = col, ["skip"] = 2, ["take"] = 2
        }));
        var docs = page.RootElement.GetProperty("documents").EnumerateArray().ToList();
        Assert.Equal(2, docs.Count);
        Assert.Equal("d2", docs[0].GetProperty("_id").GetString());
        Assert.Equal("d3", docs[1].GetProperty("_id").GetString());
    }

    [Fact]
    public async Task DropCollection_RemovesCollection()
    {
        var col = NewCollection();
        await _fixture.CallAsync("create_collection", new() { ["name"] = col });
        await _fixture.CallAsync("drop_collection", new() { ["name"] = col });

        var list = await _fixture.CallAsync("list_collections");
        Assert.DoesNotContain(col, list);
    }

    [Fact]
    public async Task Users_CreateListSetRoleDelete()
    {
        var user = "mcpuser_" + Guid.NewGuid().ToString("N")[..8];
        var created = JsonDocument.Parse(await _fixture.CallAsync("create_user", new()
        {
            ["username"] = user, ["password"] = "Passw0rd!", ["role"] = "readonly"
        }));
        Assert.True(created.RootElement.GetProperty("created").GetBoolean());

        var list = await _fixture.CallAsync("list_users");
        Assert.Contains(user, list);

        var role = JsonDocument.Parse(await _fixture.CallAsync("set_user_role", new()
        {
            ["username"] = user, ["role"] = "readwrite"
        }));
        Assert.True(role.RootElement.GetProperty("changed").GetBoolean());

        var deleted = JsonDocument.Parse(await _fixture.CallAsync("delete_user", new()
        {
            ["username"] = user
        }));
        Assert.True(deleted.RootElement.GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public async Task Attachments_UploadDownloadInfoDelete()
    {
        var col = NewCollection();
        await _fixture.CallAsync("set_document", new()
        {
            ["collection"] = col, ["documentJson"] = "{\"name\":\"withfile\"}", ["id"] = "f1"
        });

        var content = Convert.ToBase64String("hello mcp"u8.ToArray());
        await _fixture.CallAsync("upload_attachment", new()
        {
            ["collection"] = col, ["id"] = "f1", ["name"] = "note.txt",
            ["contentType"] = "text/plain", ["base64Content"] = content
        });

        var list = await _fixture.CallAsync("list_attachments", new()
        {
            ["collection"] = col, ["id"] = "f1"
        });
        Assert.Contains("note.txt", list);

        var info = JsonDocument.Parse(await _fixture.CallAsync("attachment_info", new()
        {
            ["collection"] = col, ["id"] = "f1", ["name"] = "note.txt"
        }));
        Assert.Equal(9, info.RootElement.GetProperty("size").GetInt64());

        var download = JsonDocument.Parse(await _fixture.CallAsync("download_attachment", new()
        {
            ["collection"] = col, ["id"] = "f1", ["name"] = "note.txt"
        }));
        Assert.True(download.RootElement.GetProperty("found").GetBoolean());
        Assert.Equal(content, download.RootElement.GetProperty("base64Content").GetString());

        var deleted = JsonDocument.Parse(await _fixture.CallAsync("delete_attachment", new()
        {
            ["collection"] = col, ["id"] = "f1", ["name"] = "note.txt"
        }));
        Assert.True(deleted.RootElement.GetProperty("deleted").GetBoolean());

        var storage = JsonDocument.Parse(await _fixture.CallAsync("total_storage"));
        Assert.True(storage.RootElement.GetProperty("totalStorageBytes").GetInt64() >= 0);
    }

    [Fact]
    public async Task ServerStats_ReturnsLiveStats()
    {
        var stats = JsonDocument.Parse(await _fixture.CallAsync("server_stats"));
        Assert.True(stats.RootElement.GetProperty("uptimeSeconds").GetDouble() >= 0);
    }

    [Fact]
    public async Task SetDocument_InvalidJson_ReturnsToolError()
    {
        var result = await _fixture.Client.CallToolAsync("set_document", new Dictionary<string, object>
        {
            ["collection"] = NewCollection(),
            ["documentJson"] = "{not valid json"
        });
        Assert.True(result.IsError ?? false);
    }
}