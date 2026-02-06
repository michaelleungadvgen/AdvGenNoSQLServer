using System.Text.Json;
using System.IO;
using System;

namespace AdvGenNoSqlServer.Core.Configuration;

/// <summary>
/// Implementation of configuration manager that reads from JSON files and environment variables
/// </summary>
public class ConfigurationManager : IConfigurationManager
{
    private readonly string _configPath;
    private ServerConfiguration _configuration;

    public ConfigurationManager(string configPath = "appsettings.json")
    {
        _configPath = configPath;
        _configuration = new ServerConfiguration();
        LoadConfiguration();
    }

    public ServerConfiguration Configuration => _configuration;

    public void ReloadConfiguration()
    {
        LoadConfiguration();
    }

    public void UpdateConfiguration(string key, object value)
    {
        // Simple reflection-based property update
        var property = typeof(ServerConfiguration).GetProperty(key);
        if (property != null && property.CanWrite)
        {
            try
            {
                var convertedValue = Convert.ChangeType(value, property.PropertyType);
                property.SetValue(_configuration, convertedValue);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to update configuration key '{key}': {ex.Message}", ex);
            }
        }
        else
        {
            throw new ArgumentException($"Configuration key '{key}' not found or not writable");
        }
    }

    private void LoadConfiguration()
    {
        // Load from JSON file if it exists
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<ServerConfiguration>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config != null)
                {
                    _configuration = config;
                }
            }
            catch (Exception ex)
            {
                // Log error but continue with default configuration
                Console.WriteLine($"Warning: Failed to load configuration from file: {ex.Message}");
            }
        }

        // Override with environment variables
        LoadFromEnvironmentVariables();
    }

    private void LoadFromEnvironmentVariables()
    {
        // Port
        var portEnv = Environment.GetEnvironmentVariable("NOSQL_PORT");
        if (!string.IsNullOrEmpty(portEnv) && int.TryParse(portEnv, out int port))
        {
            _configuration.Port = port;
        }

        // MaxCacheSize
        var maxCacheSizeEnv = Environment.GetEnvironmentVariable("NOSQL_MAX_CACHE_SIZE");
        if (!string.IsNullOrEmpty(maxCacheSizeEnv) && int.TryParse(maxCacheSizeEnv, out int maxCacheSize))
        {
            _configuration.MaxCacheSize = maxCacheSize;
        }

        // CacheTimeoutMinutes
        var cacheTimeoutEnv = Environment.GetEnvironmentVariable("NOSQL_CACHE_TIMEOUT_MINUTES");
        if (!string.IsNullOrEmpty(cacheTimeoutEnv) && int.TryParse(cacheTimeoutEnv, out int cacheTimeout))
        {
            _configuration.CacheTimeoutMinutes = cacheTimeout;
        }

        // StoragePath
        var storagePathEnv = Environment.GetEnvironmentVariable("NOSQL_STORAGE_PATH");
        if (!string.IsNullOrEmpty(storagePathEnv))
        {
            _configuration.StoragePath = storagePathEnv;
        }

        // MaxConcurrentConnections
        var maxConnectionsEnv = Environment.GetEnvironmentVariable("NOSQL_MAX_CONNECTIONS");
        if (!string.IsNullOrEmpty(maxConnectionsEnv) && int.TryParse(maxConnectionsEnv, out int maxConnections))
        {
            _configuration.MaxConcurrentConnections = maxConnections;
        }

        // EnableDetailedLogging
        var enableLoggingEnv = Environment.GetEnvironmentVariable("NOSQL_ENABLE_DETAILED_LOGGING");
        if (!string.IsNullOrEmpty(enableLoggingEnv))
        {
            if (bool.TryParse(enableLoggingEnv, out bool enableLogging))
            {
                _configuration.EnableDetailedLogging = enableLogging;
            }
            else if (enableLoggingEnv.Equals("1", StringComparison.OrdinalIgnoreCase) || 
                     enableLoggingEnv.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                _configuration.EnableDetailedLogging = true;
            }
        }

        // DatabaseTimeoutSeconds
        var dbTimeoutEnv = Environment.GetEnvironmentVariable("NOSQL_DB_TIMEOUT_SECONDS");
        if (!string.IsNullOrEmpty(dbTimeoutEnv) && int.TryParse(dbTimeoutEnv, out int dbTimeout))
        {
            _configuration.DatabaseTimeoutSeconds = dbTimeout;
        }

        // MasterPassword
        var masterPasswordEnv = Environment.GetEnvironmentVariable("NOSQL_MASTER_PASSWORD");
        if (!string.IsNullOrEmpty(masterPasswordEnv))
        {
            _configuration.MasterPassword = masterPasswordEnv;
        }

        // RequireAuthentication
        var requireAuthEnv = Environment.GetEnvironmentVariable("NOSQL_REQUIRE_AUTHENTICATION");
        if (!string.IsNullOrEmpty(requireAuthEnv))
        {
            if (bool.TryParse(requireAuthEnv, out bool requireAuth))
            {
                _configuration.RequireAuthentication = requireAuth;
            }
            else if (requireAuthEnv.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                     requireAuthEnv.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                _configuration.RequireAuthentication = true;
            }
        }

        // TokenExpirationHours
        var tokenExpirationEnv = Environment.GetEnvironmentVariable("NOSQL_TOKEN_EXPIRATION_HOURS");
        if (!string.IsNullOrEmpty(tokenExpirationEnv) && int.TryParse(tokenExpirationEnv, out int tokenExpiration))
        {
            _configuration.TokenExpirationHours = tokenExpiration;
        }
    }
}