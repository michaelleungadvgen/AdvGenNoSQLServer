namespace AdvGenNoSqlServer.Core.Configuration;

/// <summary>
/// Configuration settings for the NoSQL server
/// </summary>
public class ServerConfiguration
{
    /// <summary>
    /// The port number the server will listen on
    /// </summary>
    public int Port { get; set; } = 8080;

    /// <summary>
    /// The maximum number of items to store in the cache
    /// </summary>
    public int MaxCacheSize { get; set; } = 1000;

    /// <summary>
    /// The timeout for cache items in minutes
    /// </summary>
    public int CacheTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// The base path for file storage
    /// </summary>
    public string StoragePath { get; set; } = "data";

    /// <summary>
    /// The maximum number of concurrent connections
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 100;

    /// <summary>
    /// Whether to enable detailed logging
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// The timeout for database operations in seconds
    /// </summary>
    public int DatabaseTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Master password for server authentication (should be stored securely)
    /// </summary>
    public string? MasterPassword { get; set; }

    /// <summary>
    /// Whether authentication is required for server connections
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// Token expiration time in hours
    /// </summary>
    public int TokenExpirationHours { get; set; } = 24;
}