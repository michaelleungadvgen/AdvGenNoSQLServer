// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Caching;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Transactions;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AdvGenNoSqlServer.Host;

/// <summary>
/// Entry point for the AdvGenNoSQL Server host application.
/// Provides a standalone console application to run the NoSQL server.
/// </summary>
public class Program
{
    /// <summary>
    /// Main entry point for the host application.
    /// </summary>
    public static async Task Main(string[] args)
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           AdvGenNoSQL Server - Host Application                ║");
        Console.WriteLine("║                   MIT Licensed - Version 1.0.0                 ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        
        // Configure logging
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddConsole();
        
        // Register all services
        ConfigureServices(builder.Services);
        
        using var host = builder.Build();
        
        // Start the host
        await host.RunAsync();
    }
    
    /// <summary>
    /// Configures services for dependency injection.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Add configuration manager
        services.AddSingleton<Core.Configuration.IConfigurationManager>(provider =>
        {
            var configPath = "appsettings.json";
            return new Core.Configuration.ConfigurationManager(configPath, enableHotReload: true);
        });
        
        // Add cache manager
        services.AddSingleton<ICacheManager>(provider =>
        {
            var configManager = provider.GetRequiredService<Core.Configuration.IConfigurationManager>();
            var config = configManager.Configuration;
            return new AdvancedMemoryCacheManager(
                maxItemCount: config.MaxCacheItemCount > 0 ? config.MaxCacheItemCount : 10000,
                maxSizeInBytes: config.MaxCacheSizeInBytes > 0 ? config.MaxCacheSizeInBytes : 104857600,
                defaultTtlMilliseconds: config.DefaultCacheTtlMilliseconds > 0 ? config.DefaultCacheTtlMilliseconds : 1800000);
        });
        
        // Add audit logger
        services.AddSingleton<IAuditLogger>(provider =>
        {
            var configManager = provider.GetRequiredService<Core.Configuration.IConfigurationManager>();
            var config = configManager.Configuration;
            return new AuditLogger(config);
        });
        
        // Add authentication manager
        services.AddSingleton<AuthenticationManager>(provider =>
        {
            var configManager = provider.GetRequiredService<Core.Configuration.IConfigurationManager>();
            var config = configManager.Configuration;
            return new AuthenticationManager(config);
        });
        
        // Add Write-Ahead Log
        services.AddSingleton<IWriteAheadLog>(provider =>
        {
            var configManager = provider.GetRequiredService<Core.Configuration.IConfigurationManager>();
            var config = configManager.Configuration;
            var walOptions = new WalOptions
            {
                LogDirectory = Path.Combine(config.StoragePath, "wal"),
                MaxFileSize = 10 * 1024 * 1024 // 10MB
            };
            return new WriteAheadLog(walOptions);
        });
        
        // Add Lock Manager
        services.AddSingleton<ILockManager>(provider =>
        {
            return new LockManager(enableDeadlockDetection: true);
        });
        
        // Add transaction coordinator
        services.AddSingleton<ITransactionCoordinator>(provider =>
        {
            var writeAheadLog = provider.GetRequiredService<IWriteAheadLog>();
            var lockManager = provider.GetRequiredService<ILockManager>();
            return new TransactionCoordinator(writeAheadLog, lockManager);
        });
        
        // Add the hosted NoSQL server service
        services.AddHostedService<NoSqlServerHost>();
    }
}

/// <summary>
/// Hosted service wrapper for the NoSQL server.
/// </summary>
internal class NoSqlServerHost : IHostedService, IAsyncDisposable
{
    private readonly ILogger<NoSqlServerHost> _logger;
    private readonly Core.Configuration.IConfigurationManager _configManager;
    private readonly IAuditLogger _auditLogger;
    private readonly AuthenticationManager _authManager;
    private TcpServer? _tcpServer;
    private HybridDocumentStore? _documentStore;
    private bool _disposed;

    public NoSqlServerHost(
        ILogger<NoSqlServerHost> logger,
        Core.Configuration.IConfigurationManager configManager,
        IAuditLogger auditLogger,
        AuthenticationManager authManager)
    {
        _logger = logger;
        _configManager = configManager;
        _auditLogger = auditLogger;
        _authManager = authManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = _configManager.Configuration;

        _logger.LogInformation("Starting NoSQL Server on {Host}:{Port}...", config.Host, config.Port);
        _logger.LogInformation("Max connections: {MaxConnections}", config.MaxConcurrentConnections);

        // Ensure data directories exist
        EnsureDirectoriesExist(config);

        // Initialize hybrid document store (cache + disk)
        var storagePath = string.IsNullOrEmpty(config.StoragePath) ? "data" : config.StoragePath;
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
        await _tcpServer.StartAsync(cancellationToken);

        _logger.LogInformation("NoSQL Server started successfully");
        
        // Log server start event
        _auditLogger.Log(new AuditEvent
        {
            EventType = AuditEventType.ServerStarted,
            Action = "ServerStart",
            Details = $"v1.0.0 - Max Connections: {config.MaxConcurrentConnections}",
            Timestamp = DateTime.UtcNow
        });
        
        // Log startup banner
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  Server is running on {config.Host}:{config.Port}");
        Console.WriteLine($"  Press Ctrl+C to stop the server");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping NoSQL Server...");
        
        // Log server stop event
        _auditLogger.Log(new AuditEvent
        {
            EventType = AuditEventType.ServerStopped,
            Action = "ServerStop",
            Details = "Graceful shutdown initiated",
            Timestamp = DateTime.UtcNow
        });

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

    private void EnsureDirectoriesExist(ServerConfiguration config)
    {
        var storagePath = string.IsNullOrEmpty(config.StoragePath) ? "data" : config.StoragePath;
        if (!Path.IsPathRooted(storagePath))
        {
            storagePath = Path.Combine(AppContext.BaseDirectory, storagePath);
        }
        
        if (!string.IsNullOrEmpty(storagePath))
        {
            Directory.CreateDirectory(storagePath);
        }
    }

    private void OnConnectionEstablished(object? sender, ConnectionEventArgs e)
    {
        var remoteEndPoint = e.Client.Client?.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogDebug("Connection established: {ConnectionId} from {RemoteAddress}", e.ConnectionId, remoteEndPoint);
        
        _auditLogger.Log(new AuditEvent
        {
            EventType = AuditEventType.ConnectionEstablished,
            Action = "ConnectionEstablished",
            SessionId = e.ConnectionId,
            IpAddress = remoteEndPoint,
            Timestamp = DateTime.UtcNow
        });
    }

    private void OnConnectionClosed(object? sender, ConnectionEventArgs e)
    {
        _logger.LogDebug("Connection closed: {ConnectionId}", e.ConnectionId);
        
        _auditLogger.Log(new AuditEvent
        {
            EventType = AuditEventType.ConnectionClosed,
            Action = "ConnectionClosed",
            SessionId = e.ConnectionId,
            Timestamp = DateTime.UtcNow
        });
    }

    private async void OnMessageReceivedAsync(object? sender, MessageReceivedEventArgs e)
    {
        try
        {
            var response = await ProcessMessageAsync(e.Message, e.ConnectionId);
            await e.SendResponseAsync(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {ConnectionId}", e.ConnectionId);
            var errorResponse = NoSqlMessage.CreateError("INTERNAL_ERROR", "An error occurred processing the message");
            await e.SendResponseAsync(errorResponse);
        }
    }

    private Task<NoSqlMessage> ProcessMessageAsync(NoSqlMessage message, string connectionId)
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
        var responsePayload = new
        {
            success = true,
            serverVersion = "1.0.0",
            protocolVersion = 1,
            timestamp = DateTime.UtcNow,
            connectionId
        };

        return Task.FromResult(NoSqlMessage.Create(MessageType.Response, System.Text.Json.JsonSerializer.Serialize(responsePayload)));
    }

    private Task<NoSqlMessage> HandlePingAsync(NoSqlMessage message, string connectionId)
    {
        return Task.FromResult(new NoSqlMessage
        {
            MessageType = MessageType.Pong,
            Payload = Array.Empty<byte>(),
            PayloadLength = 0
        });
    }

    private Task<NoSqlMessage> HandleAuthenticationAsync(NoSqlMessage message, string connectionId)
    {
        if (!_configManager.Configuration.RequireAuthentication)
        {
            return Task.FromResult(NoSqlMessage.CreateSuccess(new { authenticated = true, token = "anonymous" }));
        }

        if (message.Payload == null || message.PayloadLength == 0)
        {
            _auditLogger.Log(new AuditEvent
            {
                EventType = AuditEventType.AuthenticationFailure,
                Action = "Authentication",
                Details = "Missing credentials",
                SessionId = connectionId,
                Timestamp = DateTime.UtcNow
            });
            return Task.FromResult(NoSqlMessage.CreateError("AUTH_FAILED", "Missing credentials"));
        }

        try
        {
            var payload = message.GetPayloadAsString();
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            
            string? username = null;
            string? password = null;
            
            if (doc.RootElement.TryGetProperty("username", out var usernameProp))
                username = usernameProp.GetString();
            if (doc.RootElement.TryGetProperty("password", out var passwordProp))
                password = passwordProp.GetString();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                _auditLogger.Log(new AuditEvent
                {
                    EventType = AuditEventType.AuthenticationFailure,
                    Action = "Authentication",
                    Username = username ?? "unknown",
                    Details = "Missing username or password",
                    SessionId = connectionId,
                    Timestamp = DateTime.UtcNow
                });
                return Task.FromResult(NoSqlMessage.CreateError("AUTH_FAILED", "Missing username or password"));
            }

            var result = _authManager.Authenticate(username, password);
            
            if (result != null)
            {
                _auditLogger.Log(new AuditEvent
                {
                    EventType = AuditEventType.AuthenticationSuccess,
                    Action = "Authentication",
                    Username = username,
                    Details = "Authentication successful",
                    SessionId = connectionId,
                    Timestamp = DateTime.UtcNow
                });
                return Task.FromResult(NoSqlMessage.CreateSuccess(new { authenticated = true, token = result.TokenId, username }));
            }
            else
            {
                _auditLogger.Log(new AuditEvent
                {
                    EventType = AuditEventType.AuthenticationFailure,
                    Action = "Authentication",
                    Username = username,
                    Details = "Invalid credentials",
                    SessionId = connectionId,
                    Timestamp = DateTime.UtcNow
                });
                return Task.FromResult(NoSqlMessage.CreateError("AUTH_FAILED", "Invalid credentials"));
            }
        }
        catch (Exception ex)
        {
            _auditLogger.Log(new AuditEvent
            {
                EventType = AuditEventType.AuthenticationFailure,
                Action = "Authentication",
                Details = $"Authentication error: {ex.Message}",
                SessionId = connectionId,
                Timestamp = DateTime.UtcNow
            });
            return Task.FromResult(NoSqlMessage.CreateError("AUTH_FAILED", "Invalid authentication format"));
        }
    }

    private async Task<NoSqlMessage> HandleCommandAsync(NoSqlMessage message, string connectionId)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("NOT_INITIALIZED", "Document store not initialized");
        }

        if (message.Payload == null || message.PayloadLength == 0)
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Empty command");
        }

        try
        {
            var payload = message.GetPayloadAsString();
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            
            if (!doc.RootElement.TryGetProperty("command", out var commandProp))
            {
                return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing command property");
            }

            var command = commandProp.GetString()?.ToLowerInvariant();
            
            return command switch
            {
                "get" => await HandleGetCommandAsync(doc.RootElement),
                "set" => await HandleSetCommandAsync(doc.RootElement),
                "delete" => await HandleDeleteCommandAsync(doc.RootElement),
                "exists" => await HandleExistsCommandAsync(doc.RootElement),
                "count" => await HandleCountCommandAsync(doc.RootElement),
                "listcollections" => await HandleListCollectionsCommandAsync(),
                _ => NoSqlMessage.CreateError("UNKNOWN_COMMAND", $"Unknown command: {command}")
            };
        }
        catch (Exception ex)
        {
            return NoSqlMessage.CreateError("COMMAND_ERROR", ex.Message);
        }
    }

    private async Task<NoSqlMessage> HandleGetCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("id", out var idProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id");
        }

        var collection = collectionProp.GetString();
        var id = idProp.GetString();

        if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(id))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Collection and id are required");
        }

        var document = await _documentStore!.GetAsync(collection, id);
        
        if (document == null)
        {
            return NoSqlMessage.CreateSuccess(new { found = false, document = (object?)null });
        }

        return NoSqlMessage.CreateSuccess(new { found = true, document });
    }

    private async Task<NoSqlMessage> HandleSetCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("document", out var documentProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or document");
        }

        var collection = collectionProp.GetString();
        
        if (string.IsNullOrEmpty(collection))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Collection is required");
        }

        var json = documentProp.GetRawText();
        var document = System.Text.Json.JsonSerializer.Deserialize<Core.Models.Document>(json);
        
        if (document == null)
        {
            return NoSqlMessage.CreateError("INVALID_DOCUMENT", "Failed to parse document");
        }

        if (string.IsNullOrEmpty(document.Id))
        {
            document.Id = Guid.NewGuid().ToString("N");
        }

        var exists = await _documentStore!.ExistsAsync(collection, document.Id);
        if (exists)
        {
            await _documentStore.UpdateAsync(collection, document);
        }
        else
        {
            await _documentStore.InsertAsync(collection, document);
        }
        
        return NoSqlMessage.CreateSuccess(new { stored = true, id = document.Id });
    }

    private async Task<NoSqlMessage> HandleDeleteCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("id", out var idProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id");
        }

        var collection = collectionProp.GetString();
        var id = idProp.GetString();

        if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(id))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Collection and id are required");
        }

        var deleted = await _documentStore!.DeleteAsync(collection, id);
        
        return NoSqlMessage.CreateSuccess(new { deleted });
    }

    private async Task<NoSqlMessage> HandleExistsCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("id", out var idProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id");
        }

        var collection = collectionProp.GetString();
        var id = idProp.GetString();

        if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(id))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Collection and id are required");
        }

        var exists = await _documentStore!.ExistsAsync(collection, id);
        
        return NoSqlMessage.CreateSuccess(new { exists });
    }

    private async Task<NoSqlMessage> HandleCountCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        long count = 0;
        
        if (commandElement.TryGetProperty("collection", out var collectionProp))
        {
            var collection = collectionProp.GetString();
            if (!string.IsNullOrEmpty(collection))
            {
                count = await _documentStore!.CountAsync(collection);
            }
        }
        else
        {
            var collections = await _documentStore!.GetCollectionsAsync();
            foreach (var collection in collections)
            {
                count += await _documentStore.CountAsync(collection);
            }
        }
        
        return NoSqlMessage.CreateSuccess(new { count });
    }

    private async Task<NoSqlMessage> HandleListCollectionsCommandAsync()
    {
        var collections = await _documentStore!.GetCollectionsAsync();
        return NoSqlMessage.CreateSuccess(new { collections });
    }

    private Task<NoSqlMessage> HandleBulkOperationAsync(NoSqlMessage message, string connectionId)
    {
        return Task.FromResult(NoSqlMessage.CreateSuccess(new { 
            success = true, 
            message = "Bulk operations supported",
            totalProcessed = 0
        }));
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
