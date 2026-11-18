// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Admin.Services;

using System.Net.Http.Json;
using AdvGenNoSqlServer.Core.Models;

/// <summary>
/// Interface for NoSQL server client operations.
/// Provides a client library pattern for Admin App (uses HTTP in WASM, TCP in native).
/// </summary>
public interface INoSqlServerClient
{
    /// <summary>
    /// Gets whether connected to the server.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the current server URL.
    /// </summary>
    string ServerUrl { get; }

    /// <summary>
    /// Gets the last connection error message.
    /// </summary>
    string? LastConnectError { get; }

    /// <summary>
    /// Event raised when connection state changes.
    /// </summary>
    event EventHandler? ConnectionStateChanged;

    /// <summary>
    /// Gets the currently selected database name.
    /// </summary>
    string? CurrentDatabase { get; }

    /// <summary>
    /// Connects to the NoSQL server.
    /// </summary>
    Task<bool> ConnectAsync(string serverUrl, string? username = null, string? password = null);

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Gets the list of databases.
    /// </summary>
    Task<List<string>> GetDatabasesAsync();

    /// <summary>
    /// Creates a new database.
    /// </summary>
    Task<bool> CreateDatabaseAsync(string name);

    /// <summary>
    /// Deletes a database.
    /// </summary>
    Task<bool> DeleteDatabaseAsync(string name);

    /// <summary>
    /// Selects a database to use.
    /// </summary>
    Task<bool> SelectDatabaseAsync(string name);

    /// <summary>
    /// Authenticates with the server.
    /// </summary>
    Task<bool> AuthenticateAsync(string username, string password);

    /// <summary>
    /// Returns server statistics.
    /// </summary>
    Task<ServerStats> GetServerStatsAsync();

    /// <summary>
    /// Creates a collection.
    /// </summary>
    Task<(bool Success, string? Error)> CreateCollectionAsync(string name);

    /// <summary>
    /// Deletes a collection.
    /// </summary>
    Task<bool> DeleteCollectionAsync(string name);

    /// <summary>
    /// Gets document count for a collection.
    /// </summary>
    Task<long> GetCollectionCountAsync(string name);

    /// <summary>
    /// Gets list of collections.
    /// </summary>
    Task<List<string>> GetCollectionsAsync();

    /// <summary>
    /// Gets documents from a collection.
    /// </summary>
    Task<List<Document>> GetDocumentsAsync(string collectionName, int skip = 0, int take = 50);

    /// <summary>
    /// Gets a single document by ID.
    /// </summary>
    Task<Document?> GetDocumentAsync(string collectionName, string documentId);

    /// <summary>
    /// Executes a query.
    /// </summary>
    Task<QueryResult> ExecuteQueryAsync(string query);

    /// <summary>
    /// Inserts a document and returns the result with document ID.
    /// </summary>
    Task<(bool Success, string? DocumentId, string? Error)> InsertDocumentAsync(string collectionName, Document document);

    /// <summary>
    /// Updates a document and returns the result.
    /// </summary>
    Task<(bool Success, string? Error)> UpdateDocumentAsync(string collectionName, Document document);

    /// <summary>
    /// Deletes a document.
    /// </summary>
    Task<bool> DeleteDocumentAsync(string collectionName, string documentId);
}

/// <summary>
/// Service for managing connection to the NoSQL server via HTTP API.
/// Implements INoSqlServerClient to provide client library pattern.
/// Note: Blazor WebAssembly uses HTTP (port+1) as TCP sockets are not supported in browsers.
/// </summary>
public class ServerConnectionService : INoSqlServerClient
{
    private readonly HttpClient _http;
    private string _serverUrl = "localhost:9090";
    private string? _baseApiUrl;
    private bool _isConnected;
    private string? _lastConnectError;
    private string? _currentDatabase;
    private string? _authToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerConnectionService"/> class.
    /// </summary>
    /// <param name="http">The HTTP client used for all server API requests.</param>
    public ServerConnectionService(HttpClient http) => _http = http;

    /// <inheritdoc />
    public string? LastConnectError => _lastConnectError;

    /// <inheritdoc />
    public event EventHandler? ConnectionStateChanged;

    /// <inheritdoc />
    public bool IsConnected => _isConnected;

    /// <inheritdoc />
    public string ServerUrl => _serverUrl;

    /// <inheritdoc />
    public string? CurrentDatabase => _currentDatabase;

    /// <summary>
    /// Gets or sets the JWT authentication token.
    /// </summary>
    public string? AuthToken 
    { 
        get => _authToken;
        set
        {
            _authToken = value;
            // Update HttpClient default headers
            _http.DefaultRequestHeaders.Authorization = 
                string.IsNullOrEmpty(value) ? null : 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", value);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ConnectAsync(string serverUrl, string? username = null, string? password = null)
    {
        _serverUrl = serverUrl;
        _lastConnectError = null;

        var httpUrl = DeriveApiUrl(serverUrl);

        try
        {
            Console.WriteLine($"[Connect] GET {httpUrl}/api/health");
            var resp = await _http.GetAsync($"{httpUrl}/api/health");
            Console.WriteLine($"[Connect] Response: {(int)resp.StatusCode}");
            _isConnected = resp.IsSuccessStatusCode;
            if (_isConnected)
            {
                _baseApiUrl = httpUrl;
            }
            else
            {
                _lastConnectError = $"HTTP {(int)resp.StatusCode} from {httpUrl}/api/health";
            }
        }
        catch (Exception ex)
        {
            _isConnected = false;
            _lastConnectError = $"{ex.GetType().Name}: {ex.Message} (URL: {httpUrl}/api/health)";
            Console.WriteLine($"[Connect] Exception: {_lastConnectError}");
        }

        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        return _isConnected;
    }

    /// <inheritdoc />
    public Task DisconnectAsync()
    {
        _isConnected = false;
        _baseApiUrl = null;
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> AuthenticateAsync(string username, string password)
    {
        if (!_isConnected || _baseApiUrl == null) return false;
        try
        {
            var resp = await _http.PostAsJsonAsync($"{_baseApiUrl}/api/auth/login", new { username, password });
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<(bool Success, string? Error)> CreateCollectionAsync(string name)
    {
        if (!_isConnected || _baseApiUrl == null) return (false, "Not connected");
        try
        {
            var resp = await _http.PostAsync($"{_baseApiUrl}/api/collections/{Uri.EscapeDataString(name)}", null);
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadAsStringAsync();
            return (false, $"HTTP {(int)resp.StatusCode}: {body}");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteCollectionAsync(string name)
    {
        if (!_isConnected || _baseApiUrl == null) return false;
        try
        {
            var resp = await _http.DeleteAsync($"{_baseApiUrl}/api/collections/{Uri.EscapeDataString(name)}");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <inheritdoc />
    public async Task<long> GetCollectionCountAsync(string name)
    {
        if (!_isConnected || _baseApiUrl == null) return 0;
        try
        {
            var result = await _http.GetFromJsonAsync<CountResult>($"{_baseApiUrl}/api/collections/{Uri.EscapeDataString(name)}/count");
            return result?.Count ?? 0;
        }
        catch { return 0; }
    }

    /// <inheritdoc />
    public async Task<List<string>> GetCollectionsAsync()
    {
        if (!_isConnected || _baseApiUrl == null) return [];
        try { return await _http.GetFromJsonAsync<List<string>>($"{_baseApiUrl}/api/collections") ?? []; }
        catch { return []; }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<(bool Success, string? DocumentId, string? Error)> InsertDocumentAsync(string collectionName, Document document)
    {
        if (!_isConnected || _baseApiUrl == null) return (false, null, "Not connected");
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"{_baseApiUrl}/api/collections/{Uri.EscapeDataString(collectionName)}/documents", document);
            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<DocumentResponse>();
                return (result?.Success ?? false, result?.Id, null);
            }
            var error = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
            return (false, null, error?.Error ?? $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? Error)> UpdateDocumentAsync(string collectionName, Document document)
    {
        if (!_isConnected || _baseApiUrl == null) return (false, "Not connected");
        try
        {
            var resp = await _http.PutAsJsonAsync(
                $"{_baseApiUrl}/api/collections/{Uri.EscapeDataString(collectionName)}/documents/{Uri.EscapeDataString(document.Id)}", document);
            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<DocumentResponse>();
                return (result?.Success ?? false, null);
            }
            var error = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
            return (false, error?.Error ?? $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDocumentAsync(string collectionName, string documentId)
    {
        if (!_isConnected || _baseApiUrl == null) return false;
        try
        {
            var resp = await _http.DeleteAsync(
                $"{_baseApiUrl}/api/collections/{Uri.EscapeDataString(collectionName)}/documents/{Uri.EscapeDataString(documentId)}");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <inheritdoc />
    public async Task<List<string>> GetDatabasesAsync()
    {
        if (!_isConnected || _baseApiUrl == null) return [];
        try
        {
            return await _http.GetFromJsonAsync<List<string>>($"{_baseApiUrl}/api/databases") ?? [];
        }
        catch { return []; }
    }

    /// <inheritdoc />
    public async Task<bool> CreateDatabaseAsync(string name)
    {
        if (!_isConnected || _baseApiUrl == null) return false;
        try
        {
            var resp = await _http.PostAsync($"{_baseApiUrl}/api/databases/{Uri.EscapeDataString(name)}", null);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDatabaseAsync(string name)
    {
        if (!_isConnected || _baseApiUrl == null) return false;
        try
        {
            var resp = await _http.DeleteAsync($"{_baseApiUrl}/api/databases/{Uri.EscapeDataString(name)}");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <inheritdoc />
    public async Task<bool> SelectDatabaseAsync(string name)
    {
        if (!_isConnected || _baseApiUrl == null) return false;
        try
        {
            var resp = await _http.PostAsync($"{_baseApiUrl}/api/databases/{Uri.EscapeDataString(name)}/select", null);
            if (resp.IsSuccessStatusCode)
            {
                _currentDatabase = name;
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    // --- User management (admin only) ---

    /// <summary>Lists all server user accounts.</summary>
    public async Task<List<AdminUser>> GetUsersAsync()
    {
        EnsureConnected();
        var resp = await _http.GetAsync($"{_baseApiUrl}/api/users");
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(await ErrorOf(resp));
        var payload = await resp.Content.ReadFromJsonAsync<UsersResponse>();
        return payload?.Users ?? new List<AdminUser>();
    }

    /// <summary>Creates a new user with the given role.</summary>
    public async Task CreateUserAsync(string username, string password, string role)
    {
        EnsureConnected();
        var resp = await _http.PostAsJsonAsync($"{_baseApiUrl}/api/users", new { username, password, role });
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(await ErrorOf(resp));
    }

    /// <summary>Changes a user's role (revokes their live sessions).</summary>
    public async Task SetUserRoleAsync(string username, string role)
    {
        EnsureConnected();
        var resp = await _http.PutAsJsonAsync($"{_baseApiUrl}/api/users/{Uri.EscapeDataString(username)}/role", new { role });
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(await ErrorOf(resp));
    }

    /// <summary>Resets a user's password (revokes their live sessions).</summary>
    public async Task SetUserPasswordAsync(string username, string password)
    {
        EnsureConnected();
        var resp = await _http.PutAsJsonAsync($"{_baseApiUrl}/api/users/{Uri.EscapeDataString(username)}/password", new { password });
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(await ErrorOf(resp));
    }

    /// <summary>Deletes a user (revokes their live sessions).</summary>
    public async Task DeleteUserAsync(string username)
    {
        EnsureConnected();
        var resp = await _http.DeleteAsync($"{_baseApiUrl}/api/users/{Uri.EscapeDataString(username)}");
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(await ErrorOf(resp));
    }

    private void EnsureConnected()
    {
        if (!_isConnected || _baseApiUrl == null)
            throw new InvalidOperationException("Not connected to the server.");
    }

    private static async Task<string> ErrorOf(HttpResponseMessage resp)
    {
        try
        {
            var e = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
            return string.IsNullOrEmpty(e?.Error) ? $"HTTP {(int)resp.StatusCode}" : e!.Error!;
        }
        catch { return $"HTTP {(int)resp.StatusCode}"; }
    }

    private record UsersResponse(bool Success, List<AdminUser>? Users);

    private record DocumentResponse(bool Success, string? Id, string? Message);
    private record ErrorResponse(string? Error);

    private static string DeriveApiUrl(string serverUrl)
    {
        // API always uses HTTPS (TCP port + 1). Blazor WASM is served from HTTPS so the API
        // must match to avoid browser mixed-content blocks.
        var url = serverUrl.Replace("http://", "").Replace("https://", "").TrimEnd('/');
        var lastColon = url.LastIndexOf(':');
        if (lastColon >= 0 && int.TryParse(url[(lastColon + 1)..], out var port))
            return $"https://{url[..lastColon]}:{port + 1}";
        return $"https://{url}";
    }

    private record CountResult(long Count);

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
    /// <summary>
    /// Gets or sets the total number of documents stored on the server.
    /// </summary>
    public int TotalDocuments { get; set; }

    /// <summary>
    /// Gets or sets the total number of collections on the server.
    /// </summary>
    public int TotalCollections { get; set; }

    /// <summary>
    /// Gets or sets the number of currently active client connections.
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// Gets or sets how long the server has been running.
    /// </summary>
    public TimeSpan Uptime { get; set; }

    /// <summary>
    /// Gets or sets the server's memory usage in megabytes.
    /// </summary>
    public int MemoryUsageMB { get; set; }

    /// <summary>
    /// Gets or sets the server's query throughput in queries per second.
    /// </summary>
    public int QueriesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the server software version. Defaults to "1.0.0".
    /// </summary>
    public string ServerVersion { get; set; } = "1.0.0";
}

/// <summary>
/// Query result model.
/// </summary>
public class QueryResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the query executed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the documents returned by the query.
    /// </summary>
    public List<Document> Documents { get; set; } = new();

    /// <summary>
    /// Gets or sets the query execution time in milliseconds.
    /// </summary>
    public int ExecutionTimeMs { get; set; }

    /// <summary>
    /// Gets or sets the total number of documents matching the query.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the error message when the query fails; otherwise <c>null</c>.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// A server user account for admin management (no secrets).
/// </summary>
public class AdminUser
{
    /// <summary>
    /// Gets or sets the user's login name.
    /// </summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// Gets or sets the user's server role (admin / readwrite / readonly).
    /// </summary>
    public string Role { get; set; } = "";

    /// <summary>
    /// Gets or sets the time when the account was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
