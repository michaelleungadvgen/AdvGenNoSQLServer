// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Configuration;

/// <summary>
/// Configuration settings for the NoSQL server
/// </summary>
public class ServerConfiguration
{
    /// <summary>
    /// The host IP address to bind to (default: 0.0.0.0)
    /// </summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>
    /// The port number the server will listen on (default: 9090)
    /// </summary>
    public int Port { get; set; } = 9090;

    /// <summary>
    /// The maximum number of concurrent connections (default: 10000)
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 10000;

    /// <summary>
    /// Connection timeout duration (default: 30 seconds)
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Keep-alive interval for connections (default: 60 seconds)
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Size of the receive buffer (default: 65536)
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 65536;

    /// <summary>
    /// Size of the send buffer (default: 65536)
    /// </summary>
    public int SendBufferSize { get; set; } = 65536;

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
