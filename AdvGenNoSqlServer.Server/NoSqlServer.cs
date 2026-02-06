using AdvGenNoSqlServer.Core.Caching;
using AdvGenNoSqlServer.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AdvGenNoSqlServer.Server;

public class NoSqlServer : IHostedService
{
    private readonly ILogger<NoSqlServer> _logger;
    private readonly ICacheManager _cacheManager;
    private readonly IConfigurationManager _configurationManager;

    public NoSqlServer(ILogger<NoSqlServer> logger, ICacheManager cacheManager, IConfigurationManager configurationManager)
    {
        _logger = logger;
        _cacheManager = cacheManager;
        _configurationManager = configurationManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting NoSQL Server on port {Port}...", _configurationManager.Configuration.Port);
        _logger.LogInformation("Cache size limit: {MaxCacheSize}", _configurationManager.Configuration.MaxCacheSize);
        _logger.LogInformation("Storage path: {StoragePath}", _configurationManager.Configuration.StoragePath);
        
        // TODO: Initialize server components
        // - Start listening on network ports
        // - Initialize storage systems
        // - Load configuration
        
        _logger.LogInformation("NoSQL Server started successfully");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping NoSQL Server...");
        
        // TODO: Clean up resources
        // - Close network connections
        // - Flush cache to persistent storage if needed
        // - Dispose of resources
        
        _logger.LogInformation("NoSQL Server stopped successfully");
        return Task.CompletedTask;
    }
}