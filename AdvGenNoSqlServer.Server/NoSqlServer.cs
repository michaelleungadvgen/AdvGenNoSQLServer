// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Network;
using AdvGenNoSqlServer.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace AdvGenNoSqlServer.Server;

/// <summary>
/// Main NoSQL server implementation that integrates the TCP server with message handling
/// </summary>
public class NoSqlServer : IHostedService, IAsyncDisposable
{
    private readonly ILogger<NoSqlServer> _logger;
    private readonly IConfigurationManager _configurationManager;
    private HybridDocumentStore? _documentStore;
    private TcpServer? _tcpServer;
    private bool _disposed;

    /// <summary>
    /// Server version for handshake responses
    /// </summary>
    public const string ServerVersion = "1.0.0";

    public NoSqlServer(ILogger<NoSqlServer> logger, IConfigurationManager configurationManager)
    {
        _logger = logger;
        _configurationManager = configurationManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = _configurationManager.Configuration;

        _logger.LogInformation("Starting NoSQL Server on {Host}:{Port}...", config.Host, config.Port);
        _logger.LogInformation("Max connections: {MaxConnections}", config.MaxConcurrentConnections);
        _logger.LogInformation("Storage path: {StoragePath}", config.StoragePath);

        // Initialize hybrid document store (cache + disk)
        var storagePath = config.StoragePath;
        if (string.IsNullOrEmpty(storagePath))
        {
            storagePath = "data";
        }

        // Ensure storage path is absolute
        if (!Path.IsPathRooted(storagePath))
        {
            storagePath = Path.Combine(AppContext.BaseDirectory, storagePath);
        }

        _logger.LogInformation("Initializing hybrid storage at: {Path}", storagePath);
        _documentStore = new HybridDocumentStore(storagePath);
        await _documentStore.InitializeAsync();
        _logger.LogInformation("Hybrid storage initialized successfully");

        // Create and configure the TCP server
        _tcpServer = new TcpServer(config);
        _tcpServer.ConnectionEstablished += OnConnectionEstablished;
        _tcpServer.ConnectionClosed += OnConnectionClosed;
        _tcpServer.MessageReceived += OnMessageReceivedAsync;

        // Start the TCP server
        _tcpServer.StartAsync(cancellationToken);

        _logger.LogInformation("NoSQL Server started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping NoSQL Server...");

        if (_tcpServer != null)
        {
            _tcpServer.ConnectionEstablished -= OnConnectionEstablished;
            _tcpServer.ConnectionClosed -= OnConnectionClosed;
            _tcpServer.MessageReceived -= OnMessageReceivedAsync;

            await _tcpServer.StopAsync(TimeSpan.FromSeconds(30));
            _tcpServer.Dispose();
            _tcpServer = null;
        }

        // Flush and dispose document store
        if (_documentStore != null)
        {
            _logger.LogInformation("Flushing pending writes to disk...");
            await _documentStore.FlushAsync();
            await _documentStore.DisposeAsync();
            _documentStore = null;
            _logger.LogInformation("Storage shutdown complete");
        }

        _logger.LogInformation("NoSQL Server stopped successfully");
    }

    private void OnConnectionEstablished(object? sender, ConnectionEventArgs e)
    {
        _logger.LogDebug("Connection established: {ConnectionId} from {RemoteAddress}", 
            e.ConnectionId, e.Client.Client?.RemoteEndPoint?.ToString() ?? "unknown");
    }

    private void OnConnectionClosed(object? sender, ConnectionEventArgs e)
    {
        _logger.LogDebug("Connection closed: {ConnectionId}", e.ConnectionId);
    }

    private async void OnMessageReceivedAsync(object? sender, MessageReceivedEventArgs e)
    {
        try
        {
            _logger.LogDebug("Received message type {MessageType} from {ConnectionId}", 
                e.Message.MessageType, e.ConnectionId);

            var response = await HandleMessageAsync(e.Message, e.ConnectionId);
            await e.SendResponseAsync(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message from {ConnectionId}", e.ConnectionId);
            
            // Send error response
            var errorResponse = NoSqlMessage.CreateError("INTERNAL_ERROR", "An error occurred processing the message");
            await e.SendResponseAsync(errorResponse);
        }
    }

    private Task<NoSqlMessage> HandleMessageAsync(NoSqlMessage message, string connectionId)
    {
        return message.MessageType switch
        {
            MessageType.Handshake => HandleHandshakeAsync(message, connectionId),
            MessageType.Ping => HandlePingAsync(message, connectionId),
            MessageType.Authentication => HandleAuthenticationAsync(message, connectionId),
            MessageType.Command => HandleCommandAsync(message, connectionId),
            MessageType.BulkOperation => HandleBulkOperationAsync(message, connectionId),
            _ => Task.FromResult(NoSqlMessage.CreateError("UNSUPPORTED_MESSAGE", $"Message type {message.MessageType} is not supported"))
        };
    }

    private Task<NoSqlMessage> HandleHandshakeAsync(NoSqlMessage message, string connectionId)
    {
        _logger.LogDebug("Processing handshake for connection {ConnectionId}", connectionId);

        // Parse client handshake info if provided
        string? clientVersion = null;
        if (message.Payload != null && message.PayloadLength > 0)
        {
            try
            {
                var payload = message.GetPayloadAsString();
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("version", out var versionProp))
                {
                    clientVersion = versionProp.GetString();
                }
            }
            catch { /* Ignore parsing errors */ }
        }

        // Create handshake response - use Response type for success
        var responsePayload = new
        {
            success = true,
            serverVersion = ServerVersion,
            protocolVersion = 1,
            timestamp = DateTime.UtcNow,
            clientVersion = clientVersion ?? "unknown"
        };

        var response = NoSqlMessage.Create(MessageType.Response, JsonSerializer.Serialize(responsePayload));
        return Task.FromResult(response);
    }

    private Task<NoSqlMessage> HandlePingAsync(NoSqlMessage message, string connectionId)
    {
        _logger.LogDebug("Processing ping for connection {ConnectionId}", connectionId);

        // Return Pong message type - client PingAsync() expects MessageType.Pong
        var response = new NoSqlMessage
        {
            MessageType = MessageType.Pong,
            Payload = Array.Empty<byte>(),
            PayloadLength = 0
        };
        return Task.FromResult(response);
    }

    private Task<NoSqlMessage> HandleAuthenticationAsync(NoSqlMessage message, string connectionId)
    {
        _logger.LogDebug("Processing authentication for connection {ConnectionId}", connectionId);

        if (message.Payload == null || message.PayloadLength == 0)
        {
            return Task.FromResult(NoSqlMessage.CreateError("AUTH_FAILED", "Missing credentials"));
        }

        try
        {
            var payload = message.GetPayloadAsString();
            using var doc = JsonDocument.Parse(payload);
            
            string? username = null;
            string? password = null;
            
            if (doc.RootElement.TryGetProperty("username", out var usernameProp))
                username = usernameProp.GetString();
            if (doc.RootElement.TryGetProperty("password", out var passwordProp))
                password = passwordProp.GetString();

            var config = _configurationManager.Configuration;
            
            // Simple authentication check
            if (!config.RequireAuthentication)
            {
                return Task.FromResult(NoSqlMessage.CreateSuccess(new { authenticated = true, token = "anonymous" }));
            }

            // Check against master password (simplified for now)
            if (username == "admin" && password == config.MasterPassword)
            {
                var token = Guid.NewGuid().ToString("N");
                return Task.FromResult(NoSqlMessage.CreateSuccess(new { authenticated = true, token }));
            }

            return Task.FromResult(NoSqlMessage.CreateError("AUTH_FAILED", "Invalid credentials"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Authentication parsing error for connection {ConnectionId}", connectionId);
            return Task.FromResult(NoSqlMessage.CreateError("AUTH_FAILED", "Invalid authentication format"));
        }
    }

    private Task<NoSqlMessage> HandleCommandAsync(NoSqlMessage message, string connectionId)
    {
        _logger.LogDebug("Processing command for connection {ConnectionId}", connectionId);

        if (message.Payload == null || message.PayloadLength == 0)
        {
            return Task.FromResult(NoSqlMessage.CreateError("INVALID_COMMAND", "Empty command"));
        }

        try
        {
            var payload = message.GetPayloadAsString();
            using var doc = JsonDocument.Parse(payload);
            
            if (!doc.RootElement.TryGetProperty("command", out var commandProp))
            {
                return Task.FromResult(NoSqlMessage.CreateError("INVALID_COMMAND", "Missing command property"));
            }

            var command = commandProp.GetString()?.ToLowerInvariant();
            
            return command switch
            {
                "get" => HandleGetCommand(doc.RootElement),
                "set" => HandleSetCommand(doc.RootElement),
                "delete" => HandleDeleteCommand(doc.RootElement),
                "exists" => HandleExistsCommand(doc.RootElement),
                _ => Task.FromResult(NoSqlMessage.CreateError("UNKNOWN_COMMAND", $"Unknown command: {command}"))
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Command parsing error for connection {ConnectionId}", connectionId);
            return Task.FromResult(NoSqlMessage.CreateError("INVALID_COMMAND", "Invalid command format"));
        }
    }

    private async Task<NoSqlMessage> HandleGetCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("id", out var idProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id property");
        }

        var collection = collectionProp.GetString() ?? "default";
        var id = idProp.GetString();

        if (string.IsNullOrEmpty(id))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Document id cannot be empty");
        }

        var document = await _documentStore.GetAsync(collection, id);
        if (document == null)
        {
            return NoSqlMessage.CreateSuccess(new { found = false, value = (object?)null });
        }

        return NoSqlMessage.CreateSuccess(new { found = true, value = document });
    }

    private async Task<NoSqlMessage> HandleSetCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("document", out var documentProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or document property");
        }

        var collection = collectionProp.GetString() ?? "default";

        // Extract document data
        string? id = null;
        if (documentProp.TryGetProperty("_id", out var idProp))
        {
            id = idProp.GetString();
        }

        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString();
        }

        // Convert JsonElement to Dictionary
        var data = new Dictionary<string, object>();
        foreach (var prop in documentProp.EnumerateObject())
        {
            if (prop.Name != "_id")
            {
                data[prop.Name] = JsonElementToObject(prop.Value);
            }
        }

        var document = new Document
        {
            Id = id,
            Data = data
        };

        try
        {
            // Check if document exists to determine insert vs update
            var exists = await _documentStore.ExistsAsync(collection, id);
            if (exists)
            {
                await _documentStore.UpdateAsync(collection, document);
            }
            else
            {
                await _documentStore.InsertAsync(collection, document);
            }

            return NoSqlMessage.CreateSuccess(new { stored = true, id = id });
        }
        catch (Exception ex)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", ex.Message);
        }
    }

    private async Task<NoSqlMessage> HandleDeleteCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("id", out var idProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id property");
        }

        var collection = collectionProp.GetString() ?? "default";
        var id = idProp.GetString();

        if (string.IsNullOrEmpty(id))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Document id cannot be empty");
        }

        var deleted = await _documentStore.DeleteAsync(collection, id);
        return NoSqlMessage.CreateSuccess(new { deleted = deleted });
    }

    private async Task<NoSqlMessage> HandleExistsCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("id", out var idProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id property");
        }

        var collection = collectionProp.GetString() ?? "default";
        var id = idProp.GetString();

        if (string.IsNullOrEmpty(id))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Document id cannot be empty");
        }

        var exists = await _documentStore.ExistsAsync(collection, id);
        return NoSqlMessage.CreateSuccess(new { exists = exists });
    }

    private static object JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            _ => element.GetRawText()
        };
    }

    private async Task<NoSqlMessage> HandleBulkOperationAsync(NoSqlMessage message, string connectionId)
    {
        _logger.LogDebug("Processing bulk operation for connection {ConnectionId}", connectionId);

        if (message.Payload == null || message.PayloadLength == 0)
        {
            return NoSqlMessage.CreateError("INVALID_BATCH", "Empty batch request");
        }

        try
        {
            var payload = message.GetPayloadAsString();
            var request = JsonSerializer.Deserialize<BatchOperationRequest>(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (request == null)
            {
                return NoSqlMessage.CreateError("INVALID_BATCH", "Failed to deserialize batch request");
            }

            if (string.IsNullOrEmpty(request.Collection))
            {
                return NoSqlMessage.CreateError("INVALID_BATCH", "Collection name is required");
            }

            if (request.Operations.Count == 0)
            {
                return NoSqlMessage.CreateSuccess(new BatchOperationResponse
                {
                    Success = true,
                    TotalProcessed = 0,
                    Results = new List<BatchOperationItemResult>()
                });
            }

            var response = await ProcessBatchRequestAsync(request);
            return NoSqlMessage.CreateSuccess(response);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Batch operation parsing error for connection {ConnectionId}", connectionId);
            return NoSqlMessage.CreateError("INVALID_BATCH", "Invalid batch request format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batch operation for connection {ConnectionId}", connectionId);
            return NoSqlMessage.CreateError("BATCH_ERROR", "Internal error processing batch operation");
        }
    }

    private async Task<BatchOperationResponse> ProcessBatchRequestAsync(BatchOperationRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = new BatchOperationResponse
        {
            Success = true,
            Results = new List<BatchOperationItemResult>()
        };

        for (int i = 0; i < request.Operations.Count; i++)
        {
            var operation = request.Operations[i];
            var result = await ProcessBatchOperationItemAsync(request.Collection, operation, i);

            response.Results.Add(result);

            if (result.Success)
            {
                switch (operation.OperationType)
                {
                    case BatchOperationType.Insert:
                        response.InsertedCount++;
                        break;
                    case BatchOperationType.Update:
                        response.UpdatedCount++;
                        break;
                    case BatchOperationType.Delete:
                        response.DeletedCount++;
                        break;
                }
            }
            else if (request.StopOnError)
            {
                response.Success = false;
                response.ErrorMessage = $"Batch stopped due to error at index {i}: {result.ErrorMessage}";
                break;
            }
        }

        response.TotalProcessed = response.Results.Count;
        stopwatch.Stop();
        response.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;

        _logger.LogDebug("Batch operation completed: {Inserted} inserted, {Updated} updated, {Deleted} deleted in {Ms}ms",
            response.InsertedCount, response.UpdatedCount, response.DeletedCount, response.ProcessingTimeMs);

        return response;
    }

    private async Task<BatchOperationItemResult> ProcessBatchOperationItemAsync(string collection, BatchOperationItem operation, int index)
    {
        var result = new BatchOperationItemResult
        {
            Index = index,
            Success = false
        };

        try
        {
            switch (operation.OperationType)
            {
                case BatchOperationType.Insert:
                    result.Success = await ProcessBatchInsertAsync(collection, operation, result);
                    break;

                case BatchOperationType.Update:
                    result.Success = await ProcessBatchUpdateAsync(collection, operation, result);
                    break;

                case BatchOperationType.Delete:
                    result.Success = await ProcessBatchDeleteAsync(collection, operation, result);
                    break;

                default:
                    result.ErrorCode = "UNSUPPORTED_OPERATION";
                    result.ErrorMessage = $"Operation type {operation.OperationType} is not supported";
                    break;
            }
        }
        catch (Exception ex)
        {
            result.ErrorCode = "INTERNAL_ERROR";
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<bool> ProcessBatchInsertAsync(string collection, BatchOperationItem operation, BatchOperationItemResult result)
    {
        if (_documentStore == null)
        {
            result.ErrorCode = "STORAGE_ERROR";
            result.ErrorMessage = "Storage not initialized";
            return false;
        }

        if (operation.Document == null || operation.Document.Count == 0)
        {
            result.ErrorCode = "MISSING_DOCUMENT";
            result.ErrorMessage = "Document data is required for insert operation";
            return false;
        }

        // Extract document ID if present, or generate one
        if (operation.Document.TryGetValue("_id", out var idValue))
        {
            result.DocumentId = idValue?.ToString();
        }

        if (string.IsNullOrEmpty(result.DocumentId))
        {
            result.DocumentId = Guid.NewGuid().ToString("N");
        }

        // Convert to Document and insert
        var data = new Dictionary<string, object>();
        foreach (var kvp in operation.Document)
        {
            if (kvp.Key != "_id")
            {
                data[kvp.Key] = kvp.Value;
            }
        }

        var document = new Document
        {
            Id = result.DocumentId,
            Data = data
        };

        try
        {
            await _documentStore.InsertAsync(collection, document);
            _logger.LogTrace("Batch insert into {Collection}: {DocumentId}", collection, result.DocumentId);
            return true;
        }
        catch (DocumentAlreadyExistsException)
        {
            result.ErrorCode = "DUPLICATE_KEY";
            result.ErrorMessage = $"Document '{result.DocumentId}' already exists";
            return false;
        }
    }

    private async Task<bool> ProcessBatchUpdateAsync(string collection, BatchOperationItem operation, BatchOperationItemResult result)
    {
        if (_documentStore == null)
        {
            result.ErrorCode = "STORAGE_ERROR";
            result.ErrorMessage = "Storage not initialized";
            return false;
        }

        if (string.IsNullOrEmpty(operation.DocumentId) && (operation.Filter == null || operation.Filter.Count == 0))
        {
            result.ErrorCode = "MISSING_CRITERIA";
            result.ErrorMessage = "DocumentId or Filter is required for update operation";
            return false;
        }

        if ((operation.Document == null || operation.Document.Count == 0) &&
            (operation.UpdateFields == null || operation.UpdateFields.Count == 0))
        {
            result.ErrorCode = "MISSING_UPDATE_DATA";
            result.ErrorMessage = "Document or UpdateFields is required for update operation";
            return false;
        }

        result.DocumentId = operation.DocumentId;

        try
        {
            // Get existing document
            var existing = await _documentStore.GetAsync(collection, operation.DocumentId!);
            if (existing == null)
            {
                result.ErrorCode = "NOT_FOUND";
                result.ErrorMessage = $"Document '{operation.DocumentId}' not found";
                return false;
            }

            // Apply updates
            var data = existing.Data ?? new Dictionary<string, object>();
            var updateSource = operation.UpdateFields ?? operation.Document;
            if (updateSource != null)
            {
                foreach (var kvp in updateSource)
                {
                    if (kvp.Key != "_id")
                    {
                        data[kvp.Key] = kvp.Value;
                    }
                }
            }

            existing.Data = data;
            await _documentStore.UpdateAsync(collection, existing);
            _logger.LogTrace("Batch update in {Collection}: {DocumentId}", collection, result.DocumentId);
            return true;
        }
        catch (DocumentNotFoundException)
        {
            result.ErrorCode = "NOT_FOUND";
            result.ErrorMessage = $"Document '{operation.DocumentId}' not found";
            return false;
        }
    }

    private async Task<bool> ProcessBatchDeleteAsync(string collection, BatchOperationItem operation, BatchOperationItemResult result)
    {
        if (_documentStore == null)
        {
            result.ErrorCode = "STORAGE_ERROR";
            result.ErrorMessage = "Storage not initialized";
            return false;
        }

        if (string.IsNullOrEmpty(operation.DocumentId) && (operation.Filter == null || operation.Filter.Count == 0))
        {
            result.ErrorCode = "MISSING_CRITERIA";
            result.ErrorMessage = "DocumentId or Filter is required for delete operation";
            return false;
        }

        result.DocumentId = operation.DocumentId;

        var deleted = await _documentStore.DeleteAsync(collection, operation.DocumentId!);
        if (!deleted)
        {
            result.ErrorCode = "NOT_FOUND";
            result.ErrorMessage = $"Document '{operation.DocumentId}' not found";
            return false;
        }

        _logger.LogTrace("Batch delete from {Collection}: {DocumentId}", collection, result.DocumentId);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _tcpServer?.Dispose();

        if (_documentStore != null)
        {
            await _documentStore.DisposeAsync();
        }
    }
}
