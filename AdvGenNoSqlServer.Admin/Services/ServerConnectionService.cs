// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Admin.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Core.Models;

/// <summary>
/// Service for managing connection to the NoSQL server.
/// </summary>
public class ServerConnectionService
{
    private AdvGenNoSqlClient? _client;
    private string _serverUrl = "localhost:9090";
    private string? _username;
    private string? _password;

    /// <summary>
    /// Event raised when connection state changes.
    /// </summary>
    public event EventHandler? ConnectionStateChanged;

    /// <summary>
    /// Gets whether connected to the server.
    /// </summary>
    public bool IsConnected => _client?.IsConnected ?? false;

    /// <summary>
    /// Gets the current server URL.
    /// </summary>
    public string ServerUrl => _serverUrl;

    /// <summary>
    /// Connects to the server and authenticates.
    /// </summary>
    public async Task<bool> ConnectAsync(string serverUrl, string? username = null, string? password = null)
    {
        try
        {
            _serverUrl = serverUrl;
            _username = username;
            _password = password;

            var options = new AdvGenNoSqlClientOptions
            {
                ConnectionTimeout = 10000,
                AutoReconnect = true
            };

            _client = new AdvGenNoSqlClient(serverUrl, options);
            await _client.ConnectAsync();

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                var authenticated = await _client.AuthenticateAsync(username, password);
                if (!authenticated)
                {
                    await _client.DisconnectAsync();
                    _client = null;
                    return false;
                }
            }

            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch
        {
            _client = null;
            return false;
        }
    }

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_client != null)
        {
            await _client.DisconnectAsync();
            _client = null;
        }

        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets real server statistics via the stats command.
    /// </summary>
    public async Task<ServerStats> GetServerStatsAsync()
    {
        if (_client == null || !_client.IsConnected)
            return new ServerStats();

        try
        {
            var response = await _client.ExecuteCommandAsync("stats", "");
            if (!response.Success || response.Data is not JsonElement je)
                return new ServerStats();

            return new ServerStats
            {
                ServerVersion = je.TryGetProperty("version", out var v) ? v.GetString() ?? "1.0.0" : "1.0.0",
                Uptime = je.TryGetProperty("uptimeSeconds", out var u)
                    ? TimeSpan.FromSeconds(u.GetInt64()) : TimeSpan.Zero,
                MemoryUsageMB = je.TryGetProperty("memoryUsageMB", out var m) ? m.GetInt32() : 0,
                TotalDocuments = je.TryGetProperty("totalDocuments", out var td) ? (int)td.GetInt64() : 0,
                TotalCollections = je.TryGetProperty("totalCollections", out var tc) ? tc.GetInt32() : 0,
                ActiveConnections = je.TryGetProperty("activeConnections", out var ac) ? ac.GetInt32() : 0,
                QueriesPerSecond = 0   // server doesn't track QPS yet
            };
        }
        catch
        {
            return new ServerStats();
        }
    }

    /// <summary>
    /// Gets real list of collections from the server.
    /// </summary>
    public async Task<List<string>> GetCollectionsAsync()
    {
        if (_client == null || !_client.IsConnected)
            return [];

        try
        {
            var response = await _client.ExecuteCommandAsync("listcollections", "");
            if (!response.Success || response.Data is not JsonElement je)
                return [];

            if (je.TryGetProperty("collections", out var colsEl))
                return colsEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s != "").ToList();

            return [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Gets documents from a collection.
    /// </summary>
    public async Task<List<Document>> GetDocumentsAsync(string collectionName, int skip = 0, int take = 50)
    {
        if (_client == null || !_client.IsConnected)
            return [];

        try
        {
            var response = await _client.ExecuteCommandAsync("get", collectionName, new { skip, take });
            if (!response.Success || response.Data is not JsonElement je)
                return [];

            var docs = new List<Document>();
            var arr = je.ValueKind == JsonValueKind.Array ? je : je.TryGetProperty("documents", out var d) ? d : default;
            if (arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var doc = new Document
                    {
                        Id = item.TryGetProperty("_id", out var id) ? id.GetString() ?? "" : "",
                        Data = item.Deserialize<Dictionary<string, object?>>() ?? new()
                    };
                    docs.Add(doc);
                }
            }
            return docs;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Gets a single document by ID.
    /// </summary>
    public async Task<Document?> GetDocumentAsync(string collectionName, string documentId)
    {
        if (_client == null || !_client.IsConnected)
            return null;

        try
        {
            var response = await _client.ExecuteCommandAsync("get", collectionName, new { id = documentId });
            if (!response.Success || response.Data is not JsonElement je)
                return null;

            return new Document
            {
                Id = documentId,
                Data = je.Deserialize<Dictionary<string, object?>>() ?? new()
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Executes a raw query.
    /// </summary>
    public async Task<QueryResult> ExecuteQueryAsync(string query)
    {
        if (_client == null || !_client.IsConnected)
            return new QueryResult { Success = false, ErrorMessage = "Not connected" };

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await _client.ExecuteQueryAsync(query);
            sw.Stop();

            if (!response.Success)
                return new QueryResult { Success = false, ErrorMessage = response.Error?.Message };

            var docs = new List<Document>();
            if (response.Data is JsonElement je)
            {
                var arr = je.ValueKind == JsonValueKind.Array ? je
                    : je.TryGetProperty("documents", out var d) ? d : default;
                if (arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        docs.Add(new Document
                        {
                            Id = item.TryGetProperty("_id", out var id) ? id.GetString() ?? "" : "",
                            Data = item.Deserialize<Dictionary<string, object?>>() ?? new()
                        });
                    }
                }
            }

            return new QueryResult
            {
                Success = true,
                Documents = docs,
                TotalCount = docs.Count,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            return new QueryResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}

/// <summary>
/// Server statistics model.
/// </summary>
public class ServerStats
{
    public int TotalDocuments { get; set; }
    public int TotalCollections { get; set; }
    public int ActiveConnections { get; set; }
    public TimeSpan Uptime { get; set; }
    public int MemoryUsageMB { get; set; }
    public int QueriesPerSecond { get; set; }
    public string ServerVersion { get; set; } = "1.0.0";
}

/// <summary>
/// Query result model.
/// </summary>
public class QueryResult
{
    public bool Success { get; set; }
    public List<Document> Documents { get; set; } = new();
    public int ExecutionTimeMs { get; set; }
    public int TotalCount { get; set; }
    public string? ErrorMessage { get; set; }
}
