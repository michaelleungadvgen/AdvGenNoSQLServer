// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;
using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Examples.TodoApp.Models;
using Microsoft.Extensions.Options;

namespace AdvGenNoSqlServer.Examples.TodoApp.Services;

/// <summary>
/// Todo CRUD over the AdvGenNoSQL TCP client. A single connection is shared by all
/// circuits and every operation is serialized through a gate: the client library is
/// not safe for concurrent request/response use on one connection.
/// </summary>
public sealed class TodoService : IAsyncDisposable
{
    private readonly NoSqlConnectionOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AdvGenNoSqlClient? _client;

    public TodoService(IOptions<NoSqlConnectionOptions> options) => _options = options.Value;

    public string ConnectionDescription => $"{_options.Host}:{_options.Port}";
    public string Username => _options.Username;
    public string Collection => _options.Collection;

    public async Task<List<TodoItem>> ListAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var client = await ConnectLockedAsync();
            var response = await client.ExecuteCommandAsync(
                "listdocuments", _options.Collection, new { skip = 0, take = 500 });
            if (!response.Success)
                throw new InvalidOperationException(response.Error?.Message ?? "listdocuments failed");

            var items = new List<TodoItem>();
            if (response.Data is JsonElement data &&
                data.TryGetProperty("documents", out var documents))
            {
                foreach (var document in documents.EnumerateArray())
                    items.Add(Map(document));
            }
            return items.OrderBy(i => i.CreatedAt).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TodoItem> AddAsync(string title)
    {
        var item = new TodoItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            IsCompleted = false,
            CreatedAt = DateTime.UtcNow
        };

        await _gate.WaitAsync();
        try
        {
            var client = await ConnectLockedAsync();
            await client.SetAsync(_options.Collection, ToWire(item));
            return item;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(TodoItem item)
    {
        await _gate.WaitAsync();
        try
        {
            var client = await ConnectLockedAsync();
            await client.SetAsync(_options.Collection, ToWire(item));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id)
    {
        await _gate.WaitAsync();
        try
        {
            var client = await ConnectLockedAsync();
            await client.DeleteAsync(_options.Collection, id);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static object ToWire(TodoItem item) => new
    {
        _id = item.Id,
        title = item.Title,
        isCompleted = item.IsCompleted,
        createdAt = item.CreatedAt
    };

    private static TodoItem Map(JsonElement document)
    {
        var item = new TodoItem();
        if (document.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            item.Id = id.GetString() ?? "";
        else if (document.TryGetProperty("_id", out var underId) && underId.ValueKind == JsonValueKind.String)
            item.Id = underId.GetString() ?? "";

        // listdocuments returns Core Document shape ({id, data:{...}}); fall back to
        // a flat document if the fields sit at the top level.
        var fields = document.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? data
            : document;

        if (fields.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            item.Title = title.GetString() ?? "";
        if (fields.TryGetProperty("isCompleted", out var completed) &&
            (completed.ValueKind == JsonValueKind.True || completed.ValueKind == JsonValueKind.False))
            item.IsCompleted = completed.GetBoolean();
        if (fields.TryGetProperty("createdAt", out var created) && created.TryGetDateTime(out var createdAt))
            item.CreatedAt = createdAt;

        return item;
    }

    private async Task<AdvGenNoSqlClient> ConnectLockedAsync()
    {
        if (_client is { IsConnected: true })
            return _client;

        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        var client = new AdvGenNoSqlClient($"{_options.Host}:{_options.Port}",
            new AdvGenNoSqlClientOptions
            {
                UseSsl = _options.UseSsl,
                SslTargetHost = _options.Host,
                CheckCertificateRevocation = false
            });

        try
        {
            await client.ConnectAsync();
            if (!await client.AuthenticateAsync(_options.Username, _options.Password))
                throw new InvalidOperationException(
                    "Authentication failed — check NoSql:Username/Password in appsettings.json (the server seeds 'admin' from its MasterPassword).");
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }

        _client = client;
        return _client;
    }

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