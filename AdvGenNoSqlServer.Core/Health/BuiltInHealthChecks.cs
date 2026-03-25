// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Profiling;
using AdvGenNoSqlServer.Core.Abstractions;
using System.Diagnostics;

namespace AdvGenNoSqlServer.Core.Health;

/// <summary>
/// Health check for storage engine.
/// </summary>
public class StorageHealthCheck : IHealthCheck
{
    private readonly IDocumentStore _documentStore;

    /// <summary>
    /// Gets the name of the health check.
    /// </summary>
    public string Name => "storage";

    /// <summary>
    /// Gets the tags associated with the health check.
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; } = new[] { "storage", "core" };

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageHealthCheck"/> class.
    /// </summary>
    public StorageHealthCheck(IDocumentStore documentStore)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
    }

    /// <summary>
    /// Performs the storage health check.
    /// </summary>
    public async Task<HealthCheckResult> CheckAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get collection list to verify storage is accessible
            var collections = await _documentStore.GetCollectionsAsync(cancellationToken);
            var collectionsList = collections.ToList();
            
            var data = new Dictionary<string, object>();
            data.Add("collectionCount", collectionsList.Count);
            data.Add("collections", collectionsList.Take(10).ToList());

            return HealthCheckResult.Healthy("Storage is accessible and functioning normally.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Storage health check failed.", ex);
        }
    }
}

/// <summary>
/// Health check for memory usage.
/// </summary>
public class MemoryHealthCheck : IHealthCheck
{
    private readonly IMemoryProfiler? _memoryProfiler;
    private readonly double _warningThresholdPercent;
    private readonly double _criticalThresholdPercent;

    /// <summary>
    /// Gets the name of the health check.
    /// </summary>
    public string Name => "memory";

    /// <summary>
    /// Gets the tags associated with the health check.
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; } = new[] { "memory", "system", "core" };

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryHealthCheck"/> class.
    /// </summary>
    public MemoryHealthCheck(IMemoryProfiler? memoryProfiler = null, 
        double warningThresholdPercent = 80, 
        double criticalThresholdPercent = 95)
    {
        _memoryProfiler = memoryProfiler;
        _warningThresholdPercent = warningThresholdPercent;
        _criticalThresholdPercent = criticalThresholdPercent;
    }

    /// <summary>
    /// Performs the memory health check.
    /// </summary>
    public Task<HealthCheckResult> CheckAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            var totalMemory = GC.GetTotalMemory(false);
            var totalMemoryMb = totalMemory / (1024 * 1024);
            var heapSizeBytes = gcInfo.HeapSizeBytes;
            var heapSizeMb = heapSizeBytes / (1024 * 1024);
            var memoryLoadPercent = gcInfo.MemoryLoadBytes * 100.0 / gcInfo.TotalAvailableMemoryBytes;

            var data = new Dictionary<string, object>
            {
                ["totalMemoryBytes"] = totalMemory,
                ["totalMemoryMB"] = totalMemoryMb,
                ["heapSizeBytes"] = heapSizeBytes,
                ["heapSizeMB"] = heapSizeMb,
                ["memoryLoadPercent"] = Math.Round(memoryLoadPercent, 2),
                ["gen0Collections"] = GC.CollectionCount(0),
                ["gen1Collections"] = GC.CollectionCount(1),
                ["gen2Collections"] = GC.CollectionCount(2)
            };

            // Add memory profiler data if available
            if (_memoryProfiler != null)
            {
                var snapshot = _memoryProfiler.GetSnapshot();
                data["managedMemoryMB"] = snapshot.TotalManagedMemory / (1024 * 1024);
                data["lohSizeMB"] = snapshot.LargeObjectHeapSize / (1024 * 1024);
            }

            if (memoryLoadPercent >= _criticalThresholdPercent)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Memory usage is critical: {memoryLoadPercent:F1}% (threshold: {_criticalThresholdPercent:F1}%)", 
                    data: data));
            }

            if (memoryLoadPercent >= _warningThresholdPercent)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Memory usage is high: {memoryLoadPercent:F1}% (threshold: {_warningThresholdPercent:F1}%)", 
                    data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Memory usage is normal: {memoryLoadPercent:F1}%", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Memory health check failed.", ex));
        }
    }
}

/// <summary>
/// Health check for disk space.
/// </summary>
public class DiskHealthCheck : IHealthCheck
{
    private readonly string _path;
    private readonly double _warningThresholdPercent;
    private readonly double _criticalThresholdPercent;

    /// <summary>
    /// Gets the name of the health check.
    /// </summary>
    public string Name => "disk";

    /// <summary>
    /// Gets the tags associated with the health check.
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; } = new[] { "disk", "system", "storage" };

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskHealthCheck"/> class.
    /// </summary>
    public DiskHealthCheck(string path = ".", 
        double warningThresholdPercent = 85, 
        double criticalThresholdPercent = 95)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _warningThresholdPercent = warningThresholdPercent;
        _criticalThresholdPercent = criticalThresholdPercent;
    }

    /// <summary>
    /// Performs the disk health check.
    /// </summary>
    public Task<HealthCheckResult> CheckAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var driveInfo = new DriveInfo(Path.GetPathRoot(_path) ?? _path);
            
            if (!driveInfo.IsReady)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy($"Drive '{driveInfo.Name}' is not ready."));
            }

            var totalSizeBytes = driveInfo.TotalSize;
            var availableBytes = driveInfo.AvailableFreeSpace;
            var usedBytes = totalSizeBytes - availableBytes;
            var usedPercent = (double)usedBytes / totalSizeBytes * 100;

            var totalSizeGb = totalSizeBytes / (1024.0 * 1024 * 1024);
            var availableGb = availableBytes / (1024.0 * 1024 * 1024);

            var data = new Dictionary<string, object>
            {
                ["driveName"] = driveInfo.Name,
                ["driveFormat"] = driveInfo.DriveFormat,
                ["totalSizeGB"] = Math.Round(totalSizeGb, 2),
                ["availableGB"] = Math.Round(availableGb, 2),
                ["usedPercent"] = Math.Round(usedPercent, 2),
                ["isReady"] = driveInfo.IsReady
            };

            if (usedPercent >= _criticalThresholdPercent)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Disk usage is critical: {usedPercent:F1}% full (threshold: {_criticalThresholdPercent:F1}%)", 
                    data: data));
            }

            if (usedPercent >= _warningThresholdPercent)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Disk usage is high: {usedPercent:F1}% full (threshold: {_warningThresholdPercent:F1}%)", 
                    data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Disk usage is normal: {usedPercent:F1}% full", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Disk health check failed.", ex));
        }
    }
}

/// <summary>
/// Health check for network connectivity.
/// </summary>
public class NetworkHealthCheck : IHealthCheck
{
    private readonly List<string>? _endpointsToCheck;

    /// <summary>
    /// Gets the name of the health check.
    /// </summary>
    public string Name => "network";

    /// <summary>
    /// Gets the tags associated with the health check.
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; } = new[] { "network", "system" };

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkHealthCheck"/> class.
    /// </summary>
    public NetworkHealthCheck(List<string>? endpointsToCheck = null)
    {
        _endpointsToCheck = endpointsToCheck;
    }

    /// <summary>
    /// Performs the network health check.
    /// </summary>
    public async Task<HealthCheckResult> CheckAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var data = new Dictionary<string, object>();
            var issues = new List<string>();

            // Check network availability
            var isNetworkAvailable = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            data["isNetworkAvailable"] = isNetworkAvailable;

            if (!isNetworkAvailable)
            {
                return HealthCheckResult.Unhealthy("Network is not available.", data: data);
            }

            // Check specific endpoints if provided
            if (_endpointsToCheck != null && _endpointsToCheck.Count > 0)
            {
                var endpointResults = new Dictionary<string, bool>();
                foreach (var endpoint in _endpointsToCheck)
                {
                    try
                    {
                        using var client = new System.Net.Http.HttpClient();
                        client.Timeout = TimeSpan.FromSeconds(5);
                        var response = await client.GetAsync(endpoint, cancellationToken);
                        endpointResults[endpoint] = response.IsSuccessStatusCode;
                        if (!response.IsSuccessStatusCode)
                        {
                            issues.Add($"Endpoint '{endpoint}' returned status {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        endpointResults[endpoint] = false;
                        issues.Add($"Endpoint '{endpoint}' failed: {ex.Message}");
                    }
                }
                data["endpoints"] = endpointResults;
            }

            // Get network interface info
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .Select(ni => new { ni.Name, ni.NetworkInterfaceType, ni.Speed })
                .ToList();
            data["activeInterfaces"] = interfaces.Count;
            data["interfaces"] = interfaces.Select(i => i.Name).ToList();

            if (issues.Count > 0)
            {
                return HealthCheckResult.Degraded(
                    $"Network is available but some issues detected: {string.Join(", ", issues)}",
                    data: data);
            }

            return HealthCheckResult.Healthy("Network is available and functioning normally.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Network health check failed.", ex);
        }
    }
}

/// <summary>
/// A simple liveness health check that always returns healthy.
/// </summary>
public class LivenessHealthCheck : IHealthCheck
{
    /// <summary>
    /// Gets the name of the health check.
    /// </summary>
    public string Name => "liveness";

    /// <summary>
    /// Gets the tags associated with the health check.
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; } = new[] { "liveness", "core" };

    /// <summary>
    /// Performs the liveness health check.
    /// </summary>
    public Task<HealthCheckResult> CheckAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["processId"] = Environment.ProcessId,
            ["uptime"] = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
            ["timestamp"] = DateTime.UtcNow
        };

        return Task.FromResult(HealthCheckResult.Healthy("Process is live.", data));
    }
}

/// <summary>
/// Health check that aggregates the results of multiple health checks.
/// </summary>
public class CompositeHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IHealthCheck> _healthChecks;

    /// <summary>
    /// Gets the name of the health check.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the tags associated with the health check.
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeHealthCheck"/> class.
    /// </summary>
    public CompositeHealthCheck(string name, IEnumerable<IHealthCheck> healthChecks, IEnumerable<string>? tags = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _healthChecks = healthChecks ?? throw new ArgumentNullException(nameof(healthChecks));
        Tags = tags?.ToList().AsReadOnly() ?? new List<string>().AsReadOnly();
    }

    /// <summary>
    /// Performs the composite health check.
    /// </summary>
    public async Task<HealthCheckResult> CheckAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, HealthCheckResult>();
        var tasks = _healthChecks.Select(async hc =>
        {
            var result = await hc.CheckAsync(context, cancellationToken);
            results[hc.Name] = result;
        });

        await Task.WhenAll(tasks);

        var overallStatus = CalculateOverallStatus(results.Values);
        var data = new Dictionary<string, object>
        {
            ["checks"] = results.ToDictionary(r => r.Key, r => new
            {
                status = r.Value.Status.ToString(),
                description = r.Value.Description,
                data = r.Value.Data
            })
        };

        var description = overallStatus switch
        {
            HealthStatus.Healthy => "All composite health checks passed.",
            HealthStatus.Degraded => "Some composite health checks are degraded.",
            HealthStatus.Unhealthy => "One or more composite health checks failed.",
            _ => "Unknown health status."
        };

        return overallStatus switch
        {
            HealthStatus.Healthy => HealthCheckResult.Healthy(description, data),
            HealthStatus.Degraded => HealthCheckResult.Degraded(description, data, null),
            HealthStatus.Unhealthy => HealthCheckResult.Unhealthy(description, null, data),
            _ => HealthCheckResult.Healthy(description, data)
        };
    }

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
