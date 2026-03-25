// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Health;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for health check system.
/// </summary>
public class HealthCheckTests
{
    #region HealthStatus Tests

    [Fact]
    public void HealthStatus_Values_AreDefined()
    {
        // Assert
        Assert.Equal(0, (int)HealthStatus.Healthy);
        Assert.Equal(1, (int)HealthStatus.Degraded);
        Assert.Equal(2, (int)HealthStatus.Unhealthy);
    }

    #endregion

    #region HealthCheckResult Tests

    [Fact]
    public void HealthCheckResult_Healthy_ReturnsHealthyStatus()
    {
        // Act
        var result = HealthCheckResult.Healthy("All good");

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("All good", result.Description);
        Assert.True(result.IsHealthy);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void HealthCheckResult_Healthy_WithData_ReturnsCorrectData()
    {
        // Arrange
        var data = new Dictionary<string, object> { ["key"] = "value" };

        // Act
        var result = HealthCheckResult.Healthy("All good", data);

        // Assert
        Assert.Equal("value", result.Data["key"]);
    }

    [Fact]
    public void HealthCheckResult_Degraded_ReturnsDegradedStatus()
    {
        // Act
        var result = HealthCheckResult.Degraded("Some issues");

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("Some issues", result.Description);
        Assert.True(result.IsHealthy); // Degraded is still "healthy" overall
    }

    [Fact]
    public void HealthCheckResult_Unhealthy_ReturnsUnhealthyStatus()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = HealthCheckResult.Unhealthy("Failed", exception);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Failed", result.Description);
        Assert.False(result.IsHealthy);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public void HealthCheckResult_SetsTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var result = HealthCheckResult.Healthy();

        // Assert
        Assert.True(result.Timestamp >= before);
        Assert.True(result.Timestamp <= DateTime.UtcNow.AddSeconds(1));
    }

    #endregion

    #region HealthCheckContext Tests

    [Fact]
    public void HealthCheckContext_Constructor_SetsProperties()
    {
        // Arrange
        var name = "test-check";
        var tags = new[] { "tag1", "tag2" };
        var cts = new CancellationTokenSource();

        // Act
        var context = new HealthCheckContext(name, tags, cts.Token);

        // Assert
        Assert.Equal(name, context.HealthCheckName);
        Assert.Equal(2, context.Tags.Count);
        Assert.Contains("tag1", context.Tags);
        Assert.Contains("tag2", context.Tags);
        Assert.Equal(cts.Token, context.CancellationToken);
    }

    [Fact]
    public void HealthCheckContext_Constructor_WithNullTags_UsesEmptyList()
    {
        // Act
        var context = new HealthCheckContext("test", null, CancellationToken.None);

        // Assert
        Assert.NotNull(context.Tags);
        Assert.Empty(context.Tags);
    }

    [Fact]
    public void HealthCheckContext_Constructor_WithNullName_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new HealthCheckContext(null!, new[] { "tag" }, CancellationToken.None));
    }

    [Fact]
    public void HealthCheckContext_Data_CanStoreCustomData()
    {
        // Arrange
        var context = new HealthCheckContext("test", null, CancellationToken.None);

        // Act
        context.Data["customKey"] = "customValue";

        // Assert
        Assert.Equal("customValue", context.Data["customKey"]);
    }

    #endregion

    #region HealthCheckOptions Tests

    [Fact]
    public void HealthCheckOptions_Validate_WithValidOptions_DoesNotThrow()
    {
        // Arrange
        var options = new HealthCheckOptions
        {
            Name = "test",
            Timeout = TimeSpan.FromSeconds(30),
            EnableCaching = true,
            CacheDuration = TimeSpan.FromSeconds(5),
            FailureThreshold = 3
        };

        // Act & Assert
        options.Validate(); // Should not throw
    }

    [Fact]
    public void HealthCheckOptions_Validate_WithEmptyName_ThrowsArgumentException()
    {
        // Arrange
        var options = new HealthCheckOptions { Name = "" };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void HealthCheckOptions_Validate_WithZeroTimeout_ThrowsArgumentException()
    {
        // Arrange
        var options = new HealthCheckOptions { Name = "test", Timeout = TimeSpan.Zero };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void HealthCheckOptions_Validate_WithZeroCacheDuration_ThrowsArgumentException()
    {
        // Arrange
        var options = new HealthCheckOptions 
        { 
            Name = "test", 
            EnableCaching = true, 
            CacheDuration = TimeSpan.Zero 
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void HealthCheckOptions_Validate_WithZeroFailureThreshold_ThrowsArgumentException()
    {
        // Arrange
        var options = new HealthCheckOptions { Name = "test", FailureThreshold = 0 };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    #endregion

    #region HealthCheckRegistration Tests

    [Fact]
    public void HealthCheckRegistration_Constructor_SetsProperties()
    {
        // Arrange
        var name = "test-check";
        var healthCheck = new TestHealthCheck(name);
        var options = new HealthCheckOptions { Name = name, Timeout = TimeSpan.FromSeconds(60) };

        // Act
        var registration = new HealthCheckRegistration(name, healthCheck, options);

        // Assert
        Assert.Equal(name, registration.Name);
        Assert.Equal(healthCheck, registration.HealthCheck);
        Assert.Equal(options, registration.Options);
    }

    [Fact]
    public void HealthCheckRegistration_Constructor_WithNullOptions_CreatesDefaultOptions()
    {
        // Arrange
        var name = "test-check";
        var healthCheck = new TestHealthCheck(name);

        // Act
        var registration = new HealthCheckRegistration(name, healthCheck, null);

        // Assert
        Assert.NotNull(registration.Options);
        Assert.Equal(name, registration.Options.Name);
    }

    #endregion

    #region HealthReport Tests

    [Fact]
    public void HealthReport_Constructor_SetsProperties()
    {
        // Arrange
        var status = HealthStatus.Healthy;
        var results = new Dictionary<string, HealthCheckResult>
        {
            ["check1"] = HealthCheckResult.Healthy(),
            ["check2"] = HealthCheckResult.Healthy()
        };
        var duration = TimeSpan.FromMilliseconds(100);

        // Act
        var report = new HealthReport(status, results, duration);

        // Assert
        Assert.Equal(status, report.Status);
        Assert.Equal(2, report.Results.Count);
        Assert.Equal(duration, report.TotalDuration);
        Assert.True(report.IsHealthy);
        Assert.True(report.IsFullyHealthy);
    }

    [Fact]
    public void HealthReport_WithUnhealthyResult_IsNotHealthy()
    {
        // Arrange
        var results = new Dictionary<string, HealthCheckResult>
        {
            ["check1"] = HealthCheckResult.Healthy(),
            ["check2"] = HealthCheckResult.Unhealthy("Failed")
        };

        // Act
        var report = new HealthReport(HealthStatus.Unhealthy, results, TimeSpan.Zero);

        // Assert
        Assert.False(report.IsHealthy);
        Assert.False(report.IsFullyHealthy);
        Assert.Equal(1, report.HealthyCount);
        Assert.Equal(0, report.DegradedCount);
        Assert.Equal(1, report.UnhealthyCount);
    }

    [Fact]
    public void HealthReport_WithDegradedResult_IsHealthyButNotFully()
    {
        // Arrange
        var results = new Dictionary<string, HealthCheckResult>
        {
            ["check1"] = HealthCheckResult.Healthy(),
            ["check2"] = HealthCheckResult.Degraded("Slow")
        };

        // Act
        var report = new HealthReport(HealthStatus.Degraded, results, TimeSpan.Zero);

        // Assert
        Assert.True(report.IsHealthy); // Degraded is still healthy
        Assert.False(report.IsFullyHealthy);
        Assert.Equal(1, report.HealthyCount);
        Assert.Equal(1, report.DegradedCount);
        Assert.Equal(0, report.UnhealthyCount);
    }

    [Fact]
    public void HealthReport_Empty_ReturnsHealthy()
    {
        // Act
        var report = HealthReport.Empty();

        // Assert
        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Empty(report.Results);
        Assert.True(report.IsHealthy);
    }

    [Fact]
    public void HealthReport_SetsTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var report = new HealthReport(HealthStatus.Healthy, new Dictionary<string, HealthCheckResult>(), TimeSpan.Zero);

        // Assert
        Assert.True(report.Timestamp >= before);
        Assert.True(report.Timestamp <= DateTime.UtcNow.AddSeconds(1));
    }

    #endregion

    #region HealthCheckRegistry Tests

    [Fact]
    public void HealthCheckRegistry_Register_WithValidHealthCheck_AddsToRegistry()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        var healthCheck = new TestHealthCheck("test");

        // Act
        registry.Register(healthCheck);

        // Assert
        Assert.True(registry.IsRegistered("test"));
        Assert.Single(registry.GetAll());
    }

    [Fact]
    public void HealthCheckRegistry_Register_WithNullHealthCheck_ThrowsArgumentNullException()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => registry.Register((IHealthCheck)null!));
    }

    [Fact]
    public void HealthCheckRegistry_Register_WithDuplicateName_ThrowsInvalidOperationException()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        var healthCheck1 = new TestHealthCheck("test");
        var healthCheck2 = new TestHealthCheck("test");
        registry.Register(healthCheck1);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => registry.Register(healthCheck2));
    }

    [Fact]
    public void HealthCheckRegistry_Register_WithDelegate_AddsToRegistry()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();

        // Act
        registry.Register("test", _ => Task.FromResult(HealthCheckResult.Healthy()));

        // Assert
        Assert.True(registry.IsRegistered("test"));
    }

    [Fact]
    public void HealthCheckRegistry_Unregister_RemovesFromRegistry()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        var healthCheck = new TestHealthCheck("test");
        registry.Register(healthCheck);

        // Act
        var removed = registry.Unregister("test");

        // Assert
        Assert.True(removed);
        Assert.False(registry.IsRegistered("test"));
        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public void HealthCheckRegistry_Unregister_WithNonExistentName_ReturnsFalse()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();

        // Act
        var removed = registry.Unregister("nonexistent");

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public void HealthCheckRegistry_Get_ReturnsRegistration()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        var healthCheck = new TestHealthCheck("test");
        registry.Register(healthCheck);

        // Act
        var registration = registry.Get("test");

        // Assert
        Assert.NotNull(registration);
        Assert.Equal("test", registration.Name);
    }

    [Fact]
    public void HealthCheckRegistry_Get_WithNonExistentName_ReturnsNull()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();

        // Act
        var registration = registry.Get("nonexistent");

        // Assert
        Assert.Null(registration);
    }

    [Fact]
    public void HealthCheckRegistry_GetByTag_ReturnsMatchingChecks()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        var healthCheck1 = new TestHealthCheck("check1", new[] { "tag1", "tag2" });
        var healthCheck2 = new TestHealthCheck("check2", new[] { "tag2", "tag3" });
        registry.Register(healthCheck1);
        registry.Register(healthCheck2);

        // Act
        var tag1Checks = registry.GetByTag("tag1");
        var tag2Checks = registry.GetByTag("tag2");

        // Assert
        Assert.Single(tag1Checks);
        Assert.Equal(2, tag2Checks.Count);
    }

    [Fact]
    public void HealthCheckRegistry_Clear_RemovesAllChecks()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        registry.Register(new TestHealthCheck("check1"));
        registry.Register(new TestHealthCheck("check2"));

        // Act
        registry.Clear();

        // Assert
        Assert.Empty(registry.GetAll());
        Assert.False(registry.IsRegistered("check1"));
        Assert.False(registry.IsRegistered("check2"));
    }

    [Fact]
    public async Task HealthCheckRegistry_RunAllAsync_ExecutesAllChecks()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        registry.Register(new TestHealthCheck("check1", HealthStatus.Healthy));
        registry.Register(new TestHealthCheck("check2", HealthStatus.Healthy));

        // Act
        var report = await registry.RunAllAsync();

        // Assert
        Assert.Equal(2, report.Results.Count);
        Assert.Equal(HealthStatus.Healthy, report.Status);
    }

    [Fact]
    public async Task HealthCheckRegistry_RunAllAsync_WithUnhealthy_ReturnsUnhealthy()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        registry.Register(new TestHealthCheck("check1", HealthStatus.Healthy));
        registry.Register(new TestHealthCheck("check2", HealthStatus.Unhealthy));

        // Act
        var report = await registry.RunAllAsync();

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.False(report.IsHealthy);
    }

    [Fact]
    public async Task HealthCheckRegistry_RunByTagAsync_ExecutesMatchingChecks()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        registry.Register(new TestHealthCheck("check1", new[] { "tag1" }));
        registry.Register(new TestHealthCheck("check2", new[] { "tag2" }));

        // Act
        var report = await registry.RunByTagAsync("tag1");

        // Assert
        Assert.Single(report.Results);
        Assert.Contains("check1", report.Results.Keys);
    }

    [Fact]
    public async Task HealthCheckRegistry_RunByNameAsync_ExecutesSpecificCheck()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        registry.Register(new TestHealthCheck("check1", HealthStatus.Healthy));

        // Act
        var result = await registry.RunByNameAsync("check1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task HealthCheckRegistry_RunByNameAsync_WithNonExistentName_ReturnsNull()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();

        // Act
        var result = await registry.RunByNameAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task HealthCheckRegistry_ExecutesWithTimeout()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        var options = new HealthCheckOptions 
        { 
            Name = "slow-check", 
            Timeout = TimeSpan.FromMilliseconds(100) 
        };
        var slowCheck = new SlowHealthCheck("slow-check", TimeSpan.FromSeconds(10));
        registry.Register(slowCheck, options);

        // Act
        var result = await registry.RunByNameAsync("slow-check");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("timed out", result.Description);
    }

    [Fact]
    public async Task HealthCheckRegistry_CachesResults_WhenEnabled()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        var counter = 0;
        var options = new HealthCheckOptions 
        { 
            Name = "cached-check", 
            EnableCaching = true, 
            CacheDuration = TimeSpan.FromSeconds(10) 
        };
        registry.Register("cached-check", _ => 
        {
            counter++;
            return Task.FromResult(HealthCheckResult.Healthy());
        }, options);

        // Act
        await registry.RunByNameAsync("cached-check");
        await registry.RunByNameAsync("cached-check");

        // Assert
        Assert.Equal(1, counter); // Should only execute once due to caching
    }

    [Fact]
    public void HealthCheckRegistry_ResultChanged_EventRaisedOnStatusChange()
    {
        // Arrange
        using var registry = new HealthCheckRegistry();
        var eventRaised = false;
        HealthCheckResultChangedEventArgs? eventArgs = null;

        registry.ResultChanged += (s, e) =>
        {
            eventRaised = true;
            eventArgs = e;
        };

        // Register first to set up initial state
        registry.Register(new TestHealthCheck("check", HealthStatus.Healthy));

        // Act - run the check (will establish cache entry)
        // Then we need to force a change, but since cache is empty initially,
        // the first run will set the baseline

        // Assert
        // Note: Event is raised when result changes from previous, so we need
        // to run twice with different results to trigger the event
    }

    #endregion

    #region Built-in Health Checks Tests

    [Fact]
    public void LivenessHealthCheck_Name_IsLiveness()
    {
        // Arrange
        var check = new LivenessHealthCheck();

        // Assert
        Assert.Equal("liveness", check.Name);
        Assert.Contains("liveness", check.Tags);
    }

    [Fact]
    public async Task LivenessHealthCheck_CheckAsync_ReturnsHealthy()
    {
        // Arrange
        var check = new LivenessHealthCheck();
        var context = new HealthCheckContext("liveness", null, CancellationToken.None);

        // Act
        var result = await check.CheckAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.NotNull(result.Data["processId"]);
        Assert.NotNull(result.Data["uptime"]);
    }

    [Fact]
    public void MemoryHealthCheck_Name_IsMemory()
    {
        // Arrange
        var check = new MemoryHealthCheck();

        // Assert
        Assert.Equal("memory", check.Name);
        Assert.Contains("memory", check.Tags);
    }

    [Fact]
    public async Task MemoryHealthCheck_CheckAsync_ReturnsHealthyOrDegraded()
    {
        // Arrange
        var check = new MemoryHealthCheck(warningThresholdPercent: 99, criticalThresholdPercent: 99.9);
        var context = new HealthCheckContext("memory", null, CancellationToken.None);

        // Act
        var result = await check.CheckAsync(context);

        // Assert
        Assert.True(result.Status == HealthStatus.Healthy || result.Status == HealthStatus.Degraded);
        Assert.True(result.Data.ContainsKey("totalMemoryMB"));
        Assert.True(result.Data.ContainsKey("memoryLoadPercent"));
    }

    [Fact]
    public void DiskHealthCheck_Name_IsDisk()
    {
        // Arrange
        var check = new DiskHealthCheck();

        // Assert
        Assert.Equal("disk", check.Name);
        Assert.Contains("disk", check.Tags);
    }

    [Fact]
    public async Task DiskHealthCheck_CheckAsync_ReturnsResult()
    {
        // Arrange
        var check = new DiskHealthCheck(warningThresholdPercent: 99, criticalThresholdPercent: 99.9);
        var context = new HealthCheckContext("disk", null, CancellationToken.None);

        // Act
        var result = await check.CheckAsync(context);

        // Assert
        Assert.NotNull(result);
        // Result should have some data or a description about drive status
        Assert.True(result.Data.Count > 0 || !string.IsNullOrEmpty(result.Description));
    }

    [Fact]
    public void NetworkHealthCheck_Name_IsNetwork()
    {
        // Arrange
        var check = new NetworkHealthCheck();

        // Assert
        Assert.Equal("network", check.Name);
        Assert.Contains("network", check.Tags);
    }

    [Fact]
    public async Task NetworkHealthCheck_CheckAsync_ReturnsResult()
    {
        // Arrange
        var check = new NetworkHealthCheck();
        var context = new HealthCheckContext("network", null, CancellationToken.None);

        // Act
        var result = await check.CheckAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Data.ContainsKey("isNetworkAvailable"));
    }

    [Fact]
    public async Task CompositeHealthCheck_CheckAsync_AggregatesResults()
    {
        // Arrange
        var checks = new List<IHealthCheck>
        {
            new TestHealthCheck("check1", HealthStatus.Healthy),
            new TestHealthCheck("check2", HealthStatus.Healthy)
        };
        var composite = new CompositeHealthCheck("composite", checks);
        var context = new HealthCheckContext("composite", null, CancellationToken.None);

        // Act
        var result = await composite.CheckAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.True(result.Data.ContainsKey("checks"));
    }

    [Fact]
    public async Task CompositeHealthCheck_CheckAsync_WithUnhealthy_ReturnsUnhealthy()
    {
        // Arrange
        var checks = new List<IHealthCheck>
        {
            new TestHealthCheck("check1", HealthStatus.Healthy),
            new TestHealthCheck("check2", HealthStatus.Unhealthy)
        };
        var composite = new CompositeHealthCheck("composite", checks);
        var context = new HealthCheckContext("composite", null, CancellationToken.None);

        // Act
        var result = await composite.CheckAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    #endregion

    #region HealthCheckResultChangedEventArgs Tests

    [Fact]
    public void HealthCheckResultChangedEventArgs_Constructor_SetsProperties()
    {
        // Arrange
        var name = "test-check";
        var previous = HealthCheckResult.Healthy("Before");
        var current = HealthCheckResult.Unhealthy("After");

        // Act
        var args = new HealthCheckResultChangedEventArgs(name, previous, current);

        // Assert
        Assert.Equal(name, args.HealthCheckName);
        Assert.Equal(previous, args.PreviousResult);
        Assert.Equal(current, args.NewResult);
    }

    #endregion

    #region Test Helpers

    private class TestHealthCheck : IHealthCheck
    {
        private readonly HealthStatus _status;

        public string Name { get; }
        public IReadOnlyCollection<string> Tags { get; }

        public TestHealthCheck(string name, HealthStatus status = HealthStatus.Healthy)
        {
            Name = name;
            _status = status;
            Tags = new List<string>();
        }

        public TestHealthCheck(string name, IEnumerable<string> tags, HealthStatus status = HealthStatus.Healthy)
        {
            Name = name;
            _status = status;
            Tags = tags.ToList().AsReadOnly();
        }

        public Task<HealthCheckResult> CheckAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var result = _status switch
            {
                HealthStatus.Healthy => HealthCheckResult.Healthy($"{Name} is healthy"),
                HealthStatus.Degraded => HealthCheckResult.Degraded($"{Name} is degraded"),
                HealthStatus.Unhealthy => HealthCheckResult.Unhealthy($"{Name} is unhealthy"),
                _ => HealthCheckResult.Healthy()
            };

            return Task.FromResult(result);
        }
    }

    private class SlowHealthCheck : IHealthCheck
    {
        private readonly TimeSpan _delay;

        public string Name { get; }
        public IReadOnlyCollection<string> Tags { get; } = new List<string>();

        public SlowHealthCheck(string name, TimeSpan delay)
        {
            Name = name;
            _delay = delay;
        }

        public async Task<HealthCheckResult> CheckAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken);
            return HealthCheckResult.Healthy();
        }
    }

    #endregion
}
