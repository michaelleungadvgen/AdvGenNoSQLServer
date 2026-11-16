// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdvGenNoSqlServer.Client;

namespace AdvGenNoSqlServer.Host.E2ETests;

[CollectionDefinition(Name)]
public class HostE2ECollection : ICollectionFixture<HostE2EFixture>
{
    public const string Name = "Host E2E";
}

/// <summary>
/// End-to-end tests against the real production Host over both protocols it exposes:
/// the TCP binary protocol (via the client library) and the REST API (via HttpClient).
/// The fixture runs the Host in cache mode.
/// </summary>
[Collection(HostE2ECollection.Name)]
public class HostE2ETests
{
    private readonly HostE2EFixture _fixture;

    public HostE2ETests(HostE2EFixture fixture) => _fixture = fixture;

    private static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..24];

    // ---------- TCP protocol ----------

    [Fact]
    public async Task Tcp_Auth_Succeeds_WithAdminRole()
    {
        await using var client = await _fixture.NewClientAsync();
        Assert.True(client.IsConnected);
        Assert.Equal("admin", client.CurrentRole);
    }

    [Fact]
    public async Task Tcp_WrongPassword_Fails()
    {
        var client = new AdvGenNoSqlClient($"localhost:{HostE2EFixture.TcpPort}", new AdvGenNoSqlClientOptions { UseSsl = false });
        await client.ConnectAsync();
        Assert.False(await client.AuthenticateAsync("admin", "definitely-wrong"));
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Tcp_DocumentCrud_RoundTrip()
    {
        await using var client = await _fixture.NewClientAsync();
        var collection = Unique("tcpdocs");
        await client.ExecuteCommandAsync("createcollection", collection);

        var id = await client.SetAsync(collection, new { _id = "d1", name = "alpha", value = 1 });
        Assert.Equal("d1", id);
        Assert.True(await client.ExistsAsync(collection, "d1"));

        var doc = await client.GetAsync(collection, "d1");
        Assert.NotNull(doc);

        Assert.True(await client.DeleteAsync(collection, "d1"));
        Assert.False(await client.ExistsAsync(collection, "d1"));
    }

    [Fact]
    public async Task Tcp_Collections_CreateListDrop()
    {
        await using var client = await _fixture.NewClientAsync();
        var collection = Unique("tcpcoll");
        await client.ExecuteCommandAsync("createcollection", collection);

        var list = await client.ExecuteCommandAsync("listcollections", "");
        Assert.True(list.Success);
        var json = JsonSerializer.Serialize(list.Data);
        Assert.Contains(collection, json);

        var drop = await client.ExecuteCommandAsync("dropcollection", collection);
        Assert.True(drop.Success);
    }

    [Fact]
    public async Task Tcp_Count_And_Stats()
    {
        await using var client = await _fixture.NewClientAsync();
        var collection = Unique("tcpcount");
        await client.ExecuteCommandAsync("createcollection", collection);
        await client.SetAsync(collection, new { _id = "a", n = 1 });
        await client.SetAsync(collection, new { _id = "b", n = 2 });

        var count = await client.ExecuteCommandAsync("count", collection);
        Assert.True(count.Success);
        Assert.Contains("2", JsonSerializer.Serialize(count.Data));

        var stats = await client.ExecuteCommandAsync("stats", "");
        Assert.True(stats.Success);
        var statsJson = JsonSerializer.Serialize(stats.Data);
        Assert.Contains("version", statsJson);
        Assert.Contains("uptimeSeconds", statsJson);
    }

    [Fact]
    public async Task Tcp_Users_CreateListSetRoleDelete()
    {
        await using var client = await _fixture.NewClientAsync();
        var user = Unique("tcpuser");
        Assert.True(await client.CreateUserAsync(user, "Passw0rd!", "readonly"));

        var users = await client.ListUsersAsync();
        Assert.Contains(users, u => u.Username == user && u.Role == "readonly");

        Assert.True(await client.SetUserRoleAsync(user, "readwrite"));
        users = await client.ListUsersAsync();
        Assert.Contains(users, u => u.Username == user && u.Role == "readwrite");

        Assert.True(await client.DeleteUserAsync(user));
        users = await client.ListUsersAsync();
        Assert.DoesNotContain(users, u => u.Username == user);
    }

    [Fact]
    public async Task Tcp_Attachments_UploadDownloadDelete()
    {
        await using var client = await _fixture.NewClientAsync();
        var collection = Unique("tcpatt");
        await client.ExecuteCommandAsync("createcollection", collection);
        await client.SetAsync(collection, new { _id = "doc1", title = "with file" });

        var content = new byte[] { 1, 2, 3, 4, 5 };
        var meta = await client.Attachments.UploadAsync(collection, "doc1", "data.bin", "application/octet-stream", content);
        Assert.Equal("data.bin", meta.Name);
        Assert.Equal(content.Length, meta.Size);

        var downloaded = await client.Attachments.DownloadAsync(collection, "doc1", "data.bin");
        Assert.Equal(content, downloaded);

        var list = await client.Attachments.ListAsync(collection, "doc1");
        Assert.Contains(list, m => m.Name == "data.bin");

        Assert.True(await client.Attachments.DeleteAsync(collection, "doc1", "data.bin"));
        Assert.Null(await client.Attachments.DownloadAsync(collection, "doc1", "data.bin"));
    }

    // ---------- REST API ----------

    [Fact]
    public async Task Rest_Health_IsAnonymous()
    {
        using var client = _fixture.NewRestClient();
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Rest_Login_ReturnsTokenAndRole()
    {
        using var client = _fixture.NewRestClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin123" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));
        Assert.Equal("admin", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Rest_Stats_RequiresAuth()
    {
        using var anonymous = _fixture.NewRestClient();
        var unauth = await anonymous.GetAsync("/api/stats");
        Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

        using var authed = await _fixture.NewAuthenticatedRestClientAsync();
        var authedResponse = await authed.GetAsync("/api/stats");
        Assert.Equal(HttpStatusCode.OK, authedResponse.StatusCode);
    }

    [Fact]
    public async Task Rest_CollectionAndDocumentCrud()
    {
        using var client = await _fixture.NewAuthenticatedRestClientAsync();
        var collection = Unique("restcoll");

        var create = await client.PostAsync($"/api/collections/{collection}", null);
        Assert.True(create.IsSuccessStatusCode);

        var insert = await client.PostAsJsonAsync($"/api/collections/{collection}/documents",
            new { id = "r1", data = new { name = "beta", value = 2 } });
        Assert.True(insert.IsSuccessStatusCode);

        var get = await client.GetAsync($"/api/collections/{collection}/documents/r1");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var doc = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("beta", doc.ToString());

        var delete = await client.DeleteAsync($"/api/collections/{collection}/documents/r1");
        Assert.True(delete.IsSuccessStatusCode);

        var drop = await client.DeleteAsync($"/api/collections/{collection}");
        Assert.True(drop.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Rest_UsersCrud()
    {
        using var client = await _fixture.NewAuthenticatedRestClientAsync();
        var user = Unique("restuser");

        var create = await client.PostAsJsonAsync("/api/users", new { username = user, password = "Passw0rd!", role = "readonly" });
        Assert.True(create.IsSuccessStatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/users");
        Assert.Contains(user, list.ToString());

        var role = await client.PutAsJsonAsync($"/api/users/{user}/role", new { role = "readwrite" });
        Assert.True(role.IsSuccessStatusCode);

        var delete = await client.DeleteAsync($"/api/users/{user}");
        Assert.True(delete.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Rest_Metrics_RequiresAuth()
    {
        using var anonymous = _fixture.NewRestClient();
        var unauth = await anonymous.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

        using var authed = await _fixture.NewAuthenticatedRestClientAsync();
        var authedResponse = await authed.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, authedResponse.StatusCode);
    }

    // ---------- cache mode ----------

    [Fact]
    public async Task CacheMode_WritesNoDiskFiles()
    {
        await using var client = await _fixture.NewClientAsync();
        var collection = Unique("cachecoll");
        await client.ExecuteCommandAsync("createcollection", collection);
        await client.SetAsync(collection, new { _id = "x", n = 1 });

        using var rest = await _fixture.NewAuthenticatedRestClientAsync();
        await rest.PostAsJsonAsync("/api/users", new { username = Unique("cacheuser"), password = "Passw0rd!", role = "readonly" });

        var entries = Directory.Exists(_fixture.StorageDir)
            ? Directory.EnumerateFileSystemEntries(_fixture.StorageDir).ToArray()
            : Array.Empty<string>();
        Assert.Empty(entries);
    }
}