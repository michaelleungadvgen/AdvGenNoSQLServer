// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Clustering;

namespace AdvGenNoSqlServer.Network.Clustering
{
    /// <summary>
    /// Establishes mutually-authenticated TLS for inter-node P2P links. Peers are
    /// validated against a cluster CA or an explicit thumbprint allow-list; when
    /// neither is configured the peer certificate must still chain cleanly. Fail-closed:
    /// an untrusted peer certificate aborts the connection.
    /// </summary>
    public static class P2PTlsHelper
    {
        private const SslProtocols AllowedProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

        /// <summary>Whether a node certificate is configured (TLS can be established).</summary>
        public static bool IsTlsConfigured(P2PSecurityConfiguration security)
            => !string.IsNullOrEmpty(security.CertificatePath);

        /// <summary>Loads the node certificate (PEM cert+key, or PFX/PKCS#12).</summary>
        public static X509Certificate2 LoadNodeCertificate(P2PSecurityConfiguration security)
        {
            var certPath = security.CertificatePath
                ?? throw new InvalidOperationException("P2P TLS requires Security.CertificatePath.");

            if (certPath.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
                certPath.EndsWith(".p12", StringComparison.OrdinalIgnoreCase))
            {
                return new X509Certificate2(certPath, security.CertificatePassword,
                    X509KeyStorageFlags.Exportable);
            }

            if (!string.IsNullOrEmpty(security.PrivateKeyPath))
            {
                var cert = X509Certificate2.CreateFromPemFile(certPath, security.PrivateKeyPath);
                // Re-import as exportable so SslStream can use the private key on all platforms.
                return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null,
                    X509KeyStorageFlags.Exportable);
            }

            return new X509Certificate2(certPath);
        }

        /// <summary>Server-side TLS handshake; requires a client certificate when RequireMutualTls.</summary>
        public static async Task<SslStream> AuthenticateAsServerAsync(
            Stream stream, P2PSecurityConfiguration security, CancellationToken ct)
        {
            var certificate = LoadNodeCertificate(security);
            var ssl = new SslStream(stream, leaveInnerStreamOpen: false, ValidatePeerCertificate(security));
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = security.RequireMutualTls,
                EnabledSslProtocols = AllowedProtocols,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, ct);
            return ssl;
        }

        /// <summary>Client-side TLS handshake; presents the node certificate and validates the server.</summary>
        public static async Task<SslStream> AuthenticateAsClientAsync(
            Stream stream, string targetHost, P2PSecurityConfiguration security, CancellationToken ct)
        {
            var certificates = new X509CertificateCollection();
            if (IsTlsConfigured(security))
                certificates.Add(LoadNodeCertificate(security));

            var ssl = new SslStream(stream, leaveInnerStreamOpen: false, ValidatePeerCertificate(security));
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                ClientCertificates = certificates,
                EnabledSslProtocols = AllowedProtocols,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, ct);
            return ssl;
        }

        private static RemoteCertificateValidationCallback ValidatePeerCertificate(P2PSecurityConfiguration security)
        {
            return (sender, certificate, chain, sslPolicyErrors) =>
            {
                if (certificate == null)
                    return false;

                var cert = certificate as X509Certificate2 ?? new X509Certificate2(certificate);

                // Explicit thumbprint allow-list (pinning) takes precedence.
                if (security.AllowedThumbprints.Length > 0)
                {
                    foreach (var thumbprint in security.AllowedThumbprints)
                    {
                        if (string.Equals(cert.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    return false;
                }

                // Cluster CA: build a chain anchored only at the configured CA.
                if (!string.IsNullOrEmpty(security.CaCertificatePath))
                {
                    using var ca = new X509Certificate2(security.CaCertificatePath);
                    using var customChain = new X509Chain();
                    customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    customChain.ChainPolicy.CustomTrustStore.Add(ca);
                    customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    return customChain.Build(cert);
                }

                // No CA / pinning configured: accept only a certificate with no policy errors.
                return sslPolicyErrors == SslPolicyErrors.None;
            };
        }
    }
}