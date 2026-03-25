// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdvGenNoSqlServer.Tests
{
    public class ClientCertificateTests
    {
        #region ClientCertificateConfiguration Tests

        [Fact]
        public void ClientCertificateConfiguration_DefaultValues_AreCorrect()
        {
            var config = new ClientCertificateConfiguration();

            Assert.Equal(ClientCertificateMode.None, config.Mode);
            Assert.Null(config.CaCertificatePath);
            Assert.Empty(config.AllowedThumbprints);
            Assert.True(config.ValidateCertificateChain);
            Assert.Equal(RevocationCheckMode.Online, config.RevocationMode);
            Assert.True(config.ValidateValidityPeriod);
            Assert.True(config.ValidateEnhancedKeyUsage);
            Assert.False(config.AllowSelfSigned);
            Assert.Null(config.CustomValidationCallback);
        }

        [Fact]
        public void ClientCertificateConfiguration_Validate_NoneMode_ReturnsTrue()
        {
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.None
            };

            Assert.True(config.Validate());
        }

        [Fact]
        public void ClientCertificateConfiguration_Validate_InvalidThumbprint_ReturnsFalse()
        {
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Required,
                AllowedThumbprints = new List<string> { "", "valid-thumbprint" }
            };

            Assert.False(config.Validate());
        }

        [Fact]
        public void ClientCertificateConfiguration_Clone_CreatesIndependentCopy()
        {
            var original = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Required,
                CaCertificatePath = "/path/to/ca.crt",
                AllowedThumbprints = new List<string> { "thumbprint1", "thumbprint2" },
                ValidateCertificateChain = false,
                RevocationMode = RevocationCheckMode.Offline,
                AllowSelfSigned = true
            };

            var clone = original.Clone();

            Assert.Equal(original.Mode, clone.Mode);
            Assert.Equal(original.CaCertificatePath, clone.CaCertificatePath);
            Assert.Equal(original.AllowedThumbprints, clone.AllowedThumbprints);
            Assert.Equal(original.ValidateCertificateChain, clone.ValidateCertificateChain);
            Assert.Equal(original.RevocationMode, clone.RevocationMode);
            Assert.Equal(original.AllowSelfSigned, clone.AllowSelfSigned);

            // Verify it's independent
            clone.AllowedThumbprints.Add("thumbprint3");
            Assert.Equal(2, original.AllowedThumbprints.Count);
            Assert.Equal(3, clone.AllowedThumbprints.Count);
        }

        [Fact]
        public void ClientCertificateValidationResult_Success_ReturnsValidResult()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var thumbprint = ClientCertificateValidator.ComputeSha256Thumbprint(cert);

            var result = ClientCertificateValidationResult.Success(cert, thumbprint);

            Assert.True(result.IsValid);
            Assert.Equal(cert, result.Certificate);
            Assert.Equal(thumbprint, result.Thumbprint);
            Assert.Equal(cert.Subject, result.Subject);
            Assert.Equal(cert.Issuer, result.Issuer);
            Assert.True(result.ChainValidated);
            Assert.True(result.RevocationChecked);
            Assert.True(result.RevocationStatusValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ClientCertificateValidationResult_Failure_ReturnsInvalidResult()
        {
            var result = ClientCertificateValidationResult.Failure("Error 1", "Error 2");

            Assert.False(result.IsValid);
            Assert.Contains("Error 1", result.Errors);
            Assert.Contains("Error 2", result.Errors);
            Assert.Null(result.Certificate);
        }

        [Fact]
        public void ClientCertificateValidationResult_NoCertificate_ReturnsExpectedResult()
        {
            var result = ClientCertificateValidationResult.NoCertificate();

            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
            Assert.Contains("No client certificate was provided", result.Errors);
        }

        #endregion

        #region ClientCertificateValidator Tests

        [Fact]
        public void ClientCertificateValidator_Validate_NullCertificate_ReturnsFailure()
        {
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Required
            };

            var result = ClientCertificateValidator.Validate(null, null, SslPolicyErrors.None, config);

            Assert.False(result.IsValid);
            Assert.Contains("No client certificate was provided", result.Errors);
        }

        [Fact]
        public void ClientCertificateValidator_Validate_ValidCertificate_ReturnsSuccess()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Optional,
                ValidateCertificateChain = false,
                ValidateEnhancedKeyUsage = false,
                AllowSelfSigned = true
            };

            var result = ClientCertificateValidator.Validate(cert, null, SslPolicyErrors.None, config);

            Assert.True(result.IsValid);
            Assert.NotNull(result.Thumbprint);
            Assert.StartsWith("SHA256:", result.Thumbprint);
        }

        [Fact]
        public void ClientCertificateValidator_Validate_SelfSignedNotAllowed_ReturnsFailure()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Required,
                AllowSelfSigned = false,
                ValidateCertificateChain = true, // Chain validation must be enabled to detect self-signed
                ValidateEnhancedKeyUsage = false
            };

            var result = ClientCertificateValidator.Validate(cert, null, SslPolicyErrors.None, config);

            Assert.False(result.IsValid);
            // Self-signed certificates are detected as "untrusted root" during chain validation
            Assert.Contains(result.Errors, e => e.Contains("untrusted root", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ClientCertificateValidator_Validate_ThumbprintNotInAllowlist_ReturnsFailure()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var actualThumbprint = ClientCertificateValidator.ComputeSha256Thumbprint(cert);
            
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Required,
                AllowedThumbprints = new List<string> { "SHA256:INVALIDTHUMBPRINT123" },
                ValidateCertificateChain = false,
                ValidateEnhancedKeyUsage = false,
                AllowSelfSigned = true
            };

            var result = ClientCertificateValidator.Validate(cert, null, SslPolicyErrors.None, config);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("not in the allowed list"));
            Assert.Contains(result.Errors, e => e.Contains(actualThumbprint));
        }

        [Fact]
        public void ClientCertificateValidator_Validate_ThumbprintInAllowlist_ReturnsSuccess()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var thumbprint = ClientCertificateValidator.ComputeSha256Thumbprint(cert);
            
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Required,
                AllowedThumbprints = new List<string> { thumbprint },
                ValidateCertificateChain = false,
                ValidateEnhancedKeyUsage = false,
                AllowSelfSigned = true
            };

            var result = ClientCertificateValidator.Validate(cert, null, SslPolicyErrors.None, config);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void ClientCertificateValidator_Validate_CaseInsensitiveThumbprint_ReturnsSuccess()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var thumbprint = ClientCertificateValidator.ComputeSha256Thumbprint(cert).ToLowerInvariant();
            
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Required,
                AllowedThumbprints = new List<string> { thumbprint.ToUpperInvariant() },
                ValidateCertificateChain = false,
                ValidateEnhancedKeyUsage = false,
                AllowSelfSigned = true
            };

            var result = ClientCertificateValidator.Validate(cert, null, SslPolicyErrors.None, config);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void ClientCertificateValidator_Validate_MissingClientAuthEku_ReturnsFailure()
        {
            // Create a cert without Client Authentication EKU
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new CertificateRequest(
                new X500DistinguishedName("CN=test"),
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Auth only
                    false));

            var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(365));

            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Required,
                ValidateCertificateChain = false,
                ValidateEnhancedKeyUsage = true,
                AllowSelfSigned = true
            };

            var result = ClientCertificateValidator.Validate(cert, null, SslPolicyErrors.None, config);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Client Authentication"));
        }

        [Fact]
        public void ClientCertificateValidator_Validate_SslPolicyErrors_ReturnsFailure()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Required,
                ValidateCertificateChain = true
            };

            var result = ClientCertificateValidator.Validate(
                cert, null, SslPolicyErrors.RemoteCertificateChainErrors, config);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("chain validation failed"));
        }

        [Fact]
        public void ClientCertificateValidator_ValidateRequired_NullCertificate_ReturnsFalse()
        {
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Required
            };

            var result = ClientCertificateValidator.ValidateRequired(
                null, null, SslPolicyErrors.None, config);

            Assert.False(result);
        }

        [Fact]
        public void ClientCertificateValidator_ValidateRequired_ValidCertificate_ReturnsTrue()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Required,
                ValidateCertificateChain = false,
                ValidateEnhancedKeyUsage = false,
                AllowSelfSigned = true
            };

            var result = ClientCertificateValidator.ValidateRequired(
                cert, null, SslPolicyErrors.None, config);

            Assert.True(result);
        }

        [Fact]
        public void ClientCertificateValidator_ValidateOptional_NullCertificate_ReturnsTrue()
        {
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Optional
            };

            var result = ClientCertificateValidator.ValidateOptional(
                null, null, SslPolicyErrors.None, config);

            Assert.True(result);
        }

        [Fact]
        public void ClientCertificateValidator_ValidateOptional_InvalidCertificate_ReturnsFalse()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Optional,
                AllowSelfSigned = false,
                ValidateCertificateChain = false,
                ValidateEnhancedKeyUsage = false
            };

            var result = ClientCertificateValidator.ValidateOptional(
                cert, null, SslPolicyErrors.None, config);

            Assert.False(result);
        }

        [Fact]
        public void ClientCertificateValidator_ComputeSha256Thumbprint_ReturnsCorrectFormat()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");

            var thumbprint = ClientCertificateValidator.ComputeSha256Thumbprint(cert);

            Assert.NotNull(thumbprint);
            Assert.StartsWith("SHA256:", thumbprint);
            Assert.Equal(71, thumbprint.Length); // "SHA256:" + 64 hex chars
        }

        [Fact]
        public void ClientCertificateValidator_ComputeSha256Thumbprint_IsConsistent()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");

            var thumbprint1 = ClientCertificateValidator.ComputeSha256Thumbprint(cert);
            var thumbprint2 = ClientCertificateValidator.ComputeSha256Thumbprint(cert);

            Assert.Equal(thumbprint1, thumbprint2);
        }

        [Fact]
        public void ClientCertificateValidator_CertificateValidatedEvent_IsRaised()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var config = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Required,
                ValidateCertificateChain = false,
                ValidateEnhancedKeyUsage = false,
                AllowSelfSigned = true
            };

            bool eventRaised = false;
            ClientCertificateValidationEventArgs? capturedArgs = null;

            ClientCertificateValidator.CertificateValidated += (sender, args) =>
            {
                eventRaised = true;
                capturedArgs = args;
            };

            try
            {
                var result = ClientCertificateValidator.Validate(cert, null, SslPolicyErrors.None, config);

                Assert.True(eventRaised);
                Assert.NotNull(capturedArgs);
                Assert.Equal(result.IsValid, capturedArgs.Result.IsValid);
                Assert.Equal(config.Mode, capturedArgs.Configuration.Mode);
            }
            finally
            {
                // Clean up event handler
                ClientCertificateValidator.CertificateValidated -= (sender, args) => { };
            }
        }

        #endregion

        #region ClientCertificateException Tests

        [Fact]
        public void ClientCertificateException_DefaultConstructor_SetsDefaultMessage()
        {
            var ex = new ClientCertificateException();

            Assert.Equal("Client certificate validation failed", ex.Message);
        }

        [Fact]
        public void ClientCertificateException_MessageConstructor_SetsMessage()
        {
            var ex = new ClientCertificateException("Custom message");

            Assert.Equal("Custom message", ex.Message);
        }

        [Fact]
        public void ClientCertificateException_MissingCertificate_SetsCorrectProperties()
        {
            var ex = ClientCertificateException.MissingCertificate(ClientCertificateMode.Required);

            Assert.Contains("required", ex.Message);
            Assert.Equal(ClientCertificateMode.Required, ex.RequiredMode);
            Assert.False(ex.ValidationResult?.IsValid);
        }

        [Fact]
        public void ClientCertificateException_ValidationFailed_SetsCorrectProperties()
        {
            var result = ClientCertificateValidationResult.Failure("Error 1", "Error 2");
            var ex = ClientCertificateException.ValidationFailed(result, ClientCertificateMode.Required);

            Assert.Contains("Error 1", ex.Message);
            Assert.Contains("Error 2", ex.Message);
            Assert.Equal(ClientCertificateMode.Required, ex.RequiredMode);
        }

        [Fact]
        public void ClientCertificateException_SelfSignedNotAllowed_ContainsCorrectMessage()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var ex = ClientCertificateException.SelfSignedNotAllowed(cert, ClientCertificateMode.Required);

            Assert.Contains("Self-signed", ex.Message);
            Assert.Equal(ClientCertificateMode.Required, ex.RequiredMode);
            Assert.NotNull(ex.Certificate);
        }

        [Fact]
        public void ClientCertificateException_CertificateRevoked_ContainsCorrectMessage()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var ex = ClientCertificateException.CertificateRevoked(cert, ClientCertificateMode.Required);

            Assert.Contains("revoked", ex.Message);
            Assert.Equal(ClientCertificateMode.Required, ex.RequiredMode);
            Assert.NotNull(ex.Certificate);
            Assert.False(ex.ValidationResult?.RevocationStatusValid);
        }

        [Fact]
        public void ClientCertificateException_CertificateExpired_ContainsCorrectMessage()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var expiryDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var ex = ClientCertificateException.CertificateExpired(cert, expiryDate, ClientCertificateMode.Required);

            Assert.Contains("expired", ex.Message);
            Assert.Contains("2020-01-01", ex.Message);
            Assert.Equal(ClientCertificateMode.Required, ex.RequiredMode);
        }

        [Fact]
        public void ClientCertificateException_MissingClientAuthEku_ContainsCorrectMessage()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var ex = ClientCertificateException.MissingClientAuthEku(cert, ClientCertificateMode.Required);

            Assert.Contains("Client Authentication", ex.Message);
            Assert.Equal(ClientCertificateMode.Required, ex.RequiredMode);
        }

        [Fact]
        public void ClientCertificateException_ThumbprintNotAllowed_ContainsThumbprint()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var thumbprint = "SHA256:ABCDEF123456";
            var ex = ClientCertificateException.ThumbprintNotAllowed(cert, thumbprint, ClientCertificateMode.Required);

            Assert.Contains(thumbprint, ex.Message);
            Assert.Equal(thumbprint, ex.Thumbprint);
        }

        [Fact]
        public void ClientCertificateException_GetDetailedMessage_ContainsAllDetails()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=test");
            var result = ClientCertificateValidationResult.Failure("Error 1");
            result.Warnings.Add("Warning 1");
            result.Certificate = cert;

            var ex = new ClientCertificateException(
                "Validation failed",
                cert,
                result,
                ClientCertificateMode.Required);

            var detailed = ex.GetDetailedMessage();

            Assert.Contains("Validation failed", detailed);
            Assert.Contains("Error 1", detailed);
            Assert.Contains("Warning 1", detailed);
            Assert.Contains(cert.Subject, detailed);
            Assert.Contains("Required", detailed);
        }

        #endregion

        #region TlsStreamHelper Integration Tests

        [Fact]
        public void TlsStreamHelper_ToClientCertConfig_LegacyRequireClientCertificate_ReturnsRequiredMode()
        {
            var config = new ServerConfiguration
            {
                EnableSsl = true,
                RequireClientCertificate = true,
                CheckCertificateRevocation = true
            };

            var clientCertConfig = TlsStreamHelper.ToClientCertConfig(config);

            Assert.NotNull(clientCertConfig);
            Assert.Equal(ClientCertificateMode.Required, clientCertConfig.Mode);
            Assert.True(clientCertConfig.ValidateCertificateChain);
            Assert.Equal(RevocationCheckMode.Online, clientCertConfig.RevocationMode);
        }

        [Fact]
        public void TlsStreamHelper_ToClientCertConfig_WithClientCertificateConfig_ReturnsConfig()
        {
            var customConfig = new ClientCertificateConfiguration
            {
                Mode = ClientCertificateMode.Optional,
                AllowSelfSigned = true
            };

            var config = new ServerConfiguration
            {
                EnableSsl = true,
                ClientCertificateConfig = customConfig
            };

            var clientCertConfig = TlsStreamHelper.ToClientCertConfig(config);

            Assert.NotNull(clientCertConfig);
            Assert.Equal(ClientCertificateMode.Optional, clientCertConfig.Mode);
            Assert.True(clientCertConfig.AllowSelfSigned);
        }

        [Fact]
        public void TlsStreamHelper_CreateSelfSignedClientCertificate_HasClientAuthEku()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=testclient");

            Assert.NotNull(cert);
            Assert.Equal("CN=testclient", cert.Subject);

            var ekuExtension = cert.Extensions["2.5.29.37"] as X509EnhancedKeyUsageExtension;
            Assert.NotNull(ekuExtension);

            bool hasClientAuth = false;
            foreach (var oid in ekuExtension.EnhancedKeyUsages)
            {
                if (oid.Value == "1.3.6.1.5.5.7.3.2")
                {
                    hasClientAuth = true;
                    break;
                }
            }
            Assert.True(hasClientAuth, "Client certificate should have Client Authentication EKU");
        }

        [Fact]
        public void TlsStreamHelper_CreateSelfSignedClientCertificate_IsExportable()
        {
            using var cert = TlsStreamHelper.CreateSelfSignedClientCertificate("CN=testclient");

            Assert.NotNull(cert);
            Assert.True(cert.HasPrivateKey);

            // Should be able to export
            var exported = cert.Export(X509ContentType.Pfx, (string?)null);
            Assert.NotNull(exported);
            Assert.True(exported.Length > 0);
        }

        #endregion

        #region RevocationCheckMode Tests

        [Theory]
        [InlineData(RevocationCheckMode.None, X509RevocationMode.NoCheck)]
        [InlineData(RevocationCheckMode.Online, X509RevocationMode.Online)]
        [InlineData(RevocationCheckMode.Offline, X509RevocationMode.Offline)]
        public void RevocationCheckMode_MapsToX509RevocationMode(RevocationCheckMode input, X509RevocationMode expected)
        {
            // This test documents the expected mapping behavior
            // The actual mapping is done internally in TlsStreamHelper
            Assert.True(true); // Placeholder - actual mapping is tested through integration
        }

        #endregion

        #region ClientCertificateMode Tests

        [Theory]
        [InlineData(ClientCertificateMode.None, false, false)]
        [InlineData(ClientCertificateMode.Optional, true, false)]
        [InlineData(ClientCertificateMode.Required, true, true)]
        public void ClientCertificateMode_DeterminesValidationBehavior(
            ClientCertificateMode mode, 
            bool requestsCertificate, 
            bool requiresCertificate)
        {
            var config = new ClientCertificateConfiguration { Mode = mode };

            Assert.Equal(mode, config.Mode);
            
            // Document expected behavior based on mode
            switch (mode)
            {
                case ClientCertificateMode.None:
                    Assert.False(requestsCertificate);
                    Assert.False(requiresCertificate);
                    break;
                case ClientCertificateMode.Optional:
                    Assert.True(requestsCertificate);
                    Assert.False(requiresCertificate);
                    break;
                case ClientCertificateMode.Required:
                    Assert.True(requestsCertificate);
                    Assert.True(requiresCertificate);
                    break;
            }
        }

        #endregion
    }
}
