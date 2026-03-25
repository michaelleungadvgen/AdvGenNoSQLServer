// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Configuration
{
    /// <summary>
    /// Specifies the mode for client certificate requirements
    /// </summary>
    public enum ClientCertificateMode
    {
        /// <summary>
        /// No client certificate is required and one will not be requested
        /// </summary>
        None,

        /// <summary>
        /// A client certificate will be requested but not required.
        /// If a certificate is provided, it will be validated.
        /// </summary>
        Optional,

        /// <summary>
        /// A client certificate is required and must be valid.
        /// Connections without a valid certificate will be rejected.
        /// </summary>
        Required
    }

    /// <summary>
    /// Specifies the certificate revocation checking mode
    /// </summary>
    public enum RevocationCheckMode
    {
        /// <summary>
        /// No revocation checking is performed
        /// </summary>
        None,

        /// <summary>
        /// Revocation checking is performed using the online certificate status protocol (OCSP)
        /// </summary>
        Online,

        /// <summary>
        /// Revocation checking is performed using cached certificate revocation lists (CRL)
        /// </summary>
        Offline
    }

    /// <summary>
    /// Configuration for client certificate authentication (mTLS)
    /// </summary>
    public class ClientCertificateConfiguration
    {
        /// <summary>
        /// The client certificate requirement mode (default: None)
        /// </summary>
        public ClientCertificateMode Mode { get; set; } = ClientCertificateMode.None;

        /// <summary>
        /// Path to the CA certificate file for validating client certificates (PEM or DER format)
        /// If not specified, the system certificate store will be used
        /// </summary>
        public string? CaCertificatePath { get; set; }

        /// <summary>
        /// List of allowed client certificate thumbprints (SHA-256 hashes)
        /// If specified, only certificates matching these thumbprints will be accepted
        /// </summary>
        public List<string> AllowedThumbprints { get; set; } = new();

        /// <summary>
        /// Whether to validate the certificate chain (default: true)
        /// </summary>
        public bool ValidateCertificateChain { get; set; } = true;

        /// <summary>
        /// The revocation checking mode (default: Online)
        /// </summary>
        public RevocationCheckMode RevocationMode { get; set; } = RevocationCheckMode.Online;

        /// <summary>
        /// Whether to validate that the client certificate is within its validity period (default: true)
        /// </summary>
        public bool ValidateValidityPeriod { get; set; } = true;

        /// <summary>
        /// Whether to validate the certificate's enhanced key usage (EKU) for client authentication (default: true)
        /// </summary>
        public bool ValidateEnhancedKeyUsage { get; set; } = true;

        /// <summary>
        /// Whether to allow self-signed client certificates (default: false)
        /// </summary>
        public bool AllowSelfSigned { get; set; } = false;

        /// <summary>
        /// Custom validation callback for additional certificate validation
        /// Return true to accept the certificate, false to reject
        /// </summary>
        public Func<System.Security.Cryptography.X509Certificates.X509Certificate2?, bool>? CustomValidationCallback { get; set; }

        /// <summary>
        /// Validates the configuration
        /// </summary>
        public bool Validate()
        {
            // None mode is always valid
            if (Mode == ClientCertificateMode.None)
                return true;

            // Optional and Required modes need valid settings
            if (ValidateCertificateChain && !string.IsNullOrWhiteSpace(CaCertificatePath))
            {
                if (!File.Exists(CaCertificatePath))
                    return false;
            }

            // Validate thumbprints format if specified
            foreach (var thumbprint in AllowedThumbprints)
            {
                if (string.IsNullOrWhiteSpace(thumbprint))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Creates a new configuration that is a copy of this one
        /// </summary>
        public ClientCertificateConfiguration Clone()
        {
            return new ClientCertificateConfiguration
            {
                Mode = Mode,
                CaCertificatePath = CaCertificatePath,
                AllowedThumbprints = new List<string>(AllowedThumbprints),
                ValidateCertificateChain = ValidateCertificateChain,
                RevocationMode = RevocationMode,
                ValidateValidityPeriod = ValidateValidityPeriod,
                ValidateEnhancedKeyUsage = ValidateEnhancedKeyUsage,
                AllowSelfSigned = AllowSelfSigned,
                CustomValidationCallback = CustomValidationCallback
            };
        }
    }

    /// <summary>
    /// Represents the result of client certificate validation
    /// </summary>
    public class ClientCertificateValidationResult
    {
        /// <summary>
        /// Whether the certificate validation succeeded
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// The certificate that was validated (null if no certificate was provided)
        /// </summary>
        public System.Security.Cryptography.X509Certificates.X509Certificate2? Certificate { get; set; }

        /// <summary>
        /// The certificate thumbprint (SHA-256)
        /// </summary>
        public string? Thumbprint { get; set; }

        /// <summary>
        /// The subject name of the certificate
        /// </summary>
        public string? Subject { get; set; }

        /// <summary>
        /// The issuer of the certificate
        /// </summary>
        public string? Issuer { get; set; }

        /// <summary>
        /// The validation timestamp
        /// </summary>
        public DateTime ValidationTime { get; set; }

        /// <summary>
        /// List of validation errors (empty if validation succeeded)
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// List of validation warnings
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// Whether the certificate chain was validated
        /// </summary>
        public bool ChainValidated { get; set; }

        /// <summary>
        /// Whether the certificate's revocation status was checked
        /// </summary>
        public bool RevocationChecked { get; set; }

        /// <summary>
        /// Whether the certificate passed revocation checking
        /// </summary>
        public bool RevocationStatusValid { get; set; }

        /// <summary>
        /// Creates a successful validation result
        /// </summary>
        public static ClientCertificateValidationResult Success(
            System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
            string thumbprint)
        {
            return new ClientCertificateValidationResult
            {
                IsValid = true,
                Certificate = certificate,
                Thumbprint = thumbprint,
                Subject = certificate.Subject,
                Issuer = certificate.Issuer,
                ValidationTime = DateTime.UtcNow,
                ChainValidated = true,
                RevocationChecked = true,
                RevocationStatusValid = true
            };
        }

        /// <summary>
        /// Creates a failed validation result
        /// </summary>
        public static ClientCertificateValidationResult Failure(params string[] errors)
        {
            return new ClientCertificateValidationResult
            {
                IsValid = false,
                ValidationTime = DateTime.UtcNow,
                Errors = new List<string>(errors)
            };
        }

        /// <summary>
        /// Creates a result for when no certificate was provided
        /// </summary>
        public static ClientCertificateValidationResult NoCertificate()
        {
            return new ClientCertificateValidationResult
            {
                IsValid = false,
                ValidationTime = DateTime.UtcNow,
                Errors = new List<string> { "No client certificate was provided" }
            };
        }
    }
}
