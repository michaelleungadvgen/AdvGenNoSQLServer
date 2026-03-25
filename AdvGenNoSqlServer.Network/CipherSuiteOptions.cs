// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Net.Security;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Configuration options for TLS cipher suites
    /// </summary>
    public class CipherSuiteOptions
    {
        /// <summary>
        /// Gets or sets whether to use the default strong cipher suites only (default: true)
        /// When enabled, weak ciphers like RC4, DES, 3DES, MD5, and SHA1 are disabled
        /// </summary>
        public bool UseStrongCipherSuitesOnly { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to allow RC4 cipher suites (default: false)
        /// RC4 is cryptographically broken and should not be used
        /// </summary>
        public bool AllowRc4 { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to allow DES and 3DES cipher suites (default: false)
        /// DES is insecure due to small key size, 3DES is deprecated
        /// </summary>
        public bool AllowDes { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to allow MD5 hash algorithms (default: false)
        /// MD5 is cryptographically broken and should not be used
        /// </summary>
        public bool AllowMd5 { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to allow SHA1 hash algorithms (default: false)
        /// SHA1 is deprecated and should be avoided for new connections
        /// </summary>
        public bool AllowSha1 { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to allow NULL encryption (default: false)
        /// NULL encryption provides no confidentiality and should never be used
        /// </summary>
        public bool AllowNullEncryption { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to allow anonymous key exchange (default: false)
        /// Anonymous key exchange provides no authentication
        /// </summary>
        public bool AllowAnonymous { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to allow export-grade cryptography (default: false)
        /// Export-grade ciphers are intentionally weak and should not be used
        /// </summary>
        public bool AllowExport { get; set; } = false;

        /// <summary>
        /// Gets or sets the list of explicitly allowed cipher suites
        /// If specified, only these cipher suites will be permitted
        /// </summary>
        public List<TlsCipherSuite>? AllowedCipherSuites { get; set; }

        /// <summary>
        /// Gets or sets the list of explicitly blocked cipher suites
        /// These cipher suites will always be rejected even if they would otherwise be allowed
        /// </summary>
        public List<TlsCipherSuite>? BlockedCipherSuites { get; set; }

        /// <summary>
        /// Gets or sets the minimum cipher strength in bits (default: 128)
        /// Ciphers with lower strength will be rejected
        /// </summary>
        public int MinimumCipherStrength { get; set; } = 128;

        /// <summary>
        /// Gets or sets whether to prefer perfect forward secrecy (PFS) ciphers (default: true)
        /// When enabled, ECDHE and DHE ciphers are preferred
        /// </summary>
        public bool PreferForwardSecrecy { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to allow TLS 1.3 specific cipher suites (default: true)
        /// TLS 1.3 uses different cipher suites than earlier versions
        /// </summary>
        public bool AllowTls13Ciphers { get; set; } = true;

        /// <summary>
        /// Creates a copy of these options
        /// </summary>
        public CipherSuiteOptions Clone()
        {
            return new CipherSuiteOptions
            {
                UseStrongCipherSuitesOnly = this.UseStrongCipherSuitesOnly,
                AllowRc4 = this.AllowRc4,
                AllowDes = this.AllowDes,
                AllowMd5 = this.AllowMd5,
                AllowSha1 = this.AllowSha1,
                AllowNullEncryption = this.AllowNullEncryption,
                AllowAnonymous = this.AllowAnonymous,
                AllowExport = this.AllowExport,
                AllowedCipherSuites = this.AllowedCipherSuites?.ToList(),
                BlockedCipherSuites = this.BlockedCipherSuites?.ToList(),
                MinimumCipherStrength = this.MinimumCipherStrength,
                PreferForwardSecrecy = this.PreferForwardSecrecy,
                AllowTls13Ciphers = this.AllowTls13Ciphers
            };
        }

        /// <summary>
        /// Validates the cipher suite options
        /// </summary>
        /// <returns>True if options are valid, false otherwise</returns>
        public bool Validate(out string? errorMessage)
        {
            if (MinimumCipherStrength < 0)
            {
                errorMessage = "Minimum cipher strength must be non-negative";
                return false;
            }

            if (MinimumCipherStrength > 0 && MinimumCipherStrength < 40)
            {
                errorMessage = "Minimum cipher strength is very low (< 40 bits), this is insecure";
                return false;
            }

            // Check for conflicting settings
            if (UseStrongCipherSuitesOnly && AllowRc4)
            {
                errorMessage = "Cannot use strong cipher suites only and allow RC4 simultaneously";
                return false;
            }

            if (UseStrongCipherSuitesOnly && AllowDes)
            {
                errorMessage = "Cannot use strong cipher suites only and allow DES/3DES simultaneously";
                return false;
            }

            if (UseStrongCipherSuitesOnly && AllowMd5)
            {
                errorMessage = "Cannot use strong cipher suites only and allow MD5 simultaneously";
                return false;
            }

            if (UseStrongCipherSuitesOnly && AllowNullEncryption)
            {
                errorMessage = "Cannot use strong cipher suites only and allow NULL encryption simultaneously";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}
