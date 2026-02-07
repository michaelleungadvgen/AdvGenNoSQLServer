// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Caching;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Network;
using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Transactions;
using System.Runtime;
using System.Text;

namespace AdvGenNoSqlServer.Host;

/// <summary>
/// Entry point for the AdvGenNoSQL Server host application.
/// Provides a standalone console application to run the NoSQL server.
/// </summary>
public class Program
{
    private static IConfigurationManager? _configurationManager;
    private static TcpServer? _tcpServer;
    private static ICacheManager? _cacheManager;
    private static IDocumentStore? _documentStore;
    private static AuthenticationManager? _authenticationManager;
    private static ITransactionCoordinator? _transactionCoordinator;
    private static IAuditLogger? _auditLogger;
    private static bool _isRunning = false;
    private static CancellationTokenSource? _shutdownCts;

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

        // Parse command line arguments
        var configPath = ParseCommandLineArgs(args);
        
        // Setup cancellation handling
        _shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        try
        {
            // Initialize the server
            await InitializeServerAsync(configPath);
            
            // Start the server
            await StartServerAsync(_shutdownCts.Token);
            
            // Wait for shutdown signal
            await WaitForShutdownAsync();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Fatal error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
            Environment.ExitCode = 1;
        }
        finally
        {
            await ShutdownServerAsync();
        }
    }

    /// <summary>
    /// Parses command line arguments and returns the configuration file path.
    /// </summary>
    private static string ParseCommandLineArgs(string[] args)
    {
        var configPath = "appsettings.json";
        
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--config":
                case "-c":
                    if (i + 1 < args.Length)
                    {
                        configPath = args[++i];
                    }
                    break;
                    
                case "--help":
                case "-h":
                    ShowHelp();
                    Environment.Exit(0);
                    break;
                    
                case "--version":
                case "-v":
                    ShowVersion();
                    Environment.Exit(0);
                    break;
                    
                case "--environment":
                case "-e":
                    if (i + 1 < args.Length)
                    {
                        var env = args[++i];
                        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", env);
                        configPath = $"appsettings.{env}.json";
                    }
                    break;
            }
        }
        
        return configPath;
    }

    /// <summary>
    /// Displays help information.
    /// </summary>
    private static void ShowHelp()
    {
        Console.WriteLine("Usage: AdvGenNoSqlServer.Host [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -c, --config <path>       Path to configuration file (default: appsettings.json)");
        Console.WriteLine("  -e, --environment <env>   Environment name (Development, Production, Testing)");
        Console.WriteLine("  -h, --help                Show this help message");
        Console.WriteLine("  -v, --version             Show version information");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  AdvGenNoSqlServer.Host");
        Console.WriteLine("  AdvGenNoSqlServer.Host --config /etc/nosql-server/config.json");
        Console.WriteLine("  AdvGenNoSqlServer.Host --environment Production");
    }

    /// <summary>
    /// Displays version information.
    /// </summary>
    private static void ShowVersion()
    {
        Console.WriteLine("AdvGenNoSQL Server Host v1.0.0");
        Console.WriteLine("MIT License - Copyright (c) 2026 AdvanGeneration Pty. Ltd.");
    }

    /// <summary>
    /// Initializes all server components.
    /// </summary>
    private static async Task InitializeServerAsync(string configPath)
    {
        Console.WriteLine($"Loading configuration from: {configPath}");
        
        // Load configuration
        _configurationManager = new ConfigurationManager(configPath, enableHotReload: true);
        _configurationManager.ConfigurationChanged += OnConfigurationChanged;
        
        var config = _configurationManager.Configuration;
        
        Console.WriteLine($"Server Configuration:");
        Console.WriteLine($"  Host: {config.Host}:{config.Port}");
        Console.WriteLine($"  Max Connections: {config.MaxConcurrentConnections}");
        Console.WriteLine($"  Storage Path: {config.StoragePath}");
        Console.WriteLine($"  Data Path: {config.DataPath}");
        Console.WriteLine();
        
        // Ensure storage directories exist
        EnsureDirectoriesExist(config);
        
        // Configure GC for server workloads
        ConfigureGarbageCollector(config);
        
        // Initialize cache manager
        _cacheManager = new AdvancedMemoryCacheManager(
            maxItemCount: config.MaxCacheItemCount > 0 ? config.MaxCacheItemCount : 10000,
            maxSizeInBytes: config.MaxCacheSizeInBytes > 0 ? config.MaxCacheSizeInBytes : 104857600,
            defaultTtlMilliseconds: config.DefaultCacheTtlMilliseconds > 0 ? config.DefaultCacheTtlMilliseconds : 1800000);
        
        Console.WriteLine("Cache manager initialized.");
        
        // Initialize document store with persistence
        _documentStore = new PersistentDocumentStore(config.DataPath);
        await _documentStore.InitializeAsync();
        
        Console.WriteLine("Document store initialized.");
        
        // Initialize authentication
        if (config.RequireAuthentication)
        {
            _authenticationManager = new AuthenticationManager();
            await _authenticationManager.InitializeAsync();
            Console.WriteLine("Authentication manager initialized.");
        }
        
        // Initialize audit logger
        if (config.EnableAuditLogging)
        {
            _auditLogger = new AuditLogger(config.LogPath, config.AuditLogFlushIntervalSeconds);
            await _auditLogger.InitializeAsync();
            Console.WriteLine("Audit logger initialized.");
        }
        
        // Initialize transaction coordinator
        _transactionCoordinator = new TransactionCoordinator(
            TimeSpan.FromMilliseconds(config.TransactionTimeoutMs),
            config.MaxConcurrentTransactions);
        
        Console.WriteLine("Transaction coordinator initialized.");
        Console.WriteLine();
    }

    /// <summary>
    /// Starts the TCP server.
    /// </summary>
    private static async Task StartServerAsync(CancellationToken cancellationToken)
    {
        if (_configurationManager == null)
            throw new InvalidOperationException("Server not initialized");
        
        var config = _configurationManager.Configuration;
        
        Console.WriteLine("Starting TCP server...");
        
        _tcpServer = new TcpServer(config);
        _tcpServer.ConnectionEstablished += OnConnectionEstablished;
        _tcpServer.ConnectionClosed += OnConnectionClosed;
        _tcpServer.MessageReceived += OnMessageReceivedAsync;
        
        await _tcpServer.StartAsync(cancellationToken);
        _isRunning = true;
        
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  Server is running on {config.Host}:{config.Port}");
        Console.WriteLine($"  Press Ctrl+C to stop the server");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();
        
        // Log server start
        _auditLogger?.LogServerStart(config.Host, config.Port, $"v1.0.0 - Max Connections: {config.MaxConcurrentConnections}");
    }

    /// <summary>
    /// Waits for the shutdown signal.
    /// </summary>
    private static async Task WaitForShutdownAsync()
    {
        if (_shutdownCts == null)
            return;
        
        try
        {
            await Task.Delay(Timeout.Infinite, _shutdownCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when shutdown is requested
        }
    }

    /// <summary>
    /// Shuts down the server gracefully.
    /// </summary>
    private static async Task ShutdownServerAsync()
    {
        if (!_isRunning)
            return;
        
        Console.WriteLine();
        Console.WriteLine("Shutting down server...");
        
        _auditLogger?.LogServerStop("Graceful shutdown initiated");
        
        if (_tcpServer != null)
        {
            _tcpServer.ConnectionEstablished -= OnConnectionEstablished;
            _tcpServer.ConnectionClosed -= OnConnectionClosed;
            _tcpServer.MessageReceived -= OnMessageReceivedAsync;
            
            await _tcpServer.StopAsync(TimeSpan.FromSeconds(30));
            _tcpServer.Dispose();
            _tcpServer = null;
        }
        
        if (_documentStore != null)
        {
            await _documentStore.DisposeAsync();
            _documentStore = null;
        }
        
        _auditLogger?.Dispose();
        _transactionCoordinator?.Dispose();
        
        _isRunning = false;
        
        Console.WriteLine("Server stopped.");
    }

    /// <summary>
    /// Ensures all required directories exist.
    /// </summary>
    private static void EnsureDirectoriesExist(ServerConfiguration config)
    {
        if (!string.IsNullOrEmpty(config.DataPath))
        {
            Directory.CreateDirectory(config.DataPath);
        }
        
        if (!string.IsNullOrEmpty(config.LogPath))
        {
            Directory.CreateDirectory(config.LogPath);
        }
        
        if (!string.IsNullOrEmpty(config.StoragePath))
        {
            Directory.CreateDirectory(config.StoragePath);
        }
    }

    /// <summary>
    /// Configures the garbage collector for optimal server performance.
    /// </summary>
    private static void ConfigureGarbageCollector(ServerConfiguration config)
    {
        if (config.GCMode?.Equals("Server", StringComparison.OrdinalIgnoreCase) == true)
        {
            GCSettings.LatencyMode = GCLatencyMode.Batch;
            Console.WriteLine("GC Mode: Server (optimized for throughput)");
        }
        else
        {
            Console.WriteLine("GC Mode: Workstation (optimized for responsiveness)");
        }
    }

    /// <summary>
    /// Handles configuration changes at runtime.
    /// </summary>
    private static void OnConfigurationChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        Console.WriteLine($"Configuration changed at {e.ChangeTime}");
        Console.WriteLine($"  Old Port: {e.OldConfiguration.Port}, New Port: {e.NewConfiguration.Port}");
        
        // Note: Some configuration changes require server restart
        // In a production system, you might want to selectively apply changes
    }

    /// <summary>
    /// Handles Ctrl+C key press.
    /// </summary>
    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _shutdownCts?.Cancel();
    }

    /// <summary>
    /// Handles process exit signal.
    /// </summary>
    private static void OnProcessExit(object? sender, EventArgs e)
    {
        _shutdownCts?.Cancel();
    }

    /// <summary>
    /// Handles connection established event.
    /// </summary>
    private static void OnConnectionEstablished(object? sender, ConnectionEventArgs e)
    {
        var remoteEndPoint = e.Client.Client?.RemoteEndPoint?.ToString() ?? "unknown";
        Console.WriteLine($"[+] Connection established: {e.ConnectionId} from {remoteEndPoint}");
        _auditLogger?.LogConnectionEstablished(e.ConnectionId, remoteEndPoint);
    }

    /// <summary>
    /// Handles connection closed event.
    /// </summary>
    private static void OnConnectionClosed(object? sender, ConnectionEventArgs e)
    {
        Console.WriteLine($"[-] Connection closed: {e.ConnectionId}");
        _auditLogger?.LogConnectionClosed(e.ConnectionId);
    }

    /// <summary>
    /// Handles incoming messages.
    /// </summary>
    private static async void OnMessageReceivedAsync(object? sender, MessageReceivedEventArgs e)
    {
        try
        {
            var response = await ProcessMessageAsync(e.Message, e.ConnectionId);
            await e.SendResponseAsync(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing message: {ex.Message}");
            var errorResponse = NoSqlMessage.CreateError("INTERNAL_ERROR", "An error occurred processing the message");
            await e.SendResponseAsync(errorResponse);
        }
    }

    /// <summary>
    /// Processes an incoming message and returns a response.
    /// </summary>
    private static Task<NoSqlMessage> ProcessMessageAsync(NoSqlMessage message, string connectionId)
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

    /// <summary>
    /// Handles handshake messages.
    /// </summary>
    private static Task<NoSqlMessage> HandleHandshakeAsync(NoSqlMessage message, string connectionId)
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

    /// <summary>
    /// Handles ping messages.
    /// </summary>
    private static Task<NoSqlMessage> HandlePingAsync(NoSqlMessage message, string connectionId)
    {
        return Task.FromResult(new NoSqlMessage
        {
            MessageType = MessageType.Pong,
            Payload = Array.Empty<byte>(),
            PayloadLength = 0
        });
    }

    /// <summary>
    /// Handles authentication messages.
    /// </summary>
    private static async Task<NoSqlMessage> HandleAuthenticationAsync(NoSqlMessage message, string connectionId)
    {
        if (_authenticationManager == null)
        {
            // Authentication not required
            return NoSqlMessage.CreateSuccess(new { authenticated = true, token = "anonymous" });
        }

        if (message.Payload == null || message.PayloadLength == 0)
        {
            _auditLogger?.LogAuthenticationFailed("unknown", "Missing credentials");
            return NoSqlMessage.CreateError("AUTH_FAILED", "Missing credentials");
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
                _auditLogger?.LogAuthenticationFailed(username ?? "unknown", "Missing username or password");
                return NoSqlMessage.CreateError("AUTH_FAILED", "Missing username or password");
            }

            var result = await _authenticationManager.AuthenticateAsync(username, password);
            
            if (result.Success)
            {
                _auditLogger?.LogAuthenticationSuccess(username, connectionId);
                return NoSqlMessage.CreateSuccess(new { authenticated = true, token = result.Token, username });
            }
            else
            {
                _auditLogger?.LogAuthenticationFailed(username, result.ErrorMessage ?? "Invalid credentials");
                return NoSqlMessage.CreateError("AUTH_FAILED", result.ErrorMessage ?? "Invalid credentials");
            }
        }
        catch (Exception ex)
        {
            _auditLogger?.LogAuthenticationFailed("unknown", $"Authentication error: {ex.Message}");
            return NoSqlMessage.CreateError("AUTH_FAILED", "Invalid authentication format");
        }
    }

    /// <summary>
    /// Handles command messages.
    /// </summary>
    private static async Task<NoSqlMessage> HandleCommandAsync(NoSqlMessage message, string connectionId)
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

    /// <summary>
    /// Handles GET command.
    /// </summary>
    private static async Task<NoSqlMessage> HandleGetCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (_documentStore == null)
            return NoSqlMessage.CreateError("NOT_INITIALIZED", "Document store not initialized");

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

        var document = await _documentStore.GetDocumentAsync(collection, id);
        
        if (document == null)
        {
            return NoSqlMessage.CreateSuccess(new { found = false, document = (object?)null });
        }

        return NoSqlMessage.CreateSuccess(new { found = true, document });
    }

    /// <summary>
    /// Handles SET command.
    /// </summary>
    private static async Task<NoSqlMessage> HandleSetCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (_documentStore == null)
            return NoSqlMessage.CreateError("NOT_INITIALIZED", "Document store not initialized");

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

        // Convert JsonElement to Document
        var json = documentProp.GetRawText();
        var document = System.Text.Json.JsonSerializer.Deserialize<Core.Models.Document>(json);
        
        if (document == null)
        {
            return NoSqlMessage.CreateError("INVALID_DOCUMENT", "Failed to parse document");
        }

        // Ensure document has an ID
        if (string.IsNullOrEmpty(document.Id))
        {
            document.Id = Guid.NewGuid().ToString("N");
        }

        await _documentStore.InsertOrUpdateDocumentAsync(collection, document);
        
        return NoSqlMessage.CreateSuccess(new { stored = true, id = document.Id });
    }

    /// <summary>
    /// Handles DELETE command.
    /// </summary>
    private static async Task<NoSqlMessage> HandleDeleteCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (_documentStore == null)
            return NoSqlMessage.CreateError("NOT_INITIALIZED", "Document store not initialized");

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

        var deleted = await _documentStore.DeleteDocumentAsync(collection, id);
        
        return NoSqlMessage.CreateSuccess(new { deleted });
    }

    /// <summary>
    /// Handles EXISTS command.
    /// </summary>
    private static async Task<NoSqlMessage> HandleExistsCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (_documentStore == null)
            return NoSqlMessage.CreateError("NOT_INITIALIZED", "Document store not initialized");

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

        var document = await _documentStore.GetDocumentAsync(collection, id);
        
        return NoSqlMessage.CreateSuccess(new { exists = document != null });
    }

    /// <summary>
    /// Handles COUNT command.
    /// </summary>
    private static async Task<NoSqlMessage> HandleCountCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (_documentStore == null)
            return NoSqlMessage.CreateError("NOT_INITIALIZED", "Document store not initialized");

        long count = 0;
        
        if (commandElement.TryGetProperty("collection", out var collectionProp))
        {
            var collection = collectionProp.GetString();
            if (!string.IsNullOrEmpty(collection))
            {
                count = await _documentStore.CountDocumentsAsync(collection);
            }
        }
        else
        {
            // Count all documents across all collections
            var collections = _documentStore.GetCollections();
            foreach (var collection in collections)
            {
                count += await _documentStore.CountDocumentsAsync(collection);
            }
        }
        
        return NoSqlMessage.CreateSuccess(new { count });
    }

    /// <summary>
    /// Handles LISTCOLLECTIONS command.
    /// </summary>
    private static Task<NoSqlMessage> HandleListCollectionsCommandAsync()
    {
        if (_documentStore == null)
            return Task.FromResult(NoSqlMessage.CreateError("NOT_INITIALIZED", "Document store not initialized"));

        var collections = _documentStore.GetCollections();
        
        return Task.FromResult(NoSqlMessage.CreateSuccess(new { collections }));
    }

    /// <summary>
    /// Handles bulk operation messages.
    /// </summary>
    private static Task<NoSqlMessage> HandleBulkOperationAsync(NoSqlMessage message, string connectionId)
    {
        // For now, return a simple response indicating bulk operations are supported
        // Full implementation would parse and process the batch request
        return Task.FromResult(NoSqlMessage.CreateSuccess(new { 
            success = true, 
            message = "Bulk operations supported",
            totalProcessed = 0
        }));
    }
}
