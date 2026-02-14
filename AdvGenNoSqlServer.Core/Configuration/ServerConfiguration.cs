// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
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
    public int Port { get; set; } = 9091;

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
    /// The maximum number of items to store in the cache (default: 10000)
    /// </summary>
    public int MaxCacheItemCount { get; set; } = 10000;

    /// <summary>
    /// The maximum size of the cache in bytes (default: 100MB)
    /// </summary>
    public long MaxCacheSizeInBytes { get; set; } = 104857600;

    /// <summary>
    /// The default TTL for cache items in milliseconds (default: 30 minutes)
    /// </summary>
    public long DefaultCacheTtlMilliseconds { get; set; } = 1800000;

    /// <summary>
    /// The timeout for cache items in minutes (legacy property, maps to TTL)
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

    #region JWT Configuration

    /// <summary>
    /// Secret key for JWT token signing (should be at least 32 characters)
    /// If not set, a secure key will be generated automatically
    /// </summary>
    public string? JwtSecretKey { get; set; }

    /// <summary>
    /// JWT token issuer (default: "AdvGenNoSqlServer")
    /// </summary>
    public string? JwtIssuer { get; set; } = "AdvGenNoSqlServer";

    /// <summary>
    /// JWT token audience (default: "AdvGenNoSqlClient")
    /// </summary>
    public string? JwtAudience { get; set; } = "AdvGenNoSqlClient";

    /// <summary>
    /// Whether to enable JWT authentication (default: true)
    /// </summary>
    public bool EnableJwtAuthentication { get; set; } = true;

    #endregion

    #region Encryption Configuration

    /// <summary>
    /// Master encryption key for data at rest (Base64 encoded, 32 bytes for AES-256)
    /// If not set, a random key will be generated (data will not persist across restarts)
    /// </summary>
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// Identifier for the current encryption key (for key rotation support)
    /// </summary>
    public string? EncryptionKeyId { get; set; }

    /// <summary>
    /// Whether to enable encryption for sensitive fields (default: false)
    /// </summary>
    public bool EnableFieldEncryption { get; set; } = false;

    /// <summary>
    /// Path to the key store file for encrypted key storage
    /// </summary>
    public string? KeyStorePath { get; set; }

    #endregion

    #region Pooling Configuration

    /// <summary>
    /// Whether to enable object pooling (default: true)
    /// </summary>
    public bool EnableObjectPooling { get; set; } = true;

    /// <summary>
    /// Maximum number of objects to keep in each object pool (default: 100)
    /// </summary>
    public int MaxObjectPoolSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of byte arrays to keep per bucket in the buffer pool (default: 100)
    /// </summary>
    public int MaxBufferArraysPerBucket { get; set; } = 100;

    /// <summary>
    /// Maximum size of byte arrays to pool (default: 1MB)
    /// </summary>
    public int MaxPooledBufferSize { get; set; } = 1024 * 1024;

    /// <summary>
    /// Whether to pre-populate object pools on startup (default: false)
    /// </summary>
    public bool PrePopulateObjectPools { get; set; } = false;

    /// <summary>
    /// Number of objects to pre-allocate for each pool when PrePopulateObjectPools is true (default: 10)
    /// </summary>
    public int PrePopulatePoolSize { get; set; } = 10;

    #endregion

    #region SSL/TLS Configuration

    /// <summary>
    /// Whether to enable SSL/TLS encryption for connections (default: false)
    /// </summary>
    public bool EnableSsl { get; set; } = false;

    /// <summary>
    /// Path to the SSL certificate file (PFX format)
    /// </summary>
    public string? SslCertificatePath { get; set; }

    /// <summary>
    /// Password for the SSL certificate file
    /// </summary>
    public string? SslCertificatePassword { get; set; }

    /// <summary>
    /// Thumbprint of the SSL certificate to use from the certificate store (Windows)
    /// </summary>
    public string? SslCertificateThumbprint { get; set; }

    /// <summary>
    /// Whether to use the certificate store instead of a file (default: false)
    /// </summary>
    public bool UseCertificateStore { get; set; } = false;

    /// <summary>
    /// SSL/TLS protocol version to use (default: TLS 1.2 and above)
    /// </summary>
    public System.Security.Authentication.SslProtocols SslProtocols { get; set; } = 
        System.Security.Authentication.SslProtocols.Tls12 | 
        System.Security.Authentication.SslProtocols.Tls13;

    /// <summary>
    /// Whether to require client certificates for mutual TLS (mTLS) (default: false)
    /// </summary>
    public bool RequireClientCertificate { get; set; } = false;

    /// <summary>
    /// Whether to check certificate revocation list (default: true)
    /// </summary>
    public bool CheckCertificateRevocation { get; set; } = true;

    /// <summary>
    /// Target host name for certificate validation (used by clients)
    /// </summary>
    public string? SslTargetHost { get; set; }

    #endregion
}
