// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;
using System.IO;
using System;

namespace AdvGenNoSqlServer.Core.Configuration;

/// <summary>
/// Event arguments for configuration change events
/// </summary>
public class ConfigurationChangedEventArgs : EventArgs
{
    /// <summary>
    /// The previous configuration before the change
    /// </summary>
    public ServerConfiguration OldConfiguration { get; }

    /// <summary>
    /// The new configuration after the change
    /// </summary>
    public ServerConfiguration NewConfiguration { get; }

    /// <summary>
    /// The source of the configuration change (e.g., "File", "Manual", "Environment")
    /// </summary>
    public string ChangeSource { get; }

    /// <summary>
    /// Timestamp when the change occurred
    /// </summary>
    public DateTime ChangeTime { get; }

    public ConfigurationChangedEventArgs(ServerConfiguration oldConfig, ServerConfiguration newConfig, string changeSource)
    {
        OldConfiguration = oldConfig;
        NewConfiguration = newConfig;
        ChangeSource = changeSource;
        ChangeTime = DateTime.UtcNow;
    }
}

/// <summary>
/// Implementation of configuration manager that reads from JSON files and environment variables
/// with support for hot-reloading when the configuration file changes
/// </summary>
public class ConfigurationManager : IConfigurationManager, IDisposable
{
    private readonly string _configPath;
    private readonly string _configDirectory;
    private readonly string _configFileName;
    private ServerConfiguration _configuration;
    private FileSystemWatcher? _fileWatcher;
    private readonly object _reloadLock = new object();
    private DateTime _lastReadTime = DateTime.MinValue;
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);
    private bool _isDisposed;
    private bool _enableHotReload;

    /// <summary>
    /// Event raised when the configuration is changed (either via hot-reload or manual reload)
    /// </summary>
    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

    /// <summary>
    /// Gets whether hot-reload is currently enabled
    /// </summary>
    public bool IsHotReloadEnabled => _enableHotReload && _fileWatcher != null;

    public ConfigurationManager(string configPath = "appsettings.json", bool enableHotReload = false)
    {
        _configPath = configPath;
        _configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".";
        _configFileName = Path.GetFileName(configPath);
        _configuration = new ServerConfiguration();
        _enableHotReload = enableHotReload;
        
        LoadConfiguration();
        
        if (enableHotReload)
        {
            EnableHotReload();
        }
    }

    public ServerConfiguration Configuration => _configuration;

    /// <summary>
    /// Enables hot-reload monitoring of the configuration file
    /// </summary>
    public void EnableHotReload()
    {
        if (_fileWatcher != null || _isDisposed)
        {
            return;
        }

        try
        {
            // Ensure the directory exists
            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
            }

            _fileWatcher = new FileSystemWatcher(_configDirectory, _configFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnConfigFileChanged;
            _fileWatcher.Renamed += OnConfigFileRenamed;
            _fileWatcher.Created += OnConfigFileCreated;
            _fileWatcher.Error += OnFileWatcherError;

            _enableHotReload = true;

            if (_configuration.EnableDetailedLogging)
            {
                Console.WriteLine($"[Config] Hot-reload enabled for: {_configPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Warning: Failed to enable hot-reload: {ex.Message}");
            _fileWatcher?.Dispose();
            _fileWatcher = null;
            _enableHotReload = false;
        }
    }

    /// <summary>
    /// Disables hot-reload monitoring of the configuration file
    /// </summary>
    public void DisableHotReload()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Changed -= OnConfigFileChanged;
            _fileWatcher.Renamed -= OnConfigFileRenamed;
            _fileWatcher.Created -= OnConfigFileCreated;
            _fileWatcher.Error -= OnFileWatcherError;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
        _enableHotReload = false;

        if (_configuration.EnableDetailedLogging)
        {
            Console.WriteLine("[Config] Hot-reload disabled");
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce rapid file change events
        lock (_reloadLock)
        {
            var now = DateTime.Now;
            if (now - _lastReadTime < _debounceInterval)
            {
                return;
            }
            _lastReadTime = now;
        }

        // Reload on a background thread to avoid blocking the watcher
        Task.Run(() =>
        {
            try
            {
                Thread.Sleep(100); // Brief delay to ensure file write is complete
                ReloadConfigurationInternal("File");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Error during hot-reload: {ex.Message}");
            }
        });
    }

    private void OnConfigFileRenamed(object sender, RenamedEventArgs e)
    {
        // If the file was renamed to our target name, reload
        if (string.Equals(e.Name, _configFileName, StringComparison.OrdinalIgnoreCase))
        {
            OnConfigFileChanged(sender, e);
        }
    }

    private void OnConfigFileCreated(object sender, FileSystemEventArgs e)
    {
        OnConfigFileChanged(sender, e);
    }

    private void OnFileWatcherError(object sender, ErrorEventArgs e)
    {
        Console.WriteLine($"[Config] File watcher error: {e.GetException().Message}");
        
        // Attempt to recreate the watcher
        try
        {
            DisableHotReload();
            if (_enableHotReload)
            {
                EnableHotReload();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to recreate file watcher: {ex.Message}");
        }
    }

    public void ReloadConfiguration()
    {
        ReloadConfigurationInternal("Manual");
    }

    private void ReloadConfigurationInternal(string changeSource)
    {
        var oldConfig = _configuration;
        LoadConfiguration();
        var newConfig = _configuration;

        // Notify subscribers
        ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs(oldConfig, newConfig, changeSource));

        if (_configuration.EnableDetailedLogging)
        {
            Console.WriteLine($"[Config] Configuration reloaded from {changeSource.ToLowerInvariant()} at {DateTime.UtcNow:O}");
        }
    }

    public void UpdateConfiguration(string key, object value)
    {
        var oldConfig = CloneConfiguration(_configuration);
        
        // Simple reflection-based property update
        var property = typeof(ServerConfiguration).GetProperty(key);
        if (property != null && property.CanWrite)
        {
            try
            {
                var convertedValue = Convert.ChangeType(value, property.PropertyType);
                property.SetValue(_configuration, convertedValue);
                
                // Notify subscribers
                ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs(oldConfig, _configuration, "Manual"));
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
        var newConfig = new ServerConfiguration();

        // Load from JSON file if it exists
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                var fileConfig = JsonSerializer.Deserialize<ServerConfiguration>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (fileConfig != null)
                {
                    newConfig = fileConfig;
                }
            }
            catch (Exception ex)
            {
                // Log error but continue with default configuration
                Console.WriteLine($"[Config] Warning: Failed to load configuration from file: {ex.Message}");
            }
        }

        // Override with environment variables
        LoadFromEnvironmentVariables(newConfig);

        _configuration = newConfig;
    }

    private void LoadFromEnvironmentVariables(ServerConfiguration config)
    {
        // Port
        var portEnv = Environment.GetEnvironmentVariable("NOSQL_PORT");
        if (!string.IsNullOrEmpty(portEnv) && int.TryParse(portEnv, out int port))
        {
            config.Port = port;
        }

        // MaxCacheItemCount
        var maxCacheItemCountEnv = Environment.GetEnvironmentVariable("NOSQL_MAX_CACHE_ITEM_COUNT");
        if (!string.IsNullOrEmpty(maxCacheItemCountEnv) && int.TryParse(maxCacheItemCountEnv, out int maxCacheItemCount))
        {
            config.MaxCacheItemCount = maxCacheItemCount;
        }

        // MaxCacheSizeInBytes
        var maxCacheSizeInBytesEnv = Environment.GetEnvironmentVariable("NOSQL_MAX_CACHE_SIZE_BYTES");
        if (!string.IsNullOrEmpty(maxCacheSizeInBytesEnv) && long.TryParse(maxCacheSizeInBytesEnv, out long maxCacheSizeInBytes))
        {
            config.MaxCacheSizeInBytes = maxCacheSizeInBytes;
        }

        // DefaultCacheTtlMilliseconds
        var defaultCacheTtlEnv = Environment.GetEnvironmentVariable("NOSQL_DEFAULT_CACHE_TTL_MS");
        if (!string.IsNullOrEmpty(defaultCacheTtlEnv) && long.TryParse(defaultCacheTtlEnv, out long defaultCacheTtl))
        {
            config.DefaultCacheTtlMilliseconds = defaultCacheTtl;
        }

        // CacheTimeoutMinutes
        var cacheTimeoutEnv = Environment.GetEnvironmentVariable("NOSQL_CACHE_TIMEOUT_MINUTES");
        if (!string.IsNullOrEmpty(cacheTimeoutEnv) && int.TryParse(cacheTimeoutEnv, out int cacheTimeout))
        {
            config.CacheTimeoutMinutes = cacheTimeout;
        }

        // StoragePath
        var storagePathEnv = Environment.GetEnvironmentVariable("NOSQL_STORAGE_PATH");
        if (!string.IsNullOrEmpty(storagePathEnv))
        {
            config.StoragePath = storagePathEnv;
        }

        // MaxConcurrentConnections
        var maxConnectionsEnv = Environment.GetEnvironmentVariable("NOSQL_MAX_CONNECTIONS");
        if (!string.IsNullOrEmpty(maxConnectionsEnv) && int.TryParse(maxConnectionsEnv, out int maxConnections))
        {
            config.MaxConcurrentConnections = maxConnections;
        }

        // EnableDetailedLogging
        var enableLoggingEnv = Environment.GetEnvironmentVariable("NOSQL_ENABLE_DETAILED_LOGGING");
        if (!string.IsNullOrEmpty(enableLoggingEnv))
        {
            if (bool.TryParse(enableLoggingEnv, out bool enableLogging))
            {
                config.EnableDetailedLogging = enableLogging;
            }
            else if (enableLoggingEnv.Equals("1", StringComparison.OrdinalIgnoreCase) || 
                     enableLoggingEnv.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                config.EnableDetailedLogging = true;
            }
        }

        // DatabaseTimeoutSeconds
        var dbTimeoutEnv = Environment.GetEnvironmentVariable("NOSQL_DB_TIMEOUT_SECONDS");
        if (!string.IsNullOrEmpty(dbTimeoutEnv) && int.TryParse(dbTimeoutEnv, out int dbTimeout))
        {
            config.DatabaseTimeoutSeconds = dbTimeout;
        }

        // MasterPassword
        var masterPasswordEnv = Environment.GetEnvironmentVariable("NOSQL_MASTER_PASSWORD");
        if (!string.IsNullOrEmpty(masterPasswordEnv))
        {
            config.MasterPassword = masterPasswordEnv;
        }

        // RequireAuthentication
        var requireAuthEnv = Environment.GetEnvironmentVariable("NOSQL_REQUIRE_AUTHENTICATION");
        if (!string.IsNullOrEmpty(requireAuthEnv))
        {
            if (bool.TryParse(requireAuthEnv, out bool requireAuth))
            {
                config.RequireAuthentication = requireAuth;
            }
            else if (requireAuthEnv.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                     requireAuthEnv.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                config.RequireAuthentication = true;
            }
        }

        // TokenExpirationHours
        var tokenExpirationEnv = Environment.GetEnvironmentVariable("NOSQL_TOKEN_EXPIRATION_HOURS");
        if (!string.IsNullOrEmpty(tokenExpirationEnv) && int.TryParse(tokenExpirationEnv, out int tokenExpiration))
        {
            config.TokenExpirationHours = tokenExpiration;
        }
    }

    /// <summary>
    /// Creates a shallow copy of the configuration
    /// </summary>
    private static ServerConfiguration CloneConfiguration(ServerConfiguration source)
    {
        // Serialize and deserialize to create a deep copy
        try
        {
            var json = JsonSerializer.Serialize(source);
            return JsonSerializer.Deserialize<ServerConfiguration>(json) ?? new ServerConfiguration();
        }
        catch
        {
            // Fallback to shallow copy via reflection if serialization fails
            var clone = new ServerConfiguration();
            foreach (var prop in typeof(ServerConfiguration).GetProperties())
            {
                if (prop.CanWrite && prop.CanRead)
                {
                    prop.SetValue(clone, prop.GetValue(source));
                }
            }
            return clone;
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            DisableHotReload();
            _isDisposed = true;
        }
    }
}
