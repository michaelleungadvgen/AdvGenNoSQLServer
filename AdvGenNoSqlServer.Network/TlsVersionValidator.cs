// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Security.Authentication;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Validates TLS versions and enforces minimum TLS version requirements
    /// </summary>
    public static class TlsVersionValidator
    {
        /// <summary>
        /// Validates that the negotiated TLS protocol meets the minimum version requirement
        /// </summary>
        /// <param name="negotiatedProtocol">The negotiated TLS protocol version</param>
        /// <param name="minimumVersion">The minimum required TLS version</param>
        /// <param name="requireMinimumVersion">Whether to enforce the minimum version requirement</param>
        /// <returns>True if the negotiated version is acceptable, false otherwise</returns>
        public static bool ValidateTlsVersion(
            SslProtocols negotiatedProtocol,
            SslProtocols minimumVersion,
            bool requireMinimumVersion)
        {
            if (!requireMinimumVersion)
                return true;

            // If no minimum is specified, accept any version
            if (minimumVersion == SslProtocols.None)
                return true;

            // Check if the negotiated protocol meets the minimum requirement
            return IsTlsVersionAtLeast(negotiatedProtocol, minimumVersion);
        }

        /// <summary>
        /// Validates that the client's proposed TLS versions meet the minimum requirement
        /// </summary>
        /// <param name="clientSupportedProtocols">The TLS versions the client supports</param>
        /// <param name="minimumVersion">The minimum required TLS version</param>
        /// <returns>True if the client supports at least the minimum version, false otherwise</returns>
        public static bool ClientSupportsMinimumVersion(
            SslProtocols clientSupportedProtocols,
            SslProtocols minimumVersion)
        {
            if (minimumVersion == SslProtocols.None)
                return true;

            // Check if client supports at least the minimum version
            return (clientSupportedProtocols & minimumVersion) != 0;
        }

        /// <summary>
        /// Gets a user-friendly name for a TLS protocol version
        /// </summary>
        /// <param name="protocol">The TLS protocol version</param>
        /// <returns>A human-readable name for the protocol</returns>
        public static string GetTlsVersionName(SslProtocols protocol)
        {
            return protocol switch
            {
                SslProtocols.Tls => "TLS 1.0",
                SslProtocols.Tls11 => "TLS 1.1",
                SslProtocols.Tls12 => "TLS 1.2",
                SslProtocols.Tls13 => "TLS 1.3",
                SslProtocols.Ssl2 => "SSL 2.0",
                SslProtocols.Ssl3 => "SSL 3.0",
                SslProtocols.None => "None",
                _ => $"Unknown ({protocol})"
            };
        }

        /// <summary>
        /// Checks if a TLS version is considered secure (TLS 1.2 or higher)
        /// </summary>
        /// <param name="protocol">The TLS protocol version</param>
        /// <returns>True if the version is considered secure, false otherwise</returns>
        public static bool IsSecureTlsVersion(SslProtocols protocol)
        {
            return protocol == SslProtocols.Tls12 || 
                   protocol == SslProtocols.Tls13;
        }

        /// <summary>
        /// Checks if a TLS version is deprecated and should not be used (SSL 2.0, SSL 3.0, TLS 1.0, TLS 1.1)
        /// </summary>
        /// <param name="protocol">The TLS protocol version</param>
        /// <returns>True if the version is deprecated, false otherwise</returns>
        public static bool IsDeprecatedTlsVersion(SslProtocols protocol)
        {
            return protocol == SslProtocols.Ssl2 || 
                   protocol == SslProtocols.Ssl3 ||
                   protocol == SslProtocols.Tls ||
                   protocol == SslProtocols.Tls11;
        }

        /// <summary>
        /// Gets the recommended default allowed TLS versions (TLS 1.2 and 1.3)
        /// </summary>
        /// <returns>The recommended allowed TLS versions</returns>
        public static SslProtocols GetRecommendedAllowedTlsVersions()
        {
            return SslProtocols.Tls12 | SslProtocols.Tls13;
        }

        /// <summary>
        /// Gets the strongest TLS version from a bitmask of protocols
        /// </summary>
        /// <param name="protocols">The protocol bitmask</param>
        /// <returns>The strongest TLS version in the bitmask</returns>
        public static SslProtocols GetStrongestTlsVersion(SslProtocols protocols)
        {
            if ((protocols & SslProtocols.Tls13) != 0)
                return SslProtocols.Tls13;
            if ((protocols & SslProtocols.Tls12) != 0)
                return SslProtocols.Tls12;
            if ((protocols & SslProtocols.Tls11) != 0)
                return SslProtocols.Tls11;
            if ((protocols & SslProtocols.Tls) != 0)
                return SslProtocols.Tls;
            if ((protocols & SslProtocols.Ssl3) != 0)
                return SslProtocols.Ssl3;
            if ((protocols & SslProtocols.Ssl2) != 0)
                return SslProtocols.Ssl2;

            return SslProtocols.None;
        }

        /// <summary>
        /// Compares two TLS versions to determine if the first is at least as strong as the second
        /// </summary>
        /// <param name="version">The version to check</param>
        /// <param name="minimumVersion">The minimum required version</param>
        /// <returns>True if version >= minimumVersion, false otherwise</returns>
        private static bool IsTlsVersionAtLeast(SslProtocols version, SslProtocols minimumVersion)
        {
            var versionRank = GetTlsVersionRank(version);
            var minimumRank = GetTlsVersionRank(minimumVersion);

            return versionRank >= minimumRank;
        }

        /// <summary>
        /// Gets a numeric rank for a TLS version for comparison purposes
        /// Higher numbers indicate stronger/more recent versions
        /// </summary>
        /// <param name="protocol">The TLS protocol version</param>
        /// <returns>A numeric rank for comparison</returns>
        private static int GetTlsVersionRank(SslProtocols protocol)
        {
            return protocol switch
            {
                SslProtocols.Ssl2 => 1,
                SslProtocols.Ssl3 => 2,
                SslProtocols.Tls => 3,   // TLS 1.0
                SslProtocols.Tls11 => 4, // TLS 1.1
                SslProtocols.Tls12 => 5, // TLS 1.2
                SslProtocols.Tls13 => 6, // TLS 1.3
                _ => 0
            };
        }
    }

    /// <summary>
    /// Exception thrown when a TLS version validation fails
    /// </summary>
    public class TlsVersionException : Exception
    {
        /// <summary>
        /// The negotiated TLS version that was rejected
        /// </summary>
        public SslProtocols NegotiatedVersion { get; }

        /// <summary>
        /// The minimum TLS version that was required
        /// </summary>
        public SslProtocols MinimumVersion { get; }

        /// <summary>
        /// Creates a new TlsVersionException
        /// </summary>
        public TlsVersionException(string message, SslProtocols negotiatedVersion, SslProtocols minimumVersion)
            : base(message)
        {
            NegotiatedVersion = negotiatedVersion;
            MinimumVersion = minimumVersion;
        }

        /// <summary>
        /// Creates a new TlsVersionException with an inner exception
        /// </summary>
        public TlsVersionException(string message, Exception innerException, SslProtocols negotiatedVersion, SslProtocols minimumVersion)
            : base(message, innerException)
        {
            NegotiatedVersion = negotiatedVersion;
            MinimumVersion = minimumVersion;
        }
    }
}
