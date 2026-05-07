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

        // Read TCP port the same way CoreConfigurationManager does (flat "port" key in appsettings.json)
        var tcpPort = builder.Configuration.GetValue<int>("port",
                          builder.Configuration.GetValue<int>("Port", 9091));
        var httpPort = tcpPort + 1;
        Console.WriteLine($"[HTTP API] TCP port: {tcpPort}, HTTP API port: {httpPort}");
        builder.WebHost.UseUrls($"http://0.0.0.0:{httpPort}");

        // CORS for Blazor WASM admin
        builder.Services.AddCors();

        // ApiDataService bridges the hosted service's live state to HTTP endpoints
        builder.Services.AddSingleton<ApiDataService>();

        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();

        app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

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

        // Database endpoints — this server is single-database; expose collections as the "default" database
        app.MapGet("/api/databases", (ApiDataService data) =>
            Results.Ok(new[] { "default" }));

        app.MapPost("/api/databases/{name}", (string name, ApiDataService data) =>
            Results.Ok(new { created = true, name }));

        app.MapDelete("/api/databases/{name}", (string name, ApiDataService data) =>
            Results.Ok(new { dropped = true, name }));

        app.MapPost("/api/databases/{name}/select", (string name, ApiDataService data) =>
            Results.Ok(new { selected = true, name }));

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
        // Add configuration
        services.AddSingleton<CoreIConfigurationManager, CoreConfigurationManager>();

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
