// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Configuration options for certificate pinning
    /// </summary>
    public class CertificatePinningOptions
    {
        /// <summary>
        /// Whether certificate pinning is enabled (default: false)
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// The certificate pins to validate against
        /// </summary>
        public List<CertificatePin> Pins { get; set; } = new();

        /// <summary>
        /// Whether to enforce pinning strictly (default: true)
        /// When true, validation fails if no pins match
        /// When false, validation logs a warning but allows the connection
        /// </summary>
        public bool EnforceStrict { get; set; } = true;

        /// <summary>
        /// Whether to ignore expired pins during validation (default: false)
        /// When true, expired pins are not considered for matching
        /// </summary>
        public bool IgnoreExpiredPins { get; set; } = false;

        /// <summary>
        /// Creates a new instance with default settings (pinning disabled)
        /// </summary>
        public CertificatePinningOptions()
        {
        }

        /// <summary>
        /// Creates a new instance with a single pin
        /// </summary>
        public CertificatePinningOptions(string thumbprint)
        {
            Enabled = true;
            Pins.Add(new CertificatePin(thumbprint));
        }

        /// <summary>
        /// Creates a new instance with multiple pins
        /// </summary>
        public CertificatePinningOptions(params string[] thumbprints)
        {
            Enabled = true;
            foreach (var thumbprint in thumbprints)
            {
                Pins.Add(new CertificatePin(thumbprint));
            }
        }

        /// <summary>
        /// Validates the configuration
        /// </summary>
        /// <returns>True if valid, false otherwise</returns>
        public bool Validate()
        {
            if (!Enabled)
                return true;

            if (Pins.Count == 0)
                return false;

            foreach (var pin in Pins)
            {
                if (!pin.Validate())
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Adds a certificate pin
        /// </summary>
        public CertificatePinningOptions AddPin(string thumbprint, DateTime? expiresAt = null)
        {
            Pins.Add(new CertificatePin(thumbprint, expiresAt));
            Enabled = true;
            return this;
        }

        /// <summary>
        /// Adds a certificate pin with a time span from now
        /// </summary>
        public CertificatePinningOptions AddPinWithExpiration(string thumbprint, TimeSpan validFor)
        {
            Pins.Add(new CertificatePin(thumbprint, DateTime.UtcNow.Add(validFor)));
            Enabled = true;
            return this;
        }

        /// <summary>
        /// Removes all expired pins
        /// </summary>
        /// <returns>Number of pins removed</returns>
        public int RemoveExpiredPins()
        {
            var now = DateTime.UtcNow;
            var expiredCount = Pins.Count(p => p.IsExpired);
            Pins.RemoveAll(p => p.IsExpired);
            return expiredCount;
        }

        /// <summary>
        /// Gets the number of valid (non-expired) pins
        /// </summary>
        public int ValidPinCount => Pins.Count(p => !p.IsExpired);

        /// <summary>
        /// Creates a clone of this options instance
        /// </summary>
        public CertificatePinningOptions Clone()
        {
            return new CertificatePinningOptions
            {
                Enabled = Enabled,
                EnforceStrict = EnforceStrict,
                IgnoreExpiredPins = IgnoreExpiredPins,
                Pins = Pins.Select(p => p.Clone()).ToList()
            };
        }
    }

    /// <summary>
    /// Represents a single certificate pin
    /// </summary>
    public class CertificatePin
    {
        /// <summary>
        /// The certificate thumbprint (SHA-256 hash)
        /// </summary>
        public string Thumbprint { get; set; } = string.Empty;

        /// <summary>
        /// Optional expiration date for this pin (used for certificate rotation)
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Optional description for this pin
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Whether this pin has expired
        /// </summary>
        public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;

        /// <summary>
        /// Creates a new certificate pin
        /// </summary>
        public CertificatePin()
        {
        }

        /// <summary>
        /// Creates a new certificate pin with the specified thumbprint
        /// </summary>
        public CertificatePin(string thumbprint, DateTime? expiresAt = null, string? description = null)
        {
            Thumbprint = NormalizeThumbprint(thumbprint);
            ExpiresAt = expiresAt;
            Description = description;
        }

        /// <summary>
        /// Validates the pin
        /// </summary>
        public bool Validate()
        {
            return !string.IsNullOrWhiteSpace(Thumbprint) && 
                   Thumbprint.Length >= 64; // SHA-256 is 64 hex characters
        }

        /// <summary>
        /// Checks if this pin matches the given thumbprint
        /// </summary>
        public bool Matches(string thumbprint)
        {
            return string.Equals(Thumbprint, NormalizeThumbprint(thumbprint), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a clone of this pin
        /// </summary>
        public CertificatePin Clone()
        {
            return new CertificatePin(Thumbprint, ExpiresAt, Description);
        }

        /// <summary>
        /// Normalizes a thumbprint by removing separators and converting to uppercase
        /// </summary>
        public static string NormalizeThumbprint(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
                return string.Empty;

            // Remove common separators and whitespace
            var normalized = thumbprint
                .Replace("-", "")
                .Replace(":", "")
                .Replace(" ", "")
                .Trim();

            return normalized.ToUpperInvariant();
        }

        /// <summary>
        /// Validates that a string is a valid SHA-256 thumbprint format
        /// </summary>
        public static bool IsValidThumbprintFormat(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
                return false;

            var normalized = NormalizeThumbprint(thumbprint);
            
            // SHA-256 is 64 hex characters
            if (normalized.Length != 64)
                return false;

            // Check all characters are valid hex
            return normalized.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'));
        }
    }
}
