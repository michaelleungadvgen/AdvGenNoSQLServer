# P2P / Clustering Security — Best Practices

This document describes the trust model and security controls for AdvGenNoSQL's
peer-to-peer (clustering) subsystem, and how to operate it securely. It reflects the
controls implemented in `Core/Clustering` and `Network/Clustering`.

> **Status:** clustering is **dormant by default** (`P2PConfiguration.Enabled = false`)
> and is **not yet production-validated**. Run single-node until you have reviewed this
> document, provisioned the credentials below, and tested your cluster. The controls here
> are enforced by the transport when configured; they are only as strong as their configuration.

---

## 1. Trust model: permissioned, zero-trust cluster

A database cluster is **not** an open P2P network. The correct model is a **closed,
mutually-authenticated cluster with no implicit trust between nodes**:

- **Permissioned membership.** Only explicitly provisioned nodes may join. Membership is
  controlled by credential issuance (certificates / cluster secret), not by network reachability.
- **Zero trust between peers.** Every node authenticates every peer on every connection and
  verifies every message. Being "inside the cluster" grants no implicit privilege.
- **Least privilege.** An authenticated node is not automatically allowed to perform every
  operation; separate replication traffic from membership/admin operations.
- **Confidentiality + integrity + authenticity** for all inter-node traffic (documents,
  gossip, Raft votes, replication).

Open/permissionless joining is **inappropriate** for a database: it would let any peer read
or corrupt data. Do not expose the P2P port to untrusted networks.

---

## 2. Defense in depth (the layers)

| Layer | Control | Where |
|-------|---------|-------|
| Transport | Mutual TLS (node certs, cluster CA / pinning) | `P2PTlsHelper` |
| Membership | Cluster secret handshake (constant-time) + cert allow-list | `PeerConnection`, `P2PSecurityConfiguration` |
| Message | HMAC-SHA256 signature over the whole message | `P2PMessageSecurity.Sign/VerifySignature` |
| Anti-replay | Timestamp window + per-message nonce | `P2PMessageSecurity.IsFresh`, `P2PReplayGuard` |
| Identity | Per-node `NodeIdentity` (bind `NodeId` to a key) | `NodeIdentity` |
| Discovery | Static trusted seeds; verify peers before connecting | `DiscoveryConfiguration` |
| Consensus | Quorum to prevent split-brain | `ReplicationConfiguration` |

---

## 3. Node identity

- Give every node a **stable cryptographic identity** (a key pair / certificate). Set
  `P2PConfiguration.NodeId` explicitly and keep it stable across restarts.
- **Bind `NodeId` to the node's public key** so an identity cannot be spoofed by simply
  claiming another node's `NodeId`. `NodeIdentity.PublicKey` carries the key; treat a
  `NodeId` as authentic only when it matches the presented certificate/key.
- Never rely on `NodeId` alone (it is a string) for authorization — always pair it with the
  authenticated transport identity (mTLS peer cert).

## 4. Mutual TLS with a cluster CA

Inter-node links must use **mutual TLS** (`Security.RequireMutualTls = true`, the default):

- Issue each node a certificate from a **private cluster CA** (`Security.CaCertificatePath`).
  Only CA-signed nodes can join; revoke by CRL/short-lived certs or by removing a thumbprint.
- Configure `Security.CertificatePath` (+ `PrivateKeyPath` for PEM, or a PFX with
  `Security.CertificatePassword`). Both peers present and validate certificates.
- For small/fixed clusters, **certificate pinning** via `Security.AllowedThumbprints` is a
  simple alternative to running a CA.
- Validation is **fail-closed**: an untrusted peer certificate aborts the connection.
- TLS 1.2/1.3 only; revocation checking is disabled by default for self-managed clusters —
  enable it if you operate a real CA with CRL/OCSP.

## 5. Message authentication + anti-replay

Even with TLS, sign every message so cluster state cannot be poisoned by a compromised or
rogue peer, and so captured messages cannot be replayed:

- Set `Security.SignMessages = true` and a strong `Security.ClusterSecret`. Each message is
  stamped with a fresh `Timestamp` + `Nonce` and an **HMAC-SHA256** signature computed over
  the entire canonical message (any field tampering invalidates it). Verification is
  constant-time; invalid messages are dropped (fail closed).
- **Freshness:** messages older/newer than `Security.MessageMaxAge` (default 5 min) are
  rejected. Keep node clocks synchronized (NTP) so legitimate messages are not dropped.
- **Anti-replay:** each nonce is accepted once within the window (`P2PReplayGuard`).
- The cluster secret is a **pre-shared key** — distribute it securely, rotate it periodically,
  and never hard-code it. Prefer per-node certificates (mTLS) as the primary identity and use
  the secret as an additional layer.

## 6. Trusted discovery

- Prefer **static seeds** (`Discovery.Method = "StaticSeeds"`, `Discovery.Seeds`). Treat seed
  addresses as trusted configuration.
- **Verify any discovered peer against the cluster CA / allow-list before connecting.** Do not
  trust peers learned from open multicast/DNS discovery without authentication.
- Do not use open multicast discovery on untrusted networks.

## 7. Quorum & split-brain

- Configure a replication factor and write quorum that tolerate failures without allowing two
  authoritative partitions: e.g. `ReplicationFactor = 3`, `WriteQuorum = 2` (majority).
- A minority partition must **not** accept writes. Raft consensus (`RaftConsensus`) provides
  leader election; ensure terms/votes are only accepted from authenticated members.

## 8. Network isolation

- Run P2P on a **dedicated port** (`P2PConfiguration.P2PPort`, default 9092), separate from the
  client TCP/REST ports.
- **Firewall the P2P port to cluster members only.** Never expose it to clients or the internet.
- Bind to a specific internal address (`BindAddress`) rather than `0.0.0.0` where possible.

## 9. Secrets & certificate lifecycle

- Provision certs/keys and the cluster secret via **environment/secure files**, never in source
  or tracked config. Production config validation already rejects default/weak secrets.
- Rotate node certificates and the cluster secret on a schedule; support overlap so rotation
  does not partition the cluster.
- Protect private keys at rest (file permissions / key store); do not log secrets or key material.

---

## 10. Configuration reference (`P2PSecurityConfiguration`)

| Setting | Default | Purpose |
|---------|---------|---------|
| `RequireMutualTls` | `true` | Require mutual TLS on peer links |
| `CertificatePath` | – | Node certificate (PEM or PFX) |
| `PrivateKeyPath` | – | Node private key (PEM) |
| `CertificatePassword` | – | Password for PFX certificates |
| `CaCertificatePath` | – | Cluster CA for peer validation |
| `AllowedThumbprints` | `[]` | Certificate pinning allow-list |
| `ClusterSecret` | – | Pre-shared key for handshake + message HMAC |
| `SignMessages` | `true` | HMAC-sign every message |
| `SignatureAlgorithm` | `HMAC-SHA256` | Message signature algorithm |
| `MessageMaxAge` | 5 min | Freshness / anti-replay window |

## 11. Hardening checklist

- [ ] `Enabled` only when you intend to run a cluster; otherwise keep single-node.
- [ ] Per-node certificate from a private CA (or pinned thumbprints); `RequireMutualTls = true`.
- [ ] Strong `ClusterSecret` provisioned securely; `SignMessages = true`.
- [ ] Stable `NodeId` bound to the node key; clocks synchronized (NTP).
- [ ] Static trusted seeds; peers verified before connecting.
- [ ] Majority `WriteQuorum`; minority partitions reject writes.
- [ ] P2P port firewalled to cluster members; bound to an internal address.
- [ ] Cert/secret rotation plan; private keys protected; no secrets in logs.
- [ ] Audit peer connections, membership changes, and rejected handshakes/messages.

## 12. Implemented vs. recommended

**Implemented & tested** (`P2PSecurityTests`, 14 tests): mutual TLS with CA/pinning validation,
constant-time cluster-secret handshake, HMAC message signing + tamper detection, timestamp
freshness, nonce anti-replay, **NodeId↔certificate identity binding** (a peer claiming a public
key that does not match its mTLS certificate is rejected), **security audit** (`PeerRejected`
event for failed handshakes / identity mismatch), **secure-by-default configuration validation**
(`P2PConfiguration.Validate` rejects an enabled cluster with no security, `RequireMutualTls`
without a certificate, or a secret without message signing), and signed/mTLS peer transport
end-to-end.

**Recommended / not yet enforced:** per-node least-privilege roles, certificate revocation
(CRL/OCSP), and richer audit (invalid/replayed-message counters, membership-change log).

**Production posture:** the P2P *transport/security* is production-grade and secure-by-default,
but clustering as a whole is **not yet production-validated** — consensus/replication
(`RaftConsensus`, `ReplicationManager`) still has stubbed paths (e.g. replication full-sync) and
P2P is not yet wired into the Host startup. Treat clustering as experimental until those are
implemented and the cluster is load/partition-tested. Run single-node in production today.