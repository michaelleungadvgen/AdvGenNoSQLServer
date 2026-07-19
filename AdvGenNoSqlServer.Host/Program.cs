// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Attachments;
using AdvGenNoSqlServer.Core.Caching;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.MemoryManagement;
using AdvGenNoSqlServer.Core.Metrics;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Transactions;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Attachments;
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

        // Load + validate server configuration early. Production fails fast on
        // invalid config; other environments log warnings and continue.
        var configManager = new Core.Configuration.ConfigurationManager("appsettings.json", enableHotReload: true);
        var serverConfig = configManager.Configuration;
        var configErrors = configManager.Validate();
        foreach (var configError in configErrors)
            Console.Error.WriteLine($"[CONFIG] {(configManager.IsProduction ? "ERROR" : "WARNING")}: {configError}");
        if (configErrors.Count > 0 && configManager.IsProduction)
        {
            Console.Error.WriteLine("[CONFIG] Fatal configuration errors in Production. Shutting down.");
            Environment.Exit(1);
        }

        if (!serverConfig.RequireAuthentication)
            Console.WriteLine($"[SECURITY] WARNING: Authentication is DISABLED — anonymous connections get role '{serverConfig.AnonymousRole}'. Do not run like this in production.");

        // Resolve the JWT signing secret ONCE so token issuance and validation agree.
        // Without a configured secret a random one is generated (non-production only —
        // tokens become invalid on restart). Production validation above requires a real one.
        if (serverConfig.EnableJwtAuthentication && string.IsNullOrEmpty(serverConfig.JwtSecretKey))
        {
            serverConfig.JwtSecretKey = GenerateSecureSecret();
            Console.WriteLine("[JWT] WARNING: JwtSecretKey not configured — generated a random secret for this run. Set NOSQL_JWT_SECRET_KEY.");
        }

        // Load the TLS certificate once at startup: a bad/missing cert must fail fast
        // (in Production) instead of failing every client handshake later.
        if (serverConfig.EnableSsl)
        {
            try
            {
                using var probe = TlsStreamHelper.LoadCertificate(serverConfig);
                if (probe == null)
                    throw new InvalidOperationException("certificate load returned null");
                Console.WriteLine($"[TLS] Server certificate loaded (subject: {probe.Subject}, expires: {probe.NotAfter:yyyy-MM-dd}).");
            }
            catch (Exception ex)
            {
                if (configManager.IsProduction)
                {
                    Console.Error.WriteLine($"[TLS] FATAL: Could not load the server certificate: {ex.Message}");
                    Environment.Exit(1);
                }
                Console.WriteLine($"[TLS] WARNING: Could not load the server certificate: {ex.Message}");
                Console.WriteLine("[TLS] TLS handshakes will fail until this is fixed. Set NOSQL_SSL_CERT_PATH / NOSQL_SSL_CERT_PASSWORD.");
            }
        }

        // HTTPS API port for Blazor WASM admin dashboard (TCP port 9191 + 1 = 9192, always HTTPS)
        // Blazor WASM is served from HTTPS so the API must also be HTTPS to avoid mixed-content blocks.
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
                else if (!configManager.IsProduction)
                {
                    // Developer certificate fallback — only outside explicit Production.
                    listenOptions.UseHttps();
                    Console.WriteLine("[HTTPS API] Using ASP.NET Core developer certificate (non-production)");
                }
                else
                {
                    throw new InvalidOperationException(
                        "No HTTPS certificate configured for the admin API. Set HttpsCertificatePath/HttpsCertificatePassword, or terminate TLS at a reverse proxy.");
                }
            });
        });
        Console.WriteLine("[HTTPS API] TCP port: 9191, HTTPS API port: 9192");

        // CORS for Blazor WASM (locked to configured origins in Production, permissive otherwise)
        builder.Services.AddCors();

        // Register ApiDataService (must be before NoSqlServerHost)
        builder.Services.AddSingleton<ApiDataService>();

        // Register all services (shares the configuration manager created above)
        ConfigureServices(builder.Services, configManager);

        var app = builder.Build();

        // CORS middleware (must be before auth)
        if (configManager.IsProduction)
            app.UseCors(policy => policy.WithOrigins(serverConfig.CorsAllowedOrigins).AllowAnyHeader().AllowAnyMethod());
        else
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

        app.MapPost("/api/databases/{name}", [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")] async (string name, ApiDataService data) =>
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
                app.Logger.LogError(ex, "Unhandled error in admin API endpoint");
                return Results.BadRequest(new { success = false, error = "An internal error occurred" });
            }
        });

        app.MapDelete("/api/databases/{name}", [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")] async (string name, ApiDataService data) =>
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
                app.Logger.LogError(ex, "Unhandled error in admin API endpoint");
                return Results.BadRequest(new { success = false, error = "An internal error occurred" });
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
                app.Logger.LogError(ex, "Unhandled error in admin API endpoint");
                return Results.BadRequest(new { success = false, error = "An internal error occurred" });
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

        app.MapPost("/api/collections/{name}", [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")] async (string name, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.StatusCode(503);
            try
            {
                await data.DocumentStore.CreateCollectionAsync(name);
                return Results.Ok(new { success = true, message = $"Collection '{name}' created" });
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Unhandled error in admin API endpoint");
                return Results.BadRequest(new { success = false, error = "An internal error occurred" });
            }
        });

        app.MapDelete("/api/collections/{name}", [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")] async (string name, ApiDataService data) =>
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
                app.Logger.LogError(ex, "Unhandled error in admin API endpoint");
                return Results.BadRequest(new { success = false, error = "An internal error occurred" });
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
                app.Logger.LogError(ex, "Unhandled error in admin API endpoint");
                return Results.BadRequest(new { success = false, error = "An internal error occurred" });
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
                app.Logger.LogError(ex, "Unhandled error in admin API endpoint");
                return Results.BadRequest(new { success = false, error = "An internal error occurred" });
            }
        });

        app.MapDelete("/api/collections/{name}/documents/{id}", [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")] async (string name, string id, ApiDataService data) =>
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
                app.Logger.LogError(ex, "Unhandled error in admin API endpoint");
                return Results.BadRequest(new { success = false, error = "An internal error occurred" });
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
                    // Issue the user's REAL role and permissions — every authenticated
                    // user used to receive Admin/* regardless of their actual role.
                    var roles = new[] { result.Role };
                    var permissions = result.Role == UserRole.Admin ? new[] { "*" } : Array.Empty<string>();
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
                app.Logger.LogError(ex, "Unhandled error in admin API endpoint");
                return Results.BadRequest(new { success = false, error = "An internal error occurred" });
            }
        }).AllowAnonymous();

        // Start the application (also starts IHostedServices)
        await app.RunAsync();
    }

    /// <summary>
    /// Configures services for dependency injection.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services, Core.Configuration.ConfigurationManager configManager)
    {
        // Share the configuration manager created and validated at startup
        services.AddSingleton<Core.Configuration.IConfigurationManager>(configManager);

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

        // Add authentication manager (backed by a persistent user store)
        services.AddSingleton<AuthenticationManager>(provider =>
        {
            var configManager = provider.GetRequiredService<Core.Configuration.IConfigurationManager>();
            var config = configManager.Configuration;
            var storagePath = string.IsNullOrEmpty(config.StoragePath) ? "data" : config.StoragePath;
            if (!System.IO.Path.IsPathRooted(storagePath))
                storagePath = System.IO.Path.Combine(AppContext.BaseDirectory, storagePath);
            var userPath = string.IsNullOrEmpty(config.UserStorePath)
                ? System.IO.Path.Combine(storagePath, "users.json")
                : config.UserStorePath;
            return new AuthenticationManager(config, new FileUserStore(userPath));
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
                        Encoding.UTF8.GetBytes(config.JwtSecretKey ?? GenerateSecureSecret())),
                    // Our JWTs carry roles/permissions as JSON arrays (see JwtTokenProvider)
                    RoleClaimType = "roles",
                    NameClaimType = "sub"
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Username, string Role)> _authConnections = new();
    private AttachmentStore? _attachmentStore;
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

        // Initialize attachment storage
        var attachStoragePath = string.IsNullOrEmpty(config.StoragePath) ? "data" : config.StoragePath;
        if (!System.IO.Path.IsPathRooted(attachStoragePath))
            attachStoragePath = System.IO.Path.Combine(AppContext.BaseDirectory, attachStoragePath);
        _attachmentStore = new AttachmentStore(new AttachmentStoreOptions
        {
            BasePath = System.IO.Path.Combine(attachStoragePath, "attachments"),
            MaxAttachmentSize = (long)Math.Max(config.MaxAttachmentSizeMB, 1) * 1024 * 1024
        });

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
        _authConnections.TryRemove(e.ConnectionId, out _);

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
            // Dev mode: anonymous identity with the configured least-privilege role (never Admin by default).
            var anonRole = UserRole.IsValid(_configManager.Configuration.AnonymousRole)
                ? _configManager.Configuration.AnonymousRole
                : UserRole.ReadOnly;
            _authConnections[connectionId] = ("anonymous", anonRole);
            _tcpServer?.RaisePayloadLimit(connectionId);
            return Task.FromResult(NoSqlMessage.CreateSuccess(
                new { authenticated = true, token = "anonymous", username = "anonymous", role = anonRole }));
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
                _authConnections[connectionId] = (result.Username, result.Role);
                _tcpServer?.RaisePayloadLimit(connectionId);
                _auditLogger.Log(new AuditEvent
                {
                    EventType = AuditEventType.AuthenticationSuccess,
                    Action = "Authentication",
                    Username = username,
                    Details = "Authentication successful",
                    SessionId = connectionId,
                    Timestamp = DateTime.UtcNow
                });
                return Task.FromResult(NoSqlMessage.CreateSuccess(new { authenticated = true, token = result.TokenId, username, role = result.Role }));
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

            // Role-based authorization (only enforced when authentication is required)
            if (_configManager.Configuration.RequireAuthentication && command != null)
            {
                if (!_authConnections.TryGetValue(connectionId, out var identity))
                    return NoSqlMessage.CreateError("AUTH_REQUIRED", "Authenticate before sending commands");
                if (command == "changepassword" && identity.Username == "anonymous")
                    return NoSqlMessage.CreateError("AUTH_REQUIRED", "changepassword requires an authenticated user");
                if (!CommandAuthorizer.IsAllowed(command, identity.Role))
                    return NoSqlMessage.CreateError("FORBIDDEN", $"Role '{identity.Role}' may not run '{command}'");
            }

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
                "listusers" => HandleListUsersCommand(),
                "createuser" => HandleCreateUserCommand(doc.RootElement),
                "deleteuser" => HandleDeleteUserCommand(doc.RootElement),
                "setpassword" => HandleSetPasswordCommand(doc.RootElement),
                "setrole" => HandleSetRoleCommand(doc.RootElement),
                "changepassword" => HandleChangePasswordCommand(doc.RootElement, connectionId),
                "listattachments" => await HandleListAttachmentsCommand(doc.RootElement),
                "attachmentinfo" => await HandleAttachmentInfoCommand(doc.RootElement),
                "uploadattachment" => await HandleUploadAttachmentCommand(doc.RootElement),
                "downloadattachment" => await HandleDownloadAttachmentCommand(doc.RootElement),
                "deleteattachment" => await HandleDeleteAttachmentCommand(doc.RootElement),
                "totalstorage" => await HandleTotalStorageCommand(),
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

        // Flat shape ({_id, ...data}) is the contract clients read
        return NoSqlMessage.CreateSuccess(new { found = true, document = FlattenDocument(document) });
    }

    private static Dictionary<string, object?> FlattenDocument(Core.Models.Document document)
    {
        var flat = new Dictionary<string, object?>((document.Data?.Count ?? 0) + 1) { ["_id"] = document.Id };
        if (document.Data != null)
        {
            foreach (var kvp in document.Data)
            {
                flat[kvp.Key] = kvp.Value;
            }
        }
        return flat;
    }

    private static object JsonElementToObject(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString() ?? "",
            System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null!,
            System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
            System.Text.Json.JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            _ => element.GetRawText()
        };
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

        if (documentProp.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return NoSqlMessage.CreateError("INVALID_DOCUMENT", "Document must be a JSON object");
        }

        // Wire contract is a flat document: {_id, ...fields}. Deserializing into
        // Core.Models.Document fails here (required Id, no _id mapping) — parse manually.
        string? id = null;
        var data = new Dictionary<string, object>();
        foreach (var prop in documentProp.EnumerateObject())
        {
            if (prop.Name == "_id")
            {
                id = prop.Value.GetString();
            }
            else
            {
                data[prop.Name] = JsonElementToObject(prop.Value);
            }
        }

        var document = new Core.Models.Document
        {
            Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id,
            Data = data
        };

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

        var existing = await _apiData.DocumentStore!.GetCollectionsAsync();
        bool created = !existing.Contains(collection);
        if (created)
        {
            await _apiData.DocumentStore.CreateCollectionAsync(collection);
        }
        return NoSqlMessage.CreateSuccess(new { created, name = collection });
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
        const int MaxTake = 200;
        if (commandElement.TryGetProperty("document", out var docProp) &&
            docProp.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (docProp.TryGetProperty("skip", out var skipProp)) skip = skipProp.GetInt32();
            if (docProp.TryGetProperty("take", out var takeProp)) take = takeProp.GetInt32();
        }
        if (skip < 0) skip = 0;
        if (take <= 0 || take > MaxTake) take = 50;

        var all = (await _apiData.DocumentStore!.GetAllAsync(collection)).ToList();
        var page = all
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .Skip(skip).Take(take)
            .Select(FlattenDocument)
            .ToList();
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

    private NoSqlMessage HandleListUsersCommand()
        => NoSqlMessage.CreateSuccess(new
        {
            users = _authManager.ListUsers()
                .Select(u => new { username = u.Username, role = u.Role, createdAt = u.CreatedAt })
        });

    private NoSqlMessage HandleCreateUserCommand(System.Text.Json.JsonElement e)
    {
        var username = e.TryGetProperty("username", out var u) ? u.GetString() : null;
        var password = e.TryGetProperty("password", out var p) ? p.GetString() : null;
        var role = e.TryGetProperty("role", out var r) ? r.GetString() : UserRole.ReadWrite;
        if (string.IsNullOrWhiteSpace(username) || username.Length > 64)
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Username required (<= 64 chars)");
        if (string.IsNullOrEmpty(password) || password.Length < 6)
            return NoSqlMessage.CreateError("WEAK_PASSWORD", "Password must be at least 6 characters");
        if (!UserRole.IsValid(role))
            return NoSqlMessage.CreateError("INVALID_ROLE", $"Invalid role '{role}'");
        if (!_authManager.RegisterUser(username, password, role!))
            return NoSqlMessage.CreateError("USER_EXISTS", $"User '{username}' already exists");
        return NoSqlMessage.CreateSuccess(new { created = true, username });
    }

    private NoSqlMessage HandleDeleteUserCommand(System.Text.Json.JsonElement e)
    {
        var username = e.TryGetProperty("username", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(username))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Username required");
        return _authManager.RemoveUserGuarded(username) switch
        {
            UserOperationResult.Ok => NoSqlMessage.CreateSuccess(new { deleted = true }),
            UserOperationResult.NotFound => NoSqlMessage.CreateError("USER_NOT_FOUND", $"User '{username}' not found"),
            UserOperationResult.LastAdmin => NoSqlMessage.CreateError("LAST_ADMIN", "Cannot delete the last admin"),
            _ => NoSqlMessage.CreateError("COMMAND_ERROR", "Delete failed")
        };
    }

    private NoSqlMessage HandleSetPasswordCommand(System.Text.Json.JsonElement e)
    {
        var username = e.TryGetProperty("username", out var u) ? u.GetString() : null;
        var password = e.TryGetProperty("password", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(username))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Username required");
        if (string.IsNullOrEmpty(password) || password.Length < 6)
            return NoSqlMessage.CreateError("WEAK_PASSWORD", "Password must be at least 6 characters");
        return _authManager.SetPassword(username, password)
            ? NoSqlMessage.CreateSuccess(new { changed = true })
            : NoSqlMessage.CreateError("USER_NOT_FOUND", $"User '{username}' not found");
    }

    private NoSqlMessage HandleSetRoleCommand(System.Text.Json.JsonElement e)
    {
        var username = e.TryGetProperty("username", out var u) ? u.GetString() : null;
        var role = e.TryGetProperty("role", out var r) ? r.GetString() : null;
        if (string.IsNullOrWhiteSpace(username))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Username required");
        return _authManager.SetRoleGuarded(username, role ?? "") switch
        {
            UserOperationResult.Ok => NoSqlMessage.CreateSuccess(new { changed = true }),
            UserOperationResult.NotFound => NoSqlMessage.CreateError("USER_NOT_FOUND", $"User '{username}' not found"),
            UserOperationResult.InvalidRole => NoSqlMessage.CreateError("INVALID_ROLE", $"Invalid role '{role}'"),
            UserOperationResult.LastAdmin => NoSqlMessage.CreateError("LAST_ADMIN", "Cannot demote the last admin"),
            _ => NoSqlMessage.CreateError("COMMAND_ERROR", "Set role failed")
        };
    }

    private NoSqlMessage HandleChangePasswordCommand(System.Text.Json.JsonElement e, string connectionId)
    {
        if (!_authConnections.TryGetValue(connectionId, out var identity) || identity.Username == "anonymous")
            return NoSqlMessage.CreateError("AUTH_REQUIRED", "changepassword requires an authenticated user");
        var oldPw = e.TryGetProperty("oldPassword", out var o) ? o.GetString() : null;
        var newPw = e.TryGetProperty("newPassword", out var n) ? n.GetString() : null;
        if (string.IsNullOrEmpty(newPw) || newPw.Length < 6)
            return NoSqlMessage.CreateError("WEAK_PASSWORD", "Password must be at least 6 characters");
        return _authManager.ChangePassword(identity.Username, oldPw ?? "", newPw)
            ? NoSqlMessage.CreateSuccess(new { changed = true })
            : NoSqlMessage.CreateError("AUTH_FAILED", "Old password is incorrect");
    }

    private async Task<NoSqlMessage> HandleListAttachmentsCommand(System.Text.Json.JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        if (!e.TryGetProperty("collection", out var col) || !e.TryGetProperty("id", out var id) ||
            string.IsNullOrEmpty(col.GetString()) || string.IsNullOrEmpty(id.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id");
        var list = await _attachmentStore.ListAsync(col.GetString()!, id.GetString()!);
        return NoSqlMessage.CreateSuccess(new
        {
            attachments = list.Select(a => new { name = a.Name, contentType = a.ContentType, size = a.Size, hash = a.Hash, createdAt = a.CreatedAt, updatedAt = a.UpdatedAt })
        });
    }

    private async Task<NoSqlMessage> HandleAttachmentInfoCommand(System.Text.Json.JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        if (!e.TryGetProperty("collection", out var col) || !e.TryGetProperty("id", out var id) || !e.TryGetProperty("name", out var nm) ||
            string.IsNullOrEmpty(col.GetString()) || string.IsNullOrEmpty(id.GetString()) || string.IsNullOrEmpty(nm.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection, id or name");
        var info = await _attachmentStore.GetInfoAsync(col.GetString()!, id.GetString()!, nm.GetString()!);
        if (info == null) return NoSqlMessage.CreateSuccess(new { found = false, info = (object?)null });
        return NoSqlMessage.CreateSuccess(new { found = true, info = new { name = info.Name, contentType = info.ContentType, size = info.Size, hash = info.Hash, createdAt = info.CreatedAt, updatedAt = info.UpdatedAt } });
    }

    private async Task<NoSqlMessage> HandleUploadAttachmentCommand(System.Text.Json.JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        var collection = e.TryGetProperty("collection", out var col) ? col.GetString() : null;
        var id = e.TryGetProperty("id", out var idp) ? idp.GetString() : null;
        var name = e.TryGetProperty("name", out var nm) ? nm.GetString() : null;
        var contentType = e.TryGetProperty("contentType", out var ct) ? ct.GetString() : null;
        var b64 = e.TryGetProperty("contentBase64", out var cb) ? cb.GetString() : null;
        if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || b64 == null)
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection, id, name or contentBase64");
        if (name.Length > 255)
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Attachment name too long (max 255)");
        if (string.IsNullOrEmpty(contentType)) contentType = "application/octet-stream";

        byte[] content;
        try { content = Convert.FromBase64String(b64); }
        catch (FormatException) { return NoSqlMessage.CreateError("INVALID_CONTENT", "contentBase64 is not valid base64"); }

        int maxMb = _configManager.Configuration.MaxAttachmentSizeMB;
        long maxBytes = (long)Math.Max(maxMb, 1) * 1024 * 1024;
        if (content.Length > maxBytes)
            return NoSqlMessage.CreateError("ATTACHMENT_TOO_LARGE", $"Attachment exceeds {maxMb} MB limit");

        var result = await _attachmentStore.StoreAsync(collection, id, name, contentType, content);
        if (!result.Success)
        {
            var msg = result.ErrorMessage ?? "Upload failed";
            if (msg.Contains("not allowed")) return NoSqlMessage.CreateError("CONTENT_TYPE_BLOCKED", msg);
            return NoSqlMessage.CreateError("COMMAND_ERROR", msg);
        }
        return NoSqlMessage.CreateSuccess(new { stored = true, name = result.Info!.Name, hash = result.Info.Hash, size = result.Info.Size });
    }

    private async Task<NoSqlMessage> HandleDownloadAttachmentCommand(System.Text.Json.JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        if (!e.TryGetProperty("collection", out var col) || !e.TryGetProperty("id", out var id) || !e.TryGetProperty("name", out var nm) ||
            string.IsNullOrEmpty(col.GetString()) || string.IsNullOrEmpty(id.GetString()) || string.IsNullOrEmpty(nm.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection, id or name");
        var att = await _attachmentStore.GetAsync(col.GetString()!, id.GetString()!, nm.GetString()!);
        if (att == null) return NoSqlMessage.CreateSuccess(new { found = false });
        return NoSqlMessage.CreateSuccess(new { found = true, name = att.Name, contentType = att.ContentType, size = att.Size, contentBase64 = Convert.ToBase64String(att.Content) });
    }

    private async Task<NoSqlMessage> HandleDeleteAttachmentCommand(System.Text.Json.JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        if (!e.TryGetProperty("collection", out var col) || !e.TryGetProperty("id", out var id) || !e.TryGetProperty("name", out var nm) ||
            string.IsNullOrEmpty(col.GetString()) || string.IsNullOrEmpty(id.GetString()) || string.IsNullOrEmpty(nm.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection, id or name");
        var deleted = await _attachmentStore.DeleteAsync(col.GetString()!, id.GetString()!, nm.GetString()!);
        return NoSqlMessage.CreateSuccess(new { deleted });
    }

    private async Task<NoSqlMessage> HandleTotalStorageCommand()
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        var bytes = await _attachmentStore.GetTotalStorageSizeAsync();
        return NoSqlMessage.CreateSuccess(new { bytes });
    }

    private Task<NoSqlMessage> HandleBulkOperationAsync(NoSqlMessage message, string connectionId)
    {
        // Bulk operations are not implemented in this host — fail honestly instead of
        // pretending the batch was applied. Same auth gate as regular commands.
        if (_configManager.Configuration.RequireAuthentication && !_authConnections.ContainsKey(connectionId))
        {
            return Task.FromResult(NoSqlMessage.CreateError("AUTH_REQUIRED", "Authenticate before sending bulk operations"));
        }
        return Task.FromResult(NoSqlMessage.CreateError("UNSUPPORTED", "Bulk operations are not supported by this server"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _attachmentStore?.Dispose();
        _attachmentStore = null;
        _tcpServer?.Dispose();
    }
}
