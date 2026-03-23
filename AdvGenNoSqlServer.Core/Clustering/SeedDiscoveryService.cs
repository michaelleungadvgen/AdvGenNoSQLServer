// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Interface for seed-based node discovery.
    /// </summary>
    public interface ISeedDiscoveryService : IDisposable
    {
        /// <summary>
        /// Discovers nodes from configured seeds.
        /// </summary>
        Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default);

        /// <summary>
        /// Attempts to connect to discovered seeds and join the cluster.
        /// </summary>
        Task<JoinResult> ConnectToClusterAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets the list of configured seeds.
        /// </summary>
        IReadOnlyList<SeedEndpoint> GetConfiguredSeeds();

        /// <summary>
        /// Event raised when a seed is successfully contacted.
        /// </summary>
        event EventHandler<SeedDiscoveredEventArgs>? SeedDiscovered;

        /// <summary>
        /// Event raised when a seed fails to respond.
        /// </summary>
        event EventHandler<SeedFailedEventArgs>? SeedFailed;
    }

    /// <summary>
    /// Represents a seed endpoint with parsed host and port.
    /// </summary>
    public class SeedEndpoint
    {
        /// <summary>
        /// The original endpoint string (host:port).
        /// </summary>
        public string RawEndpoint { get; set; } = "";

        /// <summary>
        /// The host or IP address.
        /// </summary>
        public string Host { get; set; } = "";

        /// <summary>
        /// The port number.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Whether this endpoint is the local node.
        /// </summary>
        public bool IsLocal { get; set; }

        /// <summary>
        /// Parses an endpoint string into a SeedEndpoint.
        /// </summary>
        public static bool TryParse(string endpoint, out SeedEndpoint? result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(endpoint))
                return false;

            // Handle IPv6 addresses in brackets [IPv6]:port
            string host;
            int port;

            if (endpoint.StartsWith("["))
            {
                var closingBracket = endpoint.IndexOf("]");
                if (closingBracket == -1)
                    return false;

                host = endpoint.Substring(1, closingBracket - 1);
                var portPart = endpoint.Substring(closingBracket + 1);

                if (!portPart.StartsWith(":") || !int.TryParse(portPart.Substring(1), out port))
                    return false;
            }
            else
            {
                var parts = endpoint.Split(':');
                if (parts.Length != 2)
                    return false;

                host = parts[0];
                if (!int.TryParse(parts[1], out port) || port < 1 || port > 65535)
                    return false;
            }

            if (string.IsNullOrWhiteSpace(host))
                return false;

            result = new SeedEndpoint
            {
                RawEndpoint = endpoint,
                Host = host,
                Port = port
            };
            return true;
        }

        /// <summary>
        /// Checks if this endpoint refers to the local machine.
        /// </summary>
        public bool CheckIsLocal(int localP2PPort)
        {
            // Check for localhost variants
            if (Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                Host.Equals("127.0.0.1") ||
                Host.Equals("::1"))
            {
                IsLocal = Port == localP2PPort;
                return IsLocal;
            }

            // Try to resolve the host and compare with local IPs
            try
            {
                var hostEntry = Dns.GetHostEntry(Host);
                var localHost = Dns.GetHostEntry(Dns.GetHostName());

                foreach (var ip in hostEntry.AddressList)
                {
                    if (localHost.AddressList.Any(localIp => localIp.Equals(ip)))
                    {
                        IsLocal = Port == localP2PPort;
                        return IsLocal;
                    }
                }
            }
            catch
            {
                // DNS resolution failed, assume not local
            }

            IsLocal = false;
            return false;
        }

        /// <inheritdoc/>
        public override string ToString() => RawEndpoint;
    }

    /// <summary>
    /// Result of a discovery operation.
    /// </summary>
    public class DiscoveryResult
    {
        /// <summary>
        /// Whether discovery found any reachable seeds.
        /// </summary>
        public bool Success => ReachableSeeds.Count > 0;

        /// <summary>
        /// List of seeds that were successfully contacted.
        /// </summary>
        public List<SeedEndpoint> ReachableSeeds { get; set; } = new();

        /// <summary>
        /// List of seeds that failed to respond.
        /// </summary>
        public List<(SeedEndpoint Seed, string Error)> FailedSeeds { get; set; } = new();

        /// <summary>
        /// Total number of seeds configured.
        /// </summary>
        public int TotalSeedsConfigured { get; set; }

        /// <summary>
        /// The seed that was used for joining (if any).
        /// </summary>
        public SeedEndpoint? ConnectedSeed { get; set; }
    }

    /// <summary>
    /// Event arguments for seed discovered event.
    /// </summary>
    public class SeedDiscoveredEventArgs : EventArgs
    {
        /// <summary>
        /// The seed that was discovered.
        /// </summary>
        public required SeedEndpoint Seed { get; set; }

        /// <summary>
        /// Information about the node at this seed.
        /// </summary>
        public NodeInfo? NodeInfo { get; set; }

        /// <summary>
        /// When the event occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Event arguments for seed failed event.
    /// </summary>
    public class SeedFailedEventArgs : EventArgs
    {
        /// <summary>
        /// The seed that failed.
        /// </summary>
        public required SeedEndpoint Seed { get; set; }

        /// <summary>
        /// The error message.
        /// </summary>
        public string Error { get; set; } = "";

        /// <summary>
        /// When the event occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Service for discovering cluster nodes from static seed configuration.
    /// </summary>
    public class StaticSeedDiscoveryService : ISeedDiscoveryService
    {
        private readonly P2PConfiguration _config;
        private readonly IClusterManager _clusterManager;
        private readonly Func<string, CancellationToken, Task<JoinResult>>? _connectFunc;
        private readonly List<SeedEndpoint> _configuredSeeds;
        private bool _disposed;

        /// <inheritdoc/>
        public event EventHandler<SeedDiscoveredEventArgs>? SeedDiscovered;

        /// <inheritdoc/>
        public event EventHandler<SeedFailedEventArgs>? SeedFailed;

        /// <summary>
        /// Creates a new static seed discovery service.
        /// </summary>
        public StaticSeedDiscoveryService(
            P2PConfiguration config,
            IClusterManager clusterManager,
            Func<string, CancellationToken, Task<JoinResult>>? connectFunc = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _clusterManager = clusterManager ?? throw new ArgumentNullException(nameof(clusterManager));
            _connectFunc = connectFunc;
            _configuredSeeds = ParseSeeds(config.Discovery?.Seeds ?? Array.Empty<string>());
        }

        /// <inheritdoc/>
        public IReadOnlyList<SeedEndpoint> GetConfiguredSeeds()
        {
            return _configuredSeeds.AsReadOnly();
        }

        /// <inheritdoc/>
        public async Task<DiscoveryResult> DiscoverAsync(CancellationToken ct = default)
        {
            var result = new DiscoveryResult
            {
                TotalSeedsConfigured = _configuredSeeds.Count
            };

            if (_configuredSeeds.Count == 0)
            {
                return result;
            }

            // Filter out local seeds (we don't need to discover ourselves)
            var remoteSeeds = _configuredSeeds
                .Where(s => !s.CheckIsLocal(_config.GetAdvertisePort()))
                .ToList();

            // Try to connect to each seed in parallel with timeout
            var discoveryTasks = remoteSeeds.Select(seed => TryConnectToSeedAsync(seed, ct));
            var discoveryResults = await Task.WhenAll(discoveryTasks);

            foreach (var (seed, success, error) in discoveryResults)
            {
                if (success)
                {
                    result.ReachableSeeds.Add(seed);
                    OnSeedDiscovered(new SeedDiscoveredEventArgs { Seed = seed });
                }
                else
                {
                    result.FailedSeeds.Add((seed, error ?? "Unknown error"));
                    OnSeedFailed(new SeedFailedEventArgs { Seed = seed, Error = error ?? "Unknown error" });
                }
            }

            return result;
        }

        /// <inheritdoc/>
        public async Task<JoinResult> ConnectToClusterAsync(CancellationToken ct = default)
        {
            if (_connectFunc == null)
            {
                return JoinResult.FailureResult("No connection function provided");
            }

            // First, run discovery to find reachable seeds
            var discoveryResult = await DiscoverAsync(ct);

            if (!discoveryResult.Success)
            {
                var allErrors = string.Join("; ", discoveryResult.FailedSeeds.Select(f => $"{f.Seed}: {f.Error}"));
                return JoinResult.FailureResult($"Failed to connect to any seed. Errors: {allErrors}");
            }

            // Try to join through each reachable seed in order
            foreach (var seed in discoveryResult.ReachableSeeds)
            {
                try
                {
                    var joinResult = await _connectFunc(seed.RawEndpoint, ct);
                    if (joinResult.Success)
                    {
                        discoveryResult.ConnectedSeed = seed;
                        return joinResult;
                    }
                }
                catch (Exception ex)
                {
                    OnSeedFailed(new SeedFailedEventArgs
                    {
                        Seed = seed,
                        Error = $"Join failed: {ex.Message}"
                    });
                }
            }

            return JoinResult.FailureResult("Failed to join cluster through any reachable seed");
        }

        private async Task<(SeedEndpoint Seed, bool Success, string? Error)> TryConnectToSeedAsync(
            SeedEndpoint seed,
            CancellationToken ct)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_config.ConnectionTimeout);

                // Try to resolve the hostname
                IPAddress[] addresses;
                try
                {
                    addresses = await Dns.GetHostAddressesAsync(seed.Host, timeoutCts.Token);
                    if (addresses.Length == 0)
                    {
                        return (seed, false, "Could not resolve hostname");
                    }
                }
                catch (OperationCanceledException)
                {
                    return (seed, false, "DNS resolution timeout");
                }
                catch (Exception ex)
                {
                    return (seed, false, $"DNS resolution failed: {ex.Message}");
                }

                // Try to connect to each resolved IP address
                foreach (var address in addresses)
                {
                    try
                    {
                        using var client = new System.Net.Sockets.TcpClient();
                        await client.ConnectAsync(address, seed.Port, timeoutCts.Token);

                        // Successfully connected - seed is reachable
                        client.Close();
                        return (seed, true, null);
                    }
                    catch (OperationCanceledException)
                    {
                        continue; // Try next address
                    }
                    catch
                    {
                        continue; // Try next address
                    }
                }

                return (seed, false, "Could not connect to any resolved address");
            }
            catch (OperationCanceledException)
            {
                return (seed, false, "Connection timeout");
            }
            catch (Exception ex)
            {
                return (seed, false, ex.Message);
            }
        }

        private static List<SeedEndpoint> ParseSeeds(string[] seeds)
        {
            var result = new List<SeedEndpoint>();

            foreach (var seed in seeds)
            {
                if (SeedEndpoint.TryParse(seed, out var endpoint) && endpoint != null)
                {
                    result.Add(endpoint);
                }
            }

            return result;
        }

        private void OnSeedDiscovered(SeedDiscoveredEventArgs e)
        {
            SeedDiscovered?.Invoke(this, e);
        }

        private void OnSeedFailed(SeedFailedEventArgs e)
        {
            SeedFailed?.Invoke(this, e);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }

    /// <summary>
    /// Factory for creating seed discovery services.
    /// </summary>
    public static class SeedDiscoveryServiceFactory
    {
        /// <summary>
        /// Creates the appropriate discovery service based on configuration.
        /// </summary>
        public static ISeedDiscoveryService Create(
            P2PConfiguration config,
            IClusterManager clusterManager,
            Func<string, CancellationToken, Task<JoinResult>>? connectFunc = null)
        {
            var method = config.Discovery?.Method?.ToLowerInvariant() ?? "staticseeds";

            return method switch
            {
                "staticseeds" => new StaticSeedDiscoveryService(config, clusterManager, connectFunc),
                _ => new StaticSeedDiscoveryService(config, clusterManager, connectFunc) // Default to static
            };
        }
    }
}
