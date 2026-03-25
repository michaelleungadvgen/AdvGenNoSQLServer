// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Net.Security;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Validator for TLS cipher suites
    /// </summary>
    public static class CipherSuiteValidator
    {
        // Known weak cipher suites that should be rejected by default
        private static readonly HashSet<TlsCipherSuite> _rc4Ciphers = new()
        {
            TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA,
            TlsCipherSuite.TLS_RSA_WITH_RC4_128_MD5,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_RC4_128_SHA,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_RC4_128_SHA,
            TlsCipherSuite.TLS_PSK_WITH_RC4_128_SHA,
            TlsCipherSuite.TLS_DHE_PSK_WITH_RC4_128_SHA,
            TlsCipherSuite.TLS_RSA_PSK_WITH_RC4_128_SHA
        };

        private static readonly HashSet<TlsCipherSuite> _desCiphers = new()
        {
            TlsCipherSuite.TLS_RSA_WITH_DES_CBC_SHA,
            TlsCipherSuite.TLS_DHE_RSA_WITH_DES_CBC_SHA,
            TlsCipherSuite.TLS_RSA_WITH_3DES_EDE_CBC_SHA,
            TlsCipherSuite.TLS_DHE_RSA_WITH_3DES_EDE_CBC_SHA,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_3DES_EDE_CBC_SHA,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_3DES_EDE_CBC_SHA
        };

        private static readonly HashSet<TlsCipherSuite> _md5Ciphers = new()
        {
            TlsCipherSuite.TLS_RSA_WITH_RC4_128_MD5,
            TlsCipherSuite.TLS_RSA_EXPORT_WITH_RC4_40_MD5
        };

        private static readonly HashSet<TlsCipherSuite> _sha1Ciphers = new()
        {
            TlsCipherSuite.TLS_RSA_WITH_NULL_SHA,
            TlsCipherSuite.TLS_RSA_WITH_DES_CBC_SHA,
            TlsCipherSuite.TLS_RSA_WITH_3DES_EDE_CBC_SHA,
            TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA,
            TlsCipherSuite.TLS_DHE_RSA_WITH_DES_CBC_SHA,
            TlsCipherSuite.TLS_DHE_RSA_WITH_3DES_EDE_CBC_SHA,
            TlsCipherSuite.TLS_DHE_DSS_WITH_DES_CBC_SHA,
            TlsCipherSuite.TLS_DHE_DSS_WITH_3DES_EDE_CBC_SHA,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_3DES_EDE_CBC_SHA,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_RC4_128_SHA,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_3DES_EDE_CBC_SHA,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_RC4_128_SHA,
            TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
            TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA,
            TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_CBC_SHA,
            TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_CBC_SHA,
            TlsCipherSuite.TLS_DHE_DSS_WITH_AES_128_CBC_SHA,
            TlsCipherSuite.TLS_DHE_DSS_WITH_AES_256_CBC_SHA,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA
        };

        private static readonly HashSet<TlsCipherSuite> _nullEncryptionCiphers = new()
        {
            TlsCipherSuite.TLS_RSA_WITH_NULL_MD5,
            TlsCipherSuite.TLS_RSA_WITH_NULL_SHA,
            TlsCipherSuite.TLS_RSA_WITH_NULL_SHA256,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_NULL_SHA,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_NULL_SHA
        };

        private static readonly HashSet<TlsCipherSuite> _anonymousCiphers = new()
        {
            TlsCipherSuite.TLS_DH_anon_WITH_RC4_128_MD5,
            TlsCipherSuite.TLS_DH_anon_WITH_DES_CBC_SHA,
            TlsCipherSuite.TLS_DH_anon_WITH_3DES_EDE_CBC_SHA,
            TlsCipherSuite.TLS_DH_anon_WITH_AES_128_CBC_SHA,
            TlsCipherSuite.TLS_DH_anon_WITH_AES_256_CBC_SHA,
            TlsCipherSuite.TLS_DH_anon_WITH_AES_128_CBC_SHA256,
            TlsCipherSuite.TLS_DH_anon_WITH_AES_256_CBC_SHA256
        };

        private static readonly HashSet<TlsCipherSuite> _exportCiphers = new()
        {
            TlsCipherSuite.TLS_RSA_EXPORT_WITH_RC4_40_MD5,
            TlsCipherSuite.TLS_RSA_EXPORT_WITH_DES40_CBC_SHA,
            TlsCipherSuite.TLS_DHE_RSA_EXPORT_WITH_DES40_CBC_SHA,
            TlsCipherSuite.TLS_DHE_DSS_EXPORT_WITH_DES40_CBC_SHA
        };

        // Strong cipher suites that are recommended
        private static readonly HashSet<TlsCipherSuite> _strongCiphers = new()
        {
            // TLS 1.3 ciphers (always strong)
            TlsCipherSuite.TLS_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
            TlsCipherSuite.TLS_AES_128_CCM_SHA256,
            TlsCipherSuite.TLS_AES_128_CCM_8_SHA256,

            // AES-GCM with ECDHE (strong, PFS)
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,

            // AES-GCM with DHE (strong, PFS)
            TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_DHE_DSS_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_DHE_DSS_WITH_AES_128_GCM_SHA256,

            // ChaCha20-Poly1305 with ECDHE (strong, PFS)
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,

            // ChaCha20-Poly1305 with DHE (strong, PFS)
            TlsCipherSuite.TLS_DHE_RSA_WITH_CHACHA20_POLY1305_SHA256,

            // AES-CCM with ECDHE (strong, PFS, authenticated encryption)
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CCM,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_CCM,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CCM_8,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_CCM_8,

            // AES-CBC with ECDHE and SHA-256 (acceptable with TLS 1.2+)
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA384,

            // AES-CBC with DHE and SHA-256 (acceptable with TLS 1.2+)
            TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_CBC_SHA256,
            TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_CBC_SHA256,
            TlsCipherSuite.TLS_DHE_DSS_WITH_AES_128_CBC_SHA256,
            TlsCipherSuite.TLS_DHE_DSS_WITH_AES_256_CBC_SHA256
        };

        // Cipher suites that provide perfect forward secrecy
        private static readonly HashSet<TlsCipherSuite> _forwardSecrecyCiphers = new()
        {
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_DHE_DSS_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_DHE_DSS_WITH_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
            TlsCipherSuite.TLS_DHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA384,
            TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_CBC_SHA256,
            TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_CBC_SHA256,
            TlsCipherSuite.TLS_DHE_DSS_WITH_AES_128_CBC_SHA256,
            TlsCipherSuite.TLS_DHE_DSS_WITH_AES_256_CBC_SHA256
        };

        /// <summary>
        /// Validates whether a cipher suite is allowed based on the given options
        /// </summary>
        /// <param name="cipherSuite">The cipher suite to validate</param>
        /// <param name="options">The cipher suite options</param>
        /// <returns>True if the cipher suite is allowed, false otherwise</returns>
        public static bool IsCipherAllowed(TlsCipherSuite cipherSuite, CipherSuiteOptions options)
        {
            // Check explicit blocked list first
            if (options.BlockedCipherSuites != null && options.BlockedCipherSuites.Contains(cipherSuite))
            {
                return false;
            }

            // Check explicit allowed list
            if (options.AllowedCipherSuites != null)
            {
                return options.AllowedCipherSuites.Contains(cipherSuite);
            }

            // Check for NULL encryption (never allow unless explicitly requested)
            if (_nullEncryptionCiphers.Contains(cipherSuite) && !options.AllowNullEncryption)
            {
                return false;
            }

            // Check for anonymous key exchange (never allow unless explicitly requested)
            if (_anonymousCiphers.Contains(cipherSuite) && !options.AllowAnonymous)
            {
                return false;
            }

            // Check for export-grade ciphers (never allow unless explicitly requested)
            if (_exportCiphers.Contains(cipherSuite) && !options.AllowExport)
            {
                return false;
            }

            // If using strong cipher suites only, check against strong list
            if (options.UseStrongCipherSuitesOnly)
            {
                return _strongCiphers.Contains(cipherSuite);
            }

            // Check individual weak cipher settings
            if (_rc4Ciphers.Contains(cipherSuite) && !options.AllowRc4)
            {
                return false;
            }

            if (_desCiphers.Contains(cipherSuite) && !options.AllowDes)
            {
                return false;
            }

            if (_md5Ciphers.Contains(cipherSuite) && !options.AllowMd5)
            {
                return false;
            }

            if (_sha1Ciphers.Contains(cipherSuite) && !options.AllowSha1)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets a list of recommended cipher suites for TLS 1.2+
        /// </summary>
        /// <param name="preferForwardSecrecy">Whether to prefer forward secrecy ciphers</param>
        /// <returns>List of recommended cipher suites</returns>
        public static List<TlsCipherSuite> GetRecommendedCipherSuites(bool preferForwardSecrecy = true)
        {
            var ciphers = _strongCiphers.ToList();

            if (preferForwardSecrecy)
            {
                // Order by forward secrecy first
                ciphers = ciphers
                    .OrderByDescending(c => _forwardSecrecyCiphers.Contains(c))
                    .ThenByDescending(c => GetCipherStrength(c))
                    .ToList();
            }
            else
            {
                ciphers = ciphers
                    .OrderByDescending(c => GetCipherStrength(c))
                    .ToList();
            }

            return ciphers;
        }

        /// <summary>
        /// Gets the list of TLS 1.3 cipher suites
        /// </summary>
        public static List<TlsCipherSuite> GetTls13CipherSuites()
        {
            return new List<TlsCipherSuite>
            {
                TlsCipherSuite.TLS_AES_256_GCM_SHA384,
                TlsCipherSuite.TLS_AES_128_GCM_SHA256,
                TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
                TlsCipherSuite.TLS_AES_128_CCM_SHA256,
                TlsCipherSuite.TLS_AES_128_CCM_8_SHA256
            };
        }

        /// <summary>
        /// Checks if a cipher suite provides perfect forward secrecy
        /// </summary>
        public static bool ProvidesForwardSecrecy(TlsCipherSuite cipherSuite)
        {
            return _forwardSecrecyCiphers.Contains(cipherSuite);
        }

        /// <summary>
        /// Gets the estimated cipher strength in bits
        /// </summary>
        public static int GetCipherStrength(TlsCipherSuite cipherSuite)
        {
            var cipherName = cipherSuite.ToString();

            // Check for specific cipher names to determine strength
            if (cipherName.Contains("_AES_256_"))
                return 256;
            if (cipherName.Contains("_AES_128_"))
                return 128;
            if (cipherName.Contains("_CHACHA20_"))
                return 256;
            if (cipherName.Contains("_3DES_"))
                return 168;
            if (cipherName.Contains("_DES_") || cipherName.Contains("_DES40_"))
                return 56;
            if (cipherName.Contains("_RC4_128_"))
                return 128;
            if (cipherName.Contains("_RC4_40_"))
                return 40;
            if (cipherName.Contains("_RC4_56_"))
                return 56;
            if (cipherName.Contains("_NULL_"))
                return 0;

            // Default to 128 for unknown ciphers
            return 128;
        }

        /// <summary>
        /// Checks if a cipher suite is considered weak
        /// </summary>
        public static bool IsWeakCipher(TlsCipherSuite cipherSuite)
        {
            return _rc4Ciphers.Contains(cipherSuite) ||
                   _desCiphers.Contains(cipherSuite) ||
                   _md5Ciphers.Contains(cipherSuite) ||
                   _nullEncryptionCiphers.Contains(cipherSuite) ||
                   _anonymousCiphers.Contains(cipherSuite) ||
                   _exportCiphers.Contains(cipherSuite) ||
                   GetCipherStrength(cipherSuite) < 128;
        }

        /// <summary>
        /// Gets a human-readable description of why a cipher might be considered weak
        /// </summary>
        public static string? GetCipherWeaknessReason(TlsCipherSuite cipherSuite)
        {
            // Check most severe issues first
            if (_nullEncryptionCiphers.Contains(cipherSuite))
                return "NULL encryption provides no confidentiality";
            if (_anonymousCiphers.Contains(cipherSuite))
                return "Anonymous key exchange provides no authentication";
            if (_exportCiphers.Contains(cipherSuite))
                return "Export-grade ciphers are intentionally weak";
            if (_rc4Ciphers.Contains(cipherSuite))
                return "RC4 is cryptographically broken";
            if (_desCiphers.Contains(cipherSuite))
                return "DES/3DES is deprecated and considered weak";
            if (_md5Ciphers.Contains(cipherSuite))
                return "MD5 is cryptographically broken";
            if (_sha1Ciphers.Contains(cipherSuite))
                return "SHA1 is deprecated";

            var strength = GetCipherStrength(cipherSuite);
            if (strength < 128)
                return $"Cipher strength ({strength} bits) is below recommended minimum (128 bits)";

            return null;
        }

        /// <summary>
        /// Gets a human-readable name for a TLS cipher suite
        /// </summary>
        public static string GetTlsCipherSuiteName(TlsCipherSuite cipherSuite)
        {
            return cipherSuite.ToString();
        }
    }
}
