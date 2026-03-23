// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Clustering;

namespace AdvGenNoSqlServer.Network.Clustering
{
    /// <summary>
    /// Client for connecting to peer nodes in the cluster.
    /// </summary>
    public class P2PClient : IAsyncDisposable
    {
        private readonly P2PConfiguration _config;
        private readonly IClusterManager _clusterManager;
        private readonly ConcurrentDictionary<string, PeerConnection> _connections = new();
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// Event raised when connected to a peer.
        /// </summary>
        public event EventHandler<PeerConnectedEventArgs>? Connected;

        /// <summary>
        /// Event raised when disconnected from a peer.
        /// </summary>
        public event EventHandler<PeerDisconnectedEventArgs>? Disconnected;

        /// <summary>
        /// Event raised when a message is received.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// Creates a new P2P client.
        /// </summary>
        public P2PClient(P2PConfiguration config, IClusterManager clusterManager)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _clusterManager = clusterManager ?? throw new ArgumentNullException(nameof(clusterManager));
        }

        /// <summary>
        /// Connects to a seed node to join the cluster.
        /// </summary>
        public async Task<JoinResult> ConnectToSeedAsync(string seedEndpoint, CancellationToken ct = default)
        {
            try
            {
                var parts = seedEndpoint.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
                    return JoinResult.FailureResult("Invalid seed endpoint format. Expected host:port");

                var host = parts[0];
                var client = new TcpClient();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_config.ConnectionTimeout);

                await client.ConnectAsync(host, port, timeoutCts.Token);

                var connectionId = Guid.NewGuid().ToString("N")[..8];
                var connection = new PeerConnection(connectionId, client, _config.Security);

                // Perform handshake
                var result = await connection.InitiateHandshakeAsync(
                    _clusterManager.LocalNode,
                    _config.Security.ClusterSecret,
                    ct);

                if (!result.Success)
                {
                    await connection.DisposeAsync();
                    return JoinResult.FailureResult(result.ErrorMessage ?? "Handshake failed");
                }

                var nodeId = result.NodeId!;
                _connections[nodeId] = connection;

                OnConnected(new PeerConnectedEventArgs
                {
                    NodeId = nodeId,
                    Endpoint = seedEndpoint
                });

                // Start message processing
                _ = ProcessMessagesAsync(connection, nodeId, ct);

                // Return successful join
                return new JoinResult
                {
                    Success = true,
                    ClusterInfo = new ClusterInfo
                    {
                        ClusterId = _config.ClusterId,
                        ClusterName = _config.ClusterName,
                        Nodes = new List<NodeInfo>()
                    }
                };
            }
            catch (OperationCanceledException)
            {
                return JoinResult.FailureResult("Connection timeout");
            }
            catch (Exception ex)
            {
                return JoinResult.FailureResult($"Connection failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Connects to multiple seed nodes.
        /// </summary>
        public async Task<JoinResult> ConnectToSeedsAsync(string[] seedEndpoints, CancellationToken ct = default)
        {
            foreach (var seed in seedEndpoints)
            {
                var result = await ConnectToSeedAsync(seed, ct);
                if (result.Success)
                    return result;
            }

            return JoinResult.FailureResult("Failed to connect to any seed node");
        }

        /// <summary>
        /// Disconnects from all peers.
        /// </summary>
        public async Task DisconnectAllAsync(CancellationToken ct = default)
        {
            foreach (var connection in _connections.Values)
            {
                await connection.DisposeAsync();
            }
            _connections.Clear();
        }

        /// <summary>
        /// Sends a message to a specific peer.
        /// </summary>
        public async Task<bool> SendAsync(string nodeId, P2PMessage message, CancellationToken ct = default)
        {
            if (!_connections.TryGetValue(nodeId, out var connection))
                return false;

            return await connection.SendAsync(message, ct);
        }

        /// <summary>
        /// Broadcasts a message to all connected peers.
        /// </summary>
        public async Task<int> BroadcastAsync(P2PMessage message, CancellationToken ct = default)
        {
            var tasks = new List<Task<bool>>();

            foreach (var connection in _connections.Values)
            {
                tasks.Add(connection.SendAsync(message, ct));
            }

            var results = await Task.WhenAll(tasks);
            return results.Count(r => r);
        }

        /// <summary>
        /// Gets the number of active connections.
        /// </summary>
        public int ConnectionCount => _connections.Count;

        /// <summary>
        /// Gets the IDs of connected nodes.
        /// </summary>
        public IEnumerable<string> ConnectedNodeIds => _connections.Keys;

        private async Task ProcessMessagesAsync(PeerConnection connection, string nodeId, CancellationToken ct)
        {
            try
            {
                await connection.ProcessMessagesAsync(
                    msg => OnMessageReceived(new MessageReceivedEventArgs
                    {
                        NodeId = nodeId,
                        Message = msg
                    }),
                    ct);
            }
            finally
            {
                _connections.TryRemove(nodeId, out _);
                OnDisconnected(new PeerDisconnectedEventArgs
                {
                    NodeId = nodeId,
                    Endpoint = connection.RemoteEndpoint
                });
                await connection.DisposeAsync();
            }
        }

        private void OnConnected(PeerConnectedEventArgs e)
        {
            Connected?.Invoke(this, e);
        }

        private void OnDisconnected(PeerDisconnectedEventArgs e)
        {
            Disconnected?.Invoke(this, e);
        }

        private void OnMessageReceived(MessageReceivedEventArgs e)
        {
            MessageReceived?.Invoke(this, e);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            await DisconnectAllAsync();
            _cts.Dispose();
        }
    }
}
