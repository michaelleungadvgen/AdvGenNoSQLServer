// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Event arguments for client certificate validation events in TlsStreamHelper
    /// </summary>
    public class ClientCertValidationEventArgs : EventArgs
    {
        /// <summary>
        /// The client certificate
        /// </summary>
        public X509Certificate2? Certificate { get; }

        /// <summary>
        /// Whether the certificate is valid
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        /// Validation errors if any
        /// </summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// The client certificate mode
        /// </summary>
        public ClientCertificateMode Mode { get; }

        /// <summary>
        /// Creates a new instance of ClientCertValidationEventArgs
        /// </summary>
        public ClientCertValidationEventArgs(
            X509Certificate2? certificate,
            bool isValid,
            IReadOnlyList<string> errors,
            ClientCertificateMode mode)
        {
            Certificate = certificate;
            IsValid = isValid;
            Errors = errors;
            Mode = mode;
        }
    }

    /// <summary>
    /// Delegate for client certificate validation events in TlsStreamHelper
    /// </summary>
    public delegate void ClientCertValidationEventHandler(object sender, ClientCertValidationEventArgs e);
    /// <summary>
    /// Event arguments for cipher suite validation
    /// </summary>
    public class CipherValidationEventArgs : EventArgs
    {
        /// <summary>
        /// The negotiated cipher suite
        /// </summary>
        public TlsCipherSuite CipherSuite { get; }

        /// <summary>
        /// Whether the cipher suite is allowed
        /// </summary>
        public bool IsAllowed { get; set; }

        /// <summary>
        /// Reason why the cipher was rejected (null if allowed)
        /// </summary>
        public string? RejectionReason { get; set; }

        /// <summary>
        /// Creates a new instance of CipherValidationEventArgs
        /// </summary>
        public CipherValidationEventArgs(TlsCipherSuite cipherSuite, bool isAllowed)
        {
            CipherSuite = cipherSuite;
            IsAllowed = isAllowed;
        }
    }

    /// <summary>
    /// Delegate for cipher suite validation events
    /// </summary>
    public delegate void CipherValidationEventHandler(object sender, CipherValidationEventArgs e);
    /// <summary>
    /// Helper class for SSL/TLS stream operations
    /// </summary>
    public static class TlsStreamHelper
    {
        /// <summary>
        /// Event raised when a cipher suite is validated
        /// </summary>
        public static event CipherValidationEventHandler? CipherValidated;

        /// <summary>
        /// Event raised when a client certificate is validated
        /// </summary>
        public static event ClientCertValidationEventHandler? ClientCertValidated;
        /// <summary>
        /// Creates an SSL server stream and performs the TLS handshake
        /// </summary>
        /// <param name="client">The TCP client</param>
        /// <param name="configuration">Server configuration with SSL settings</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An authenticated SSL stream</returns>
        public static async Task<SslStream> CreateServerSslStreamAsync(
            TcpClient client,
            ServerConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            if (!configuration.EnableSsl)
                throw new InvalidOperationException("SSL is not enabled in configuration");

            var certificate = LoadCertificate(configuration);
            if (certificate == null)
                throw new InvalidOperationException("Failed to load SSL certificate");

            var sslStream = new SslStream(
                client.GetStream(),
                false,
                configuration.RequireClientCertificate ? RemoteCertificateValidationCallback : null,
                null,
                EncryptionPolicy.RequireEncryption);

            try
            {
                await sslStream.AuthenticateAsServerAsync(
                    certificate,
                    configuration.RequireClientCertificate,
                    configuration.SslProtocols,
                    configuration.CheckCertificateRevocation);

                // Validate the negotiated TLS version meets minimum requirements
                if (!TlsVersionValidator.ValidateTlsVersion(
                    sslStream.SslProtocol,
                    configuration.MinimumTlsVersion,
                    configuration.RequireMinimumTlsVersion))
                {
                    var negotiatedVersion = TlsVersionValidator.GetTlsVersionName(sslStream.SslProtocol);
                    var minimumVersion = TlsVersionValidator.GetTlsVersionName(configuration.MinimumTlsVersion);
                    
                    sslStream.Dispose();
                    throw new TlsVersionException(
                        $"TLS version {negotiatedVersion} is below the minimum required version {minimumVersion}. " +
                        $"Please upgrade your client to support at least {minimumVersion}.",
                        sslStream.SslProtocol,
                        configuration.MinimumTlsVersion);
                }

                // Validate the negotiated cipher suite
                var cipherOptions = ToCipherSuiteOptions(configuration.CipherSuiteConfig);
                if (!ValidateCipherSuite(sslStream, cipherOptions))
                {
                    var cipherSuite = sslStream.NegotiatedCipherSuite;
                    var reason = CipherSuiteValidator.GetCipherWeaknessReason(cipherSuite);
                    var cipherName = CipherSuiteValidator.GetTlsCipherSuiteName(cipherSuite);
                    
                    sslStream.Dispose();
                    throw new InvalidOperationException(
                        $"Cipher suite '{cipherName}' is not allowed. {reason}. " +
                        "Please configure your client to use a stronger cipher suite.");
                }

                // Validate certificate pinning if enabled
                var pinningOptions = ToPinningOptions(configuration.CertificatePinningConfig);
                if (pinningOptions.Enabled)
                {
                    var remoteCertificate = sslStream.RemoteCertificate;
                    if (!CertificatePinValidator.ValidateCertificate(remoteCertificate, pinningOptions))
                    {
                        sslStream.Dispose();
                        throw new CertificatePinningException(
                            "Certificate pinning validation failed. The remote certificate does not match any configured pin.",
                            remoteCertificate,
                            remoteCertificate != null ? CertificatePinValidator.ComputeSha256Thumbprint(remoteCertificate) : null,
                            pinningOptions.Pins.Count,
                            pinningOptions.EnforceStrict);
                    }
                }

                return sslStream;
            }
            catch
            {
                sslStream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates an SSL client stream and performs the TLS handshake
        /// </summary>
        /// <param name="client">The TCP client</param>
        /// <param name="targetHost">The target host name for certificate validation</param>
        /// <param name="clientCertificate">Optional client certificate for mutual TLS</param>
        /// <param name="checkCertificateRevocation">Whether to check certificate revocation</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An authenticated SSL stream</returns>
        public static async Task<SslStream> CreateClientSslStreamAsync(
            TcpClient client,
            string targetHost,
            X509Certificate? clientCertificate = null,
            bool checkCertificateRevocation = true,
            CancellationToken cancellationToken = default)
        {
            return await CreateClientSslStreamAsync(
                client,
                targetHost,
                clientCertificate,
                SslProtocols.Tls12 | SslProtocols.Tls13,
                checkCertificateRevocation,
                cancellationToken);
        }

        /// <summary>
        /// Creates an SSL client stream with specific TLS protocol versions and performs the TLS handshake
        /// </summary>
        /// <param name="client">The TCP client</param>
        /// <param name="targetHost">The target host name for certificate validation</param>
        /// <param name="clientCertificate">Optional client certificate for mutual TLS</param>
        /// <param name="enabledProtocols">The TLS protocol versions to enable</param>
        /// <param name="checkCertificateRevocation">Whether to check certificate revocation</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An authenticated SSL stream</returns>
        public static async Task<SslStream> CreateClientSslStreamAsync(
            TcpClient client,
            string targetHost,
            X509Certificate? clientCertificate,
            SslProtocols enabledProtocols,
            bool checkCertificateRevocation,
            CancellationToken cancellationToken = default)
        {
            var sslStream = new SslStream(
                client.GetStream(),
                false,
                ServerCertificateValidationCallback,
                null,
                EncryptionPolicy.RequireEncryption);

            try
            {
                await sslStream.AuthenticateAsClientAsync(
                    targetHost,
                    clientCertificate != null ? new X509CertificateCollection { clientCertificate } : null,
                    enabledProtocols,
                    checkCertificateRevocation);

                return sslStream;
            }
            catch
            {
                sslStream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Loads the SSL certificate based on configuration settings
        /// </summary>
        public static X509Certificate2? LoadCertificate(ServerConfiguration configuration)
        {
            if (configuration.UseCertificateStore)
            {
                return LoadCertificateFromStore(configuration.SslCertificateThumbprint);
            }
            else
            {
                return LoadCertificateFromFile(
                    configuration.SslCertificatePath,
                    configuration.SslCertificatePassword);
            }
        }

        /// <summary>
        /// Loads a certificate from a PFX file
        /// </summary>
        public static X509Certificate2? LoadCertificateFromFile(string? path, string? password)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (!File.Exists(path))
                throw new FileNotFoundException($"SSL certificate file not found: {path}");

            try
            {
                var bytes = File.ReadAllBytes(path);
                if (string.IsNullOrEmpty(password))
                {
                    return X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.Exportable);
                }
                return X509CertificateLoader.LoadPkcs12(bytes, password, X509KeyStorageFlags.Exportable);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load SSL certificate from {path}", ex);
            }
        }

        /// <summary>
        /// Loads a certificate from the certificate store (Windows)
        /// </summary>
        public static X509Certificate2? LoadCertificateFromStore(string? thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
                return null;

            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);

            var certificates = store.Certificates.Find(
                X509FindType.FindByThumbprint,
                thumbprint,
                validOnly: true);

            store.Close();

            return certificates.Count > 0 ? certificates[0] : null;
        }

        /// <summary>
        /// Creates a self-signed certificate for development/testing purposes
        /// </summary>
        /// <param name="subjectName">The subject name for the certificate (e.g., "CN=localhost")</param>
        /// <param name="validDays">Number of days the certificate is valid (default: 365)</param>
        /// <returns>A self-signed certificate with exportable private key</returns>
        public static X509Certificate2 CreateSelfSignedCertificate(
            string subjectName = "CN=localhost",
            int validDays = 365)
        {
            using var rsa = RSA.Create(2048);
            var distinguishedName = new X500DistinguishedName(subjectName);
            var request = new CertificateRequest(
                distinguishedName,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    false));

            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new System.Security.Cryptography.OidCollection
                    {
                        new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1") // Server Authentication
                    },
                    false));

            // Add Subject Alternative Name for localhost and IP addresses
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(System.Net.IPAddress.Parse("127.0.0.1"));
            sanBuilder.AddIpAddress(System.Net.IPAddress.Parse("::1"));
            request.CertificateExtensions.Add(sanBuilder.Build());

            var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(validDays));

            // Export and re-import to create a certificate with exportable private key
            // Use ephemeral key set to allow export
            var export = certificate.Export(X509ContentType.Pfx, (string?)null);
            var loadedCert = X509CertificateLoader.LoadPkcs12(
                export,
                null,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

            return loadedCert;
        }

        /// <summary>
        /// Saves a certificate to a PFX file
        /// </summary>
        public static void SaveCertificateToFile(X509Certificate2 certificate, string path, string? password = null)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bytes = certificate.Export(X509ContentType.Pfx, password);
            File.WriteAllBytes(path, bytes);
        }

        /// <summary>
        /// Validates the server certificate on the client side
        /// </summary>
        private static bool ServerCertificateValidationCallback(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            // For development/testing, you might want to be more lenient
            // In production, you should validate the certificate properly
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            // Log the specific error
            Console.WriteLine($"SSL Certificate validation error: {sslPolicyErrors}");

            // Allow local development certificates
            if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch)
            {
                if (certificate?.Subject.Contains("localhost") == true)
                {
                    Console.WriteLine("Allowing localhost certificate with name mismatch");
                    return true;
                }
            }

            // In production, return false here to reject invalid certificates
            return false;
        }

        /// <summary>
        /// Validates the client certificate on the server side (for mutual TLS)
        /// </summary>
        private static bool RemoteCertificateValidationCallback(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            Console.WriteLine($"Client certificate validation error: {sslPolicyErrors}");
            return false;
        }

        /// <summary>
        /// Gets SSL/TLS connection info for logging/diagnostics
        /// </summary>
        public static string GetSslConnectionInfo(SslStream sslStream)
        {
            return $"Protocol: {sslStream.SslProtocol}, Cipher: {sslStream.NegotiatedCipherSuite}, " +
                   $"KeyExchange: {sslStream.KeyExchangeAlgorithm}, CipherStrength: {sslStream.CipherStrength}";
        }

        /// <summary>
        /// Validates the negotiated cipher suite against the configured options
        /// </summary>
        /// <param name="sslStream">The SSL stream to validate</param>
        /// <param name="options">The cipher suite options</param>
        /// <returns>True if the cipher suite is allowed, false otherwise</returns>
        public static bool ValidateCipherSuite(SslStream sslStream, CipherSuiteOptions? options)
        {
            if (options == null)
            {
                // Use default strong options
                options = new CipherSuiteOptions();
            }

            var cipherSuite = sslStream.NegotiatedCipherSuite;
            var isAllowed = CipherSuiteValidator.IsCipherAllowed(cipherSuite, options);
            var reason = isAllowed ? null : CipherSuiteValidator.GetCipherWeaknessReason(cipherSuite);

            var eventArgs = new CipherValidationEventArgs(cipherSuite, isAllowed)
            {
                RejectionReason = reason
            };

            CipherValidated?.Invoke(null, eventArgs);

            return eventArgs.IsAllowed;
        }

        /// <summary>
        /// Validates the negotiated cipher suite after handshake and throws exception if weak
        /// </summary>
        /// <param name="sslStream">The SSL stream to validate</param>
        /// <param name="options">The cipher suite options</param>
        /// <exception cref="InvalidOperationException">Thrown when cipher suite is not allowed</exception>
        public static void ValidateAndEnforceCipherSuite(SslStream sslStream, CipherSuiteOptions? options)
        {
            if (!ValidateCipherSuite(sslStream, options))
            {
                var cipherSuite = sslStream.NegotiatedCipherSuite;
                var reason = CipherSuiteValidator.GetCipherWeaknessReason(cipherSuite);
                var cipherName = CipherSuiteValidator.GetTlsCipherSuiteName(cipherSuite);
                
                throw new InvalidOperationException(
                    $"Cipher suite '{cipherName}' is not allowed. {reason}. " +
                    "Please configure your client to use a stronger cipher suite.");
            }
        }

        /// <summary>
        /// Converts CipherSuiteConfiguration from ServerConfiguration to CipherSuiteOptions
        /// </summary>
        public static CipherSuiteOptions ToCipherSuiteOptions(CipherSuiteConfiguration? config)
        {
            if (config == null)
            {
                return new CipherSuiteOptions();
            }

            return new CipherSuiteOptions
            {
                UseStrongCipherSuitesOnly = config.UseStrongCipherSuitesOnly,
                AllowRc4 = config.AllowRc4,
                AllowDes = config.AllowDes,
                AllowMd5 = config.AllowMd5,
                AllowSha1 = config.AllowSha1,
                AllowNullEncryption = config.AllowNullEncryption,
                MinimumCipherStrength = config.MinimumCipherStrength
            };
        }

        /// <summary>
        /// Converts CertificatePinningConfiguration from ServerConfiguration to CertificatePinningOptions
        /// </summary>
        public static CertificatePinningOptions ToPinningOptions(CertificatePinningConfiguration? config)
        {
            if (config == null || !config.Enabled)
            {
                return new CertificatePinningOptions { Enabled = false };
            }

            var options = new CertificatePinningOptions
            {
                Enabled = config.Enabled,
                EnforceStrict = config.EnforceStrict,
                IgnoreExpiredPins = config.IgnoreExpiredPins
            };

            foreach (var thumbprint in config.Thumbprints)
            {
                DateTime? expiresAt = null;
                if (config.PinExpirations?.TryGetValue(thumbprint, out var expiration) == true)
                {
                    expiresAt = expiration;
                }
                options.Pins.Add(new CertificatePin(thumbprint, expiresAt));
            }

            return options;
        }

        /// <summary>
        /// Converts ClientCertificateConfiguration to the format used by the validator
        /// </summary>
        public static ClientCertificateConfiguration? ToClientCertConfig(ServerConfiguration configuration)
        {
            if (configuration.RequireClientCertificate)
            {
                // Legacy mode - require client certificate
                return new ClientCertificateConfiguration
                {
                    Mode = ClientCertificateMode.Required,
                    ValidateCertificateChain = true,
                    RevocationMode = configuration.CheckCertificateRevocation 
                        ? RevocationCheckMode.Online 
                        : RevocationCheckMode.None
                };
            }

            // Check for new configuration property
            var config = configuration.ClientCertificateConfig;
            if (config != null)
            {
                return config;
            }

            return null;
        }

        /// <summary>
        /// Creates an SSL server stream with client certificate validation
        /// </summary>
        /// <param name="client">The TCP client</param>
        /// <param name="configuration">Server configuration with SSL settings</param>
        /// <param name="clientCertConfig">Client certificate configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An authenticated SSL stream with validated client certificate</returns>
        public static async Task<SslStream> CreateServerSslStreamWithClientCertAsync(
            TcpClient client,
            ServerConfiguration configuration,
            ClientCertificateConfiguration clientCertConfig,
            CancellationToken cancellationToken = default)
        {
            if (!configuration.EnableSsl)
                throw new InvalidOperationException("SSL is not enabled in configuration");

            var certificate = LoadCertificate(configuration);
            if (certificate == null)
                throw new InvalidOperationException("Failed to load SSL certificate");

            // Determine if we should request/verify client certificates
            bool requireClientCert = IsClientCertificateRequired(clientCertConfig.Mode);
            bool verifyClientCert = clientCertConfig.Mode != ClientCertificateMode.None;

            var sslStream = new SslStream(
                client.GetStream(),
                false,
                (sender, cert, chain, errors) => 
                    ClientCertificateValidationCallback(cert, chain, errors, clientCertConfig),
                null,
                EncryptionPolicy.RequireEncryption);

            try
            {
                var sslOptions = new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ClientCertificateRequired = clientCertConfig.Mode == ClientCertificateMode.Required,
                    EnabledSslProtocols = configuration.SslProtocols,
                    CertificateRevocationCheckMode = MapToRevocationCheckMode(clientCertConfig.RevocationMode)
                };

                await sslStream.AuthenticateAsServerAsync(sslOptions, cancellationToken);

                // Validate the negotiated TLS version meets minimum requirements
                if (!TlsVersionValidator.ValidateTlsVersion(
                    sslStream.SslProtocol,
                    configuration.MinimumTlsVersion,
                    configuration.RequireMinimumTlsVersion))
                {
                    var negotiatedVersion = TlsVersionValidator.GetTlsVersionName(sslStream.SslProtocol);
                    var minimumVersion = TlsVersionValidator.GetTlsVersionName(configuration.MinimumTlsVersion);
                    
                    sslStream.Dispose();
                    throw new TlsVersionException(
                        $"TLS version {negotiatedVersion} is below the minimum required version {minimumVersion}. " +
                        $"Please upgrade your client to support at least {minimumVersion}.",
                        sslStream.SslProtocol,
                        configuration.MinimumTlsVersion);
                }

                // Validate the negotiated cipher suite
                var cipherOptions = ToCipherSuiteOptions(configuration.CipherSuiteConfig);
                if (!ValidateCipherSuite(sslStream, cipherOptions))
                {
                    var cipherSuite = sslStream.NegotiatedCipherSuite;
                    var reason = CipherSuiteValidator.GetCipherWeaknessReason(cipherSuite);
                    var cipherName = CipherSuiteValidator.GetTlsCipherSuiteName(cipherSuite);
                    
                    sslStream.Dispose();
                    throw new InvalidOperationException(
                        $"Cipher suite '{cipherName}' is not allowed. {reason}. " +
                        "Please configure your client to use a stronger cipher suite.");
                }

                // Validate certificate pinning if enabled
                var pinningOptions = ToPinningOptions(configuration.CertificatePinningConfig);
                if (pinningOptions.Enabled)
                {
                    var remoteCertificate = sslStream.RemoteCertificate;
                    if (!CertificatePinValidator.ValidateCertificate(remoteCertificate, pinningOptions))
                    {
                        sslStream.Dispose();
                        throw new CertificatePinningException(
                            "Certificate pinning validation failed. The remote certificate does not match any configured pin.",
                            remoteCertificate,
                            remoteCertificate != null ? CertificatePinValidator.ComputeSha256Thumbprint(remoteCertificate) : null,
                            pinningOptions.Pins.Count,
                            pinningOptions.EnforceStrict);
                    }
                }

                // Validate client certificate based on mode
                if (clientCertConfig.Mode != ClientCertificateMode.None)
                {
                    var clientCert = sslStream.RemoteCertificate;
                    
                    if (clientCertConfig.Mode == ClientCertificateMode.Required && clientCert == null)
                    {
                        sslStream.Dispose();
                        throw ClientCertificateException.MissingCertificate(clientCertConfig.Mode);
                    }

                    if (clientCert != null)
                    {
                        // Perform full validation
                        var result = ClientCertificateValidator.Validate(
                            clientCert,
                            null,
                            SslPolicyErrors.None,
                            clientCertConfig);

                        ClientCertValidated?.Invoke(null, new ClientCertValidationEventArgs(
                            result.Certificate,
                            result.IsValid,
                            result.Errors,
                            clientCertConfig.Mode));

                        if (!result.IsValid)
                        {
                            sslStream.Dispose();
                            throw ClientCertificateException.ValidationFailed(result, clientCertConfig.Mode);
                        }
                    }
                }

                return sslStream;
            }
            catch
            {
                sslStream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Callback for validating client certificates during TLS handshake
        /// </summary>
        private static bool ClientCertificateValidationCallback(
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors,
            ClientCertificateConfiguration config)
        {
            // If no client cert is required or provided, allow the connection
            if (config.Mode == ClientCertificateMode.None)
                return true;

            // If no certificate provided
            if (certificate == null)
            {
                // For Required mode, this will fail later
                // For Optional mode, it's OK
                return config.Mode != ClientCertificateMode.Required;
            }

            // For Optional mode, basic validation is sufficient
            if (config.Mode == ClientCertificateMode.Optional)
            {
                // Allow if no SSL policy errors, or if we allow self-signed
                if (sslPolicyErrors == SslPolicyErrors.None)
                    return true;

                // Allow untrusted root if self-signed is allowed
                if (config.AllowSelfSigned && 
                    sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
                {
                    return true;
                }

                // Otherwise, do full validation
            }

            // Do full validation
            var result = ClientCertificateValidator.Validate(certificate, chain, sslPolicyErrors, config);
            
            ClientCertValidated?.Invoke(null, new ClientCertValidationEventArgs(
                result.Certificate,
                result.IsValid,
                result.Errors,
                config.Mode));

            return result.IsValid;
        }

        /// <summary>
        /// Maps ClientCertificateMode to determine if client certificate is required
        /// </summary>
        private static bool IsClientCertificateRequired(ClientCertificateMode mode)
        {
            return mode == ClientCertificateMode.Required;
        }

        /// <summary>
        /// Maps ClientCertificateMode to determine if client certificate should be requested
        /// </summary>
        private static bool IsClientCertificateRequested(ClientCertificateMode mode)
        {
            return mode == ClientCertificateMode.Optional || mode == ClientCertificateMode.Required;
        }

        /// <summary>
        /// Maps RevocationCheckMode to X509RevocationMode
        /// </summary>
        private static X509RevocationMode MapToRevocationCheckMode(RevocationCheckMode mode)
        {
            return mode switch
            {
                RevocationCheckMode.None => X509RevocationMode.NoCheck,
                RevocationCheckMode.Online => X509RevocationMode.Online,
                RevocationCheckMode.Offline => X509RevocationMode.Offline,
                _ => X509RevocationMode.Online
            };
        }

        /// <summary>
        /// Creates a self-signed client certificate for testing purposes
        /// </summary>
        /// <param name="subjectName">The subject name (e.g., "CN=testclient")</param>
        /// <param name="validDays">Number of days the certificate is valid</param>
        /// <returns>A self-signed client certificate</returns>
        public static X509Certificate2 CreateSelfSignedClientCertificate(
            string subjectName = "CN=testclient",
            int validDays = 365)
        {
            using var rsa = RSA.Create(2048);
            var distinguishedName = new X500DistinguishedName(subjectName);
            var request = new CertificateRequest(
                distinguishedName,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    false));

            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new System.Security.Cryptography.OidCollection
                    {
                        new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.2") // Client Authentication
                    },
                    false));

            var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(validDays));

            // Export and re-import to make it exportable
            var export = certificate.Export(X509ContentType.Pfx, (string?)null);
            return X509CertificateLoader.LoadPkcs12(
                export,
                null,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }
    }
}
