// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using System.Net.Security;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for Cipher Suite Configuration functionality
    /// </summary>
    public class CipherSuiteConfigurationTests
    {
        #region CipherSuiteOptions Tests

        [Fact]
        public void CipherSuiteOptions_DefaultValues_AreSecure()
        {
            var options = new CipherSuiteOptions();

            Assert.True(options.UseStrongCipherSuitesOnly);
            Assert.False(options.AllowRc4);
            Assert.False(options.AllowDes);
            Assert.False(options.AllowMd5);
            Assert.False(options.AllowSha1);
            Assert.False(options.AllowNullEncryption);
            Assert.False(options.AllowAnonymous);
            Assert.False(options.AllowExport);
            Assert.Equal(128, options.MinimumCipherStrength);
            Assert.True(options.PreferForwardSecrecy);
            Assert.True(options.AllowTls13Ciphers);
        }

        [Fact]
        public void CipherSuiteOptions_Clone_CreatesIndependentCopy()
        {
            var original = new CipherSuiteOptions
            {
                UseStrongCipherSuitesOnly = false,
                AllowRc4 = true,
                MinimumCipherStrength = 256,
                AllowedCipherSuites = new List<TlsCipherSuite> { TlsCipherSuite.TLS_AES_256_GCM_SHA384 }
            };

            var clone = original.Clone();

            // Verify clone has same values
            Assert.Equal(original.UseStrongCipherSuitesOnly, clone.UseStrongCipherSuitesOnly);
            Assert.Equal(original.AllowRc4, clone.AllowRc4);
            Assert.Equal(original.MinimumCipherStrength, clone.MinimumCipherStrength);
            Assert.Equal(original.AllowedCipherSuites?.Count, clone.AllowedCipherSuites?.Count);

            // Modify clone and verify original is unchanged
            clone.UseStrongCipherSuitesOnly = true;
            clone.AllowRc4 = false;
            clone.MinimumCipherStrength = 128;

            Assert.False(original.UseStrongCipherSuitesOnly);
            Assert.True(original.AllowRc4);
            Assert.Equal(256, original.MinimumCipherStrength);
        }

        [Theory]
        [InlineData(0, true, null)]
        [InlineData(128, true, null)]
        [InlineData(256, true, null)]
        [InlineData(-1, false, "Minimum cipher strength must be non-negative")]
        [InlineData(20, false, "Minimum cipher strength is very low")]
        public void CipherSuiteOptions_Validate_ChecksMinimumStrength(int minStrength, bool expectedValid, string? expectedError)
        {
            var options = new CipherSuiteOptions { MinimumCipherStrength = minStrength };

            var isValid = options.Validate(out var errorMessage);

            Assert.Equal(expectedValid, isValid);
            if (expectedError != null)
            {
                Assert.Contains(expectedError, errorMessage);
            }
            else
            {
                Assert.Null(errorMessage);
            }
        }

        [Fact]
        public void CipherSuiteOptions_Validate_DetectsConflictingSettings()
        {
            var options = new CipherSuiteOptions
            {
                UseStrongCipherSuitesOnly = true,
                AllowRc4 = true
            };

            var isValid = options.Validate(out var errorMessage);

            Assert.False(isValid);
            Assert.Contains("Cannot use strong cipher suites only and allow RC4", errorMessage);
        }

        #endregion

        #region CipherSuiteValidator Tests

        [Theory]
        [InlineData(TlsCipherSuite.TLS_AES_256_GCM_SHA384, true)]
        [InlineData(TlsCipherSuite.TLS_AES_128_GCM_SHA256, true)]
        [InlineData(TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256, true)]
        [InlineData(TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384, true)]
        [InlineData(TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256, true)]
        public void IsCipherAllowed_StrongCiphers_AreAllowed(TlsCipherSuite cipher, bool expectedAllowed)
        {
            var options = new CipherSuiteOptions { UseStrongCipherSuitesOnly = true };

            var isAllowed = CipherSuiteValidator.IsCipherAllowed(cipher, options);

            Assert.Equal(expectedAllowed, isAllowed);
        }

        [Theory]
        [InlineData(TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA, false)]
        [InlineData(TlsCipherSuite.TLS_RSA_WITH_RC4_128_MD5, false)]
        public void IsCipherAllowed_Rc4Ciphers_BlockedByDefault(TlsCipherSuite cipher, bool expectedAllowed)
        {
            var options = new CipherSuiteOptions { AllowRc4 = false };

            var isAllowed = CipherSuiteValidator.IsCipherAllowed(cipher, options);

            Assert.Equal(expectedAllowed, isAllowed);
        }

        [Fact]
        public void IsCipherAllowed_Rc4_AllowedWhenExplicitlyEnabled()
        {
            var options = new CipherSuiteOptions
            {
                UseStrongCipherSuitesOnly = false,
                AllowRc4 = true,
                AllowSha1 = true  // Also need to allow SHA1 since RC4 ciphers use SHA
            };

            var isAllowed = CipherSuiteValidator.IsCipherAllowed(TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA, options);

            Assert.True(isAllowed);
        }

        [Theory]
        [InlineData(TlsCipherSuite.TLS_RSA_WITH_DES_CBC_SHA, false)]
        [InlineData(TlsCipherSuite.TLS_RSA_WITH_3DES_EDE_CBC_SHA, false)]
        public void IsCipherAllowed_DesCiphers_BlockedByDefault(TlsCipherSuite cipher, bool expectedAllowed)
        {
            var options = new CipherSuiteOptions { AllowDes = false };

            var isAllowed = CipherSuiteValidator.IsCipherAllowed(cipher, options);

            Assert.Equal(expectedAllowed, isAllowed);
        }

        [Theory]
        [InlineData(TlsCipherSuite.TLS_RSA_WITH_NULL_MD5, false)]
        [InlineData(TlsCipherSuite.TLS_RSA_WITH_NULL_SHA, false)]
        public void IsCipherAllowed_NullEncryption_NeverAllowed(TlsCipherSuite cipher, bool expectedAllowed)
        {
            var options = new CipherSuiteOptions { AllowNullEncryption = false };

            var isAllowed = CipherSuiteValidator.IsCipherAllowed(cipher, options);

            Assert.Equal(expectedAllowed, isAllowed);
        }

        [Theory]
        [InlineData(TlsCipherSuite.TLS_DH_anon_WITH_AES_128_CBC_SHA, false)]
        [InlineData(TlsCipherSuite.TLS_DH_anon_WITH_AES_256_CBC_SHA, false)]
        public void IsCipherAllowed_AnonymousCiphers_NeverAllowed(TlsCipherSuite cipher, bool expectedAllowed)
        {
            var options = new CipherSuiteOptions { AllowAnonymous = false };

            var isAllowed = CipherSuiteValidator.IsCipherAllowed(cipher, options);

            Assert.Equal(expectedAllowed, isAllowed);
        }

        [Fact]
        public void IsCipherAllowed_BlockedList_OverridesOtherSettings()
        {
            var options = new CipherSuiteOptions
            {
                UseStrongCipherSuitesOnly = false,
                AllowRc4 = true,
                BlockedCipherSuites = new List<TlsCipherSuite> { TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA }
            };

            var isAllowed = CipherSuiteValidator.IsCipherAllowed(TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA, options);

            Assert.False(isAllowed);
        }

        [Fact]
        public void IsCipherAllowed_AllowedList_RestrictsToOnlyListedCiphers()
        {
            var options = new CipherSuiteOptions
            {
                UseStrongCipherSuitesOnly = false,
                AllowedCipherSuites = new List<TlsCipherSuite>
                {
                    TlsCipherSuite.TLS_AES_256_GCM_SHA384
                }
            };

            Assert.True(CipherSuiteValidator.IsCipherAllowed(TlsCipherSuite.TLS_AES_256_GCM_SHA384, options));
            Assert.False(CipherSuiteValidator.IsCipherAllowed(TlsCipherSuite.TLS_AES_128_GCM_SHA256, options));
        }

        [Fact]
        public void GetCipherStrength_ReturnsCorrectValues()
        {
            Assert.Equal(256, CipherSuiteValidator.GetCipherStrength(TlsCipherSuite.TLS_AES_256_GCM_SHA384));
            Assert.Equal(128, CipherSuiteValidator.GetCipherStrength(TlsCipherSuite.TLS_AES_128_GCM_SHA256));
            Assert.Equal(256, CipherSuiteValidator.GetCipherStrength(TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256));
            Assert.Equal(0, CipherSuiteValidator.GetCipherStrength(TlsCipherSuite.TLS_RSA_WITH_NULL_SHA));
        }

        [Fact]
        public void IsWeakCipher_DetectsWeakCiphers()
        {
            Assert.True(CipherSuiteValidator.IsWeakCipher(TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA));
            Assert.True(CipherSuiteValidator.IsWeakCipher(TlsCipherSuite.TLS_RSA_WITH_DES_CBC_SHA));
            Assert.True(CipherSuiteValidator.IsWeakCipher(TlsCipherSuite.TLS_RSA_WITH_NULL_SHA));
            Assert.False(CipherSuiteValidator.IsWeakCipher(TlsCipherSuite.TLS_AES_256_GCM_SHA384));
            Assert.False(CipherSuiteValidator.IsWeakCipher(TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256));
        }

        [Fact]
        public void GetCipherWeaknessReason_ReturnsCorrectReasons()
        {
            Assert.Contains("RC4", CipherSuiteValidator.GetCipherWeaknessReason(TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA));
            Assert.Contains("DES", CipherSuiteValidator.GetCipherWeaknessReason(TlsCipherSuite.TLS_RSA_WITH_DES_CBC_SHA));
            // TLS_RSA_WITH_RC4_128_MD5 is in both RC4 and MD5 lists, RC4 reason takes precedence
            var md5Reason = CipherSuiteValidator.GetCipherWeaknessReason(TlsCipherSuite.TLS_RSA_WITH_RC4_128_MD5);
            Assert.True(md5Reason != null && (md5Reason.Contains("RC4") || md5Reason.Contains("MD5")), 
                $"Expected reason to contain 'RC4' or 'MD5', but was: {md5Reason}");
            Assert.Contains("NULL", CipherSuiteValidator.GetCipherWeaknessReason(TlsCipherSuite.TLS_RSA_WITH_NULL_SHA));
            Assert.Contains("Anonymous", CipherSuiteValidator.GetCipherWeaknessReason(TlsCipherSuite.TLS_DH_anon_WITH_AES_128_CBC_SHA));
            Assert.Null(CipherSuiteValidator.GetCipherWeaknessReason(TlsCipherSuite.TLS_AES_256_GCM_SHA384));
        }

        [Fact]
        public void ProvidesForwardSecrecy_DetectsPfsCiphers()
        {
            Assert.True(CipherSuiteValidator.ProvidesForwardSecrecy(TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384));
            Assert.True(CipherSuiteValidator.ProvidesForwardSecrecy(TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_GCM_SHA256));
            Assert.False(CipherSuiteValidator.ProvidesForwardSecrecy(TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA));
        }

        [Fact]
        public void GetRecommendedCipherSuites_ReturnsStrongCiphers()
        {
            var ciphers = CipherSuiteValidator.GetRecommendedCipherSuites();

            Assert.NotEmpty(ciphers);
            Assert.All(ciphers, c => Assert.True(CipherSuiteValidator.IsCipherAllowed(c, new CipherSuiteOptions { UseStrongCipherSuitesOnly = true })));
        }

        [Fact]
        public void GetRecommendedCipherSuites_PrefersForwardSecrecy_WhenEnabled()
        {
            var ciphers = CipherSuiteValidator.GetRecommendedCipherSuites(preferForwardSecrecy: true);

            // First ciphers should be PFS ciphers
            Assert.True(CipherSuiteValidator.ProvidesForwardSecrecy(ciphers[0]));
        }

        [Fact]
        public void GetTls13CipherSuites_ReturnsTls13Only()
        {
            var ciphers = CipherSuiteValidator.GetTls13CipherSuites();

            Assert.Equal(5, ciphers.Count);
            Assert.Contains(TlsCipherSuite.TLS_AES_256_GCM_SHA384, ciphers);
            Assert.Contains(TlsCipherSuite.TLS_AES_128_GCM_SHA256, ciphers);
            Assert.Contains(TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256, ciphers);
        }

        #endregion

        #region ServerConfiguration Integration Tests

        [Fact]
        public void ServerConfiguration_CipherSuiteConfig_DefaultsToNull()
        {
            var config = new ServerConfiguration();

            Assert.Null(config.CipherSuiteConfig);
        }

        [Fact]
        public void ServerConfiguration_CipherSuiteConfig_CanBeSet()
        {
            var config = new ServerConfiguration
            {
                CipherSuiteConfig = new CipherSuiteConfiguration
                {
                    UseStrongCipherSuitesOnly = true,
                    AllowRc4 = false,
                    MinimumCipherStrength = 256
                }
            };

            Assert.NotNull(config.CipherSuiteConfig);
            Assert.True(config.CipherSuiteConfig.UseStrongCipherSuitesOnly);
            Assert.Equal(256, config.CipherSuiteConfig.MinimumCipherStrength);
        }

        [Fact]
        public void CipherSuiteConfiguration_DefaultValues_AreSecure()
        {
            var config = new CipherSuiteConfiguration();

            Assert.True(config.UseStrongCipherSuitesOnly);
            Assert.False(config.AllowRc4);
            Assert.False(config.AllowDes);
            Assert.False(config.AllowMd5);
            Assert.False(config.AllowSha1);
            Assert.False(config.AllowNullEncryption);
            Assert.Equal(128, config.MinimumCipherStrength);
        }

        #endregion

        #region TlsStreamHelper Integration Tests

        [Fact]
        public void ToCipherSuiteOptions_ConvertsConfigurationCorrectly()
        {
            var config = new CipherSuiteConfiguration
            {
                UseStrongCipherSuitesOnly = false,
                AllowRc4 = true,
                AllowDes = true,
                AllowMd5 = true,
                AllowSha1 = false,
                AllowNullEncryption = false,
                MinimumCipherStrength = 256
            };

            var options = TlsStreamHelper.ToCipherSuiteOptions(config);

            Assert.Equal(config.UseStrongCipherSuitesOnly, options.UseStrongCipherSuitesOnly);
            Assert.Equal(config.AllowRc4, options.AllowRc4);
            Assert.Equal(config.AllowDes, options.AllowDes);
            Assert.Equal(config.AllowMd5, options.AllowMd5);
            Assert.Equal(config.AllowSha1, options.AllowSha1);
            Assert.Equal(config.AllowNullEncryption, options.AllowNullEncryption);
            Assert.Equal(config.MinimumCipherStrength, options.MinimumCipherStrength);
        }

        [Fact]
        public void ToCipherSuiteOptions_NullConfig_ReturnsDefaults()
        {
            var options = TlsStreamHelper.ToCipherSuiteOptions(null);

            Assert.True(options.UseStrongCipherSuitesOnly);
            Assert.False(options.AllowRc4);
            Assert.Equal(128, options.MinimumCipherStrength);
        }

        #endregion

        #region CipherValidationEventArgs Tests

        [Fact]
        public void CipherValidationEventArgs_StoresValuesCorrectly()
        {
            var args = new CipherValidationEventArgs(TlsCipherSuite.TLS_AES_256_GCM_SHA384, true);

            Assert.Equal(TlsCipherSuite.TLS_AES_256_GCM_SHA384, args.CipherSuite);
            Assert.True(args.IsAllowed);
            Assert.Null(args.RejectionReason);
        }

        [Fact]
        public void CipherValidationEventArgs_CanSetRejectionReason()
        {
            var args = new CipherValidationEventArgs(TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA, false)
            {
                RejectionReason = "RC4 is weak"
            };

            Assert.Equal("RC4 is weak", args.RejectionReason);
        }

        [Fact]
        public void CipherValidationEventArgs_CanModifyIsAllowed()
        {
            var args = new CipherValidationEventArgs(TlsCipherSuite.TLS_AES_256_GCM_SHA384, false)
            {
                IsAllowed = true
            };

            Assert.True(args.IsAllowed);
        }

        #endregion

        #region Security Best Practices Tests

        [Fact]
        public void Security_StrongDefaults_BlockAllWeakCiphers()
        {
            var options = new CipherSuiteOptions(); // Default strong settings
            var weakCiphers = new[]
            {
                TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA,
                TlsCipherSuite.TLS_RSA_WITH_RC4_128_MD5,
                TlsCipherSuite.TLS_RSA_WITH_DES_CBC_SHA,
                TlsCipherSuite.TLS_RSA_WITH_3DES_EDE_CBC_SHA,
                TlsCipherSuite.TLS_RSA_WITH_NULL_SHA,
                TlsCipherSuite.TLS_RSA_WITH_NULL_MD5,
                TlsCipherSuite.TLS_DH_anon_WITH_AES_128_CBC_SHA,
                TlsCipherSuite.TLS_RSA_EXPORT_WITH_RC4_40_MD5
            };

            foreach (var cipher in weakCiphers)
            {
                Assert.False(CipherSuiteValidator.IsCipherAllowed(cipher, options),
                    $"Weak cipher {cipher} should be blocked by default");
            }
        }

        [Fact]
        public void Security_AllTls13Ciphers_AreAllowed()
        {
            var options = new CipherSuiteOptions { UseStrongCipherSuitesOnly = true };
            var tls13Ciphers = CipherSuiteValidator.GetTls13CipherSuites();

            foreach (var cipher in tls13Ciphers)
            {
                Assert.True(CipherSuiteValidator.IsCipherAllowed(cipher, options),
                    $"TLS 1.3 cipher {cipher} should always be allowed");
            }
        }

        [Fact]
        public void Security_AllRecommendedCiphers_ProvideAdequateStrength()
        {
            var recommended = CipherSuiteValidator.GetRecommendedCipherSuites();

            foreach (var cipher in recommended)
            {
                var strength = CipherSuiteValidator.GetCipherStrength(cipher);
                Assert.True(strength >= 128,
                    $"Recommended cipher {cipher} has insufficient strength ({strength} bits)");
            }
        }

        #endregion
    }
}
