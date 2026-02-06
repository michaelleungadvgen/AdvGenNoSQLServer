using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AdvGenNoSqlServer.Core.Caching;
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
        services.AddMemoryCache();
        services.AddSingleton<ICacheManager>(provider =>
        {
            var configManager = provider.GetRequiredService<IConfigurationManager>();
            var memoryCache = provider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            return new AdvancedMemoryCacheManager(memoryCache, configManager.Configuration.MaxCacheSize);
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