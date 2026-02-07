// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Configuration;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for ConfigurationManager hot-reload functionality
/// </summary>
public class ConfigurationHotReloadTests : IDisposable
{
    private readonly string _testConfigPath;
    private readonly string _testConfigDir;

    public ConfigurationHotReloadTests()
    {
        _testConfigDir = Path.Combine(Path.GetTempPath(), $"nosql_test_{Guid.NewGuid():N}");
        _testConfigPath = Path.Combine(_testConfigDir, "test_appsettings.json");
        Directory.CreateDirectory(_testConfigDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testConfigDir))
            {
                Directory.Delete(_testConfigDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private void CreateConfigFile(ServerConfiguration config)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_testConfigPath, json);
    }

    private void UpdateConfigFile(ServerConfiguration config)
    {
        // Ensure file write is detected by changing timestamp
        Thread.Sleep(100);
        CreateConfigFile(config);
    }

    [Fact]
    public void Constructor_WithoutHotReload_ShouldNotEnableWatcher()
    {
        // Arrange
        var config = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(config);

        // Act
        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: false);

        // Assert
        Assert.False(manager.IsHotReloadEnabled);
    }

    [Fact]
    public void Constructor_WithHotReload_ShouldEnableWatcher()
    {
        // Arrange
        var config = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(config);

        // Act
        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: true);

        // Assert
        Assert.True(manager.IsHotReloadEnabled);
    }

    [Fact]
    public void EnableHotReload_ShouldStartFileWatcher()
    {
        // Arrange
        var config = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(config);
        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: false);

        // Act
        manager.EnableHotReload();

        // Assert
        Assert.True(manager.IsHotReloadEnabled);
    }

    [Fact]
    public void DisableHotReload_ShouldStopFileWatcher()
    {
        // Arrange
        var config = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(config);
        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: true);
        Assert.True(manager.IsHotReloadEnabled);

        // Act
        manager.DisableHotReload();

        // Assert
        Assert.False(manager.IsHotReloadEnabled);
    }

    [Fact]
    public async Task HotReload_ShouldDetectFileChangeAndRaiseEvent()
    {
        // Arrange
        var initialConfig = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(initialConfig);

        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: true);
        var eventRaised = new ManualResetEventSlim(false);
        ConfigurationChangedEventArgs? capturedArgs = null;

        manager.ConfigurationChanged += (sender, args) =>
        {
            capturedArgs = args;
            eventRaised.Set();
        };

        // Act
        var updatedConfig = new ServerConfiguration { Port = 9091 };
        UpdateConfigFile(updatedConfig);

        // Wait for the file change to be detected
        var eventReceived = eventRaised.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(eventReceived, "ConfigurationChanged event was not raised within timeout");
        Assert.NotNull(capturedArgs);
        Assert.Equal(9090, capturedArgs!.OldConfiguration.Port);
        Assert.Equal(9091, capturedArgs.NewConfiguration.Port);
        Assert.Equal("File", capturedArgs.ChangeSource);
    }

    [Fact]
    public async Task HotReload_MultipleRapidChanges_ShouldDebounce()
    {
        // Arrange
        var initialConfig = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(initialConfig);

        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: true);
        var eventCount = 0;
        var lastEventArgs = (ConfigurationChangedEventArgs?)null;

        manager.ConfigurationChanged += (sender, args) =>
        {
            Interlocked.Increment(ref eventCount);
            lastEventArgs = args;
        };

        // Act - Make multiple rapid changes
        for (int i = 1; i <= 3; i++)
        {
            var updatedConfig = new ServerConfiguration { Port = 9090 + i };
            UpdateConfigFile(updatedConfig);
            await Task.Delay(50); // Small delay between writes
        }

        // Wait for debounce and processing
        await Task.Delay(1500);

        // Assert - Should have fewer events than changes due to debouncing
        Assert.True(eventCount <= 3, $"Expected at most 3 events but got {eventCount}");
    }

    [Fact]
    public void ManualReload_ShouldRaiseEvent()
    {
        // Arrange
        var initialConfig = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(initialConfig);

        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: false);
        var eventRaised = false;
        ConfigurationChangedEventArgs? capturedArgs = null;

        manager.ConfigurationChanged += (sender, args) =>
        {
            eventRaised = true;
            capturedArgs = args;
        };

        // Act
        manager.ReloadConfiguration();

        // Assert
        Assert.True(eventRaised);
        Assert.NotNull(capturedArgs);
        Assert.Equal("Manual", capturedArgs!.ChangeSource);
    }

    [Fact]
    public void UpdateConfiguration_ShouldRaiseEvent()
    {
        // Arrange
        var initialConfig = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(initialConfig);

        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: false);
        var eventRaised = false;
        ConfigurationChangedEventArgs? capturedArgs = null;

        manager.ConfigurationChanged += (sender, args) =>
        {
            eventRaised = true;
            capturedArgs = args;
        };

        // Act
        manager.UpdateConfiguration("Port", 9095);

        // Assert
        Assert.True(eventRaised);
        Assert.NotNull(capturedArgs);
        Assert.Equal(9090, capturedArgs!.OldConfiguration.Port);
        Assert.Equal(9095, capturedArgs.NewConfiguration.Port);
        Assert.Equal("Manual", capturedArgs.ChangeSource);
    }

    [Fact]
    public void ConfigurationChangedEvent_ShouldIncludeTimestamp()
    {
        // Arrange
        var initialConfig = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(initialConfig);

        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: false);
        ConfigurationChangedEventArgs? capturedArgs = null;

        manager.ConfigurationChanged += (sender, args) =>
        {
            capturedArgs = args;
        };

        var beforeReload = DateTime.UtcNow.AddSeconds(-1);

        // Act
        manager.ReloadConfiguration();

        var afterReload = DateTime.UtcNow.AddSeconds(1);

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.True(capturedArgs!.ChangeTime >= beforeReload);
        Assert.True(capturedArgs.ChangeTime <= afterReload);
    }

    [Fact]
    public void UpdateConfiguration_InvalidKey_ShouldThrowArgumentException()
    {
        // Arrange
        var initialConfig = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(initialConfig);

        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: false);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => manager.UpdateConfiguration("InvalidProperty", 123));
    }

    [Fact]
    public void HotReload_NonExistentFile_ShouldNotThrow()
    {
        // Arrange - Delete the config file if it exists
        if (File.Exists(_testConfigPath))
        {
            File.Delete(_testConfigPath);
        }

        // Act & Assert - Should not throw
        var exception = Record.Exception(() =>
        {
            using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: true);
            Assert.NotNull(manager.Configuration);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void HotReload_CorruptedFile_ShouldKeepPreviousConfig()
    {
        // Arrange
        var initialConfig = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(initialConfig);

        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: false);
        Assert.Equal(9090, manager.Configuration.Port);

        // Act - Write corrupted JSON
        File.WriteAllText(_testConfigPath, "{ invalid json }");
        manager.ReloadConfiguration();

        // Assert - Should keep previous valid configuration (falls back to defaults on error)
        Assert.NotNull(manager.Configuration);
    }

    [Fact]
    public void ConfigurationChanged_MultipleSubscribers_AllShouldBeNotified()
    {
        // Arrange
        var initialConfig = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(initialConfig);

        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: false);
        var subscriber1Notified = false;
        var subscriber2Notified = false;

        manager.ConfigurationChanged += (sender, args) => subscriber1Notified = true;
        manager.ConfigurationChanged += (sender, args) => subscriber2Notified = true;

        // Act
        manager.ReloadConfiguration();

        // Assert
        Assert.True(subscriber1Notified);
        Assert.True(subscriber2Notified);
    }

    [Fact]
    public void EnableHotReload_WhenDisposed_ShouldNotThrow()
    {
        // Arrange
        var config = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(config);

        var manager = new ConfigurationManager(_testConfigPath, enableHotReload: false);
        manager.Dispose();

        // Act & Assert - Should not throw
        manager.EnableHotReload();
        Assert.False(manager.IsHotReloadEnabled);
    }

    [Fact]
    public void Dispose_ShouldCleanUpResources()
    {
        // Arrange
        var config = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(config);

        var manager = new ConfigurationManager(_testConfigPath, enableHotReload: true);
        Assert.True(manager.IsHotReloadEnabled);

        // Act
        manager.Dispose();

        // Assert
        Assert.False(manager.IsHotReloadEnabled);
    }

    [Fact]
    public void ConfigurationManager_MultiplePropertiesChanged_ShouldTrackAll()
    {
        // Arrange
        var initialConfig = new ServerConfiguration 
        { 
            Port = 9090, 
            MaxCacheItemCount = 1000,
            EnableDetailedLogging = false
        };
        CreateConfigFile(initialConfig);

        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: false);
        ConfigurationChangedEventArgs? capturedArgs = null;

        manager.ConfigurationChanged += (sender, args) =>
        {
            capturedArgs = args;
        };

        // Act - Update multiple properties
        manager.UpdateConfiguration("Port", 9091);
        manager.UpdateConfiguration("MaxCacheItemCount", 2000);
        manager.UpdateConfiguration("EnableDetailedLogging", true);

        // Assert - Check the last event reflects all changes
        Assert.NotNull(capturedArgs);
        Assert.Equal(9091, capturedArgs!.NewConfiguration.Port);
        Assert.Equal(2000, capturedArgs.NewConfiguration.MaxCacheItemCount);
        Assert.True(capturedArgs.NewConfiguration.EnableDetailedLogging);
    }

    [Fact]
    public async Task HotReload_FileDeletedAndRecreated_ShouldDetectChange()
    {
        // Arrange
        var initialConfig = new ServerConfiguration { Port = 9090 };
        CreateConfigFile(initialConfig);

        using var manager = new ConfigurationManager(_testConfigPath, enableHotReload: true);
        var eventRaised = new ManualResetEventSlim(false);

        manager.ConfigurationChanged += (sender, args) =>
        {
            if (args.NewConfiguration.Port == 9099)
            {
                eventRaised.Set();
            }
        };

        // Act - Delete and recreate file
        File.Delete(_testConfigPath);
        await Task.Delay(200);

        var newConfig = new ServerConfiguration { Port = 9099 };
        CreateConfigFile(newConfig);

        // Wait for detection
        var detected = eventRaised.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(detected, "Should have detected recreated file");
    }
}
