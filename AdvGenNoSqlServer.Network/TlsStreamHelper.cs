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
    /// Helper class for SSL/TLS stream operations
    /// </summary>
    public static class TlsStreamHelper
    {
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
                    SslProtocols.Tls12 | SslProtocols.Tls13,
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
    }
}
