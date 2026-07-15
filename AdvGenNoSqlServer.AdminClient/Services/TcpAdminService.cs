using System.Text.Json;
using AdvGenNoSqlServer.Client;

namespace AdvGenNoSqlServer.AdminClient.Services;

public class TcpAdminService : IAsyncDisposable
{
    private AdvGenNoSqlClient? _client;

    public bool IsConnected => _client?.IsConnected == true;
    public string? CurrentUser { get; private set; }
    public string? CurrentRole { get; private set; }
    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; }

    public async Task ConnectAndAuthenticateAsync(string host, int port, string username, string password)
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        var options = new AdvGenNoSqlClientOptions
        {
            UseSsl = true,
            SslTargetHost = host,
            CheckCertificateRevocation = false
        };

        var newClient = new AdvGenNoSqlClient($"{host}:{port}", options);
        try
        {
            await newClient.ConnectAsync();
            var authenticated = await newClient.AuthenticateAsync(username, password);
            if (!authenticated)
                throw new InvalidOperationException("Authentication failed: invalid credentials.");
        }
        catch
        {
            await newClient.DisposeAsync();
            throw;
        }
        _client = newClient;
        Host = host;
        Port = port;
        CurrentUser = username;
        CurrentRole = newClient.CurrentRole;
    }

    public async Task DisconnectAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
        CurrentUser = null;
        CurrentRole = null;
        Host = string.Empty;
        Port = 0;
    }

    public async Task<ServerStats> GetStatsAsync()
    {
        EnsureConnected();
        var response = await _client!.ExecuteCommandAsync("stats", "");
        EnsureSuccess(response, "stats");
        if (response.Data is not System.Text.Json.JsonElement data)
            throw new InvalidOperationException($"Unexpected response data from server.");
        return new ServerStats
        {
            Version = data.GetProperty("version").GetString() ?? "",
            UptimeSeconds = data.GetProperty("uptimeSeconds").GetInt64(),
            MemoryUsageMB = data.GetProperty("memoryUsageMB").GetInt32(),
            TotalDocuments = data.GetProperty("totalDocuments").GetInt64(),
            TotalCollections = data.GetProperty("totalCollections").GetInt32(),
            ActiveConnections = data.GetProperty("activeConnections").GetInt32()
        };
    }

    public async Task<List<string>> GetCollectionsAsync()
    {
        EnsureConnected();
        var response = await _client!.ExecuteCommandAsync("listcollections", "");
        EnsureSuccess(response, "listcollections");
        if (response.Data is not System.Text.Json.JsonElement data)
            throw new InvalidOperationException($"Unexpected response data from server.");
        return data.GetProperty("collections")
            .EnumerateArray()
            .Select(e => e.GetString() ?? "")
            .ToList();
    }

    public async Task<bool> CreateCollectionAsync(string name)
    {
        EnsureConnected();
        var response = await _client!.ExecuteCommandAsync("createcollection", name);
        EnsureSuccess(response, "createcollection");
        if (response.Data is not System.Text.Json.JsonElement data)
            throw new InvalidOperationException($"Unexpected response data from server.");
        return data.GetProperty("created").GetBoolean();
    }

    public async Task<bool> DeleteCollectionAsync(string name)
    {
        EnsureConnected();
        var response = await _client!.ExecuteCommandAsync("dropcollection", name);
        EnsureSuccess(response, "dropcollection");
        if (response.Data is not System.Text.Json.JsonElement data)
            throw new InvalidOperationException($"Unexpected response data from server.");
        return data.GetProperty("dropped").GetBoolean();
    }

    public async Task<long> CountAsync(string collection)
    {
        EnsureConnected();
        var response = await _client!.ExecuteCommandAsync("count", collection);
        EnsureSuccess(response, "count");
        if (response.Data is not System.Text.Json.JsonElement data)
            throw new InvalidOperationException($"Unexpected response data from server.");
        return data.GetProperty("count").GetInt64();
    }

    public async Task<(List<Dictionary<string, object>> Documents, long Total)> GetDocumentsAsync(
        string collection, int skip, int take)
    {
        EnsureConnected();
        var response = await _client!.ExecuteCommandAsync(
            "listdocuments", collection, new { skip, take });
        EnsureSuccess(response, "listdocuments");
        if (response.Data is not System.Text.Json.JsonElement data)
            throw new InvalidOperationException($"Unexpected response data from server.");

        var documents = data.GetProperty("documents")
            .EnumerateArray()
            .Select(e => JsonSerializer.Deserialize<Dictionary<string, object>>(e.GetRawText()))
            .Where(d => d != null)
            .Select(d => d!)
            .ToList();
        var total = data.GetProperty("total").GetInt64();
        return (documents, total);
    }

    public async Task<Dictionary<string, object>?> GetDocumentAsync(string collection, string id)
    {
        EnsureConnected();
        return await _client!.GetAsync(collection, id);
    }

    public async Task<string> UpsertDocumentAsync(string collection, object document)
    {
        EnsureConnected();
        return await _client!.SetAsync(collection, document);
    }

    public async Task<bool> DeleteDocumentAsync(string collection, string id)
    {
        EnsureConnected();
        return await _client!.DeleteAsync(collection, id);
    }

    public async Task<NoSqlResponse> ExecuteQueryAsync(string query)
    {
        EnsureConnected();
        return await _client!.ExecuteQueryAsync(query);
    }

    public async Task<List<UserInfo>> ListUsersAsync()
    {
        EnsureConnected();
        return (await _client!.ListUsersAsync()).ToList();
    }

    public Task<bool> CreateUserAsync(string username, string password, string role)
    {
        EnsureConnected();
        return _client!.CreateUserAsync(username, password, role);
    }

    public Task<bool> DeleteUserAsync(string username)
    {
        EnsureConnected();
        return _client!.DeleteUserAsync(username);
    }

    public Task<bool> SetUserPasswordAsync(string username, string password)
    {
        EnsureConnected();
        return _client!.SetUserPasswordAsync(username, password);
    }

    public Task<bool> SetUserRoleAsync(string username, string role)
    {
        EnsureConnected();
        return _client!.SetUserRoleAsync(username, role);
    }

    public Task<bool> ChangeMyPasswordAsync(string oldPassword, string newPassword)
    {
        EnsureConnected();
        return _client!.ChangeMyPasswordAsync(oldPassword, newPassword);
    }

    public async Task<List<AttachmentMetadata>> ListAttachmentsAsync(string collection, string id)
    {
        EnsureConnected();
        return (await _client!.Attachments.ListAsync(collection, id)).ToList();
    }

    public Task<AttachmentMetadata> UploadAttachmentAsync(string collection, string id, string name, string contentType, byte[] content)
    {
        EnsureConnected();
        return _client!.Attachments.UploadAsync(collection, id, name, contentType, content);
    }

    public Task<byte[]?> DownloadAttachmentAsync(string collection, string id, string name)
    {
        EnsureConnected();
        return _client!.Attachments.DownloadAsync(collection, id, name);
    }

    public Task<bool> DeleteAttachmentAsync(string collection, string id, string name)
    {
        EnsureConnected();
        return _client!.Attachments.DeleteAsync(collection, id, name);
    }

    public Task<long> GetTotalAttachmentStorageAsync()
    {
        EnsureConnected();
        return _client!.Attachments.TotalStorageBytesAsync();
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to server.");
    }

    private static void EnsureSuccess(NoSqlResponse response, string command)
    {
        if (!response.Success)
            throw new InvalidOperationException(
                $"Command '{command}' failed: {response.Error?.Message ?? "Unknown error"}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }
}

public record ServerStats
{
    public string Version { get; init; } = "";
    public long UptimeSeconds { get; init; }
    public int MemoryUsageMB { get; init; }
    public long TotalDocuments { get; init; }
    public int TotalCollections { get; init; }
    public int ActiveConnections { get; init; }
}
