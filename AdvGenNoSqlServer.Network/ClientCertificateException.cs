// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using System.Security.Cryptography.X509Certificates;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Exception thrown when client certificate validation fails
    /// </summary>
    public class ClientCertificateException : Exception
    {
        /// <summary>
        /// The certificate that failed validation (may be null if no certificate was provided)
        /// </summary>
        public X509Certificate2? Certificate { get; }

        /// <summary>
        /// The client certificate validation result
        /// </summary>
        public ClientCertificateValidationResult? ValidationResult { get; }

        /// <summary>
        /// The client certificate mode that was required
        /// </summary>
        public ClientCertificateMode RequiredMode { get; }

        /// <summary>
        /// The certificate thumbprint (if available)
        /// </summary>
        public string? Thumbprint { get; }

        /// <summary>
        /// Creates a new instance of ClientCertificateException
        /// </summary>
        public ClientCertificateException()
            : base("Client certificate validation failed")
        {
        }

        /// <summary>
        /// Creates a new instance of ClientCertificateException with a message
        /// </summary>
        public ClientCertificateException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates a new instance of ClientCertificateException with a message and inner exception
        /// </summary>
        public ClientCertificateException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates a new instance of ClientCertificateException with full details
        /// </summary>
        public ClientCertificateException(
            string message,
            X509Certificate2? certificate,
            ClientCertificateValidationResult result,
            ClientCertificateMode requiredMode)
            : base(message)
        {
            Certificate = certificate;
            ValidationResult = result;
            RequiredMode = requiredMode;
            Thumbprint = result.Thumbprint ?? (certificate != null 
                ? ClientCertificateValidator.ComputeSha256Thumbprint(certificate) 
                : null);
        }

        /// <summary>
        /// Creates an exception for when a required client certificate is missing
        /// </summary>
        public static ClientCertificateException MissingCertificate(ClientCertificateMode requiredMode)
        {
            return new ClientCertificateException(
                $"Client certificate is required but was not provided. Mode: {requiredMode}",
                null,
                ClientCertificateValidationResult.NoCertificate(),
                requiredMode);
        }

        /// <summary>
        /// Creates an exception for when client certificate validation fails
        /// </summary>
        public static ClientCertificateException ValidationFailed(
            ClientCertificateValidationResult result,
            ClientCertificateMode requiredMode)
        {
            var errorMessage = result.Errors.Count > 0
                ? string.Join("; ", result.Errors)
                : "Client certificate validation failed";

            return new ClientCertificateException(
                $"Client certificate validation failed: {errorMessage}. Mode: {requiredMode}",
                result.Certificate,
                result,
                requiredMode);
        }

        /// <summary>
        /// Creates an exception for when the certificate chain validation fails
        /// </summary>
        public static ClientCertificateException ChainValidationFailed(
            X509Certificate2 certificate,
            ClientCertificateMode requiredMode,
            params string[] chainErrors)
        {
            var result = ClientCertificateValidationResult.Failure(chainErrors);
            result.Certificate = certificate;
            result.ChainValidated = false;

            return new ClientCertificateException(
                $"Client certificate chain validation failed: {string.Join("; ", chainErrors)}",
                certificate,
                result,
                requiredMode);
        }

        /// <summary>
        /// Creates an exception for when the certificate thumbprint is not in the allowlist
        /// </summary>
        public static ClientCertificateException ThumbprintNotAllowed(
            X509Certificate2 certificate,
            string thumbprint,
            ClientCertificateMode requiredMode)
        {
            var result = ClientCertificateValidationResult.Failure(
                $"Certificate thumbprint {thumbprint} is not in the allowed list");
            result.Certificate = certificate;
            result.Thumbprint = thumbprint;

            return new ClientCertificateException(
                $"Client certificate thumbprint {thumbprint} is not in the allowed list",
                certificate,
                result,
                requiredMode);
        }

        /// <summary>
        /// Creates an exception for when the certificate has been revoked
        /// </summary>
        public static ClientCertificateException CertificateRevoked(
            X509Certificate2 certificate,
            ClientCertificateMode requiredMode)
        {
            var result = ClientCertificateValidationResult.Failure("Certificate has been revoked");
            result.Certificate = certificate;
            result.RevocationStatusValid = false;

            return new ClientCertificateException(
                "Client certificate has been revoked",
                certificate,
                result,
                requiredMode);
        }

        /// <summary>
        /// Creates an exception for when the certificate is expired
        /// </summary>
        public static ClientCertificateException CertificateExpired(
            X509Certificate2 certificate,
            DateTime notAfter,
            ClientCertificateMode requiredMode)
        {
            var result = ClientCertificateValidationResult.Failure(
                $"Certificate expired on {notAfter:yyyy-MM-dd HH:mm:ss} UTC");
            result.Certificate = certificate;

            return new ClientCertificateException(
                $"Client certificate expired on {notAfter:yyyy-MM-dd HH:mm:ss} UTC",
                certificate,
                result,
                requiredMode);
        }

        /// <summary>
        /// Creates an exception for when the certificate is not yet valid
        /// </summary>
        public static ClientCertificateException CertificateNotYetValid(
            X509Certificate2 certificate,
            DateTime notBefore,
            ClientCertificateMode requiredMode)
        {
            var result = ClientCertificateValidationResult.Failure(
                $"Certificate is not valid until {notBefore:yyyy-MM-dd HH:mm:ss} UTC");
            result.Certificate = certificate;

            return new ClientCertificateException(
                $"Client certificate is not valid until {notBefore:yyyy-MM-dd HH:mm:ss} UTC",
                certificate,
                result,
                requiredMode);
        }

        /// <summary>
        /// Creates an exception for when the certificate is self-signed but self-signed is not allowed
        /// </summary>
        public static ClientCertificateException SelfSignedNotAllowed(
            X509Certificate2 certificate,
            ClientCertificateMode requiredMode)
        {
            var result = ClientCertificateValidationResult.Failure("Self-signed certificates are not allowed");
            result.Certificate = certificate;

            return new ClientCertificateException(
                "Self-signed client certificates are not allowed",
                certificate,
                result,
                requiredMode);
        }

        /// <summary>
        /// Creates an exception for when the certificate lacks client authentication EKU
        /// </summary>
        public static ClientCertificateException MissingClientAuthEku(
            X509Certificate2 certificate,
            ClientCertificateMode requiredMode)
        {
            var result = ClientCertificateValidationResult.Failure(
                "Certificate does not have Client Authentication enhanced key usage");
            result.Certificate = certificate;

            return new ClientCertificateException(
                "Client certificate does not have the required Client Authentication enhanced key usage",
                certificate,
                result,
                requiredMode);
        }

        /// <summary>
        /// Gets a detailed error message including all validation errors
        /// </summary>
        public string GetDetailedMessage()
        {
            var parts = new List<string>
            {
                Message
            };

            if (ValidationResult?.Errors.Count > 0)
            {
                parts.Add("Validation errors:");
                foreach (var error in ValidationResult.Errors)
                {
                    parts.Add($"  - {error}");
                }
            }

            if (ValidationResult?.Warnings.Count > 0)
            {
                parts.Add("Warnings:");
                foreach (var warning in ValidationResult.Warnings)
                {
                    parts.Add($"  - {warning}");
                }
            }

            if (Certificate != null)
            {
                parts.Add($"Certificate: {Certificate.Subject}");
                parts.Add($"Issuer: {Certificate.Issuer}");
                parts.Add($"Thumbprint: {Thumbprint}");
                parts.Add($"Valid from: {Certificate.NotBefore:yyyy-MM-dd HH:mm:ss} UTC");
                parts.Add($"Valid until: {Certificate.NotAfter:yyyy-MM-dd HH:mm:ss} UTC");
            }

            parts.Add($"Required mode: {RequiredMode}");

            return string.Join("\n", parts);
        }
    }
}
