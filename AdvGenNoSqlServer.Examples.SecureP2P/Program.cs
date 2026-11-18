// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AdvGenNoSqlServer.Core.Clustering;
using AdvGenNoSqlServer.Network.Clustering;

namespace AdvGenNoSqlServer.Examples.SecureP2P;

/// <summary>
/// Runnable demonstration of the SECURED AdvGenNoSQL P2P transport:
///   1. Secure-by-default configuration validation (an insecure cluster is rejected).
///   2. Mutual TLS between two nodes (demo uses self-signed certs + pinning; use a CA in prod).
///   3. HMAC-signed messages with anti-replay.
///   4. NodeId-to-certificate identity binding (impersonation is rejected).
///
/// Run:  dotnet run --project AdvGenNoSqlServer.Examples.SecureP2P
/// See docs/P2P-Security-BestPractices.md for the production trust model.
/// </summary>
public static class Program
{
    public static async Task Main()
    {
        Console.WriteLine("=== AdvGenNoSQL secured P2P example ===\n");

        var tempDir = Path.Combine(Path.GetTempPath(), "secure-p2p-demo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        const string certPassword = "demo-password";
        const string clusterSecret = "demo-cluster-secret-change-me";

        try
        {
            // ---- 1. Secure-by-default validation -------------------------------------
            Console.WriteLine("[1] Secure-by-default configuration validation");
            var insecure = new P2PConfiguration { Enabled = true }; // no security configured
            insecure.Validate(out var insecureErrors);
            foreach (var e in insecureErrors) Console.WriteLine("    REJECTED: " + e);
            Console.WriteLine();

            // ---- 2. Provision node certificates --------------------------------------
            // DEMO ONLY: self-signed certs + thumbprint pinning. In production, issue node
            // certs from a private cluster CA and set Security.CaCertificatePath instead.
            Console.WriteLine("[2] Provisioning demo node certificates (self-signed; use a CA in production)");
            using var certA = CreateNodeCertificate("CN=node-a");
            using var certB = CreateNodeCertificate("CN=node-b");
            var certAPath = Path.Combine(tempDir, "node-a.pfx");
            var certBPath = Path.Combine(tempDir, "node-b.pfx");
            File.WriteAllBytes(certAPath, certA.Export(X509ContentType.Pfx, certPassword));
            File.WriteAllBytes(certBPath, certB.Export(X509ContentType.Pfx, certPassword));
            var pinned = new[] { certA.Thumbprint, certB.Thumbprint };
            Console.WriteLine($"    node-a thumbprint {certA.Thumbprint[..12]}...  node-b {certB.Thumbprint[..12]}...\n");

            // ---- 3. Node A: a secured P2P server -------------------------------------
            Console.WriteLine("[3] Starting node-a (secured P2P server: mTLS + signing)");
            var configA = new P2PConfiguration
            {
                Enabled = true,
                ClusterId = "demo-cluster",
                NodeId = "node-a",
                BindAddress = "127.0.0.1",
                P2PPort = 29092,
                Security = new P2PSecurityConfiguration
                {
                    RequireMutualTls = true,
                    CertificatePath = certAPath,
                    CertificatePassword = certPassword,
                    AllowedThumbprints = pinned,
                    ClusterSecret = clusterSecret,
                    SignMessages = true
                }
            };
            if (!configA.Validate(out var configAErrors))
            {
                Console.WriteLine("    config invalid: " + string.Join("; ", configAErrors));
                return;
            }

            var clusterA = new ClusterManager(configA);
            clusterA.InitializeLocalNode("127.0.0.1", 9191);
            clusterA.LocalNode.PublicKey = certA.GetPublicKey(); // bind identity to the cert

            var receivedByA = new TaskCompletionSource<P2PMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var server = new P2PServer(configA, clusterA);
            server.MessageReceived += (_, e) => receivedByA.TrySetResult(e.Message);
            server.PeerRejected += (_, e) => Console.WriteLine($"    AUDIT peer rejected: {e.Endpoint} ({e.Reason})");
            await server.StartAsync();
            Console.WriteLine("    node-a listening on 127.0.0.1:29092\n");

            // ---- 4. Node B joins node A securely -------------------------------------
            Console.WriteLine("[4] node-b joining node-a (mTLS handshake + signed, identity-bound join)");
            var configB = new P2PConfiguration
            {
                Enabled = true,
                ClusterId = "demo-cluster",
                NodeId = "node-b",
                BindAddress = "127.0.0.1",
                P2PPort = 29093,
                Security = new P2PSecurityConfiguration
                {
                    RequireMutualTls = true,
                    CertificatePath = certBPath,
                    CertificatePassword = certPassword,
                    AllowedThumbprints = pinned,
                    ClusterSecret = clusterSecret,
                    SignMessages = true
                }
            };
            var clusterB = new ClusterManager(configB);
            clusterB.InitializeLocalNode("127.0.0.1", 9192);
            clusterB.LocalNode.PublicKey = certB.GetPublicKey();

            await using var client = new P2PClient(configB, clusterB);
            var join = await client.ConnectToSeedAsync("127.0.0.1:29092");
            Console.WriteLine($"    join success: {join.Success}\n");
            if (!join.Success) return;

            // ---- 5. Exchange a signed message ----------------------------------------
            Console.WriteLine("[5] node-b broadcasts a signed heartbeat; node-a verifies + receives it");
            await client.BroadcastAsync(new HeartbeatMessage { SenderId = "node-b", NodeState = NodeState.Active });
            var message = await receivedByA.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Console.WriteLine($"    node-a received {message.MessageType} from {message.SenderId}");
            Console.WriteLine($"    message HMAC signature present: {message.Signature.Length > 0} ({message.Signature.Length} bytes)");
            Console.WriteLine($"    anti-replay nonce: {message.Nonce}\n");

            // ---- 6. Identity binding: impersonation is rejected -----------------------
            Console.WriteLine("[6] Impersonation attempt: a node claiming a key that doesn't match its cert");
            await DemonstrateImpersonationRejectedAsync(certBPath, certPassword, pinned, clusterSecret);

            Console.WriteLine("\n=== Done. See docs/P2P-Security-BestPractices.md for production guidance. ===");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }
    }

    private static async Task DemonstrateImpersonationRejectedAsync(
        string certPath, string certPassword, string[] pinned, string clusterSecret)
    {
        var security = new P2PSecurityConfiguration
        {
            RequireMutualTls = true,
            CertificatePath = certPath,
            CertificatePassword = certPassword,
            AllowedThumbprints = pinned,
            ClusterSecret = clusterSecret,
            SignMessages = true
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        var acceptTask = listener.AcceptTcpClientAsync(cts.Token);
        var clientTcp = new System.Net.Sockets.TcpClient();
        await clientTcp.ConnectAsync(System.Net.IPAddress.Loopback, port, cts.Token);
        var serverTcp = await acceptTask;
        listener.Stop();

        await using var serverConn = new PeerConnection("server", serverTcp, security);
        await using var clientConn = new PeerConnection("client", clientTcp, security);

        var serverTls = serverConn.AuthenticateAsServerAsync(cts.Token);
        await clientConn.AuthenticateAsClientAsync("localhost", cts.Token);
        await serverTls;

        // Present node-b's cert but claim a public key that does not match it.
        var impostor = NodeIdentity.Create("demo-cluster", "127.0.0.1", port, port);
        impostor.PublicKey = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("not-node-b's-key"));

        var serverHandshake = serverConn.PerformHandshakeAsync(cts.Token);
        var clientResult = await clientConn.InitiateHandshakeAsync(impostor, clusterSecret, cts.Token);
        var serverResult = await serverHandshake;

        Console.WriteLine($"    handshake accepted: {serverResult.Success} (expected False)");
        Console.WriteLine($"    reason: {serverResult.ErrorMessage}");
    }

    private static X509Certificate2 CreateNodeCertificate(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            new X500DistinguishedName(subject), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
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
        return new X509Certificate2(cert.Export(X509ContentType.Pfx, "demo-password"), "demo-password",
            X509KeyStorageFlags.Exportable);
    }
}