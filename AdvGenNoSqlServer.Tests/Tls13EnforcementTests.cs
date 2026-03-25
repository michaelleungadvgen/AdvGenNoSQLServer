// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using System.Security.Authentication;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for TLS 1.3 enforcement and minimum TLS version configuration
    /// </summary>
    public class Tls13EnforcementTests
    {
        #region TlsVersionValidator Tests

        [Theory]
        [InlineData(SslProtocols.Tls13, SslProtocols.Tls12, true)]
        [InlineData(SslProtocols.Tls12, SslProtocols.Tls12, true)]
        [InlineData(SslProtocols.Tls11, SslProtocols.Tls12, false)]
        [InlineData(SslProtocols.Tls, SslProtocols.Tls12, false)]
        [InlineData(SslProtocols.Ssl3, SslProtocols.Tls12, false)]
        public void ValidateTlsVersion_WithRequireMinimum_ReturnsExpectedResult(
            SslProtocols negotiated, SslProtocols minimum, bool expected)
        {
            // Act
            var result = TlsVersionValidator.ValidateTlsVersion(negotiated, minimum, requireMinimumVersion: true);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(SslProtocols.Tls)]
        [InlineData(SslProtocols.Tls11)]
        [InlineData(SslProtocols.Tls12)]
        [InlineData(SslProtocols.Tls13)]
        public void ValidateTlsVersion_WithoutRequireMinimum_AlwaysReturnsTrue(SslProtocols negotiated)
        {
            // Act
            var result = TlsVersionValidator.ValidateTlsVersion(negotiated, SslProtocols.Tls13, requireMinimumVersion: false);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ValidateTlsVersion_WithNoneMinimum_AlwaysReturnsTrue()
        {
            // Act
            var result = TlsVersionValidator.ValidateTlsVersion(SslProtocols.Tls, SslProtocols.None, requireMinimumVersion: true);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData(SslProtocols.Tls13, SslProtocols.Tls13, true)]
        [InlineData(SslProtocols.Tls12, SslProtocols.Tls13, false)]
        [InlineData(SslProtocols.Tls11, SslProtocols.Tls13, false)]
        public void ValidateTlsVersion_Tls13Minimum_ReturnsExpectedResult(
            SslProtocols negotiated, SslProtocols minimum, bool expected)
        {
            // Act
            var result = TlsVersionValidator.ValidateTlsVersion(negotiated, minimum, requireMinimumVersion: true);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(SslProtocols.Tls12 | SslProtocols.Tls13, SslProtocols.Tls12, true)]
        [InlineData(SslProtocols.Tls12 | SslProtocols.Tls13, SslProtocols.Tls13, true)]
        [InlineData(SslProtocols.Tls11 | SslProtocols.Tls, SslProtocols.Tls12, false)]
        public void ClientSupportsMinimumVersion_ReturnsExpectedResult(
            SslProtocols clientSupported, SslProtocols minimum, bool expected)
        {
            // Act
            var result = TlsVersionValidator.ClientSupportsMinimumVersion(clientSupported, minimum);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(SslProtocols.Tls, "TLS 1.0")]
        [InlineData(SslProtocols.Tls11, "TLS 1.1")]
        [InlineData(SslProtocols.Tls12, "TLS 1.2")]
        [InlineData(SslProtocols.Tls13, "TLS 1.3")]
        [InlineData(SslProtocols.Ssl2, "SSL 2.0")]
        [InlineData(SslProtocols.Ssl3, "SSL 3.0")]
        [InlineData(SslProtocols.None, "None")]
        public void GetTlsVersionName_ReturnsExpectedName(SslProtocols protocol, string expected)
        {
            // Act
            var result = TlsVersionValidator.GetTlsVersionName(protocol);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(SslProtocols.Tls12, true)]
        [InlineData(SslProtocols.Tls13, true)]
        [InlineData(SslProtocols.Tls11, false)]
        [InlineData(SslProtocols.Tls, false)]
        [InlineData(SslProtocols.Ssl3, false)]
        [InlineData(SslProtocols.Ssl2, false)]
        public void IsSecureTlsVersion_ReturnsExpectedResult(SslProtocols protocol, bool expected)
        {
            // Act
            var result = TlsVersionValidator.IsSecureTlsVersion(protocol);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(SslProtocols.Tls, true)]
        [InlineData(SslProtocols.Tls11, true)]
        [InlineData(SslProtocols.Ssl2, true)]
        [InlineData(SslProtocols.Ssl3, true)]
        [InlineData(SslProtocols.Tls12, false)]
        [InlineData(SslProtocols.Tls13, false)]
        public void IsDeprecatedTlsVersion_ReturnsExpectedResult(SslProtocols protocol, bool expected)
        {
            // Act
            var result = TlsVersionValidator.IsDeprecatedTlsVersion(protocol);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetRecommendedAllowedTlsVersions_ReturnsTls12AndTls13()
        {
            // Act
            var result = TlsVersionValidator.GetRecommendedAllowedTlsVersions();

            // Assert
            Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, result);
        }

        [Theory]
        [InlineData(SslProtocols.Tls13, SslProtocols.Tls13)]
        [InlineData(SslProtocols.Tls12 | SslProtocols.Tls13, SslProtocols.Tls13)]
        [InlineData(SslProtocols.Tls12, SslProtocols.Tls12)]
        [InlineData(SslProtocols.Tls11 | SslProtocols.Tls12, SslProtocols.Tls12)]
        [InlineData(SslProtocols.Tls | SslProtocols.Tls11, SslProtocols.Tls11)]
        [InlineData(SslProtocols.Ssl2 | SslProtocols.Ssl3, SslProtocols.Ssl3)]
        public void GetStrongestTlsVersion_ReturnsExpectedVersion(SslProtocols protocols, SslProtocols expected)
        {
            // Act
            var result = TlsVersionValidator.GetStrongestTlsVersion(protocols);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region TlsVersionException Tests

        [Fact]
        public void TlsVersionException_Constructor_SetsProperties()
        {
            // Arrange
            var message = "Test error message";
            var negotiated = SslProtocols.Tls;
            var minimum = SslProtocols.Tls12;

            // Act
            var exception = new TlsVersionException(message, negotiated, minimum);

            // Assert
            Assert.Equal(message, exception.Message);
            Assert.Equal(negotiated, exception.NegotiatedVersion);
            Assert.Equal(minimum, exception.MinimumVersion);
        }

        [Fact]
        public void TlsVersionException_ConstructorWithInnerException_SetsProperties()
        {
            // Arrange
            var message = "Test error message";
            var innerException = new InvalidOperationException("Inner exception");
            var negotiated = SslProtocols.Tls11;
            var minimum = SslProtocols.Tls13;

            // Act
            var exception = new TlsVersionException(message, innerException, negotiated, minimum);

            // Assert
            Assert.Equal(message, exception.Message);
            Assert.Equal(innerException, exception.InnerException);
            Assert.Equal(negotiated, exception.NegotiatedVersion);
            Assert.Equal(minimum, exception.MinimumVersion);
        }

        #endregion

        #region ServerConfiguration Tests

        [Fact]
        public void ServerConfiguration_DefaultMinimumTlsVersion_IsTls12()
        {
            // Arrange
            var config = new ServerConfiguration();

            // Assert
            Assert.Equal(SslProtocols.Tls12, config.MinimumTlsVersion);
        }

        [Fact]
        public void ServerConfiguration_DefaultRequireMinimumTlsVersion_IsFalse()
        {
            // Arrange
            var config = new ServerConfiguration();

            // Assert
            Assert.False(config.RequireMinimumTlsVersion);
        }

        [Fact]
        public void ServerConfiguration_DefaultRejectNonTlsConnections_IsTrue()
        {
            // Arrange
            var config = new ServerConfiguration();

            // Assert
            Assert.True(config.RejectNonTlsConnections);
        }

        [Fact]
        public void ServerConfiguration_SetMinimumTlsVersion_Works()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                // Act
                MinimumTlsVersion = SslProtocols.Tls13
            };

            // Assert
            Assert.Equal(SslProtocols.Tls13, config.MinimumTlsVersion);
        }

        [Fact]
        public void ServerConfiguration_SetRequireMinimumTlsVersion_Works()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                // Act
                RequireMinimumTlsVersion = true
            };

            // Assert
            Assert.True(config.RequireMinimumTlsVersion);
        }

        [Fact]
        public void ServerConfiguration_SetRejectNonTlsConnections_Works()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                // Act
                RejectNonTlsConnections = false
            };

            // Assert
            Assert.False(config.RejectNonTlsConnections);
        }

        [Fact]
        public void ServerConfiguration_Tls13EnforcementConfiguration_IsValid()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                EnableSsl = true,
                SslProtocols = SslProtocols.Tls13,
                MinimumTlsVersion = SslProtocols.Tls13,
                RequireMinimumTlsVersion = true,
                RejectNonTlsConnections = true
            };

            // Assert
            Assert.True(config.EnableSsl);
            Assert.Equal(SslProtocols.Tls13, config.SslProtocols);
            Assert.Equal(SslProtocols.Tls13, config.MinimumTlsVersion);
            Assert.True(config.RequireMinimumTlsVersion);
            Assert.True(config.RejectNonTlsConnections);
        }

        [Fact]
        public void ServerConfiguration_Tls12WithTls13Fallback_IsValid()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                EnableSsl = true,
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                MinimumTlsVersion = SslProtocols.Tls12,
                RequireMinimumTlsVersion = true,
                RejectNonTlsConnections = true
            };

            // Assert
            Assert.True(config.EnableSsl);
            Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, config.SslProtocols);
            Assert.Equal(SslProtocols.Tls12, config.MinimumTlsVersion);
            Assert.True(config.RequireMinimumTlsVersion);
        }

        #endregion

        #region Integration Tests - Configuration with Validator

        [Fact]
        public void Configuration_WithTls13Minimum_RejectsTls12()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                MinimumTlsVersion = SslProtocols.Tls13,
                RequireMinimumTlsVersion = true
            };

            // Act
            var isValid = TlsVersionValidator.ValidateTlsVersion(
                SslProtocols.Tls12,
                config.MinimumTlsVersion,
                config.RequireMinimumTlsVersion);

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void Configuration_WithTls13Minimum_AcceptsTls13()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                MinimumTlsVersion = SslProtocols.Tls13,
                RequireMinimumTlsVersion = true
            };

            // Act
            var isValid = TlsVersionValidator.ValidateTlsVersion(
                SslProtocols.Tls13,
                config.MinimumTlsVersion,
                config.RequireMinimumTlsVersion);

            // Assert
            Assert.True(isValid);
        }

        [Fact]
        public void Configuration_WithoutEnforcement_AcceptsAnyVersion()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                MinimumTlsVersion = SslProtocols.Tls13,
                RequireMinimumTlsVersion = false
            };

            // Act & Assert
            Assert.True(TlsVersionValidator.ValidateTlsVersion(
                SslProtocols.Tls, 
                config.MinimumTlsVersion, 
                config.RequireMinimumTlsVersion));
            
            Assert.True(TlsVersionValidator.ValidateTlsVersion(
                SslProtocols.Tls11, 
                config.MinimumTlsVersion, 
                config.RequireMinimumTlsVersion));
            
            Assert.True(TlsVersionValidator.ValidateTlsVersion(
                SslProtocols.Tls12, 
                config.MinimumTlsVersion, 
                config.RequireMinimumTlsVersion));
            
            Assert.True(TlsVersionValidator.ValidateTlsVersion(
                SslProtocols.Tls13, 
                config.MinimumTlsVersion, 
                config.RequireMinimumTlsVersion));
        }

        #endregion

        #region Security Best Practice Tests

        [Fact]
        public void SecurityBestPractice_Tls13Only_IsRecommendedForHighSecurity()
        {
            // This test documents the recommended high-security configuration
            // Arrange
            var highSecurityConfig = new ServerConfiguration
            {
                EnableSsl = true,
                SslProtocols = SslProtocols.Tls13,
                MinimumTlsVersion = SslProtocols.Tls13,
                RequireMinimumTlsVersion = true,
                RejectNonTlsConnections = true,
                CheckCertificateRevocation = true
            };

            // Act & Assert
            Assert.True(TlsVersionValidator.IsSecureTlsVersion(highSecurityConfig.MinimumTlsVersion));
            Assert.False(TlsVersionValidator.IsDeprecatedTlsVersion(highSecurityConfig.MinimumTlsVersion));
        }

        [Fact]
        public void SecurityBestPractice_Tls12Minimum_WithTls13Support_IsRecommendedForCompatibility()
        {
            // This test documents the recommended compatibility configuration
            // Arrange
            var compatConfig = new ServerConfiguration
            {
                EnableSsl = true,
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                MinimumTlsVersion = SslProtocols.Tls12,
                RequireMinimumTlsVersion = true,
                RejectNonTlsConnections = true
            };

            // Act & Assert
            Assert.True(TlsVersionValidator.IsSecureTlsVersion(compatConfig.MinimumTlsVersion));
            Assert.True(TlsVersionValidator.IsSecureTlsVersion(SslProtocols.Tls13));
        }

        [Theory]
        [InlineData(SslProtocols.Tls)]
        [InlineData(SslProtocols.Tls11)]
        [InlineData(SslProtocols.Ssl2)]
        [InlineData(SslProtocols.Ssl3)]
        public void SecurityBestPractice_DeprecatedVersions_AreRejected(SslProtocols deprecatedVersion)
        {
            // Arrange
            var config = new ServerConfiguration
            {
                MinimumTlsVersion = SslProtocols.Tls12,
                RequireMinimumTlsVersion = true
            };

            // Act
            var isAccepted = TlsVersionValidator.ValidateTlsVersion(
                deprecatedVersion,
                config.MinimumTlsVersion,
                config.RequireMinimumTlsVersion);

            // Assert
            Assert.False(isAccepted);
            Assert.True(TlsVersionValidator.IsDeprecatedTlsVersion(deprecatedVersion));
        }

        #endregion
    }
}
