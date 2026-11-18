# AdvGenNoSQL Secured P2P Example

A runnable demonstration of the **secured peer-to-peer transport**: mutual TLS, HMAC-signed
messages with anti-replay, NodeId↔certificate identity binding, and secure-by-default
configuration validation.

> Companion to [`docs/P2P-Security-BestPractices.md`](../docs/P2P-Security-BestPractices.md),
> which describes the production trust model. Clustering is **not yet production-validated** —
> this example shows the secure transport, not a production cluster.

## Run

```bash
dotnet run --project AdvGenNoSqlServer.Examples.SecureP2P
```

## What it demonstrates

1. **Secure-by-default validation** — `P2PConfiguration.Validate` rejects an enabled cluster
   with no security (no certificate and no cluster secret), or `RequireMutualTls` without a cert.
2. **Node certificates** — provisions two node certs. *Demo uses self-signed certs + thumbprint
   pinning; in production issue certs from a private cluster CA and set `CaCertificatePath`.*
3. **Node A (server)** — a `P2PServer` configured with `RequireMutualTls`, a node certificate,
   a thumbprint allow-list, a `ClusterSecret`, and `SignMessages`.
4. **Node B joins** — a `P2PClient` performs the mTLS handshake and a signed, identity-bound join.
5. **Signed message exchange** — node B broadcasts a heartbeat; node A verifies the HMAC
   signature + freshness + nonce and receives it.
6. **Impersonation rejected** — a peer presenting a valid cert but claiming a public key that
   does not match it is rejected (`Node identity does not match the peer certificate`).

## Key configuration (`P2PSecurityConfiguration`)

```csharp
Security = new P2PSecurityConfiguration
{
    RequireMutualTls = true,          // both peers present + validate certificates
    CertificatePath = "node.pfx",     // node certificate (PEM or PFX)
    CertificatePassword = "...",      // for PFX
    // CaCertificatePath = "cluster-ca.pem",  // production: validate against a cluster CA
    AllowedThumbprints = new[] { /* peer thumbprints */ },  // or pin specific peers
    ClusterSecret = "...",            // pre-shared key: handshake + message HMAC
    SignMessages = true,              // HMAC-SHA256 every message
    MessageMaxAge = TimeSpan.FromMinutes(5)  // freshness / anti-replay window
}
```

Bind each node's identity to its certificate so it cannot be spoofed:

```csharp
var cluster = new ClusterManager(config);
cluster.InitializeLocalNode(host, clientPort);
cluster.LocalNode.PublicKey = nodeCertificate.GetPublicKey();
```

## Production notes

- Use a **private cluster CA** (not self-signed) and rotate certs/secrets.
- Keep `ClusterSecret` out of source; provision via environment/secure storage.
- Firewall the P2P port to cluster members; bind to an internal address.
- Synchronize node clocks (NTP) so the message freshness window works.
- See the best-practices doc for the full hardening checklist and the current production posture.