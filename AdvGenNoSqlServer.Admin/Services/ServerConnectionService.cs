// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Admin.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;

/// <summary>
/// Service for managing connection to the NoSQL server.
/// NOTE: This is a Blazor WASM app. Raw TCP (AdvGenNoSqlClient) cannot be used from the browser.
/// All data methods return empty/zero until the server exposes an HTTP API.
/// </summary>
public class ServerConnectionService
{
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
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Gets the current server URL.
    /// </summary>
    public string ServerUrl => _serverUrl;

    /// <summary>
    /// Connects to the server. Note: this is a Blazor WASM app — raw TCP is not available
    /// in the browser. Connection is simulated; real data requires an HTTP API on the server.
    /// </summary>
    public async Task<bool> ConnectAsync(string serverUrl, string? username = null, string? password = null)
    {
        _serverUrl = serverUrl;
        _username = username;
        _password = password;

        // Blazor WASM cannot open raw TCP sockets — simulate the connection succeeding.
        // Real metrics require an HTTP/REST API endpoint on the server side.
        await Task.Delay(200);

        _isConnected = true;
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool _isConnected;

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    public Task DisconnectAsync()
    {
        _isConnected = false;
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns server statistics. Real values require an HTTP API on the server —
    /// Blazor WASM cannot use raw TCP. Returns zeros until an HTTP endpoint is added.
    /// </summary>
    public Task<ServerStats> GetServerStatsAsync()
    {
        return Task.FromResult(new ServerStats { ServerVersion = "1.0.0" });
    }

    /// <summary>
    /// Gets list of collections. Requires HTTP API on server — returns empty until implemented.
    /// </summary>
    public Task<List<string>> GetCollectionsAsync() => Task.FromResult(new List<string>());

    /// <summary>
    /// Gets documents from a collection. Requires HTTP API on server — returns empty until implemented.
    /// </summary>
    public Task<List<Document>> GetDocumentsAsync(string collectionName, int skip = 0, int take = 50)
        => Task.FromResult(new List<Document>());

    /// <summary>
    /// Gets a single document by ID. Requires HTTP API on server — returns null until implemented.
    /// </summary>
    public Task<Document?> GetDocumentAsync(string collectionName, string documentId)
        => Task.FromResult<Document?>(null);

    /// <summary>
    /// Executes a raw query. Requires HTTP API on server — returns empty until implemented.
    /// </summary>
    public Task<QueryResult> ExecuteQueryAsync(string query)
        => Task.FromResult(new QueryResult { Success = false, ErrorMessage = "HTTP API required — Blazor WASM cannot use raw TCP." });
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
