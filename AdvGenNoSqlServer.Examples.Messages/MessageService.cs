// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;
using AdvGenNoSqlServer.Client;

namespace AdvGenNoSqlServer.Examples.Messages;

/// <summary>A single chat message stored in the cache-backed collection.</summary>
public sealed record Message(string Id, string User, string Text, DateTime Timestamp);

/// <summary>
/// Message-board operations over the AdvGenNoSQL TCP client. The server is expected to
/// run in <c>StorageMode=Cache</c>, so every message lives only in server memory and is
/// lost when the server restarts - the point of this demo.
/// </summary>
public sealed class MessageService : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly bool _useSsl;
    private readonly string _collection;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AdvGenNoSqlClient? _client;

    public MessageService(string host, int port, string username, string password, bool useSsl, string collection)
    {
        _host = host;
        _port = port;
        _username = username;
        _password = password;
        _useSsl = useSsl;
        _collection = collection;
    }

    public string ConnectionDescription => $"{_host}:{_port}";
    public string Username => _username;
    public string Collection => _collection;
    public string? Role => _client?.CurrentRole;
    public bool IsConnected => _client is { IsConnected: true };

    public async Task ConnectAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_client is { IsConnected: true })
                return;

            if (_client != null)
            {
                await _client.DisposeAsync();
                _client = null;
            }

            var client = new AdvGenNoSqlClient($"{_host}:{_port}", new AdvGenNoSqlClientOptions
            {
                UseSsl = _useSsl,
                SslTargetHost = _host,
                CheckCertificateRevocation = false
            });

            try
            {
                await client.ConnectAsync();
                if (!await client.AuthenticateAsync(_username, _password))
                    throw new InvalidOperationException("Authentication failed - check the username/password (the server seeds 'admin' from its MasterPassword).");
            }
            catch
            {
                await client.DisposeAsync();
                throw;
            }

            _client = client;
            await EnsureCollectionLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureCollectionLockedAsync()
    {
        var response = await Client.ExecuteCommandAsync("createcollection", _collection);
        // "already exists" is fine; surface any other error.
        if (!response.Success && response.Error?.Code != "ALREADY_EXISTS")
            throw new InvalidOperationException($"createcollection failed: {response.Error?.Message}");
    }

    public async Task<string> SendAsync(string user, string text)
    {
        await _gate.WaitAsync();
        try
        {
            var id = Guid.NewGuid().ToString("N");
            await Client.SetAsync(_collection, new
            {
                _id = id,
                user,
                text,
                timestamp = DateTime.UtcNow
            });
            return id;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<Message>> ListAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var response = await Client.ExecuteCommandAsync("listdocuments", _collection, new { skip = 0, take = 500 });
            if (!response.Success)
                throw new InvalidOperationException($"listdocuments failed: {response.Error?.Message}");

            var messages = new List<Message>();
            if (response.Data is JsonElement data && data.TryGetProperty("documents", out var documents))
            {
                foreach (var document in documents.EnumerateArray())
                    messages.Add(Map(document));
            }
            return messages.OrderBy(m => m.Timestamp).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> CountAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var response = await Client.ExecuteCommandAsync("count", _collection);
            if (!response.Success)
                throw new InvalidOperationException($"count failed: {response.Error?.Message}");

            if (response.Data is JsonElement data && data.TryGetProperty("count", out var count))
                return count.GetInt64();
            return 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var drop = await Client.ExecuteCommandAsync("dropcollection", _collection);
            if (!drop.Success)
                throw new InvalidOperationException($"dropcollection failed: {drop.Error?.Message}");
            await EnsureCollectionLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<JsonElement?> StatsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var response = await Client.ExecuteCommandAsync("stats", "");
            if (!response.Success)
                throw new InvalidOperationException($"stats failed: {response.Error?.Message}");
            return response.Data as JsonElement?;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Message Map(JsonElement document)
    {
        var id = "";
        if (document.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
            id = idEl.GetString() ?? "";
        else if (document.TryGetProperty("_id", out var underId) && underId.ValueKind == JsonValueKind.String)
            id = underId.GetString() ?? "";

        // listdocuments returns the Core Document shape ({id, data:{...}}); fall back to flat.
        var fields = document.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? data
            : document;

        var user = fields.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "?" : "?";
        var text = fields.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
        var timestamp = fields.TryGetProperty("timestamp", out var ts) && ts.TryGetDateTime(out var dt) ? dt : DateTime.MinValue;

        return new Message(id, user, text, timestamp);
    }

    private AdvGenNoSqlClient Client => _client ?? throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

    public async ValueTask DisposeAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
        _gate.Dispose();
    }
}