// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Event arguments for certificate pinning validation events
    /// </summary>
    public class PinValidationEventArgs : EventArgs
    {
        /// <summary>
        /// The certificate being validated
        /// </summary>
        public X509Certificate? Certificate { get; }

        /// <summary>
        /// The certificate thumbprint
        /// </summary>
        public string CertificateThumbprint { get; }

        /// <summary>
        /// Whether the certificate matched a pin
        /// </summary>
        public bool IsPinned { get; set; }

        /// <summary>
        /// The matching pin (if any)
        /// </summary>
        public CertificatePin? MatchedPin { get; set; }

        /// <summary>
        /// Number of pins checked
        /// </summary>
        public int PinCount { get; }

        /// <summary>
        /// Whether the validation should fail (can be modified by event handlers)
        /// </summary>
        public bool ShouldFail { get; set; }

        /// <summary>
        /// Custom failure message (can be set by event handlers)
        /// </summary>
        public string? FailureMessage { get; set; }

        /// <summary>
        /// Creates new event args
        /// </summary>
        public PinValidationEventArgs(
            X509Certificate? certificate,
            string certificateThumbprint,
            bool isPinned,
            int pinCount)
        {
            Certificate = certificate;
            CertificateThumbprint = certificateThumbprint;
            IsPinned = isPinned;
            PinCount = pinCount;
            ShouldFail = !isPinned;
        }
    }

    /// <summary>
    /// Delegate for pin validation events
    /// </summary>
    public delegate void PinValidationEventHandler(object sender, PinValidationEventArgs e);

    /// <summary>
    /// Validates certificates against pinned thumbprints
    /// </summary>
    public static class CertificatePinValidator
    {
        /// <summary>
        /// Event raised when a certificate is validated against pins
        /// </summary>
        public static event PinValidationEventHandler? PinValidated;

        /// <summary>
        /// Validates a certificate against the configured pinning options
        /// </summary>
        /// <param name="certificate">The certificate to validate</param>
        /// <param name="options">The pinning options</param>
        /// <returns>True if valid or pinning disabled, false if validation failed</returns>
        public static bool ValidateCertificate(X509Certificate? certificate, CertificatePinningOptions? options)
        {
            // If pinning is disabled or no options, validation passes
            if (options == null || !options.Enabled)
                return true;

            // If no certificate provided, validation fails in strict mode
            if (certificate == null)
            {
                var nullArgs = new PinValidationEventArgs(null, string.Empty, false, options.Pins.Count)
                {
                    ShouldFail = options.EnforceStrict,
                    FailureMessage = "No certificate provided for pinning validation"
                };
                PinValidated?.Invoke(null, nullArgs);
                return !nullArgs.ShouldFail;
            }

            // Compute certificate thumbprint
            var thumbprint = ComputeSha256Thumbprint(certificate);
            
            // Check against pins
            var matchingPin = FindMatchingPin(thumbprint, options);
            var isPinned = matchingPin != null;

            var eventArgs = new PinValidationEventArgs(certificate, thumbprint, isPinned, options.Pins.Count)
            {
                MatchedPin = matchingPin,
                ShouldFail = !isPinned && options.EnforceStrict
            };

            PinValidated?.Invoke(null, eventArgs);

            return !eventArgs.ShouldFail;
        }

        /// <summary>
        /// Validates a certificate and throws an exception if validation fails
        /// </summary>
        /// <param name="certificate">The certificate to validate</param>
        /// <param name="options">The pinning options</param>
        /// <exception cref="CertificatePinningException">Thrown when validation fails</exception>
        public static void ValidateAndEnforce(X509Certificate? certificate, CertificatePinningOptions? options)
        {
            if (options == null || !options.Enabled)
                return;

            if (certificate == null)
            {
                if (options.EnforceStrict)
                {
                    throw new CertificatePinningException(
                        "Certificate pinning validation failed: No certificate provided.",
                        null,
                        string.Empty,
                        options.Pins.Count,
                        options.EnforceStrict);
                }
                return;
            }

            var thumbprint = ComputeSha256Thumbprint(certificate);
            var matchingPin = FindMatchingPin(thumbprint, options);

            if (matchingPin == null)
            {
                var ex = CertificatePinningException.PinValidationFailed(
                    certificate,
                    thumbprint,
                    options.Pins.Count,
                    options.EnforceStrict);

                if (options.EnforceStrict)
                    throw ex;
            }
        }

        /// <summary>
        /// Checks if a certificate matches any of the configured pins without throwing
        /// </summary>
        /// <param name="certificate">The certificate to check</param>
        /// <param name="options">The pinning options</param>
        /// <param name="matchedPin">The pin that matched (if any)</param>
        /// <returns>True if certificate matches a pin or pinning is disabled</returns>
        public static bool IsCertificatePinned(
            X509Certificate? certificate, 
            CertificatePinningOptions? options,
            out CertificatePin? matchedPin)
        {
            matchedPin = null;

            if (options == null || !options.Enabled)
                return true;

            if (certificate == null)
                return false;

            var thumbprint = ComputeSha256Thumbprint(certificate);
            matchedPin = FindMatchingPin(thumbprint, options);
            return matchedPin != null;
        }

        /// <summary>
        /// Computes the SHA-256 thumbprint of a certificate
        /// </summary>
        public static string ComputeSha256Thumbprint(X509Certificate certificate)
        {
            var rawData = certificate.GetRawCertData();
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(rawData);
            return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
        }

        /// <summary>
        /// Computes the SHA-256 thumbprint from raw certificate data
        /// </summary>
        public static string ComputeSha256Thumbprint(byte[] rawData)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(rawData);
            return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
        }

        /// <summary>
        /// Finds a matching pin for the given thumbprint
        /// </summary>
        private static CertificatePin? FindMatchingPin(string thumbprint, CertificatePinningOptions options)
        {
            var normalizedThumbprint = CertificatePin.NormalizeThumbprint(thumbprint);

            foreach (var pin in options.Pins)
            {
                // Skip expired pins if configured to ignore them
                if (options.IgnoreExpiredPins && pin.IsExpired)
                    continue;

                if (pin.Matches(normalizedThumbprint))
                    return pin;
            }

            return null;
        }

        /// <summary>
        /// Gets all valid (non-expired) pins
        /// </summary>
        public static IEnumerable<CertificatePin> GetValidPins(CertificatePinningOptions options)
        {
            return options.Pins.Where(p => !p.IsExpired);
        }

        /// <summary>
        /// Checks if any pins are configured and valid
        /// </summary>
        public static bool HasValidPins(CertificatePinningOptions? options)
        {
            if (options == null || !options.Enabled)
                return false;

            return options.Pins.Any(p => !p.IsExpired);
        }

        /// <summary>
        /// Validates the format of a thumbprint
        /// </summary>
        public static bool ValidateThumbprintFormat(string thumbprint)
        {
            return CertificatePin.IsValidThumbprintFormat(thumbprint);
        }

        /// <summary>
        /// Creates a certificate pin from a certificate file
        /// </summary>
        public static CertificatePin CreatePinFromCertificateFile(string filePath, DateTime? expiresAt = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Certificate file not found", filePath);

            var bytes = File.ReadAllBytes(filePath);
            var cert = X509CertificateLoader.LoadCertificate(bytes);
            var thumbprint = ComputeSha256Thumbprint(cert);
            
            return new CertificatePin(thumbprint, expiresAt, $"From file: {Path.GetFileName(filePath)}");
        }

        /// <summary>
        /// Creates a certificate pin from a certificate instance
        /// </summary>
        public static CertificatePin CreatePinFromCertificate(X509Certificate certificate, DateTime? expiresAt = null)
        {
            var thumbprint = ComputeSha256Thumbprint(certificate);
            return new CertificatePin(thumbprint, expiresAt, certificate.Subject);
        }
    }
}
