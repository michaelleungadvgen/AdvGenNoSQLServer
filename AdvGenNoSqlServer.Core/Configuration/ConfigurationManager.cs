// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// The environment name resolved from DOTNET_ENVIRONMENT / ASPNETCORE_ENVIRONMENT (empty if unset).
    /// Determines which appsettings.{Environment}.json overlay is applied.
    /// </summary>
    public string EnvironmentName { get; }

    /// <summary>True when <see cref="EnvironmentName"/> equals "Production" (case-insensitive).</summary>
    public bool IsProduction => string.Equals(EnvironmentName, "Production", StringComparison.OrdinalIgnoreCase);

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
        EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? string.Empty;

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
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100); // Brief delay to ensure file write is complete
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

        // Never apply an invalid configuration to a running server in Production.
        if (IsProduction)
        {
            var errors = newConfig.Validate(true);
            if (errors.Count > 0)
            {
                _configuration = oldConfig;
                Console.WriteLine($"[Config] ERROR: Reloaded configuration is invalid; keeping previous settings: {string.Join("; ", errors)}");
                return;
            }
        }

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
        var newConfig = LoadJsonLayered();

        // Override with environment variables
        LoadFromEnvironmentVariables(newConfig);

        _configuration = newConfig;
    }

    /// <summary>
    /// Loads the base config file and overlays appsettings.{Environment}.json on top of it.
    /// Malformed JSON is fatal in Production; otherwise a warning is printed and the
    /// remaining layers/defaults are used.
    /// </summary>
    private ServerConfiguration LoadJsonLayered()
    {
        var merged = new JsonObject();
        MergeJsonFile(_configPath, merged);

        if (!string.IsNullOrEmpty(EnvironmentName))
        {
            var overlayPath = Path.Combine(_configDirectory, $"appsettings.{EnvironmentName}.json");
            if (!string.Equals(overlayPath, Path.GetFullPath(_configPath), StringComparison.OrdinalIgnoreCase))
                MergeJsonFile(overlayPath, merged);
        }

        if (merged.Count == 0)
            return new ServerConfiguration();

        try
        {
            return merged.Deserialize<ServerConfiguration>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new ServerConfiguration();
        }
        catch (Exception ex)
        {
            if (IsProduction)
                throw new InvalidOperationException(
                    $"Configuration is invalid and cannot be used in Production: {ex.Message}", ex);
            Console.WriteLine($"[Config] Warning: Failed to deserialize configuration: {ex.Message}");
            return new ServerConfiguration();
        }
    }

    private void MergeJsonFile(string path, JsonObject target)
    {
        if (!File.Exists(path))
            return;

        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject obj)
                return;

            foreach (var (key, value) in obj)
                target[key] = value?.DeepClone();
        }
        catch (Exception ex)
        {
            if (IsProduction)
                throw new InvalidOperationException(
                    $"Configuration file '{path}' is invalid and cannot be used in Production: {ex.Message}", ex);
            Console.WriteLine($"[Config] Warning: Failed to load configuration from '{path}': {ex.Message}");
        }
    }

    /// <summary>
    /// Validates the current configuration; returns a list of errors (empty = valid).
    /// Production rules are applied when <see cref="IsProduction"/> is true.
    /// </summary>
    public IReadOnlyList<string> Validate() => _configuration.Validate(IsProduction);

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

        // Host
        var hostEnv = Environment.GetEnvironmentVariable("NOSQL_HOST");
        if (!string.IsNullOrEmpty(hostEnv))
        {
            config.Host = hostEnv;
        }

        // EnableSsl
        var enableSslEnv = Environment.GetEnvironmentVariable("NOSQL_ENABLE_SSL");
        if (!string.IsNullOrEmpty(enableSslEnv) && bool.TryParse(enableSslEnv, out bool enableSsl))
        {
            config.EnableSsl = enableSsl;
        }

        // SslCertificatePath / SslCertificatePassword
        var sslCertPathEnv = Environment.GetEnvironmentVariable("NOSQL_SSL_CERT_PATH");
        if (!string.IsNullOrEmpty(sslCertPathEnv))
        {
            config.SslCertificatePath = sslCertPathEnv;
        }
        var sslCertPasswordEnv = Environment.GetEnvironmentVariable("NOSQL_SSL_CERT_PASSWORD");
        if (!string.IsNullOrEmpty(sslCertPasswordEnv))
        {
            config.SslCertificatePassword = sslCertPasswordEnv;
        }

        // JwtSecretKey
        var jwtSecretEnv = Environment.GetEnvironmentVariable("NOSQL_JWT_SECRET_KEY");
        if (!string.IsNullOrEmpty(jwtSecretEnv))
        {
            config.JwtSecretKey = jwtSecretEnv;
        }

        // AnonymousRole
        var anonymousRoleEnv = Environment.GetEnvironmentVariable("NOSQL_ANONYMOUS_ROLE");
        if (!string.IsNullOrEmpty(anonymousRoleEnv))
        {
            config.AnonymousRole = anonymousRoleEnv;
        }

        // AdminApiKey
        var adminApiKeyEnv = Environment.GetEnvironmentVariable("NOSQL_ADMIN_API_KEY");
        if (!string.IsNullOrEmpty(adminApiKeyEnv))
        {
            config.AdminApiKey = adminApiKeyEnv;
        }

        // MaxMessageSizeMb
        var maxMessageSizeEnv = Environment.GetEnvironmentVariable("NOSQL_MAX_MESSAGE_SIZE_MB");
        if (!string.IsNullOrEmpty(maxMessageSizeEnv) && int.TryParse(maxMessageSizeEnv, out int maxMessageSize))
        {
            config.MaxMessageSizeMb = maxMessageSize;
        }

        // Pbkdf2Iterations
        var pbkdf2Env = Environment.GetEnvironmentVariable("NOSQL_PBKDF2_ITERATIONS");
        if (!string.IsNullOrEmpty(pbkdf2Env) && int.TryParse(pbkdf2Env, out int pbkdf2Iterations))
        {
            config.Pbkdf2Iterations = pbkdf2Iterations;
        }

        // CorsAllowedOrigins (semicolon-separated)
        var corsOriginsEnv = Environment.GetEnvironmentVariable("NOSQL_CORS_ORIGINS");
        if (!string.IsNullOrEmpty(corsOriginsEnv))
        {
            config.CorsAllowedOrigins = corsOriginsEnv.Split(';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        // AdminApiPort
        var adminApiPortEnv = Environment.GetEnvironmentVariable("NOSQL_ADMIN_HTTP_PORT");
        if (!string.IsNullOrEmpty(adminApiPortEnv) && int.TryParse(adminApiPortEnv, out int adminApiPort))
        {
            config.AdminApiPort = adminApiPort;
        }

        // AdminApiUseHttps
        var adminApiUseHttpsEnv = Environment.GetEnvironmentVariable("NOSQL_ADMIN_HTTP_USE_HTTPS");
        if (!string.IsNullOrEmpty(adminApiUseHttpsEnv) && bool.TryParse(adminApiUseHttpsEnv, out bool adminApiUseHttps))
        {
            config.AdminApiUseHttps = adminApiUseHttps;
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
