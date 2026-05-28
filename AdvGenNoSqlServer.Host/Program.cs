// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Caching;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.MemoryManagement;
using AdvGenNoSqlServer.Core.Metrics;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Transactions;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Network;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Text;

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

        var builder = WebApplication.CreateBuilder(args);

        // Configure logging
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddConsole();

        // HTTPS API port for Blazor WASM admin dashboard (TCP port 9191 + 1 = 9192, always HTTPS)
        // Blazor WASM is served from HTTPS so the API must also be HTTPS to avoid mixed-content blocks.
        // Uses the ASP.NET Core developer certificate. Run: dotnet dev-certs https --trust
        var certPath = builder.Configuration.GetValue<string>("HttpsCertificatePath");
        var certPassword = builder.Configuration.GetValue<string>("HttpsCertificatePassword");

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(9192, listenOptions =>
            {
                if (!string.IsNullOrEmpty(certPath) && File.Exists(certPath))
                {
                    listenOptions.UseHttps(certPath, certPassword);
                    Console.WriteLine($"[HTTPS API] Using certificate: {certPath}");
                }
                else
                {
                    listenOptions.UseHttps(); // Uses ASP.NET Core developer certificate
                }
            });
        });
        Console.WriteLine("[HTTPS API] TCP port: 9191, HTTPS API port: 9192");

        // CORS for Blazor WASM
        builder.Services.AddCors();

        // Register ApiDataService (must be before NoSqlServerHost)
        builder.Services.AddSingleton<ApiDataService>();

        // Register all services
        ConfigureServices(builder.Services);

        var app = builder.Build();

        // CORS middleware (must be before auth)
        app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

        // Authentication & Authorization middleware
        app.UseAuthentication();
        app.UseAuthorization();

        // --- REST API endpoints ---

        // Public health check — used by Admin ConnectAsync before auth token is available
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

        // Secured endpoints (require JWT auth)
        app.MapGet("/api/stats", [Microsoft.AspNetCore.Authorization.Authorize] async (ApiDataService data) =>
        {
            var uptime = DateTime.UtcNow - data.StartTime;
            var memMB = (int)(GC.GetTotalMemory(false) / 1_048_576);
            long totalDocs = 0;
            int totalCols = 0;
            int totalDbs = 0;
            if (data.DatabaseManager != null)
            {
                totalDbs = data.DatabaseManager.GetDatabaseNames().Count();
                // Get stats from default database
                if (data.DocumentStore != null)
                {
                    var cols = (await data.DocumentStore.GetCollectionsAsync()).ToList();
                    totalCols = cols.Count;
                    foreach (var c in cols) totalDocs += await data.DocumentStore.CountAsync(c);
                }
            }
            return Results.Ok(new
            {
                version = "1.0.0",
                uptimeSeconds = (long)uptime.TotalSeconds,
                memoryUsageMB = memMB,
                totalDocuments = totalDocs,
                totalCollections = totalCols,
                totalDatabases = totalDbs,
                activeConnections = data.TcpServer?.ActiveConnectionCount ?? 0
            });
        });

        // --- Database Management ---

        app.MapGet("/api/databases", [Microsoft.AspNetCore.Authorization.Authorize] (ApiDataService data) =>
        {
            if (data.DatabaseManager == null) return Results.Ok(Array.Empty<string>());
            var dbs = data.DatabaseManager.GetDatabaseNames();
            return Results.Ok(dbs);
        });

        app.MapPost("/api/databases/{name}", [Microsoft.AspNetCore.Authorization.Authorize] async (string name, ApiDataService data) =>
        {
            if (data.DatabaseManager == null) return Results.StatusCode(503);
            try
            {
                var created = await data.DatabaseManager.CreateDatabaseAsync(name);
                if (created)
                    return Results.Ok(new { success = true, message = $"Database '{name}' created" });
                return Results.BadRequest(new { success = false, error = "Database already exists or invalid name" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        app.MapDelete("/api/databases/{name}", [Microsoft.AspNetCore.Authorization.Authorize] async (string name, ApiDataService data) =>
        {
            if (data.DatabaseManager == null) return Results.StatusCode(503);
            try
            {
                var deleted = await data.DatabaseManager.DeleteDatabaseAsync(name);
                if (deleted)
                    return Results.Ok(new { success = true, message = $"Database '{name}' deleted" });
                return Results.BadRequest(new { success = false, error = "Cannot delete default database or database not found" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        app.MapPost("/api/databases/{name}/select", [Microsoft.AspNetCore.Authorization.Authorize] (string name, ApiDataService data) =>
        {
            if (data.DatabaseManager == null) return Results.StatusCode(503);
            try
            {
                var store = data.DatabaseManager.GetDatabase(name);
                data.DocumentStore = store;
                return Results.Ok(new { success = true, message = $"Database '{name}' selected" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        app.MapGet("/api/collections", [Microsoft.AspNetCore.Authorization.Authorize] async (ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.Ok(Array.Empty<string>());
            var cols = await data.DocumentStore.GetCollectionsAsync();
            return Results.Ok(cols);
        });

        app.MapGet("/api/collections/{name}/count", [Microsoft.AspNetCore.Authorization.Authorize] async (string name, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.Ok(new { count = 0L });
            var count = await data.DocumentStore.CountAsync(name);
            return Results.Ok(new { count });
        });

        app.MapGet("/api/collections/{name}/documents", [Microsoft.AspNetCore.Authorization.Authorize] async (string name, int skip, int take, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.Ok(Array.Empty<object>());
            var all = await data.DocumentStore.GetAllAsync(name);
            var page = all.Skip(skip).Take(take > 0 ? take : 50);
            return Results.Ok(page);
        });

        app.MapGet("/api/collections/{name}/documents/{id}", [Microsoft.AspNetCore.Authorization.Authorize] async (string name, string id, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.NotFound();
            var doc = await data.DocumentStore.GetAsync(name, id);
            return doc == null ? Results.NotFound() : Results.Ok(doc);
        });

        app.MapPost("/api/query", [Microsoft.AspNetCore.Authorization.Authorize] async (QueryRequest req, ApiDataService data) =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var docs = new List<object>();
            if (data.DocumentStore != null && !string.IsNullOrEmpty(req.Collection))
            {
                var all = await data.DocumentStore.GetAllAsync(req.Collection);
                docs.AddRange(all.Take(100));
            }
            sw.Stop();
            return Results.Ok(new
            {
                success = true,
                documents = docs,
                executionTimeMs = (int)sw.ElapsedMilliseconds,
                totalCount = docs.Count
            });
        });

        // --- Collection Management ---

        app.MapPost("/api/collections/{name}", [Microsoft.AspNetCore.Authorization.Authorize] async (string name, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.StatusCode(503);
            try
            {
                await data.DocumentStore.CreateCollectionAsync(name);
                return Results.Ok(new { success = true, message = $"Collection '{name}' created" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        app.MapDelete("/api/collections/{name}", [Microsoft.AspNetCore.Authorization.Authorize] async (string name, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.StatusCode(503);
            try
            {
                var deleted = await data.DocumentStore.DropCollectionAsync(name);
                if (deleted)
                    return Results.Ok(new { success = true, message = $"Collection '{name}' deleted" });
                return Results.NotFound(new { success = false, error = "Collection not found" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        // --- Document CRUD ---

        app.MapPost("/api/collections/{name}/documents", [Microsoft.AspNetCore.Authorization.Authorize] async (string name, Document doc, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.StatusCode(503);
            try
            {
                if (string.IsNullOrEmpty(doc.Id))
                    doc.Id = Guid.NewGuid().ToString("N");
                var inserted = await data.DocumentStore.InsertAsync(name, doc);
                return Results.Ok(new { success = true, id = inserted.Id });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        app.MapPut("/api/collections/{name}/documents/{id}", [Microsoft.AspNetCore.Authorization.Authorize] async (string name, string id, Document doc, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.StatusCode(503);
            try
            {
                doc.Id = id;
                var updated = await data.DocumentStore.UpdateAsync(name, doc);
                return Results.Ok(new { success = true, id = updated.Id });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        app.MapDelete("/api/collections/{name}/documents/{id}", [Microsoft.AspNetCore.Authorization.Authorize] async (string name, string id, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.StatusCode(503);
            try
            {
                var deleted = await data.DocumentStore.DeleteAsync(name, id);
                if (deleted)
                    return Results.Ok(new { success = true, message = "Document deleted" });
                return Results.NotFound(new { success = false, error = "Document not found" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        // --- Authentication ---

        // Public endpoint: Login (no auth required)
        app.MapPost("/api/auth/login", async (LoginRequest req, ApiDataService data, AuthenticationManager auth, IJwtTokenProvider jwtProvider) =>
        {
            if (data.DocumentStore == null) return Results.StatusCode(503);
            try
            {
                var result = auth.Authenticate(req.Username, req.Password);
                if (result != null)
                {
                    // Generate JWT token with admin role
                    var roles = new[] { "Admin" };
                    var permissions = new[] { "*" };
                    var jwtToken = jwtProvider.GenerateToken(req.Username, roles, permissions);
                    
                    return Results.Ok(new
                    {
                        success = true,
                        token = jwtToken,
                        username = req.Username,
                        expiresAt = result.ExpiresAt
                    });
                }
                return Results.Unauthorized();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        }).AllowAnonymous();

        // Start the application (also starts IHostedServices)
        await app.RunAsync();
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

        // Add metrics collector
        services.AddMetricsCollector();

        // Add memory storage engine
        services.AddMemoryEngine(new MemoryManagementConfiguration
        {
            Plan = "Managed",
            MaxMemoryMB = 512,
            MaxMemoryPercent = 75,
            EvictionPolicy = "LRU",
            DefaultTtlSeconds = 1800
        });

        // Add cache manager
        services.AddSingleton<ICacheManager>(provider =>
        {
            var engine = provider.GetRequiredService<IMemoryStorageEngine>();
            var metrics = provider.GetRequiredService<IMetricsCollector>();
            return new AdvancedMemoryCacheManager(engine, metrics);
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

        // Add JWT token provider
        services.AddSingleton<IJwtTokenProvider>(provider =>
        {
            var configManager = provider.GetRequiredService<Core.Configuration.IConfigurationManager>();
            var config = configManager.Configuration;
            return new JwtTokenProvider(config);
        });

        // Add JWT authentication
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var config = services.BuildServiceProvider()
                    .GetRequiredService<Core.Configuration.IConfigurationManager>().Configuration;
                
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = config.JwtIssuer ?? "AdvGenNoSqlServer",
                    ValidAudience = config.JwtAudience ?? "AdvGenNoSqlClient",
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(config.JwtSecretKey ?? GenerateSecureSecret()))
                };
                
                // For Blazor WASM, don't redirect to login page on 401
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                        {
                            context.Response.Headers.Append("Token-Expired", "true");
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

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

        // Add database manager
        services.AddSingleton<IDatabaseManager>(provider =>
        {
            var configManager = provider.GetRequiredService<Core.Configuration.IConfigurationManager>();
            var config = configManager.Configuration;
            var storagePath = string.IsNullOrEmpty(config.StoragePath) ? "data" : config.StoragePath;
            if (!Path.IsPathRooted(storagePath))
            {
                storagePath = Path.Combine(AppContext.BaseDirectory, storagePath);
            }
            return new DatabaseManager(storagePath);
        });

        // Add the hosted NoSQL server service
        services.AddHostedService<NoSqlServerHost>();
    }

    /// <summary>
    /// Generates a secure random secret key for JWT signing
    /// </summary>
    private static string GenerateSecureSecret()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}

/// <summary>
/// Request body for POST /api/query.
/// </summary>
public record QueryRequest(string Query, string? Collection);

/// <summary>
/// Request body for POST /api/auth/login.
/// </summary>
public record LoginRequest(string Username, string Password);

/// <summary>
/// Hosted service wrapper for the NoSQL server.
/// </summary>
internal class NoSqlServerHost : IHostedService, IAsyncDisposable
{
    private readonly ILogger<NoSqlServerHost> _logger;
    private readonly Core.Configuration.IConfigurationManager _configManager;
    private readonly IAuditLogger _auditLogger;
    private readonly AuthenticationManager _authManager;
    private readonly IDatabaseManager _databaseManager;
    private readonly ApiDataService _apiData;
    private TcpServer? _tcpServer;
    private bool _disposed;

    public NoSqlServerHost(
        ILogger<NoSqlServerHost> logger,
        Core.Configuration.IConfigurationManager configManager,
        IAuditLogger auditLogger,
        AuthenticationManager authManager,
        IDatabaseManager databaseManager,
        ApiDataService apiData)
    {
        _logger = logger;
        _configManager = configManager;
        _auditLogger = auditLogger;
        _authManager = authManager;
        _databaseManager = databaseManager;
        _apiData = apiData;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = _configManager.Configuration;

        _logger.LogInformation("Starting NoSQL Server on {Host}:{Port}...", config.Host, config.Port);
        _logger.LogInformation("Max connections: {MaxConnections}", config.MaxConcurrentConnections);

        // Ensure data directories exist
        EnsureDirectoriesExist(config);

        _logger.LogInformation("Initializing database manager...");
        // DatabaseManager is already initialized via DI
        var defaultDb = _databaseManager.GetDatabase(_databaseManager.DefaultDatabaseName);
        _logger.LogInformation("Databases: {Count}", _databaseManager.GetDatabaseNames().Count());

        // Create and configure the TCP server
        _tcpServer = new TcpServer(config);
        _tcpServer.ConnectionEstablished += OnConnectionEstablished;
        _tcpServer.ConnectionClosed += OnConnectionClosed;
        _tcpServer.MessageReceived += OnMessageReceivedAsync;

        // Expose live references to the HTTP API
        _apiData.DatabaseManager = _databaseManager;
        _apiData.DocumentStore = defaultDb;
        _apiData.TcpServer = _tcpServer;

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
        Console.WriteLine($"  HTTPS API is running on https://0.0.0.0:9192");
        Console.WriteLine($"  Press Ctrl+C to stop the server");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping NoSQL Server...");

        // Clear live references so HTTP API returns safe empty data
        _apiData.DatabaseManager = null;
        _apiData.DocumentStore = null;
        _apiData.TcpServer = null;

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
        if (_apiData.DocumentStore == null)
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
                "createcollection" => await HandleCreateCollectionCommandAsync(doc.RootElement),
                "dropcollection" => await HandleDropCollectionCommandAsync(doc.RootElement),
                "listdocuments" => await HandleListDocumentsCommandAsync(doc.RootElement),
                "stats" => await HandleStatsCommandAsync(),
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

        var document = await _apiData.DocumentStore!.GetAsync(collection, id);

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

        var exists = await _apiData.DocumentStore!.ExistsAsync(collection, document.Id);
        if (exists)
        {
            await _apiData.DocumentStore.UpdateAsync(collection, document);
        }
        else
        {
            await _apiData.DocumentStore.InsertAsync(collection, document);
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

        var deleted = await _apiData.DocumentStore!.DeleteAsync(collection, id);

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

        var exists = await _apiData.DocumentStore!.ExistsAsync(collection, id);

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
                count = await _apiData.DocumentStore!.CountAsync(collection);
            }
        }
        else
        {
            var collections = await _apiData.DocumentStore!.GetCollectionsAsync();
            foreach (var collection in collections)
            {
                count += await _apiData.DocumentStore.CountAsync(collection);
            }
        }

        return NoSqlMessage.CreateSuccess(new { count });
    }

    private async Task<NoSqlMessage> HandleListCollectionsCommandAsync()
    {
        var collections = await _apiData.DocumentStore!.GetCollectionsAsync();
        return NoSqlMessage.CreateSuccess(new { collections });
    }

    private async Task<NoSqlMessage> HandleCreateCollectionCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("collection", out var collectionProp))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection");

        var collection = collectionProp.GetString();
        if (string.IsNullOrEmpty(collection))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Collection name is required");

        await _apiData.DocumentStore!.CreateCollectionAsync(collection);
        return NoSqlMessage.CreateSuccess(new { created = true, name = collection });
    }

    private async Task<NoSqlMessage> HandleDropCollectionCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("collection", out var collectionProp))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection");

        var collection = collectionProp.GetString();
        if (string.IsNullOrEmpty(collection))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Collection name is required");

        var dropped = await _apiData.DocumentStore!.DropCollectionAsync(collection);
        return NoSqlMessage.CreateSuccess(new { dropped, name = collection });
    }

    private async Task<NoSqlMessage> HandleListDocumentsCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("collection", out var collectionProp))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection");

        var collection = collectionProp.GetString();
        if (string.IsNullOrEmpty(collection))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Collection name is required");

        int skip = 0;
        int take = 50;
        if (commandElement.TryGetProperty("document", out var docProp) &&
            docProp.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (docProp.TryGetProperty("skip", out var skipProp)) skip = skipProp.GetInt32();
            if (docProp.TryGetProperty("take", out var takeProp)) take = takeProp.GetInt32();
        }

        var all = (await _apiData.DocumentStore!.GetAllAsync(collection)).ToList();
        var page = all.Skip(skip).Take(take > 0 ? take : 50).ToList();
        var total = all.Count;
        return NoSqlMessage.CreateSuccess(new { documents = page, total, collection });
    }

    private async Task<NoSqlMessage> HandleStatsCommandAsync()
    {
        var uptime = DateTime.UtcNow - _apiData.StartTime;
        var memMB = (int)(GC.GetTotalMemory(false) / 1_048_576);
        long totalDocuments = 0;
        int totalCollections = 0;
        if (_apiData.DocumentStore != null)
        {
            var cols = (await _apiData.DocumentStore.GetCollectionsAsync()).ToList();
            totalCollections = cols.Count;
            foreach (var c in cols) totalDocuments += await _apiData.DocumentStore.CountAsync(c);
        }
        return NoSqlMessage.CreateSuccess(new
        {
            version = "1.0.0",
            uptimeSeconds = (long)uptime.TotalSeconds,
            memoryUsageMB = memMB,
            totalDocuments,
            totalCollections,
            activeConnections = _apiData.TcpServer?.ActiveConnectionCount ?? 0
        });
    }

    private Task<NoSqlMessage> HandleBulkOperationAsync(NoSqlMessage message, string connectionId)
    {
        return Task.FromResult(NoSqlMessage.CreateSuccess(new
        {
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
    }
}
