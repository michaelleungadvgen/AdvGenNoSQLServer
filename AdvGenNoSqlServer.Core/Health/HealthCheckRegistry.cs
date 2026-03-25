// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace AdvGenNoSqlServer.Core.Health;

/// <summary>
/// Thread-safe registry for managing and executing health checks.
/// </summary>
public class HealthCheckRegistry : IHealthCheckRegistry, IDisposable
{
    private readonly ConcurrentDictionary<string, HealthCheckRegistration> _registrations = new();
    private readonly ConcurrentDictionary<string, (HealthCheckResult result, DateTime timestamp)> _cache = new();
    private readonly ConcurrentDictionary<string, int> _failureCounts = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Event raised when a health check result changes.
    /// </summary>
    public event EventHandler<HealthCheckResultChangedEventArgs>? ResultChanged;

    /// <summary>
    /// Registers a health check.
    /// </summary>
    public void Register(IHealthCheck healthCheck, HealthCheckOptions? options = null)
    {
        if (healthCheck == null)
            throw new ArgumentNullException(nameof(healthCheck));

        var name = healthCheck.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Health check name cannot be empty.", nameof(healthCheck));

        options ??= new HealthCheckOptions { Name = name };
        options.Name = name;
        options.Validate();

        var registration = new HealthCheckRegistration(name, healthCheck, options);

        if (!_registrations.TryAdd(name, registration))
            throw new InvalidOperationException($"A health check with the name '{name}' is already registered.");
    }

    /// <summary>
    /// Registers a health check with a name and factory function.
    /// </summary>
    public void Register(string name, Func<CancellationToken, Task<HealthCheckResult>> checkFunc, HealthCheckOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        if (checkFunc == null)
            throw new ArgumentNullException(nameof(checkFunc));

        var healthCheck = new DelegateHealthCheck(name, checkFunc);
        Register(healthCheck, options);
    }

    /// <summary>
    /// Unregisters a health check by name.
    /// </summary>
    public bool Unregister(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        var removed = _registrations.TryRemove(name, out _);
        if (removed)
        {
            _cache.TryRemove(name, out _);
            _failureCounts.TryRemove(name, out _);
        }

        return removed;
    }

    /// <summary>
    /// Gets all registered health checks.
    /// </summary>
    public IReadOnlyCollection<HealthCheckRegistration> GetAll()
    {
        return _registrations.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets health checks by tag.
    /// </summary>
    public IReadOnlyCollection<HealthCheckRegistration> GetByTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            throw new ArgumentNullException(nameof(tag));

        return _registrations.Values
            .Where(r => r.HealthCheck.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Checks if a health check is registered.
    /// </summary>
    public bool IsRegistered(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        return _registrations.ContainsKey(name);
    }

    /// <summary>
    /// Gets a specific health check by name.
    /// </summary>
    public HealthCheckRegistration? Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        _registrations.TryGetValue(name, out var registration);
        return registration;
    }

    /// <summary>
    /// Clears all registered health checks.
    /// </summary>
    public void Clear()
    {
        _registrations.Clear();
        _cache.Clear();
        _failureCounts.Clear();
    }

    /// <summary>
    /// Runs all registered health checks and returns a health report.
    /// </summary>
    public async Task<HealthReport> RunAllAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        var registrations = GetAll();
        return await RunChecksAsync(registrations, cancellationToken);
    }

    /// <summary>
    /// Runs health checks filtered by tag.
    /// </summary>
    public async Task<HealthReport> RunByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        var registrations = GetByTag(tag);
        return await RunChecksAsync(registrations, cancellationToken);
    }

    /// <summary>
    /// Runs a specific health check by name.
    /// </summary>
    public async Task<HealthCheckResult?> RunByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        var registration = Get(name);
        if (registration == null)
            return null;

        return await ExecuteHealthCheckAsync(registration, cancellationToken);
    }

    /// <summary>
    /// Executes a collection of health checks and aggregates the results.
    /// </summary>
    private async Task<HealthReport> RunChecksAsync(IEnumerable<HealthCheckRegistration> registrations, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new ConcurrentDictionary<string, HealthCheckResult>();

        var tasks = registrations.Select(async registration =>
        {
            var result = await ExecuteHealthCheckAsync(registration, cancellationToken);
            results.TryAdd(registration.Name, result);
        });

        await Task.WhenAll(tasks);

        stopwatch.Stop();

        var overallStatus = CalculateOverallStatus(results.Values);
        return new HealthReport(overallStatus, results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), stopwatch.Elapsed);
    }

    /// <summary>
    /// Executes a single health check with caching and timeout support.
    /// </summary>
    private async Task<HealthCheckResult> ExecuteHealthCheckAsync(HealthCheckRegistration registration, CancellationToken cancellationToken)
    {
        var options = registration.Options;

        // Check cache if enabled
        if (options.EnableCaching && _cache.TryGetValue(options.Name, out var cached))
        {
            if (DateTime.UtcNow - cached.timestamp < options.CacheDuration)
            {
                return cached.result;
            }
        }

        var stopwatch = Stopwatch.StartNew();
        HealthCheckResult result;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(options.Timeout);

            var context = new HealthCheckContext(options.Name, options.Tags, cts.Token);
            result = await registration.HealthCheck.CheckAsync(context, cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = HealthCheckResult.Unhealthy($"Health check '{options.Name}' timed out after {options.Timeout.TotalSeconds} seconds.", 
                duration: stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            result = HealthCheckResult.Unhealthy($"Health check '{options.Name}' failed with exception: {ex.Message}", 
                ex, duration: stopwatch.Elapsed);
        }

        stopwatch.Stop();

        // Update failure count for threshold tracking
        if (result.Status == HealthStatus.Unhealthy)
        {
            var failures = _failureCounts.AddOrUpdate(options.Name, 1, (_, count) => count + 1);
            if (failures < options.FailureThreshold)
            {
                // Degrade instead of marking unhealthy if threshold not reached
                result = HealthCheckResult.Degraded(
                    $"Health check '{options.Name}' is experiencing issues (failure {failures}/{options.FailureThreshold}).",
                    result.Data, 
                    result.Exception, 
                    stopwatch.Elapsed);
            }
        }
        else
        {
            _failureCounts.TryRemove(options.Name, out _);
        }

        // Update cache if enabled
        if (options.EnableCaching)
        {
            _cache[options.Name] = (result, DateTime.UtcNow);
        }

        // Raise event if result changed
        var previousResult = _cache.TryGetValue(options.Name, out var prev) ? prev.result : null;
        if (previousResult?.Status != result.Status)
        {
            OnResultChanged(new HealthCheckResultChangedEventArgs(options.Name, previousResult, result));
        }

        return result;
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

    /// <summary>
    /// Raises the ResultChanged event.
    /// </summary>
    protected virtual void OnResultChanged(HealthCheckResultChangedEventArgs e)
    {
        ResultChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Ensures the registry has not been disposed.
    /// </summary>
    private void EnsureNotDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HealthCheckRegistry));
    }

    /// <summary>
    /// Disposes the registry.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Clear();
        ResultChanged = null;
    }
}

/// <summary>
/// A health check that wraps a delegate function.
/// </summary>
internal class DelegateHealthCheck : IHealthCheck
{
    private readonly Func<CancellationToken, Task<HealthCheckResult>> _checkFunc;

    /// <summary>
    /// Gets the name of the health check.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the tags associated with the health check.
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; } = new List<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateHealthCheck"/> class.
    /// </summary>
    public DelegateHealthCheck(string name, Func<CancellationToken, Task<HealthCheckResult>> checkFunc)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _checkFunc = checkFunc ?? throw new ArgumentNullException(nameof(checkFunc));
    }

    /// <summary>
    /// Performs the health check.
    /// </summary>
    public async Task<HealthCheckResult> CheckAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return await _checkFunc(cancellationToken);
    }
}
