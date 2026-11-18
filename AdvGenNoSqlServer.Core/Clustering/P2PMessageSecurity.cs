// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Message-level security for P2P traffic: HMAC-SHA256 authentication, freshness
    /// (timestamp window) and anti-replay (nonce) checks. Signing covers the whole
    /// message: the signature is computed over the canonical JSON with the Signature
    /// field cleared, so any tampering with any field invalidates it.
    /// </summary>
    public static class P2PMessageSecurity
    {
        /// <summary>Derives a fixed-length HMAC key from the pre-shared cluster secret.</summary>
        public static byte[] DeriveKey(string clusterSecret)
            => SHA256.HashData(Encoding.UTF8.GetBytes(clusterSecret ?? throw new ArgumentNullException(nameof(clusterSecret))));

        /// <summary>Stamps a fresh timestamp + nonce and sets the HMAC signature.</summary>
        public static void Sign(P2PMessage message, byte[] key, JsonSerializerOptions options)
        {
            message.Timestamp = DateTime.UtcNow;
            message.Nonce = Guid.NewGuid().ToString("N");
            message.Signature = Array.Empty<byte>();
            var payload = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), options);
            message.Signature = ComputeHmac(key, payload);
        }

        /// <summary>Constant-time verification of the HMAC signature over the canonical payload.</summary>
        public static bool VerifySignature(P2PMessage message, byte[] key, JsonSerializerOptions options)
        {
            if (message.Signature == null || message.Signature.Length == 0)
                return false;

            var received = message.Signature;
            message.Signature = Array.Empty<byte>();
            var payload = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), options);
            message.Signature = received;

            var expected = ComputeHmac(key, payload);
            return CryptographicOperations.FixedTimeEquals(expected, received);
        }

        /// <summary>Whether the message timestamp is within the allowed window (tolerates clock skew).</summary>
        public static bool IsFresh(P2PMessage message, TimeSpan maxAge)
        {
            var age = DateTime.UtcNow - message.Timestamp;
            return age >= -maxAge && age <= maxAge;
        }

        private static byte[] ComputeHmac(byte[] key, byte[] payload)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(payload);
        }
    }

    /// <summary>
    /// Tracks recently seen message nonces so a captured message cannot be replayed
    /// within the freshness window. Entries older than the window are pruned.
    /// </summary>
    public sealed class P2PReplayGuard
    {
        private readonly ConcurrentDictionary<string, DateTime> _seen = new();
        private readonly TimeSpan _window;

        /// <summary>
        /// Initializes a new instance of the <see cref="P2PReplayGuard"/> class
        /// </summary>
        /// <param name="window">Freshness window for seen nonces; non-positive values fall back to five minutes</param>
        public P2PReplayGuard(TimeSpan window)
        {
            _window = window > TimeSpan.Zero ? window : TimeSpan.FromMinutes(5);
        }

        /// <summary>Returns false if the nonce was already seen (a replay).</summary>
        public bool TryRegister(string nonce)
        {
            if (string.IsNullOrEmpty(nonce))
                return false;
            Prune();
            return _seen.TryAdd(nonce, DateTime.UtcNow);
        }

        private void Prune()
        {
            var cutoff = DateTime.UtcNow - _window;
            foreach (var entry in _seen)
            {
                if (entry.Value < cutoff)
                    _seen.TryRemove(entry.Key, out _);
            }
        }
    }
}