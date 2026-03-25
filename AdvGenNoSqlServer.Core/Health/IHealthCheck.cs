// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Health;

/// <summary>
/// Represents the health status of a component or the entire system.
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// The component is healthy and functioning normally.
    /// </summary>
    Healthy,

    /// <summary>
    /// The component is functioning but may have issues or reduced capacity.
    /// </summary>
    Degraded,

    /// <summary>
    /// The component is unhealthy and not functioning properly.
    /// </summary>
    Unhealthy
}

/// <summary>
/// Context for health check execution.
/// </summary>
public class HealthCheckContext
{
    /// <summary>
    /// Gets the name of the health check being executed.
    /// </summary>
    public string HealthCheckName { get; }

    /// <summary>
    /// Gets the tags associated with the health check.
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; }

    /// <summary>
    /// Gets the cancellation token for the health check.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets or sets custom data for the health check context.
    /// </summary>
    public Dictionary<string, object> Data { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthCheckContext"/> class.
    /// </summary>
    public HealthCheckContext(string healthCheckName, IEnumerable<string> tags, CancellationToken cancellationToken)
    {
        HealthCheckName = healthCheckName ?? throw new ArgumentNullException(nameof(healthCheckName));
        Tags = tags?.ToList().AsReadOnly() ?? new List<string>().AsReadOnly();
        CancellationToken = cancellationToken;
    }
}

/// <summary>
/// Represents the result of a health check.
/// </summary>
public class HealthCheckResult
{
    /// <summary>
    /// Gets the health status.
    /// </summary>
    public HealthStatus Status { get; }

    /// <summary>
    /// Gets a description of the health check result.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets additional data about the health check result.
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; }

    /// <summary>
    /// Gets the exception that occurred during the health check, if any.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Gets the duration of the health check execution.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets the timestamp when the health check was executed.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Gets a value indicating whether the health check passed (Healthy or Degraded).
    /// </summary>
    public bool IsHealthy => Status == HealthStatus.Healthy || Status == HealthStatus.Degraded;

    private HealthCheckResult(HealthStatus status, string? description, 
        IReadOnlyDictionary<string, object>? data, Exception? exception, TimeSpan duration)
    {
        Status = status;
        Description = description;
        Data = data ?? new Dictionary<string, object>();
        Exception = exception;
        Duration = duration;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a healthy result.
    /// </summary>
    public static HealthCheckResult Healthy(string? description = null, 
        IReadOnlyDictionary<string, object>? data = null, TimeSpan duration = default)
    {
        return new HealthCheckResult(HealthStatus.Healthy, description, data, null, duration);
    }

    /// <summary>
    /// Creates a degraded result.
    /// </summary>
    public static HealthCheckResult Degraded(string? description = null, 
        IReadOnlyDictionary<string, object>? data = null, Exception? exception = null, TimeSpan duration = default)
    {
        return new HealthCheckResult(HealthStatus.Degraded, description, data, exception, duration);
    }

    /// <summary>
    /// Creates an unhealthy result.
    /// </summary>
    public static HealthCheckResult Unhealthy(string? description = null, 
        Exception? exception = null, IReadOnlyDictionary<string, object>? data = null, TimeSpan duration = default)
    {
        return new HealthCheckResult(HealthStatus.Unhealthy, description, data, exception, duration);
    }
}

/// <summary>
/// Options for configuring health checks.
/// </summary>
public class HealthCheckOptions
{
    /// <summary>
    /// Gets or sets the name of the health check.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tags associated with the health check.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Gets or sets the timeout for the health check.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether the health check result should be cached.
    /// </summary>
    public bool EnableCaching { get; set; } = false;

    /// <summary>
    /// Gets or sets the cache duration for the health check result.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the failure threshold before marking the health check as unhealthy.
    /// </summary>
    public int FailureThreshold { get; set; } = 1;

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Health check name cannot be empty.", nameof(Name));

        if (Timeout <= TimeSpan.Zero)
            throw new ArgumentException("Timeout must be greater than zero.", nameof(Timeout));

        if (EnableCaching && CacheDuration <= TimeSpan.Zero)
            throw new ArgumentException("Cache duration must be greater than zero when caching is enabled.", nameof(CacheDuration));

        if (FailureThreshold < 1)
            throw new ArgumentException("Failure threshold must be at least 1.", nameof(FailureThreshold));
    }
}

/// <summary>
/// Represents a registered health check entry.
/// </summary>
public class HealthCheckRegistration
{
    /// <summary>
    /// Gets the name of the health check.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the health check instance.
    /// </summary>
    public IHealthCheck HealthCheck { get; }

    /// <summary>
    /// Gets the options for the health check.
    /// </summary>
    public HealthCheckOptions Options { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthCheckRegistration"/> class.
    /// </summary>
    public HealthCheckRegistration(string name, IHealthCheck healthCheck, HealthCheckOptions? options = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        HealthCheck = healthCheck ?? throw new ArgumentNullException(nameof(healthCheck));
        Options = options ?? new HealthCheckOptions { Name = name };
        Options.Name = name;
    }
}

/// <summary>
/// Represents an aggregated health report from all health checks.
/// </summary>
public class HealthReport
{
    /// <summary>
    /// Gets the overall health status.
    /// </summary>
    public HealthStatus Status { get; }

    /// <summary>
    /// Gets the results of individual health checks.
    /// </summary>
    public IReadOnlyDictionary<string, HealthCheckResult> Results { get; }

    /// <summary>
    /// Gets the total duration of all health checks.
    /// </summary>
    public TimeSpan TotalDuration { get; }

    /// <summary>
    /// Gets the timestamp when the health report was generated.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Gets a value indicating whether all health checks passed (Healthy or Degraded).
    /// </summary>
    public bool IsHealthy => Status == HealthStatus.Healthy || Status == HealthStatus.Degraded;

    /// <summary>
    /// Gets a value indicating whether the health status is Healthy.
    /// </summary>
    public bool IsFullyHealthy => Status == HealthStatus.Healthy;

    /// <summary>
    /// Gets the number of healthy checks.
    /// </summary>
    public int HealthyCount => Results.Count(r => r.Value.Status == HealthStatus.Healthy);

    /// <summary>
    /// Gets the number of degraded checks.
    /// </summary>
    public int DegradedCount => Results.Count(r => r.Value.Status == HealthStatus.Degraded);

    /// <summary>
    /// Gets the number of unhealthy checks.
    /// </summary>
    public int UnhealthyCount => Results.Count(r => r.Value.Status == HealthStatus.Unhealthy);

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthReport"/> class.
    /// </summary>
    public HealthReport(HealthStatus status, Dictionary<string, HealthCheckResult> results, TimeSpan totalDuration)
    {
        Status = status;
        Results = results.AsReadOnly();
        TotalDuration = totalDuration;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates an empty health report.
    /// </summary>
    public static HealthReport Empty()
    {
        return new HealthReport(HealthStatus.Healthy, new Dictionary<string, HealthCheckResult>(), TimeSpan.Zero);
    }
}

/// <summary>
/// Defines a contract for health checks.
/// </summary>
public interface IHealthCheck
{
    /// <summary>
    /// Gets the name of the health check.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the tags associated with the health check.
    /// </summary>
    IReadOnlyCollection<string> Tags { get; }

    /// <summary>
    /// Performs the health check.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the health check result.</returns>
    Task<HealthCheckResult> CheckAsync(HealthCheckContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a contract for the health check registry.
/// </summary>
public interface IHealthCheckRegistry
{
    /// <summary>
    /// Registers a health check.
    /// </summary>
    void Register(IHealthCheck healthCheck, HealthCheckOptions? options = null);

    /// <summary>
    /// Registers a health check with a name and factory.
    /// </summary>
    void Register(string name, Func<CancellationToken, Task<HealthCheckResult>> checkFunc, HealthCheckOptions? options = null);

    /// <summary>
    /// Unregisters a health check by name.
    /// </summary>
    bool Unregister(string name);

    /// <summary>
    /// Gets all registered health checks.
    /// </summary>
    IReadOnlyCollection<HealthCheckRegistration> GetAll();

    /// <summary>
    /// Gets health checks by tag.
    /// </summary>
    IReadOnlyCollection<HealthCheckRegistration> GetByTag(string tag);

    /// <summary>
    /// Checks if a health check is registered.
    /// </summary>
    bool IsRegistered(string name);

    /// <summary>
    /// Gets a specific health check by name.
    /// </summary>
    HealthCheckRegistration? Get(string name);

    /// <summary>
    /// Clears all registered health checks.
    /// </summary>
    void Clear();

    /// <summary>
    /// Runs all registered health checks and returns a health report.
    /// </summary>
    Task<HealthReport> RunAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs health checks filtered by tag.
    /// </summary>
    Task<HealthReport> RunByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a specific health check by name.
    /// </summary>
    Task<HealthCheckResult?> RunByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a health check result changes.
    /// </summary>
    event EventHandler<HealthCheckResultChangedEventArgs>? ResultChanged;
}

/// <summary>
/// Event arguments for health check result changes.
/// </summary>
public class HealthCheckResultChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the name of the health check.
    /// </summary>
    public string HealthCheckName { get; }

    /// <summary>
    /// Gets the previous result.
    /// </summary>
    public HealthCheckResult? PreviousResult { get; }

    /// <summary>
    /// Gets the new result.
    /// </summary>
    public HealthCheckResult NewResult { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthCheckResultChangedEventArgs"/> class.
    /// </summary>
    public HealthCheckResultChangedEventArgs(string healthCheckName, HealthCheckResult? previousResult, HealthCheckResult newResult)
    {
        HealthCheckName = healthCheckName;
        PreviousResult = previousResult;
        NewResult = newResult;
    }
}
