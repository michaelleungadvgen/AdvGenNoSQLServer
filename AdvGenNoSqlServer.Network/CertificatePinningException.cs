// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Security.Cryptography.X509Certificates;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Exception thrown when certificate pinning validation fails
    /// </summary>
    public class CertificatePinningException : Exception
    {
        /// <summary>
        /// The certificate that failed validation
        /// </summary>
        public X509Certificate? Certificate { get; }

        /// <summary>
        /// The certificate thumbprint that was rejected
        /// </summary>
        public string? CertificateThumbprint { get; }

        /// <summary>
        /// The number of pins that were checked
        /// </summary>
        public int PinCount { get; }

        /// <summary>
        /// Whether the validation was performed in strict mode
        /// </summary>
        public bool StrictMode { get; }

        /// <summary>
        /// Creates a new certificate pinning exception
        /// </summary>
        public CertificatePinningException(string message) 
            : base(message)
        {
        }

        /// <summary>
        /// Creates a new certificate pinning exception with an inner exception
        /// </summary>
        public CertificatePinningException(string message, Exception innerException) 
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates a new certificate pinning exception with certificate details
        /// </summary>
        public CertificatePinningException(
            string message, 
            X509Certificate? certificate, 
            string? certificateThumbprint,
            int pinCount,
            bool strictMode) 
            : base(message)
        {
            Certificate = certificate;
            CertificateThumbprint = certificateThumbprint;
            PinCount = pinCount;
            StrictMode = strictMode;
        }

        /// <summary>
        /// Creates a pinning failed exception
        /// </summary>
        public static CertificatePinningException PinValidationFailed(
            X509Certificate? certificate,
            string certificateThumbprint,
            int pinCount,
            bool strictMode)
        {
            var subject = certificate?.Subject ?? "unknown";
            var message = strictMode
                ? $"Certificate pinning validation failed for '{subject}'. " +
                  $"Certificate thumbprint '{certificateThumbprint}' does not match any of the {pinCount} configured pins."
                : $"Certificate pinning validation warning for '{subject}': " +
                  $"Certificate thumbprint '{certificateThumbprint}' does not match any pins (non-strict mode, connection allowed).";

            return new CertificatePinningException(message, certificate, certificateThumbprint, pinCount, strictMode);
        }

        /// <summary>
        /// Creates an exception for when no valid pins are configured
        /// </summary>
        public static CertificatePinningException NoValidPinsConfigured()
        {
            return new CertificatePinningException(
                "Certificate pinning is enabled but no valid pins are configured. " +
                "Please add at least one valid certificate pin.");
        }

        /// <summary>
        /// Creates an exception for an invalid thumbprint format
        /// </summary>
        public static CertificatePinningException InvalidThumbprintFormat(string thumbprint)
        {
            return new CertificatePinningException(
                $"Invalid certificate thumbprint format: '{thumbprint}'. " +
                "Thumbprint must be a 64-character hex string (SHA-256).");
        }
    }
}
