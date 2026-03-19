// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Clustering;

namespace AdvGenNoSqlServer.Network.Clustering
{
    /// <summary>
    /// TCP server for inter-node P2P communication.
    /// </summary>
    public class P2PServer : IAsyncDisposable
    {
        private readonly P2PConfiguration _config;
        private readonly IClusterManager _clusterManager;
        private TcpListener? _listener;
        private readonly ConcurrentDictionary<string, PeerConnection> _connections = new();
        private readonly CancellationTokenSource _cts = new();
        private Task? _listenTask;
        private bool _isRunning;
        private readonly object _startStopLock = new();

        /// <summary>
        /// Whether the server is currently running.
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Number of active peer connections.
        /// </summary>
        public int ConnectionCount => _connections.Count;

        /// <summary>
        /// Event raised when a peer connects.
        /// </summary>
        public event EventHandler<PeerConnectedEventArgs>? PeerConnected;

        /// <summary>
        /// Event raised when a peer disconnects.
        /// </summary>
        public event EventHandler<PeerDisconnectedEventArgs>? PeerDisconnected;

        /// <summary>
        /// Event raised when a message is received from a peer.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// Creates a new P2P server.
        /// </summary>
        public P2PServer(P2PConfiguration config, IClusterManager clusterManager)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _clusterManager = clusterManager ?? throw new ArgumentNullException(nameof(clusterManager));
        }

        /// <summary>
        /// Starts the P2P server.
        /// </summary>
        public async Task StartAsync(CancellationToken ct = default)
        {
            lock (_startStopLock)
            {
                if (_isRunning)
                    throw new InvalidOperationException("P2P server is already running");

                _isRunning = true;
            }

            try
            {
                var bindAddress = IPAddress.Parse(_config.BindAddress);
                _listener = new TcpListener(bindAddress, _config.P2PPort);
                _listener.Start();

                _listenTask = ListenLoopAsync(_cts.Token);
            }
            catch
            {
                _isRunning = false;
                throw;
            }
        }

        /// <summary>
        /// Stops the P2P server.
        /// </summary>
        public async Task StopAsync(CancellationToken ct = default)
        {
            lock (_startStopLock)
            {
                if (!_isRunning)
                    return;

                _isRunning = false;
            }

            _cts.Cancel();
            _listener?.Stop();

            // Close all connections
            foreach (var connection in _connections.Values)
            {
                await connection.DisposeAsync();
            }
            _connections.Clear();

            if (_listenTask != null)
            {
                try
                {
                    await _listenTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }
        }

        /// <summary>
        /// Sends a message to a specific peer.
        /// </summary>
        public async Task<bool> SendToPeerAsync(string nodeId, P2PMessage message, CancellationToken ct = default)
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

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener!.AcceptTcpClientAsync(ct);
                    _ = HandleClientAsync(client, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Log error but continue listening
                    await Task.Delay(100, ct);
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            var connectionId = Guid.NewGuid().ToString("N")[..8];
            PeerConnection? connection = null;

            try
            {
                connection = new PeerConnection(connectionId, client, _config.Security);
                
                // Perform handshake
                var handshakeResult = await connection.PerformHandshakeAsync(ct);
                if (!handshakeResult.Success)
                {
                    await connection.DisposeAsync();
                    return;
                }

                var nodeId = handshakeResult.NodeId!;
                
                // Store connection
                _connections[nodeId] = connection;

                OnPeerConnected(new PeerConnectedEventArgs 
                { 
                    NodeId = nodeId, 
                    Endpoint = connection.RemoteEndpoint 
                });

                // Process messages
                await connection.ProcessMessagesAsync(
                    msg => OnMessageReceived(new MessageReceivedEventArgs 
                    { 
                        NodeId = nodeId, 
                        Message = msg 
                    }),
                    ct);
            }
            catch (Exception)
            {
                // Connection error
            }
            finally
            {
                if (connection != null)
                {
                    var nodeId = connection.AuthenticatedNodeId;
                    if (!string.IsNullOrEmpty(nodeId))
                    {
                        _connections.TryRemove(nodeId, out _);
                        OnPeerDisconnected(new PeerDisconnectedEventArgs 
                        { 
                            NodeId = nodeId,
                            Endpoint = connection.RemoteEndpoint 
                        });
                    }
                    await connection.DisposeAsync();
                }
            }
        }

        /// <summary>
        /// Connects to a peer node.
        /// </summary>
        public async Task<bool> ConnectToPeerAsync(string host, int port, CancellationToken ct = default)
        {
            try
            {
                var client = new TcpClient();
                await client.ConnectAsync(host, port, ct);

                var connectionId = Guid.NewGuid().ToString("N")[..8];
                var connection = new PeerConnection(connectionId, client, _config.Security);

                // Send join handshake
                var result = await connection.InitiateHandshakeAsync(
                    _clusterManager.LocalNode, 
                    _config.Security.ClusterSecret,
                    ct);

                if (!result.Success)
                {
                    await connection.DisposeAsync();
                    return false;
                }

                var nodeId = result.NodeId!;
                _connections[nodeId] = connection;

                OnPeerConnected(new PeerConnectedEventArgs 
                { 
                    NodeId = nodeId, 
                    Endpoint = connection.RemoteEndpoint 
                });

                // Start processing messages
                _ = Task.Run(async () =>
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
                        OnPeerDisconnected(new PeerDisconnectedEventArgs 
                        { 
                            NodeId = nodeId,
                            Endpoint = connection.RemoteEndpoint 
                        });
                        await connection.DisposeAsync();
                    }
                }, ct);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnPeerConnected(PeerConnectedEventArgs e)
        {
            PeerConnected?.Invoke(this, e);
        }

        private void OnPeerDisconnected(PeerDisconnectedEventArgs e)
        {
            PeerDisconnected?.Invoke(this, e);
        }

        private void OnMessageReceived(MessageReceivedEventArgs e)
        {
            MessageReceived?.Invoke(this, e);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _cts.Dispose();
        }
    }

    /// <summary>
    /// Represents a connection to a peer node.
    /// </summary>
    public class PeerConnection : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly P2PSecurityConfiguration _security;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Unique connection identifier.
        /// </summary>
        public string ConnectionId { get; }

        /// <summary>
        /// Remote endpoint address.
        /// </summary>
        public string RemoteEndpoint { get; }

        /// <summary>
        /// Node ID after authentication.
        /// </summary>
        public string? AuthenticatedNodeId { get; private set; }

        /// <summary>
        /// When the connection was established.
        /// </summary>
        public DateTime ConnectedAt { get; } = DateTime.UtcNow;

        /// <summary>
        /// Creates a new peer connection.
        /// </summary>
        public PeerConnection(string connectionId, TcpClient client, P2PSecurityConfiguration security)
        {
            ConnectionId = connectionId;
            _client = client;
            _stream = client.GetStream();
            _security = security;
            RemoteEndpoint = client.Client?.RemoteEndPoint?.ToString() ?? "unknown";
            _jsonOptions = new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        /// <summary>
        /// Performs server-side handshake.
        /// </summary>
        public async Task<HandshakeResult> PerformHandshakeAsync(CancellationToken ct)
        {
            try
            {
                // Read join request
                var request = await ReadMessageAsync<JoinRequestMessage>(ct);
                if (request == null)
                    return HandshakeResult.FailedResult("Invalid join request");

                // Validate cluster secret
                if (!string.IsNullOrEmpty(_security.ClusterSecret))
                {
                    var expectedHash = ComputeClusterSecretHash(_security.ClusterSecret);
                    if (request.ClusterSecretHash != expectedHash)
                    {
                        await SendAsync(new JoinResponseMessage 
                        { 
                            SenderId = "",
                            Accepted = false, 
                            ErrorMessage = "Invalid cluster secret" 
                        }, ct);
                        return HandshakeResult.FailedResult("Invalid cluster secret");
                    }
                }

                AuthenticatedNodeId = request.NodeIdentity.NodeId;

                // Send acceptance
                var response = new JoinResponseMessage
                {
                    SenderId = "",
                    Accepted = true,
                    KnownNodes = new List<NodeInfo>()
                };

                await SendAsync(response, ct);

                return HandshakeResult.SuccessResult(AuthenticatedNodeId);
            }
            catch (Exception ex)
            {
                return HandshakeResult.FailedResult(ex.Message);
            }
        }

        /// <summary>
        /// Initiates client-side handshake.
        /// </summary>
        public async Task<HandshakeResult> InitiateHandshakeAsync(
            NodeIdentity localNode, 
            string? clusterSecret,
            CancellationToken ct)
        {
            try
            {
                // Send join request
                var request = new JoinRequestMessage
                {
                    SenderId = localNode.NodeId,
                    NodeIdentity = localNode,
                    ClusterSecretHash = clusterSecret != null ? ComputeClusterSecretHash(clusterSecret) : ""
                };

                await SendAsync(request, ct);

                // Read response
                var response = await ReadMessageAsync<JoinResponseMessage>(ct);
                if (response == null)
                    return HandshakeResult.FailedResult("No response received");

                if (!response.Accepted)
                    return HandshakeResult.FailedResult(response.ErrorMessage ?? "Join rejected");

                AuthenticatedNodeId = response.SenderId;
                return HandshakeResult.SuccessResult(AuthenticatedNodeId);
            }
            catch (Exception ex)
            {
                return HandshakeResult.FailedResult(ex.Message);
            }
        }

        /// <summary>
        /// Processes incoming messages.
        /// </summary>
        public async Task ProcessMessagesAsync(Action<P2PMessage> onMessage, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _client.Connected)
            {
                try
                {
                    var message = await ReadMessageAsync(ct);
                    if (message != null)
                    {
                        onMessage(message);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Connection error
                    break;
                }
            }
        }

        /// <summary>
        /// Sends a message to the peer.
        /// </summary>
        public async Task<bool> SendAsync(P2PMessage message, CancellationToken ct)
        {
            try
            {
                var json = JsonSerializer.Serialize(message, message.GetType(), _jsonOptions);
                var bytes = Encoding.UTF8.GetBytes(json);
                var lengthBytes = BitConverter.GetBytes(bytes.Length);

                await _stream.WriteAsync(lengthBytes, ct);
                await _stream.WriteAsync(bytes, ct);
                await _stream.FlushAsync(ct);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<P2PMessage?> ReadMessageAsync(CancellationToken ct)
        {
            var lengthBytes = new byte[4];
            if (await _stream.ReadAsync(lengthBytes, ct) != 4)
                return null;

            var length = BitConverter.ToInt32(lengthBytes);
            if (length <= 0 || length > 10 * 1024 * 1024) // Max 10MB
                return null;

            var buffer = new byte[length];
            var read = 0;
            while (read < length)
            {
                var chunk = await _stream.ReadAsync(buffer.AsMemory(read, length - read), ct);
                if (chunk == 0)
                    return null;
                read += chunk;
            }

            var json = Encoding.UTF8.GetString(buffer);
            
            // Try to determine message type
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("messageType", out var typeElement))
                return null;

            var messageType = typeElement.GetString();
            var type = messageType switch
            {
                "JoinRequest" => typeof(JoinRequestMessage),
                "JoinResponse" => typeof(JoinResponseMessage),
                "Heartbeat" => typeof(HeartbeatMessage),
                "LeaveRequest" => typeof(LeaveRequestMessage),
                "Gossip" => typeof(GossipMessage),
                _ => null
            };

            if (type == null)
                return null;

            return JsonSerializer.Deserialize(json, type, _jsonOptions) as P2PMessage;
        }

        private async Task<T?> ReadMessageAsync<T>(CancellationToken ct) where T : P2PMessage
        {
            var message = await ReadMessageAsync(ct);
            return message as T;
        }

        private static string ComputeClusterSecretHash(string secret)
        {
            var bytes = Encoding.UTF8.GetBytes(secret);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            _stream.Close();
            _client.Close();
        }
    }

    /// <summary>
    /// Result of a handshake operation.
    /// </summary>
    public class HandshakeResult
    {
        /// <summary>
        /// Whether the handshake succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The authenticated node ID.
        /// </summary>
        public string? NodeId { get; set; }

        /// <summary>
        /// Error message if failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static HandshakeResult SuccessResult(string nodeId)
        {
            return new HandshakeResult { Success = true, NodeId = nodeId };
        }

        /// <summary>
        /// Creates a failed result.
        /// </summary>
        public static HandshakeResult FailedResult(string error)
        {
            return new HandshakeResult { Success = false, ErrorMessage = error };
        }
    }

    /// <summary>
    /// Event args for peer connected.
    /// </summary>
    public class PeerConnectedEventArgs : EventArgs
    {
        /// <summary>
        /// The connected node's ID.
        /// </summary>
        public required string NodeId { get; set; }

        /// <summary>
        /// The remote endpoint.
        /// </summary>
        public required string Endpoint { get; set; }
    }

    /// <summary>
    /// Event args for peer disconnected.
    /// </summary>
    public class PeerDisconnectedEventArgs : EventArgs
    {
        /// <summary>
        /// The disconnected node's ID.
        /// </summary>
        public required string NodeId { get; set; }

        /// <summary>
        /// The remote endpoint.
        /// </summary>
        public required string Endpoint { get; set; }
    }

    /// <summary>
    /// Event args for message received.
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// The sender node's ID.
        /// </summary>
        public required string NodeId { get; set; }

        /// <summary>
        /// The received message.
        /// </summary>
        public required P2PMessage Message { get; set; }
    }
}
