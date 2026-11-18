// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Clustering;
using AdvGenNoSqlServer.Network.Clustering;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    public class P2PSecurityTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // ---------- Message signing / anti-replay (unit) ----------

        [Fact]
        public void Sign_ThenVerify_Succeeds()
        {
            var key = P2PMessageSecurity.DeriveKey("cluster-secret");
            var message = new HeartbeatMessage { SenderId = "node-a", NodeState = NodeState.Active };
            P2PMessageSecurity.Sign(message, key, JsonOptions);
            Assert.True(P2PMessageSecurity.VerifySignature(message, key, JsonOptions));
        }

        [Fact]
        public void TamperedMessage_FailsVerification()
        {
            var key = P2PMessageSecurity.DeriveKey("cluster-secret");
            var message = new GossipMessage { SenderId = "node-a", Generation = 1 };
            P2PMessageSecurity.Sign(message, key, JsonOptions);

            message.Generation = 999; // tamper after signing
            Assert.False(P2PMessageSecurity.VerifySignature(message, key, JsonOptions));
        }

        [Fact]
        public void WrongKey_FailsVerification()
        {
            var message = new HeartbeatMessage { SenderId = "node-a" };
            P2PMessageSecurity.Sign(message, P2PMessageSecurity.DeriveKey("secret-a"), JsonOptions);
            Assert.False(P2PMessageSecurity.VerifySignature(message, P2PMessageSecurity.DeriveKey("secret-b"), JsonOptions));
        }

        [Fact]
        public void UnsignedMessage_FailsVerification()
        {
            var key = P2PMessageSecurity.DeriveKey("cluster-secret");
            var message = new HeartbeatMessage { SenderId = "node-a" };
            Assert.False(P2PMessageSecurity.VerifySignature(message, key, JsonOptions));
        }

        [Fact]
        public void Freshness_RejectsStaleAndFutureMessages()
        {
            var maxAge = TimeSpan.FromMinutes(5);
            var fresh = new HeartbeatMessage { SenderId = "n", Timestamp = DateTime.UtcNow };
            var stale = new HeartbeatMessage { SenderId = "n", Timestamp = DateTime.UtcNow - TimeSpan.FromMinutes(10) };
            var future = new HeartbeatMessage { SenderId = "n", Timestamp = DateTime.UtcNow + TimeSpan.FromMinutes(10) };

            Assert.True(P2PMessageSecurity.IsFresh(fresh, maxAge));
            Assert.False(P2PMessageSecurity.IsFresh(stale, maxAge));
            Assert.False(P2PMessageSecurity.IsFresh(future, maxAge));
        }

        [Fact]
        public void ReplayGuard_RejectsSecondUseOfNonce()
        {
            var guard = new P2PReplayGuard(TimeSpan.FromMinutes(5));
            Assert.True(guard.TryRegister("nonce-1"));
            Assert.False(guard.TryRegister("nonce-1"));
            Assert.True(guard.TryRegister("nonce-2"));
            Assert.False(guard.TryRegister(""));
        }

        // ---------- Signed peer connection (integration over real TCP) ----------

        [Fact]
        public async Task SignedPeerConnection_TransfersAuthenticatedMessage()
        {
            var security = new P2PSecurityConfiguration
            {
                ClusterSecret = "integration-secret",
                SignMessages = true,
                RequireMutualTls = false
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var (serverConn, clientConn) = await CreateConnectedPairAsync(security, performTls: false, cts.Token);

            var received = new TaskCompletionSource<P2PMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var processTask = serverConn.ProcessMessagesAsync(m => received.TrySetResult(m), cts.Token);

            await clientConn.SendAsync(new HeartbeatMessage { SenderId = "client", NodeState = NodeState.Active }, cts.Token);
            var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.IsType<HeartbeatMessage>(message);
            Assert.Equal("client", message.SenderId);
            Assert.NotEmpty(message.Signature);

            cts.Cancel();
            await Task.WhenAny(processTask, Task.Delay(1000));
            await serverConn.DisposeAsync();
            await clientConn.DisposeAsync();
        }

        [Fact]
        public async Task WrongClusterSecret_HandshakeRejected()
        {
            var serverSecurity = new P2PSecurityConfiguration { ClusterSecret = "server-secret", SignMessages = false, RequireMutualTls = false };
            var clientSecurity = new P2PSecurityConfiguration { ClusterSecret = "wrong-secret", SignMessages = false, RequireMutualTls = false };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var acceptTask = listener.AcceptTcpClientAsync(cts.Token);
            var clientTcp = new TcpClient();
            await clientTcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            var serverTcp = await acceptTask;

            var serverConn = new PeerConnection("s", serverTcp, serverSecurity);
            var clientConn = new PeerConnection("c", clientTcp, clientSecurity);

            var serverHandshake = serverConn.PerformHandshakeAsync(cts.Token);
            var clientResult = await clientConn.InitiateHandshakeAsync(NodeIdentity.Create("cluster", "localhost", port, port), "wrong-secret", cts.Token);
            await serverHandshake;

            Assert.False(clientResult.Success);

            await serverConn.DisposeAsync();
            await clientConn.DisposeAsync();
            listener.Stop();
        }

        // ---------- Mutual TLS (integration with a pinned self-signed cert) ----------

        [Fact]
        public async Task MutualTls_PinnedCert_TransfersMessage()
        {
            var certPath = Path.Combine(Path.GetTempPath(), $"p2p-tls-{Guid.NewGuid():N}.pfx");
            const string password = "tls-test-password";
            string thumbprint;
            using (var cert = CreateTlsCertificate())
            {
                thumbprint = cert.Thumbprint;
                File.WriteAllBytes(certPath, cert.Export(X509ContentType.Pfx, password));
            }

            try
            {
                var security = new P2PSecurityConfiguration
                {
                    RequireMutualTls = true,
                    CertificatePath = certPath,
                    CertificatePassword = password,
                    AllowedThumbprints = new[] { thumbprint },
                    ClusterSecret = "tls-secret",
                    SignMessages = true
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var (serverConn, clientConn) = await CreateConnectedPairAsync(security, performTls: true, cts.Token);

                var received = new TaskCompletionSource<P2PMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                var processTask = serverConn.ProcessMessagesAsync(m => received.TrySetResult(m), cts.Token);

                await clientConn.SendAsync(new HeartbeatMessage { SenderId = "tls-client", NodeState = NodeState.Active }, cts.Token);
                var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

                Assert.IsType<HeartbeatMessage>(message);
                Assert.Equal("tls-client", message.SenderId);

                cts.Cancel();
                await Task.WhenAny(processTask, Task.Delay(1000));
                await serverConn.DisposeAsync();
                await clientConn.DisposeAsync();
            }
            finally
            {
                File.Delete(certPath);
            }
        }

        // ---------- Secure-by-default configuration validation ----------

        [Fact]
        public void Validate_EnabledWithoutSecurity_Fails()
        {
            var config = new P2PConfiguration { Enabled = true };
            Assert.False(config.Validate(out var errors));
            Assert.Contains(errors, e => e.Contains("no security is configured"));
        }

        [Fact]
        public void Validate_EnabledWithClusterSecret_Succeeds()
        {
            var config = new P2PConfiguration
            {
                Enabled = true,
                Security = new P2PSecurityConfiguration { ClusterSecret = "a-strong-secret", SignMessages = true, RequireMutualTls = false }
            };
            Assert.True(config.Validate(out var errors), string.Join("; ", errors));
        }

        [Fact]
        public void Validate_RequireMutualTlsWithoutCertificate_Fails()
        {
            var config = new P2PConfiguration
            {
                Enabled = true,
                Security = new P2PSecurityConfiguration { ClusterSecret = "s", RequireMutualTls = true }
            };
            Assert.False(config.Validate(out var errors));
            Assert.Contains(errors, e => e.Contains("RequireMutualTls"));
        }

        [Fact]
        public void Validate_DisabledByDefault_DoesNotRequireSecurity()
        {
            var config = new P2PConfiguration(); // Enabled = false
            Assert.True(config.Validate(out _));
        }

        // ---------- Identity binding ----------

        [Fact]
        public async Task MutualTls_IdentityMismatch_Rejected()
        {
            var certPath = Path.Combine(Path.GetTempPath(), $"p2p-tls-{Guid.NewGuid():N}.pfx");
            const string password = "tls-test-password";
            string thumbprint;
            using (var cert = CreateTlsCertificate())
            {
                thumbprint = cert.Thumbprint;
                File.WriteAllBytes(certPath, cert.Export(X509ContentType.Pfx, password));
            }

            try
            {
                var security = new P2PSecurityConfiguration
                {
                    RequireMutualTls = true,
                    CertificatePath = certPath,
                    CertificatePassword = password,
                    AllowedThumbprints = new[] { thumbprint },
                    ClusterSecret = "tls-secret",
                    SignMessages = true
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;

                var acceptTask = listener.AcceptTcpClientAsync(cts.Token);
                var clientTcp = new TcpClient();
                await clientTcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
                var serverTcp = await acceptTask;
                listener.Stop();

                var serverConn = new PeerConnection("server", serverTcp, security);
                var clientConn = new PeerConnection("client", clientTcp, security);

                var serverTls = serverConn.AuthenticateAsServerAsync(cts.Token);
                await clientConn.AuthenticateAsClientAsync("localhost", cts.Token);
                await serverTls;

                // Claim a public key that does NOT match the presented certificate.
                var impostorIdentity = NodeIdentity.Create("cluster", "localhost", port, port);
                impostorIdentity.PublicKey = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("not-the-cert-key"));

                var serverHandshake = serverConn.PerformHandshakeAsync(cts.Token);
                var clientResult = await clientConn.InitiateHandshakeAsync(impostorIdentity, security.ClusterSecret, cts.Token);
                var serverResult = await serverHandshake;

                Assert.False(clientResult.Success);
                Assert.False(serverResult.Success);
                Assert.Contains("certificate", serverResult.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);

                await serverConn.DisposeAsync();
                await clientConn.DisposeAsync();
            }
            finally
            {
                File.Delete(certPath);
            }
        }

        // ---------- helpers ----------

        private static async Task<(PeerConnection server, PeerConnection client)> CreateConnectedPairAsync(
            P2PSecurityConfiguration security, bool performTls, CancellationToken ct)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var acceptTask = listener.AcceptTcpClientAsync(ct);
            var clientTcp = new TcpClient();
            await clientTcp.ConnectAsync(IPAddress.Loopback, port, ct);
            var serverTcp = await acceptTask;
            listener.Stop();

            var serverConn = new PeerConnection("server", serverTcp, security);
            var clientConn = new PeerConnection("client", clientTcp, security);

            if (performTls)
            {
                var serverTls = serverConn.AuthenticateAsServerAsync(ct);
                await clientConn.AuthenticateAsClientAsync("localhost", ct);
                await serverTls;
            }

            var serverHandshake = serverConn.PerformHandshakeAsync(ct);
            var clientResult = await clientConn.InitiateHandshakeAsync(NodeIdentity.Create("cluster", "localhost", port, port), security.ClusterSecret, ct);
            var serverResult = await serverHandshake;

            Assert.True(serverResult.Success, $"server handshake: {serverResult.ErrorMessage}");
            Assert.True(clientResult.Success, $"client handshake: {clientResult.ErrorMessage}");

            return (serverConn, clientConn);
        }

        private static X509Certificate2 CreateTlsCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                new X500DistinguishedName("CN=localhost"),
                rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new Oid("1.3.6.1.5.5.7.3.1"), // server auth
                    new Oid("1.3.6.1.5.5.7.3.2")  // client auth
                }, false));
            var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return new X509Certificate2(cert.Export(X509ContentType.Pfx, "tls-test-password"), "tls-test-password",
                X509KeyStorageFlags.Exportable);
        }
    }
}