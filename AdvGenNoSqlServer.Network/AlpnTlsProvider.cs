// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace AdvGenNoSqlServer.Network;

/// <summary>
/// Event arguments for ALPN negotiation events
/// </summary>
public class AlpnNegotiationEventArgs : EventArgs
{
    /// <summary>
    /// The result of the ALPN negotiation
    /// </summary>
    public AlpnNegotiationResult Result { get; }

    /// <summary>
    /// The remote endpoint information
    /// </summary>
    public string RemoteEndpoint { get; }

    /// <summary>
    /// Creates a new instance of AlpnNegotiationEventArgs
    /// </summary>
    public AlpnNegotiationEventArgs(AlpnNegotiationResult result, string remoteEndpoint)
    {
        Result = result;
        RemoteEndpoint = remoteEndpoint;
    }
}

/// <summary>
/// Delegate for ALPN negotiation events
/// </summary>
public delegate void AlpnNegotiationEventHandler(object sender, AlpnNegotiationEventArgs e);

/// <summary>
/// Provides Application-Layer Protocol Negotiation (ALPN) support for TLS connections.
/// ALPN allows clients and servers to negotiate which application protocol to use 
/// during the TLS handshake, improving connection establishment performance.
/// </summary>
public static class AlpnTlsProvider
{
    /// <summary>
    /// Event raised when ALPN negotiation completes
    /// </summary>
    public static event AlpnNegotiationEventHandler? AlpnNegotiated;

    /// <summary>
    /// Creates an SSL server stream with ALPN support and performs the TLS handshake
    /// </summary>
    /// <param name="client">The TCP client</param>
    /// <param name="configuration">Server configuration with SSL and ALPN settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An authenticated SSL stream with negotiated ALPN protocol</returns>
    /// <exception cref="InvalidOperationException">If SSL is not enabled or certificate loading fails</exception>
    /// <exception cref="AlpnException">If ALPN negotiation fails and ALPN is required</exception>
    public static async Task<SslStream> CreateServerSslStreamWithAlpnAsync(
        TcpClient client,
        ServerConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.EnableSsl)
            throw new InvalidOperationException("SSL is not enabled in configuration");

        var alpnConfig = configuration.AlpnConfig;
        if (alpnConfig == null || !alpnConfig.Enabled)
        {
            // Fall back to standard TLS without ALPN
            return await TlsStreamHelper.CreateServerSslStreamAsync(client, configuration, cancellationToken);
        }

        var certificate = TlsStreamHelper.LoadCertificate(configuration);
        if (certificate == null)
            throw new InvalidOperationException("Failed to load SSL certificate");

        var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

        var sslStream = new SslStream(
            client.GetStream(),
            false,
            configuration.RequireClientCertificate ? RemoteCertificateValidationCallback : null,
            null,
            EncryptionPolicy.RequireEncryption);

        try
        {
            // Convert protocol strings to SslApplicationProtocol objects
            var applicationProtocols = alpnConfig.Protocols
                .Select(p => new SslApplicationProtocol(p))
                .ToList();

            var sslOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = configuration.RequireClientCertificate,
                EnabledSslProtocols = configuration.SslProtocols,
                CertificateRevocationCheckMode = configuration.CheckCertificateRevocation 
                    ? X509RevocationMode.Online 
                    : X509RevocationMode.NoCheck,
                ApplicationProtocols = applicationProtocols
            };

            await sslStream.AuthenticateAsServerAsync(sslOptions, cancellationToken);

            // Validate TLS version
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

            // Validate cipher suite
            var cipherOptions = TlsStreamHelper.ToCipherSuiteOptions(configuration.CipherSuiteConfig);
            if (!TlsStreamHelper.ValidateCipherSuite(sslStream, cipherOptions))
            {
                var cipherSuite = sslStream.NegotiatedCipherSuite;
                var reason = CipherSuiteValidator.GetCipherWeaknessReason(cipherSuite);
                var cipherName = CipherSuiteValidator.GetTlsCipherSuiteName(cipherSuite);
                
                sslStream.Dispose();
                throw new InvalidOperationException(
                    $"Cipher suite '{cipherName}' is not allowed. {reason}. " +
                    "Please configure your client to use a stronger cipher suite.");
            }

            // Handle ALPN result
            var negotiatedProtocol = sslStream.NegotiatedApplicationProtocol;
            AlpnNegotiationResult alpnResult;

            if (negotiatedProtocol.Protocol.IsEmpty)
            {
                // No ALPN protocol was negotiated
                if (alpnConfig.RequireAlpn)
                {
                    sslStream.Dispose();
                    throw AlpnException.AlpnRequired(alpnConfig.Protocols);
                }

                alpnResult = AlpnNegotiationResult.NoAlpnOffered(alpnConfig.GetDefaultProtocol());
            }
            else
            {
                var protocolString = negotiatedProtocol.ToString();
                alpnResult = AlpnNegotiationResult.CreateSuccess(protocolString);
            }

            // Raise ALPN negotiation event
            AlpnNegotiated?.Invoke(null, new AlpnNegotiationEventArgs(alpnResult, remoteEndpoint));

            return sslStream;
        }
        catch (AlpnException)
        {
            sslStream.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not TlsVersionException)
        {
            sslStream.Dispose();
            throw new InvalidOperationException($"ALPN TLS handshake failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates an SSL client stream with ALPN support and performs the TLS handshake
    /// </summary>
    /// <param name="client">The TCP client</param>
    /// <param name="targetHost">The target host name for certificate validation</param>
    /// <param name="clientProtocols">The ALPN protocols to offer (in order of preference)</param>
    /// <param name="clientCertificate">Optional client certificate for mutual TLS</param>
    /// <param name="checkCertificateRevocation">Whether to check certificate revocation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An authenticated SSL stream with negotiated ALPN protocol</returns>
    public static async Task<SslStream> CreateClientSslStreamWithAlpnAsync(
        TcpClient client,
        string targetHost,
        IEnumerable<string> clientProtocols,
        X509Certificate? clientCertificate = null,
        bool checkCertificateRevocation = true,
        CancellationToken cancellationToken = default)
    {
        return await CreateClientSslStreamWithAlpnAsync(
            client,
            targetHost,
            clientProtocols,
            clientCertificate,
            SslProtocols.Tls12 | SslProtocols.Tls13,
            checkCertificateRevocation,
            cancellationToken);
    }

    /// <summary>
    /// Creates an SSL client stream with ALPN support and specific TLS versions
    /// </summary>
    /// <param name="client">The TCP client</param>
    /// <param name="targetHost">The target host name for certificate validation</param>
    /// <param name="clientProtocols">The ALPN protocols to offer (in order of preference)</param>
    /// <param name="clientCertificate">Optional client certificate for mutual TLS</param>
    /// <param name="enabledProtocols">The TLS protocol versions to enable</param>
    /// <param name="checkCertificateRevocation">Whether to check certificate revocation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An authenticated SSL stream with negotiated ALPN protocol</returns>
    public static async Task<SslStream> CreateClientSslStreamWithAlpnAsync(
        TcpClient client,
        string targetHost,
        IEnumerable<string> clientProtocols,
        X509Certificate? clientCertificate,
        SslProtocols enabledProtocols,
        bool checkCertificateRevocation,
        CancellationToken cancellationToken = default)
    {
        var protocols = clientProtocols?.ToList() ?? new List<string>();
        var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

        var sslStream = new SslStream(
            client.GetStream(),
            false,
            ServerCertificateValidationCallback,
            null,
            EncryptionPolicy.RequireEncryption);

        try
        {
            // Convert protocol strings to SslApplicationProtocol objects
            var applicationProtocols = protocols
                .Select(p => new SslApplicationProtocol(p))
                .ToList();

            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                ClientCertificates = clientCertificate != null 
                    ? new X509CertificateCollection { clientCertificate } 
                    : null,
                EnabledSslProtocols = enabledProtocols,
                CertificateRevocationCheckMode = checkCertificateRevocation 
                    ? X509RevocationMode.Online 
                    : X509RevocationMode.NoCheck,
                ApplicationProtocols = applicationProtocols
            };

            await sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken);

            // Handle ALPN result
            var negotiatedProtocol = sslStream.NegotiatedApplicationProtocol;
            AlpnNegotiationResult alpnResult;

            if (negotiatedProtocol.Protocol.IsEmpty)
            {
                alpnResult = AlpnNegotiationResult.NoAlpnOffered();
            }
            else
            {
                var protocolString = negotiatedProtocol.ToString();
                alpnResult = AlpnNegotiationResult.CreateSuccess(protocolString);
            }

            // Raise ALPN negotiation event
            AlpnNegotiated?.Invoke(null, new AlpnNegotiationEventArgs(alpnResult, remoteEndpoint));

            return sslStream;
        }
        catch
        {
            sslStream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the negotiated ALPN protocol from an SSL stream
    /// </summary>
    /// <param name="sslStream">The SSL stream</param>
    /// <returns>The negotiated protocol, or null if no protocol was negotiated</returns>
    public static string? GetNegotiatedProtocol(SslStream sslStream)
    {
        if (sslStream?.NegotiatedApplicationProtocol.Protocol == null)
            return null;

        return sslStream.NegotiatedApplicationProtocol.ToString();
    }

    /// <summary>
    /// Checks if ALPN was successfully negotiated on the given SSL stream
    /// </summary>
    /// <param name="sslStream">The SSL stream</param>
    /// <returns>True if ALPN protocol was negotiated</returns>
    public static bool IsAlpnNegotiated(SslStream sslStream)
    {
        return sslStream?.NegotiatedApplicationProtocol.Protocol != null;
    }

    /// <summary>
    /// Validates that the negotiated protocol matches an expected protocol
    /// </summary>
    /// <param name="sslStream">The SSL stream</param>
    /// <param name="expectedProtocol">The expected protocol</param>
    /// <returns>True if the negotiated protocol matches the expected protocol</returns>
    public static bool ValidateNegotiatedProtocol(SslStream sslStream, string expectedProtocol)
    {
        var negotiated = GetNegotiatedProtocol(sslStream);
        return negotiated != null && 
               negotiated.Equals(expectedProtocol, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Callback for validating server certificates on the client side
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
    /// Callback for validating client certificates on the server side
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
    /// Creates an SslServerAuthenticationOptions with ALPN configuration
    /// </summary>
    /// <param name="certificate">The server certificate</param>
    /// <param name="alpnConfig">ALPN configuration</param>
    /// <param name="configuration">Server configuration</param>
    /// <returns>Configured SslServerAuthenticationOptions</returns>
    public static SslServerAuthenticationOptions CreateServerOptions(
        X509Certificate certificate,
        AlpnConfiguration alpnConfig,
        ServerConfiguration configuration)
    {
        var options = new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            ClientCertificateRequired = configuration.RequireClientCertificate,
            EnabledSslProtocols = configuration.SslProtocols,
            CertificateRevocationCheckMode = configuration.CheckCertificateRevocation 
                ? X509RevocationMode.Online 
                : X509RevocationMode.NoCheck
        };

        if (alpnConfig.Enabled && alpnConfig.Protocols.Count > 0)
        {
            options.ApplicationProtocols = alpnConfig.Protocols
                .Select(p => new SslApplicationProtocol(p))
                .ToList();
        }

        return options;
    }

    /// <summary>
    /// Creates an SslClientAuthenticationOptions with ALPN configuration
    /// </summary>
    /// <param name="targetHost">The target host name</param>
    /// <param name="clientProtocols">The ALPN protocols to offer</param>
    /// <param name="clientCertificate">Optional client certificate</param>
    /// <param name="checkCertificateRevocation">Whether to check certificate revocation</param>
    /// <returns>Configured SslClientAuthenticationOptions</returns>
    public static SslClientAuthenticationOptions CreateClientOptions(
        string targetHost,
        IEnumerable<string> clientProtocols,
        X509Certificate? clientCertificate = null,
        bool checkCertificateRevocation = true)
    {
        var protocols = clientProtocols?.ToList() ?? new List<string>();

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            ClientCertificates = clientCertificate != null 
                ? new X509CertificateCollection { clientCertificate } 
                : null,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = checkCertificateRevocation 
                ? X509RevocationMode.Online 
                : X509RevocationMode.NoCheck
        };

        if (protocols.Count > 0)
        {
            options.ApplicationProtocols = protocols
                .Select(p => new SslApplicationProtocol(p))
                .ToList();
        }

        return options;
    }
}
