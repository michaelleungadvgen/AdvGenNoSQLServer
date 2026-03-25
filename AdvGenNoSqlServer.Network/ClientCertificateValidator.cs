// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Event arguments for client certificate validation events
    /// </summary>
    public class ClientCertificateValidationEventArgs : EventArgs
    {
        /// <summary>
        /// The certificate that was validated
        /// </summary>
        public X509Certificate2? Certificate { get; }

        /// <summary>
        /// The validation result
        /// </summary>
        public ClientCertificateValidationResult Result { get; }

        /// <summary>
        /// The validation configuration used
        /// </summary>
        public ClientCertificateConfiguration Configuration { get; }

        /// <summary>
        /// Creates a new instance of ClientCertificateValidationEventArgs
        /// </summary>
        public ClientCertificateValidationEventArgs(
            X509Certificate2? certificate,
            ClientCertificateValidationResult result,
            ClientCertificateConfiguration configuration)
        {
            Certificate = certificate;
            Result = result;
            Configuration = configuration;
        }
    }

    /// <summary>
    /// Delegate for client certificate validation events
    /// </summary>
    public delegate void ClientCertificateValidationEventHandler(
        object sender,
        ClientCertificateValidationEventArgs e);

    /// <summary>
    /// Validator for client certificates in mutual TLS (mTLS) scenarios
    /// </summary>
    public static class ClientCertificateValidator
    {
        /// <summary>
        /// Event raised when a client certificate is validated
        /// </summary>
        public static event ClientCertificateValidationEventHandler? CertificateValidated;

        /// <summary>
        /// Validates a client certificate against the configuration
        /// </summary>
        /// <param name="certificate">The client certificate to validate</param>
        /// <param name="chain">The certificate chain</param>
        /// <param name="sslPolicyErrors">SSL policy errors from the TLS handshake</param>
        /// <param name="configuration">The client certificate configuration</param>
        /// <returns>A validation result with details</returns>
        public static ClientCertificateValidationResult Validate(
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors,
            ClientCertificateConfiguration configuration)
        {
            var result = new ClientCertificateValidationResult
            {
                ValidationTime = DateTime.UtcNow
            };

            // Check if certificate is null
            if (certificate == null)
            {
                result.Errors.Add("No client certificate was provided");
                RaiseValidationEvent(null, result, configuration);
                return result;
            }

            // Convert to X509Certificate2 for more functionality
            X509Certificate2 cert2;
            try
            {
                cert2 = certificate is X509Certificate2 cert2Existing
                    ? cert2Existing
                    : new X509Certificate2(certificate);

                result.Certificate = cert2;
                result.Subject = cert2.Subject;
                result.Issuer = cert2.Issuer;
                result.Thumbprint = ComputeSha256Thumbprint(cert2);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to process certificate: {ex.Message}");
                RaiseValidationEvent(null, result, configuration);
                return result;
            }

            // Check SSL policy errors first
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                ValidateSslPolicyErrors(sslPolicyErrors, result, configuration);
            }

            // Validate certificate chain if required
            if (configuration.ValidateCertificateChain && result.IsValid || result.Errors.Count == 0)
            {
                ValidateCertificateChain(cert2, chain, result, configuration);
            }

            // Validate thumbprint allowlist if specified
            if (configuration.AllowedThumbprints.Count > 0 && result.Errors.Count == 0)
            {
                ValidateThumbprintAllowlist(cert2, result, configuration);
            }

            // Validate enhanced key usage
            if (configuration.ValidateEnhancedKeyUsage && result.Errors.Count == 0)
            {
                ValidateEnhancedKeyUsage(cert2, result);
            }

            // Validate self-signed status
            if (!configuration.AllowSelfSigned && result.Errors.Count == 0)
            {
                ValidateNotSelfSigned(cert2, result);
            }

            // Run custom validation callback if provided
            if (configuration.CustomValidationCallback != null && result.Errors.Count == 0)
            {
                try
                {
                    if (!configuration.CustomValidationCallback(cert2))
                    {
                        result.Errors.Add("Certificate rejected by custom validation callback");
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Custom validation callback threw exception: {ex.Message}");
                }
            }

            // Set final validation status
            result.IsValid = result.Errors.Count == 0;
            result.Thumbprint ??= ComputeSha256Thumbprint(cert2);

            RaiseValidationEvent(cert2, result, configuration);
            return result;
        }

        /// <summary>
        /// Validates a client certificate for the Required mode
        /// </summary>
        /// <param name="certificate">The client certificate</param>
        /// <param name="chain">The certificate chain</param>
        /// <param name="sslPolicyErrors">SSL policy errors</param>
        /// <param name="configuration">The configuration</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool ValidateRequired(
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors,
            ClientCertificateConfiguration configuration)
        {
            if (certificate == null)
                return false;

            var result = Validate(certificate, chain, sslPolicyErrors, configuration);
            return result.IsValid;
        }

        /// <summary>
        /// Validates a client certificate for the Optional mode
        /// </summary>
        /// <param name="certificate">The client certificate (can be null)</param>
        /// <param name="chain">The certificate chain</param>
        /// <param name="sslPolicyErrors">SSL policy errors</param>
        /// <param name="configuration">The configuration</param>
        /// <returns>True if valid or no certificate, false if certificate is invalid</returns>
        public static bool ValidateOptional(
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors,
            ClientCertificateConfiguration configuration)
        {
            // If no certificate provided, it's valid for optional mode
            if (certificate == null)
                return true;

            var result = Validate(certificate, chain, sslPolicyErrors, configuration);
            return result.IsValid;
        }

        /// <summary>
        /// Computes the SHA-256 thumbprint of a certificate
        /// </summary>
        public static string ComputeSha256Thumbprint(X509Certificate certificate)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(certificate.GetRawCertData());
            return "SHA256:" + Convert.ToHexString(hash);
        }

        /// <summary>
        /// Loads a CA certificate from a file
        /// </summary>
        public static X509Certificate2? LoadCaCertificate(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                var bytes = File.ReadAllBytes(path);
                
                // Try as PEM first
                try
                {
                    return X509Certificate2.CreateFromPemFile(path);
                }
                catch
                {
                    // Fall back to DER/PKCS12
                    return X509CertificateLoader.LoadCertificate(bytes);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Validates SSL policy errors
        /// </summary>
        private static void ValidateSslPolicyErrors(
            SslPolicyErrors sslPolicyErrors,
            ClientCertificateValidationResult result,
            ClientCertificateConfiguration configuration)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return;

            // Check for remote certificate chain errors
            if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
            {
                if (configuration.ValidateCertificateChain)
                {
                    result.Errors.Add("Certificate chain validation failed");
                }
                else
                {
                    result.Warnings.Add("Certificate chain validation failed but chain validation is disabled");
                }
            }

            // Check for remote certificate name mismatch (less critical for client certs)
            if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            {
                result.Warnings.Add("Certificate name mismatch detected");
            }

            // Check for remote certificate not available
            if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            {
                result.Errors.Add("Remote certificate is not available");
            }
        }

        /// <summary>
        /// Validates the certificate chain
        /// </summary>
        private static void ValidateCertificateChain(
            X509Certificate2 certificate,
            X509Chain? existingChain,
            ClientCertificateValidationResult result,
            ClientCertificateConfiguration configuration)
        {
            // Use existing chain if provided
            if (existingChain != null)
            {
                result.ChainValidated = existingChain.ChainStatus.Length == 0;
                
                foreach (var element in existingChain.ChainStatus)
                {
                    if (element.Status == X509ChainStatusFlags.NoError)
                        continue;

                    // Handle specific chain errors
                    switch (element.Status)
                    {
                        case X509ChainStatusFlags.UntrustedRoot:
                            if (!configuration.AllowSelfSigned)
                            {
                                result.Errors.Add("Certificate chain has an untrusted root");
                            }
                            else
                            {
                                result.Warnings.Add("Certificate chain has an untrusted root (self-signed allowed)");
                            }
                            break;

                        case X509ChainStatusFlags.Revoked:
                            result.Errors.Add("Certificate has been revoked");
                            result.RevocationStatusValid = false;
                            break;

                        case X509ChainStatusFlags.RevocationStatusUnknown:
                            if (configuration.RevocationMode == RevocationCheckMode.Online)
                            {
                                result.Warnings.Add("Could not determine certificate revocation status");
                            }
                            break;

                        case X509ChainStatusFlags.NotTimeValid:
                            if (configuration.ValidateValidityPeriod)
                            {
                                result.Errors.Add("Certificate has expired or is not yet valid");
                            }
                            break;

                        default:
                            result.Errors.Add($"Chain validation error: {element.StatusInformation}");
                            break;
                    }
                }

                result.RevocationChecked = configuration.RevocationMode != RevocationCheckMode.None;
                result.RevocationStatusValid = result.Errors.All(e => !e.Contains("revoked", StringComparison.OrdinalIgnoreCase));
                return;
            }

            // Build and validate chain ourselves
            using var chain = new X509Chain();
            
            // Configure chain policy
            chain.ChainPolicy.RevocationMode = configuration.RevocationMode switch
            {
                RevocationCheckMode.Online => X509RevocationMode.Online,
                RevocationCheckMode.Offline => X509RevocationMode.Offline,
                _ => X509RevocationMode.NoCheck
            };

            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            chain.ChainPolicy.VerificationTime = DateTime.Now;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            // Load custom CA certificate if specified
            if (!string.IsNullOrWhiteSpace(configuration.CaCertificatePath))
            {
                var caCert = LoadCaCertificate(configuration.CaCertificatePath);
                if (caCert != null)
                {
                    chain.ChainPolicy.CustomTrustStore.Add(caCert);
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                }
            }

            // Build the chain
            bool chainBuilt = chain.Build(certificate);
            result.ChainValidated = chainBuilt;
            result.RevocationChecked = configuration.RevocationMode != RevocationCheckMode.None;

            if (!chainBuilt)
            {
                foreach (var status in chain.ChainStatus)
                {
                    if (status.Status == X509ChainStatusFlags.NoError)
                        continue;

                    switch (status.Status)
                    {
                        case X509ChainStatusFlags.UntrustedRoot:
                            if (!configuration.AllowSelfSigned)
                            {
                                result.Errors.Add("Certificate chain has an untrusted root");
                            }
                            else
                            {
                                result.Warnings.Add("Certificate chain has an untrusted root (self-signed allowed)");
                            }
                            break;

                        case X509ChainStatusFlags.Revoked:
                            result.Errors.Add("Certificate has been revoked");
                            result.RevocationStatusValid = false;
                            break;

                        case X509ChainStatusFlags.RevocationStatusUnknown:
                            result.Warnings.Add("Could not determine certificate revocation status");
                            break;

                        case X509ChainStatusFlags.NotTimeValid:
                            if (configuration.ValidateValidityPeriod)
                            {
                                result.Errors.Add("Certificate is not within its validity period");
                            }
                            break;

                        default:
                            result.Errors.Add($"Chain validation error: {status.StatusInformation}");
                            break;
                    }
                }
            }

            result.RevocationStatusValid = result.Errors.All(e => 
                !e.Contains("revoked", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Validates certificate against thumbprint allowlist
        /// </summary>
        private static void ValidateThumbprintAllowlist(
            X509Certificate2 certificate,
            ClientCertificateValidationResult result,
            ClientCertificateConfiguration configuration)
        {
            if (configuration.AllowedThumbprints.Count == 0)
                return;

            var thumbprint = ComputeSha256Thumbprint(certificate);
            
            // Case-insensitive comparison
            var isAllowed = configuration.AllowedThumbprints.Any(
                allowed => string.Equals(allowed, thumbprint, StringComparison.OrdinalIgnoreCase));

            if (!isAllowed)
            {
                result.Errors.Add($"Certificate thumbprint {thumbprint} is not in the allowed list");
            }
        }

        /// <summary>
        /// Validates enhanced key usage for client authentication
        /// </summary>
        private static void ValidateEnhancedKeyUsage(
            X509Certificate2 certificate,
            ClientCertificateValidationResult result)
        {
            var extensions = certificate.Extensions;
            var ekuExtension = extensions["2.5.29.37"] as X509EnhancedKeyUsageExtension;

            if (ekuExtension == null)
            {
                // No EKU extension means it's valid for all purposes
                return;
            }

            // Client Authentication OID: 1.3.6.1.5.5.7.3.2
            const string clientAuthOid = "1.3.6.1.5.5.7.3.2";
            
            bool hasClientAuth = false;
            foreach (var oid in ekuExtension.EnhancedKeyUsages)
            {
                if (oid.Value == clientAuthOid)
                {
                    hasClientAuth = true;
                    break;
                }
            }

            if (!hasClientAuth)
            {
                result.Errors.Add("Certificate does not have Client Authentication enhanced key usage");
            }
        }

        /// <summary>
        /// Validates that certificate is not self-signed
        /// </summary>
        private static void ValidateNotSelfSigned(
            X509Certificate2 certificate,
            ClientCertificateValidationResult result)
        {
            if (certificate.Subject == certificate.Issuer)
            {
                result.Errors.Add("Self-signed certificates are not allowed");
            }
        }

        /// <summary>
        /// Raises the CertificateValidated event
        /// </summary>
        private static void RaiseValidationEvent(
            X509Certificate2? certificate,
            ClientCertificateValidationResult result,
            ClientCertificateConfiguration configuration)
        {
            try
            {
                CertificateValidated?.Invoke(null, new ClientCertificateValidationEventArgs(
                    certificate, result, configuration));
            }
            catch
            {
                // Event handlers should not throw, but if they do, don't crash
            }
        }
    }
}
