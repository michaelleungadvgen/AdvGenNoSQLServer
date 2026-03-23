// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Admin.Services;

using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Connects to the server.
    /// </summary>
    public async Task<bool> ConnectAsync(string serverUrl, string? username = null, string? password = null)
    {
        try
        {
            _serverUrl = serverUrl;
            var options = new AdvGenNoSqlClientOptions
            {
                ConnectionTimeout = 10000,
                AutoReconnect = true
            };

            _client = new AdvGenNoSqlClient(serverUrl, options);

            // Note: In a real implementation, we would connect here
            // For now, we simulate the connection
            await Task.Delay(100);

            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception)
        {
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
            // await _client.DisconnectAsync();
            await Task.Delay(50);
            _client = null;
        }

        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets server statistics (simulated for demo).
    /// </summary>
    public Task<ServerStats> GetServerStatsAsync()
    {
        // In a real implementation, this would call the server API
        var stats = new ServerStats
        {
            TotalDocuments = new Random().Next(1000, 100000),
            TotalCollections = new Random().Next(5, 50),
            ActiveConnections = new Random().Next(1, 100),
            Uptime = TimeSpan.FromHours(new Random().Next(1, 720)),
            MemoryUsageMB = new Random().Next(50, 500),
            QueriesPerSecond = new Random().Next(10, 1000),
            ServerVersion = "1.0.0"
        };

        return Task.FromResult(stats);
    }

    /// <summary>
    /// Gets list of collections (simulated for demo).
    /// </summary>
    public Task<List<string>> GetCollectionsAsync()
    {
        var collections = new List<string>
        {
            "users",
            "products",
            "orders",
            "logs",
            "sessions",
            "configuration"
        };

        return Task.FromResult(collections);
    }

    /// <summary>
    /// Gets documents from a collection (simulated for demo).
    /// </summary>
    public Task<List<Document>> GetDocumentsAsync(string collectionName, int skip = 0, int take = 50)
    {
        var documents = new List<Document>();
        var random = new Random();

        for (int i = 0; i < take; i++)
        {
            var docId = $"doc_{skip + i}_{Guid.NewGuid().ToString()[..8]}";
            var doc = new Document
            {
                Id = docId,
                Data = new Dictionary<string, object?>
                {
                    ["_id"] = docId,
                    ["name"] = $"Item {skip + i}",
                    ["createdAt"] = DateTime.UtcNow.AddMinutes(-random.Next(1, 10000)),
                    ["status"] = random.Next(2) == 0 ? "active" : "inactive",
                    ["value"] = random.Next(100, 10000)
                }
            };
            documents.Add(doc);
        }

        return Task.FromResult(documents);
    }

    /// <summary>
    /// Gets a single document by ID (simulated for demo).
    /// </summary>
    public Task<Document?> GetDocumentAsync(string collectionName, string documentId)
    {
        var doc = new Document
        {
            Id = documentId,
            Data = new Dictionary<string, object?>
            {
                ["_id"] = documentId,
                ["name"] = $"Document {documentId}",
                ["description"] = "This is a sample document for demonstration purposes.",
                ["createdAt"] = DateTime.UtcNow.AddDays(-1),
                ["updatedAt"] = DateTime.UtcNow,
                ["status"] = "active",
                ["tags"] = new[] { "sample", "demo", "test" },
                ["metadata"] = new Dictionary<string, object>
                {
                    ["version"] = 1,
                    ["author"] = "admin"
                }
            }
        };

        return Task.FromResult<Document?>(doc);
    }

    /// <summary>
    /// Executes a query (simulated for demo).
    /// </summary>
    public Task<QueryResult> ExecuteQueryAsync(string query)
    {
        // Simulate query execution
        var result = new QueryResult
        {
            Success = true,
            Documents = new List<Document>(),
            ExecutionTimeMs = new Random().Next(1, 100),
            TotalCount = new Random().Next(10, 1000)
        };

        var random = new Random();
        for (int i = 0; i < Math.Min(10, result.TotalCount); i++)
        {
            result.Documents.Add(new Document
            {
                Id = $"result_{i}",
                Data = new Dictionary<string, object?>
                {
                    ["_id"] = $"result_{i}",
                    ["matched"] = true,
                    ["score"] = random.NextDouble()
                }
            });
        }

        return Task.FromResult(result);
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
