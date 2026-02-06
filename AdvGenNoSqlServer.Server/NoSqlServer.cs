// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Caching;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AdvGenNoSqlServer.Server;

/// <summary>
/// Main NoSQL server implementation that integrates the TCP server with message handling
/// </summary>
public class NoSqlServer : IHostedService, IDisposable
{
    private readonly ILogger<NoSqlServer> _logger;
    private readonly ICacheManager _cacheManager;
    private readonly IConfigurationManager _configurationManager;
    private TcpServer? _tcpServer;
    private bool _disposed;

    /// <summary>
    /// Server version for handshake responses
    /// </summary>
    public const string ServerVersion = "1.0.0";

    public NoSqlServer(ILogger<NoSqlServer> logger, ICacheManager cacheManager, IConfigurationManager configurationManager)
    {
        _logger = logger;
        _cacheManager = cacheManager;
        _configurationManager = configurationManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var config = _configurationManager.Configuration;
        
        _logger.LogInformation("Starting NoSQL Server on {Host}:{Port}...", config.Host, config.Port);
        _logger.LogInformation("Max connections: {MaxConnections}", config.MaxConcurrentConnections);
        _logger.LogInformation("Cache size limit: {MaxCacheItemCount} items, {MaxCacheSizeInBytes} bytes", config.MaxCacheItemCount, config.MaxCacheSizeInBytes);
        _logger.LogInformation("Storage path: {StoragePath}", config.StoragePath);

        // Create and configure the TCP server
        _tcpServer = new TcpServer(config);
        _tcpServer.ConnectionEstablished += OnConnectionEstablished;
        _tcpServer.ConnectionClosed += OnConnectionClosed;
        _tcpServer.MessageReceived += OnMessageReceivedAsync;

        // Start the TCP server
        _tcpServer.StartAsync(cancellationToken);

        _logger.LogInformation("NoSQL Server started successfully");
        return Task.CompletedTask;
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
        
        // Return pong as Response type (client expects Response, not Pong)
        var responsePayload = new { pong = true };
        var response = NoSqlMessage.Create(MessageType.Response, JsonSerializer.Serialize(responsePayload));
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

    private Task<NoSqlMessage> HandleGetCommand(JsonElement commandElement)
    {
        // TODO: Implement actual storage integration
        return Task.FromResult(NoSqlMessage.CreateSuccess(new { value = null as object }));
    }

    private Task<NoSqlMessage> HandleSetCommand(JsonElement commandElement)
    {
        // TODO: Implement actual storage integration
        return Task.FromResult(NoSqlMessage.CreateSuccess(new { stored = true }));
    }

    private Task<NoSqlMessage> HandleDeleteCommand(JsonElement commandElement)
    {
        // TODO: Implement actual storage integration
        return Task.FromResult(NoSqlMessage.CreateSuccess(new { deleted = true }));
    }

    private Task<NoSqlMessage> HandleExistsCommand(JsonElement commandElement)
    {
        // TODO: Implement actual storage integration
        return Task.FromResult(NoSqlMessage.CreateSuccess(new { exists = false }));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _tcpServer?.Dispose();
    }
}
