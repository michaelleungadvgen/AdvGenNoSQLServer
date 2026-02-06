using AdvGenNoSqlServer.Core.Configuration;
using System;
using System.IO;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class ConfigurationManagerTests : IDisposable
{
    private readonly string _testConfigPath;

    public ConfigurationManagerTests()
    {
        _testConfigPath = "test_appsettings.json";

        // Create a test config file
        var testConfig = @"{
  ""port"": 9090,
  ""maxCacheItemCount"": 500,
  ""maxCacheSizeInBytes"": 52428800,
  ""defaultCacheTtlMilliseconds"": 900000,
  ""cacheTimeoutMinutes"": 15,
  ""storagePath"": ""test_data"",
  ""maxConcurrentConnections"": 50,
  ""enableDetailedLogging"": true,
  ""databaseTimeoutSeconds"": 60
}";
        File.WriteAllText(_testConfigPath, testConfig);
    }

    [Fact]
    public void ConfigurationManager_LoadsFromFile_Correctly()
    {
        // Arrange
        var configManager = new ConfigurationManager(_testConfigPath);

        // Act
        var config = configManager.Configuration;

        // Assert
        Assert.Equal(9090, config.Port);
        Assert.Equal(500, config.MaxCacheItemCount);
        Assert.Equal(52428800, config.MaxCacheSizeInBytes);
        Assert.Equal(900000, config.DefaultCacheTtlMilliseconds);
        Assert.Equal(15, config.CacheTimeoutMinutes);
        Assert.Equal("test_data", config.StoragePath);
        Assert.Equal(50, config.MaxConcurrentConnections);
        Assert.True(config.EnableDetailedLogging);
        Assert.Equal(60, config.DatabaseTimeoutSeconds);
    }

    [Fact]
    public void ConfigurationManager_UpdatesConfiguration_Correctly()
    {
        // Arrange
        var configManager = new ConfigurationManager(_testConfigPath);

        // Act
        configManager.UpdateConfiguration("Port", 1234);
        configManager.UpdateConfiguration("MaxCacheItemCount", 750);

        // Assert
        Assert.Equal(1234, configManager.Configuration.Port);
        Assert.Equal(750, configManager.Configuration.MaxCacheItemCount);
    }

    [Fact]
    public void ConfigurationManager_InvalidKey_ThrowsException()
    {
        // Arrange
        var configManager = new ConfigurationManager(_testConfigPath);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => configManager.UpdateConfiguration("InvalidKey", "value"));
    }

    [Fact]
    public void ConfigurationManager_InvalidType_ThrowsException()
    {
        // Arrange
        var configManager = new ConfigurationManager(_testConfigPath);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => configManager.UpdateConfiguration("Port", "not_a_number"));
    }

    [Fact]
    public void ConfigurationManager_ReloadsConfiguration_Correctly()
    {
        // Arrange
        var configManager = new ConfigurationManager(_testConfigPath);

        // Modify the config file
        var updatedConfig = @"{
  ""port"": 8081,
  ""maxCacheItemCount"": 1000,
  ""maxCacheSizeInBytes"": 104857600,
  ""defaultCacheTtlMilliseconds"": 1800000,
  ""cacheTimeoutMinutes"": 30,
  ""storagePath"": ""data"",
  ""maxConcurrentConnections"": 100,
  ""enableDetailedLogging"": false,
  ""databaseTimeoutSeconds"": 30
}";
        File.WriteAllText(_testConfigPath, updatedConfig);

        // Act
        configManager.ReloadConfiguration();

        // Assert
        Assert.Equal(8081, configManager.Configuration.Port);
        Assert.Equal(1000, configManager.Configuration.MaxCacheItemCount);
    }

    public void Dispose()
    {
        // Clean up test file
        if (File.Exists(_testConfigPath))
        {
            File.Delete(_testConfigPath);
        }
    }
}