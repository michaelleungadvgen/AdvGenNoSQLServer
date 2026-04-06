using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        var builder = Host.CreateApplicationBuilder(args);
        ConfigureServices(builder.Services, builder.Configuration);
        using var host = builder.Build();
        await host.RunAsync();
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
        services.AddSingleton<ICacheManager, AdvancedMemoryCacheManager>();

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
