// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using Microsoft.Extensions.DependencyInjection;
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Profiling;

namespace AdvGenNoSqlServer.Core.Health;

/// <summary>
/// Extension methods for health check registration.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds health check services to the service collection.
    /// </summary>
    public static IServiceCollection AddHealthChecks(this IServiceCollection services)
    {
        services.AddSingleton<IHealthCheckRegistry, HealthCheckRegistry>();
        return services;
    }

    /// <summary>
    /// Adds a custom health check.
    /// </summary>
    public static IServiceCollection AddHealthCheck<T>(this IServiceCollection services, string name, 
        HealthCheckOptions? options = null) where T : class, IHealthCheck
    {
        services.AddSingleton<IHealthCheck, T>();
        
        services.Configure<HealthCheckRegistry>(registry =>
        {
            var healthCheck = registry.GetType()
                .GetMethod("GetService", new[] { typeof(Type) })
                ?.Invoke(registry, new[] { typeof(T) }) as T;
            
            if (healthCheck != null)
            {
                registry.Register(healthCheck, options);
            }
        });
        
        return services;
    }

    /// <summary>
    /// Adds the storage health check.
    /// </summary>
    public static IHealthCheckRegistry AddStorageHealthCheck(this IHealthCheckRegistry registry, 
        IDocumentStore documentStore, HealthCheckOptions? options = null)
    {
        var healthCheck = new StorageHealthCheck(documentStore);
        registry.Register(healthCheck, options);
        return registry;
    }

    /// <summary>
    /// Adds the memory health check.
    /// </summary>
    public static IHealthCheckRegistry AddMemoryHealthCheck(this IHealthCheckRegistry registry, 
        IMemoryProfiler? memoryProfiler = null, HealthCheckOptions? options = null)
    {
        var healthCheck = new MemoryHealthCheck(memoryProfiler);
        registry.Register(healthCheck, options);
        return registry;
    }

    /// <summary>
    /// Adds the disk health check.
    /// </summary>
    public static IHealthCheckRegistry AddDiskHealthCheck(this IHealthCheckRegistry registry, 
        string path = ".", HealthCheckOptions? options = null)
    {
        var healthCheck = new DiskHealthCheck(path);
        registry.Register(healthCheck, options);
        return registry;
    }

    /// <summary>
    /// Adds the network health check.
    /// </summary>
    public static IHealthCheckRegistry AddNetworkHealthCheck(this IHealthCheckRegistry registry, 
        List<string>? endpointsToCheck = null, HealthCheckOptions? options = null)
    {
        var healthCheck = new NetworkHealthCheck(endpointsToCheck);
        registry.Register(healthCheck, options);
        return registry;
    }

    /// <summary>
    /// Adds the liveness health check.
    /// </summary>
    public static IHealthCheckRegistry AddLivenessHealthCheck(this IHealthCheckRegistry registry, 
        HealthCheckOptions? options = null)
    {
        var healthCheck = new LivenessHealthCheck();
        registry.Register(healthCheck, options);
        return registry;
    }

    /// <summary>
    /// Adds a custom delegate health check.
    /// </summary>
    public static IHealthCheckRegistry AddDelegateHealthCheck(this IHealthCheckRegistry registry, 
        string name, Func<CancellationToken, Task<HealthCheckResult>> checkFunc, HealthCheckOptions? options = null)
    {
        registry.Register(name, checkFunc, options);
        return registry;
    }

    /// <summary>
    /// Adds default health checks (liveness, memory, disk).
    /// </summary>
    public static IHealthCheckRegistry AddDefaultHealthChecks(this IHealthCheckRegistry registry, 
        IMemoryProfiler? memoryProfiler = null, string? diskPath = null)
    {
        registry.AddLivenessHealthCheck();
        registry.AddMemoryHealthCheck(memoryProfiler);
        registry.AddDiskHealthCheck(diskPath ?? ".");
        return registry;
    }
}

/// <summary>
/// Health check service for easy integration with the server.
/// </summary>
public class HealthCheckService
{
    private readonly IHealthCheckRegistry _registry;

    /// <summary>
    /// Gets the health check registry.
    /// </summary>
    public IHealthCheckRegistry Registry => _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthCheckService"/> class.
    /// </summary>
    public HealthCheckService(IHealthCheckRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Gets a liveness health report (lightweight - just checks if process is running).
    /// </summary>
    public async Task<HealthReport> GetLivenessReportAsync(CancellationToken cancellationToken = default)
    {
        // Run only liveness and lightweight checks
        var livenessCheck = _registry.Get("liveness");
        if (livenessCheck != null)
        {
            var result = await _registry.RunByNameAsync("liveness", cancellationToken);
            var results = new Dictionary<string, HealthCheckResult>();
            if (result != null)
            {
                results["liveness"] = result;
            }
            return new HealthReport(result?.Status ?? HealthStatus.Healthy, results, TimeSpan.Zero);
        }

        // Fallback: just return healthy
        return HealthReport.Empty();
    }

    /// <summary>
    /// Gets a readiness health report (checks if server can accept traffic).
    /// </summary>
    public async Task<HealthReport> GetReadinessReportAsync(CancellationToken cancellationToken = default)
    {
        // Run core checks: storage, memory
        var coreChecks = _registry.GetByTag("core");
        var results = new Dictionary<string, HealthCheckResult>();
        
        foreach (var check in coreChecks)
        {
            var result = await _registry.RunByNameAsync(check.Name, cancellationToken);
            if (result != null)
            {
                results[check.Name] = result;
            }
        }

        var overallStatus = CalculateOverallStatus(results.Values);
        return new HealthReport(overallStatus, results, TimeSpan.Zero);
    }

    /// <summary>
    /// Gets a startup health report (checks if initialization is complete).
    /// </summary>
    public async Task<HealthReport> GetStartupReportAsync(CancellationToken cancellationToken = default)
    {
        // Similar to readiness but can include additional startup checks
        return await GetReadinessReportAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a detailed health report with all checks.
    /// </summary>
    public Task<HealthReport> GetDetailedReportAsync(CancellationToken cancellationToken = default)
    {
        return _registry.RunAllAsync(cancellationToken);
    }

    /// <summary>
    /// Calculates the overall health status from individual results.
    /// </summary>
    private static HealthStatus CalculateOverallStatus(IEnumerable<HealthCheckResult> results)
    {
        var resultsList = results.ToList();
        if (resultsList.Count == 0)
            return HealthStatus.Healthy;

        if (resultsList.Any(r => r.Status == HealthStatus.Unhealthy))
            return HealthStatus.Unhealthy;

        if (resultsList.Any(r => r.Status == HealthStatus.Degraded))
            return HealthStatus.Degraded;

        return HealthStatus.Healthy;
    }
}
