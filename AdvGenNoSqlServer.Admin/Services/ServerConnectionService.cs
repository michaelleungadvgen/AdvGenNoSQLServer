// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Admin.Services;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;

/// <summary>
/// Service for managing connection to the NoSQL server via HTTP API (port = TCP port + 1).
/// </summary>
public class ServerConnectionService
{
    private readonly HttpClient _http;
    private string _serverUrl = "localhost:9090";
    private string? _baseApiUrl;
    private bool _isConnected;

    public ServerConnectionService(HttpClient http) => _http = http;

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
    /// Connects by probing GET /api/stats on the HTTP API port (TCP port + 1).
    /// </summary>
    public async Task<bool> ConnectAsync(string serverUrl, string? username = null, string? password = null)
    {
        _serverUrl = serverUrl;
        // Derive HTTP API URL: parse host:port, increment port by 1
        _baseApiUrl = DeriveApiUrl(serverUrl);

        try
        {
            var resp = await _http.GetAsync($"{_baseApiUrl}/api/stats");
            _isConnected = resp.IsSuccessStatusCode;
        }
        catch
        {
            _isConnected = false;
        }

        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        return _isConnected;
    }

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    public Task DisconnectAsync()
    {
        _isConnected = false;
        _baseApiUrl = null;
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns live server statistics from GET /api/stats.
    /// </summary>
    public async Task<ServerStats> GetServerStatsAsync()
    {
        if (!_isConnected || _baseApiUrl == null) return new ServerStats();
        try
        {
            var s = await _http.GetFromJsonAsync<ServerStatsDto>($"{_baseApiUrl}/api/stats");
            if (s == null) return new ServerStats();
            return new ServerStats
            {
                ServerVersion = s.Version ?? "1.0.0",
                Uptime = TimeSpan.FromSeconds(s.UptimeSeconds),
                MemoryUsageMB = s.MemoryUsageMB,
                TotalDocuments = (int)s.TotalDocuments,
                TotalCollections = s.TotalCollections,
                ActiveConnections = s.ActiveConnections,
            };
        }
        catch { return new ServerStats(); }
    }

    /// <summary>
    /// Gets list of collections from GET /api/collections.
    /// </summary>
    public async Task<List<string>> GetCollectionsAsync()
    {
        if (!_isConnected || _baseApiUrl == null) return [];
        try { return await _http.GetFromJsonAsync<List<string>>($"{_baseApiUrl}/api/collections") ?? []; }
        catch { return []; }
    }

    /// <summary>
    /// Gets documents from a collection via GET /api/collections/{name}/documents.
    /// </summary>
    public async Task<List<Document>> GetDocumentsAsync(string collectionName, int skip = 0, int take = 50)
    {
        if (!_isConnected || _baseApiUrl == null) return [];
        try
        {
            return await _http.GetFromJsonAsync<List<Document>>(
                $"{_baseApiUrl}/api/collections/{Uri.EscapeDataString(collectionName)}/documents?skip={skip}&take={take}") ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Gets a single document by ID via GET /api/collections/{name}/documents/{id}.
    /// </summary>
    public async Task<Document?> GetDocumentAsync(string collectionName, string documentId)
    {
        if (!_isConnected || _baseApiUrl == null) return null;
        try
        {
            var resp = await _http.GetAsync(
                $"{_baseApiUrl}/api/collections/{Uri.EscapeDataString(collectionName)}/documents/{Uri.EscapeDataString(documentId)}");
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<Document>() : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Executes a query via POST /api/query.
    /// </summary>
    public async Task<QueryResult> ExecuteQueryAsync(string query)
    {
        if (!_isConnected || _baseApiUrl == null)
            return new QueryResult { Success = false, ErrorMessage = "Not connected" };
        try
        {
            var resp = await _http.PostAsJsonAsync($"{_baseApiUrl}/api/query", new { query, collection = query });
            if (!resp.IsSuccessStatusCode)
                return new QueryResult { Success = false, ErrorMessage = $"HTTP {resp.StatusCode}" };
            return await resp.Content.ReadFromJsonAsync<QueryResult>() ?? new QueryResult { Success = false };
        }
        catch (Exception ex) { return new QueryResult { Success = false, ErrorMessage = ex.Message }; }
    }

    private static string DeriveApiUrl(string serverUrl)
    {
        // Strip protocol prefix if present
        var url = serverUrl.Replace("http://", "").Replace("https://", "").TrimEnd('/');
        var lastColon = url.LastIndexOf(':');
        if (lastColon >= 0 && int.TryParse(url[(lastColon + 1)..], out var port))
            return $"http://{url[..lastColon]}:{port + 1}";
        return $"http://{url}";
    }

    // DTO for deserializing /api/stats response
    private record ServerStatsDto(
        string? Version,
        long UptimeSeconds,
        int MemoryUsageMB,
        long TotalDocuments,
        int TotalCollections,
        int ActiveConnections);
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
