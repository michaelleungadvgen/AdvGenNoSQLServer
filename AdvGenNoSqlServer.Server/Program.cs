using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AdvGenNoSqlServer.Core.Caching;
using AdvGenNoSqlServer.Core.MemoryManagement;
using AdvGenNoSqlServer.Core.Metrics;
using AdvGenNoSqlServer.Storage.Storage;
using AdvGenNoSqlServer.Core.Configuration;

namespace AdvGenNoSqlServer.Server;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Register services
        ConfigureServices(builder.Services);

        using var host = builder.Build();

        // Start the server
        await host.RunAsync();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Add configuration
        services.AddSingleton<IConfigurationManager, ConfigurationManager>();

        // Add caching with configuration
        services.AddSingleton<ICacheManager>(provider =>
        {
            var configManager = provider.GetRequiredService<IConfigurationManager>();
            var config = configManager.Configuration;
            long maxSizeInBytes = config.MaxCacheSizeInBytes > 0 ? config.MaxCacheSizeInBytes : 104857600;
            var engineConfig = new MemoryManagementConfiguration
            {
                DefaultTtlSeconds = config.DefaultCacheTtlMilliseconds > 0
                    ? (int)(config.DefaultCacheTtlMilliseconds / 1000)
                    : 1800
            };
            var engine = new ManagedMemoryStorageEngine(engineConfig, maxSizeInBytes);
            return new AdvancedMemoryCacheManager(engine, new NoOpMetricsCollector());
        });

        // Add file storage with configuration
        services.AddSingleton<IStorageManager>(provider =>
        {
            var configManager = provider.GetRequiredService<IConfigurationManager>();
            var cacheTimeout = TimeSpan.FromMinutes(configManager.Configuration.CacheTimeoutMinutes);
            return new AdvancedFileStorageManager(configManager.Configuration.StoragePath, cacheTimeout);
        });

        // Add server service
        services.AddHostedService<NoSqlServer>();
    }
}