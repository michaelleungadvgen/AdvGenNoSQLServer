// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Caching;
using AdvGenNoSqlServer.Core.MemoryManagement;
using AdvGenNoSqlServer.Core.Metrics;
using AdvGenNoSqlServer.Storage.Storage;
using CoreIConfigurationManager = AdvGenNoSqlServer.Core.Configuration.IConfigurationManager;
using CoreConfigurationManager = AdvGenNoSqlServer.Core.Configuration.ConfigurationManager;

namespace AdvGenNoSqlServer.Server;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Load + validate server configuration early. Production fails fast on
        // invalid config; other environments log warnings and continue.
        var configManager = new CoreConfigurationManager("appsettings.json");
        var serverConfig = configManager.Configuration;
        var configErrors = configManager.Validate().ToList();
        if (configManager.IsProduction && string.IsNullOrEmpty(serverConfig.AdminApiKey))
            configErrors.Add("Production requires AdminApiKey for the HTTP admin API (set NOSQL_ADMIN_API_KEY).");
        foreach (var configError in configErrors)
            Console.Error.WriteLine($"[CONFIG] {(configManager.IsProduction ? "ERROR" : "WARNING")}: {configError}");
        if (configErrors.Count > 0 && configManager.IsProduction)
        {
            Console.Error.WriteLine("[CONFIG] Fatal configuration errors in Production. Shutting down.");
            Environment.Exit(1);
        }

        if (string.IsNullOrEmpty(serverConfig.AdminApiKey))
            Console.WriteLine("[SECURITY] WARNING: AdminApiKey is not set — the HTTP admin API is UNAUTHENTICATED. Set NOSQL_ADMIN_API_KEY (required in Production).");

        if (!serverConfig.RequireAuthentication)
            Console.WriteLine($"[SECURITY] WARNING: Authentication is DISABLED — anonymous connections get role '{serverConfig.AnonymousRole}'. Do not run like this in production.");

        // Load the TLS certificate once at startup: a bad/missing cert must fail fast
        // (in Production) instead of failing every client handshake later.
        if (serverConfig.EnableSsl)
        {
            try
            {
                using var probe = AdvGenNoSqlServer.Network.TlsStreamHelper.LoadCertificate(serverConfig);
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

        // Read TCP port the same way CoreConfigurationManager does (flat "port" key in appsettings.json)
        var tcpPort = serverConfig.Port;
        var httpPort = tcpPort + 1;
        Console.WriteLine($"[HTTP API] TCP port: {tcpPort}, HTTP API port: {httpPort}");
        builder.WebHost.UseUrls($"http://0.0.0.0:{httpPort}");

        // CORS (locked to configured origins in Production, permissive otherwise)
        builder.Services.AddCors();

        // Share the validated configuration manager
        builder.Services.AddSingleton<CoreIConfigurationManager>(configManager);

        // ApiDataService bridges the hosted service's live state to HTTP endpoints
        builder.Services.AddSingleton<ApiDataService>();

        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();

        if (configManager.IsProduction)
            app.UseCors(policy => policy.WithOrigins(serverConfig.CorsAllowedOrigins).AllowAnyHeader().AllowAnyMethod());
        else
            app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

        // API-key gate for the admin HTTP API. When no key is configured the API is
        // open (Development convenience — Production validation above forbids that).
        app.Use(async (context, next) =>
        {
            var apiKey = configManager.Configuration.AdminApiKey;
            if (!string.IsNullOrEmpty(apiKey) && context.Request.Path.StartsWithSegments("/api"))
            {
                if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) || provided != apiKey)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing API key" });
                    return;
                }
            }
            await next();
        });

        // --- REST API endpoints ---

        app.MapGet("/api/stats", async (ApiDataService data) =>
        {
            var uptime = DateTime.UtcNow - data.StartTime;
            var memMB = (int)(GC.GetTotalMemory(false) / 1_048_576);
            long totalDocs = 0;
            int totalCols = 0;
            if (data.DocumentStore != null)
            {
                var cols = (await data.DocumentStore.GetCollectionsAsync()).ToList();
                totalCols = cols.Count;
                foreach (var c in cols) totalDocs += await data.DocumentStore.CountAsync(c);
            }
            return Results.Ok(new
            {
                version = "1.0.0",
                uptimeSeconds = (long)uptime.TotalSeconds,
                memoryUsageMB = memMB,
                totalDocuments = totalDocs,
                totalCollections = totalCols,
                activeConnections = data.TcpServer?.ActiveConnectionCount ?? 0
            });
        });

        // Database endpoints — this server is single-database; the collection set of the
        // "default" database is all there is. Create/delete/select are honest no-ops.
        app.MapGet("/api/databases", (ApiDataService data) =>
            Results.Ok(new[] { "default" }));

        app.MapPost("/api/databases/{name}", (string name, ApiDataService data) =>
            Results.Json(new { success = false, error = "This server runs a single default database; multi-database is only available in the Host application" }, statusCode: 501));

        app.MapDelete("/api/databases/{name}", (string name, ApiDataService data) =>
            Results.Json(new { success = false, error = "This server runs a single default database; multi-database is only available in the Host application" }, statusCode: 501));

        app.MapPost("/api/databases/{name}/select", (string name, ApiDataService data) =>
            Results.Json(new { success = false, error = "This server runs a single default database; multi-database is only available in the Host application" }, statusCode: 501));

        app.MapGet("/api/collections", async (ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.Ok(Array.Empty<string>());
            var cols = await data.DocumentStore.GetCollectionsAsync();
            return Results.Ok(cols);
        });

        app.MapPost("/api/collections/{name}", async (string name, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.Problem("Server not ready");
            await data.DocumentStore.CreateCollectionAsync(name);
            return Results.Ok(new { created = true, name });
        });

        app.MapDelete("/api/collections/{name}", async (string name, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.Problem("Server not ready");
            var dropped = await data.DocumentStore.DropCollectionAsync(name);
            return Results.Ok(new { dropped, name });
        });

        app.MapGet("/api/collections/{name}/count", async (string name, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.Ok(new { count = 0L });
            var count = await data.DocumentStore.CountAsync(name);
            return Results.Ok(new { count });
        });

        app.MapGet("/api/collections/{name}/documents", async (string name, int skip, int take, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.Ok(Array.Empty<object>());
            var all = await data.DocumentStore.GetAllAsync(name);
            var page = all.Skip(skip).Take(take > 0 ? take : 50);
            return Results.Ok(page);
        });

        app.MapGet("/api/collections/{name}/documents/{id}", async (string name, string id, ApiDataService data) =>
        {
            if (data.DocumentStore == null) return Results.NotFound();
            var doc = await data.DocumentStore.GetAsync(name, id);
            return doc == null ? Results.NotFound() : Results.Ok(doc);
        });

        await app.RunAsync();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Configuration manager is registered in Main (shared, validated instance)

        // Add metrics (no-op by default)
        services.AddSingleton<IMetricsCollector, NoOpMetricsCollector>();

        // Bind memory management config and register the selected engine
        var memConfig = configuration
            .GetSection("MemoryManagement")
            .Get<MemoryManagementConfiguration>()
            ?? new MemoryManagementConfiguration();
        services.AddMemoryEngine(memConfig);
        services.AddSingleton<ICacheManager>(provider =>
        {
            var engine = provider.GetRequiredService<IMemoryStorageEngine>();
            var metrics = provider.GetRequiredService<IMetricsCollector>();
            return new AdvancedMemoryCacheManager(engine, metrics);
        });

        // Add file storage with configuration
        services.AddSingleton<IStorageManager>(provider =>
        {
            var configManager = provider.GetRequiredService<CoreIConfigurationManager>();
            var cacheTimeout = TimeSpan.FromMinutes(configManager.Configuration.CacheTimeoutMinutes);
            return new AdvancedFileStorageManager(configManager.Configuration.StoragePath, cacheTimeout);
        });

        // Add server service
        services.AddHostedService<NoSqlServer>();
    }
}
