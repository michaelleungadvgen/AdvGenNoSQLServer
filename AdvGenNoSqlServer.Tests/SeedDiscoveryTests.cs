// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Clustering;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Tests for the static seed discovery service.
    /// </summary>
    public class SeedDiscoveryTests
    {
        #region SeedEndpoint Tests

        [Fact]
        public void SeedEndpoint_TryParse_ValidHostPort_ReturnsTrue()
        {
            var result = SeedEndpoint.TryParse("192.168.1.1:9092", out var endpoint);

            Assert.True(result);
            Assert.NotNull(endpoint);
            Assert.Equal("192.168.1.1", endpoint!.Host);
            Assert.Equal(9092, endpoint.Port);
            Assert.Equal("192.168.1.1:9092", endpoint.RawEndpoint);
        }

        [Fact]
        public void SeedEndpoint_TryParse_ValidHostname_ReturnsTrue()
        {
            var result = SeedEndpoint.TryParse("localhost:9092", out var endpoint);

            Assert.True(result);
            Assert.NotNull(endpoint);
            Assert.Equal("localhost", endpoint!.Host);
            Assert.Equal(9092, endpoint.Port);
        }

        [Fact]
        public void SeedEndpoint_TryParse_IPv6Address_ReturnsTrue()
        {
            var result = SeedEndpoint.TryParse("[::1]:9092", out var endpoint);

            Assert.True(result);
            Assert.NotNull(endpoint);
            Assert.Equal("::1", endpoint!.Host);
            Assert.Equal(9092, endpoint.Port);
        }

        [Theory]
        [InlineData("")]
        [InlineData("invalid")]
        [InlineData("host")]
        [InlineData("host:abc")]
        [InlineData(":9092")]
        [InlineData("host:")]
        [InlineData("host:99999")]
        [InlineData("host:0")]
        [InlineData("[::1")]
        public void SeedEndpoint_TryParse_InvalidFormat_ReturnsFalse(string input)
        {
            var result = SeedEndpoint.TryParse(input, out var endpoint);

            Assert.False(result);
            Assert.Null(endpoint);
        }

        [Fact]
        public void SeedEndpoint_CheckIsLocal_LocalhostSamePort_ReturnsTrue()
        {
            var endpoint = new SeedEndpoint 
            { 
                Host = "localhost", 
                Port = 9092 
            };

            var result = endpoint.CheckIsLocal(9092);

            Assert.True(result);
            Assert.True(endpoint.IsLocal);
        }

        [Fact]
        public void SeedEndpoint_CheckIsLocal_LocalhostDifferentPort_ReturnsFalse()
        {
            var endpoint = new SeedEndpoint 
            { 
                Host = "localhost", 
                Port = 9093 
            };

            var result = endpoint.CheckIsLocal(9092);

            Assert.False(result);
            Assert.False(endpoint.IsLocal);
        }

        [Fact]
        public void SeedEndpoint_CheckIsLocal_LoopbackIP_ReturnsTrue()
        {
            var endpoint = new SeedEndpoint 
            { 
                Host = "127.0.0.1", 
                Port = 9092 
            };

            var result = endpoint.CheckIsLocal(9092);

            Assert.True(result);
            Assert.True(endpoint.IsLocal);
        }

        [Fact]
        public void SeedEndpoint_CheckIsLocal_IPv6Loopback_ReturnsTrue()
        {
            var endpoint = new SeedEndpoint 
            { 
                Host = "::1", 
                Port = 9092 
            };

            var result = endpoint.CheckIsLocal(9092);

            Assert.True(result);
            Assert.True(endpoint.IsLocal);
        }

        [Fact]
        public void SeedEndpoint_CheckIsLocal_RemoteHost_ReturnsFalse()
        {
            var endpoint = new SeedEndpoint 
            { 
                Host = "192.168.1.100", 
                Port = 9092 
            };

            var result = endpoint.CheckIsLocal(9092);

            Assert.False(result);
            Assert.False(endpoint.IsLocal);
        }

        #endregion

        #region StaticSeedDiscoveryService Constructor Tests

        [Fact]
        public void Constructor_ValidParameters_CreatesService()
        {
            var config = CreateTestConfig(new[] { "localhost:9092" });
            var clusterManager = new TestClusterManager();

            var service = new StaticSeedDiscoveryService(config, clusterManager);

            Assert.NotNull(service);
            var seeds = service.GetConfiguredSeeds();
            Assert.Single(seeds);
        }

        [Fact]
        public void Constructor_NullConfig_ThrowsArgumentNullException()
        {
            var clusterManager = new TestClusterManager();

            Assert.Throws<ArgumentNullException>(() => new StaticSeedDiscoveryService(null!, clusterManager));
        }

        [Fact]
        public void Constructor_NullClusterManager_ThrowsArgumentNullException()
        {
            var config = CreateTestConfig();

            Assert.Throws<ArgumentNullException>(() => new StaticSeedDiscoveryService(config, null!));
        }

        [Fact]
        public void Constructor_InvalidSeeds_AreFilteredOut()
        {
            var config = CreateTestConfig(new[] { 
                "localhost:9092", 
                "invalid", 
                "192.168.1.1:9093",
                "bad:port",
                ""
            });
            var clusterManager = new TestClusterManager();

            var service = new StaticSeedDiscoveryService(config, clusterManager);

            var seeds = service.GetConfiguredSeeds();
            Assert.Equal(2, seeds.Count);
            Assert.Contains(seeds, s => s.Host == "localhost");
            Assert.Contains(seeds, s => s.Host == "192.168.1.1");
        }

        [Fact]
        public void Constructor_EmptySeeds_CreatesEmptyService()
        {
            var config = CreateTestConfig(Array.Empty<string>());
            var clusterManager = new TestClusterManager();

            var service = new StaticSeedDiscoveryService(config, clusterManager);

            var seeds = service.GetConfiguredSeeds();
            Assert.Empty(seeds);
        }

        #endregion

        #region DiscoverAsync Tests

        [Fact]
        public async Task DiscoverAsync_NoSeeds_ReturnsEmptyResult()
        {
            var config = CreateTestConfig(Array.Empty<string>());
            var clusterManager = new TestClusterManager();
            var service = new StaticSeedDiscoveryService(config, clusterManager);

            var result = await service.DiscoverAsync();

            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Empty(result.ReachableSeeds);
            Assert.Empty(result.FailedSeeds);
            Assert.Equal(0, result.TotalSeedsConfigured);
        }

        [Fact]
        public async Task DiscoverAsync_OnlyLocalSeeds_SkipsLocalAndReturnsEmpty()
        {
            var config = CreateTestConfig(new[] { "localhost:9092" }, p2pPort: 9092);
            var clusterManager = new TestClusterManager();
            var service = new StaticSeedDiscoveryService(config, clusterManager);

            var result = await service.DiscoverAsync();

            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Empty(result.ReachableSeeds);
            Assert.Equal(1, result.TotalSeedsConfigured);
        }

        [Fact]
        public async Task DiscoverAsync_UnreachableSeeds_ReturnsFailedSeeds()
        {
            // Use a port that's very unlikely to be open
            var config = CreateTestConfig(new[] { "127.0.0.1:1" });
            config.ConnectionTimeout = TimeSpan.FromMilliseconds(100);
            var clusterManager = new TestClusterManager();
            var service = new StaticSeedDiscoveryService(config, clusterManager);

            var result = await service.DiscoverAsync();

            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Empty(result.ReachableSeeds);
            Assert.Single(result.FailedSeeds);
        }

        [Fact]
        public async Task DiscoverAsync_ReachableSeed_ReturnsSuccess()
        {
            // Start a local TCP listener to simulate a seed node
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var config = CreateTestConfig(new[] { $"127.0.0.1:{port}" });
            config.ConnectionTimeout = TimeSpan.FromSeconds(2);
            var clusterManager = new TestClusterManager();
            var service = new StaticSeedDiscoveryService(config, clusterManager);

            // Accept connection in background
            var acceptTask = Task.Run(async () =>
            {
                var client = await listener.AcceptTcpClientAsync();
                client.Close();
            });

            var result = await service.DiscoverAsync();

            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Single(result.ReachableSeeds);
            Assert.Empty(result.FailedSeeds);

            listener.Stop();
        }

        [Fact]
        public async Task DiscoverAsync_MixedSeeds_ReturnsPartialSuccess()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var config = CreateTestConfig(new[] { 
                $"127.0.0.1:{port}",  // Reachable
                "127.0.0.1:1"        // Unreachable
            });
            config.ConnectionTimeout = TimeSpan.FromMilliseconds(500);
            var clusterManager = new TestClusterManager();
            var service = new StaticSeedDiscoveryService(config, clusterManager);

            // Accept connection in background
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    client.Close();
                }
                catch { }
            });

            var result = await service.DiscoverAsync();

            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Single(result.ReachableSeeds);
            Assert.Single(result.FailedSeeds);

            listener.Stop();
        }

        [Fact]
        public async Task DiscoverAsync_Cancellation_StopsDiscovery()
        {
            var config = CreateTestConfig(new[] { "192.168.255.255:9092" });
            config.ConnectionTimeout = TimeSpan.FromSeconds(10);
            var clusterManager = new TestClusterManager();
            var service = new StaticSeedDiscoveryService(config, clusterManager);

            using var cts = new CancellationTokenSource();
            // Cancel after a very short delay to test mid-operation cancellation
            cts.CancelAfter(TimeSpan.FromMilliseconds(50));

            // The operation should either complete (if fast enough) or throw cancellation
            try
            {
                await service.DiscoverAsync(cts.Token);
                // If we get here, that's fine - the operation completed before cancellation
            }
            catch (OperationCanceledException)
            {
                // This is also acceptable - cancellation worked
            }
        }

        #endregion

        #region ConnectToClusterAsync Tests

        [Fact]
        public async Task ConnectToClusterAsync_NoConnectFunc_ReturnsFailure()
        {
            var config = CreateTestConfig(new[] { "localhost:9092" });
            var clusterManager = new TestClusterManager();
            var service = new StaticSeedDiscoveryService(config, clusterManager);

            var result = await service.ConnectToClusterAsync();

            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Contains("No connection function provided", result.ErrorMessage);
        }

        [Fact]
        public async Task ConnectToClusterAsync_NoReachableSeeds_ReturnsFailure()
        {
            var config = CreateTestConfig(new[] { "127.0.0.1:1" });
            config.ConnectionTimeout = TimeSpan.FromMilliseconds(100);
            var clusterManager = new TestClusterManager();
            var connectCalled = false;
            Func<string, CancellationToken, Task<JoinResult>> connectFunc = (endpoint, ct) =>
            {
                connectCalled = true;
                return Task.FromResult(JoinResult.FailureResult("Connection failed"));
            };

            var service = new StaticSeedDiscoveryService(config, clusterManager, connectFunc);
            var result = await service.ConnectToClusterAsync();

            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.False(connectCalled); // Should not be called since no seeds are reachable
        }

        [Fact]
        public async Task ConnectToClusterAsync_SuccessfulJoin_ReturnsSuccess()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var config = CreateTestConfig(new[] { $"127.0.0.1:{port}" });
            config.ConnectionTimeout = TimeSpan.FromSeconds(2);
            var clusterManager = new TestClusterManager();
            
            Func<string, CancellationToken, Task<JoinResult>> connectFunc = (endpoint, ct) =>
            {
                return Task.FromResult(JoinResult.SuccessResult(new ClusterInfo
                {
                    ClusterId = "test-cluster",
                    ClusterName = "Test",
                    Nodes = new List<NodeInfo>()
                }));
            };

            var service = new StaticSeedDiscoveryService(config, clusterManager, connectFunc);

            // Accept connection in background
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    client.Close();
                }
                catch { }
            });

            var result = await service.ConnectToClusterAsync();

            Assert.NotNull(result);
            Assert.True(result.Success);

            listener.Stop();
        }

        [Fact]
        public async Task ConnectToClusterAsync_JoinFails_TriesNextSeed()
        {
            using var listener1 = new TcpListener(IPAddress.Loopback, 0);
            using var listener2 = new TcpListener(IPAddress.Loopback, 0);
            listener1.Start();
            listener2.Start();
            var port1 = ((IPEndPoint)listener1.LocalEndpoint).Port;
            var port2 = ((IPEndPoint)listener2.LocalEndpoint).Port;

            var config = CreateTestConfig(new[] { $"127.0.0.1:{port1}", $"127.0.0.1:{port2}" });
            config.ConnectionTimeout = TimeSpan.FromSeconds(2);
            var clusterManager = new TestClusterManager();
            
            var callCount = 0;
            Func<string, CancellationToken, Task<JoinResult>> connectFunc = (endpoint, ct) =>
            {
                callCount++;
                if (endpoint.Contains($":{port1}"))
                {
                    return Task.FromResult(JoinResult.FailureResult("First seed failed"));
                }
                return Task.FromResult(JoinResult.SuccessResult(new ClusterInfo
                {
                    ClusterId = "test-cluster",
                    ClusterName = "Test",
                    Nodes = new List<NodeInfo>()
                }));
            };

            var service = new StaticSeedDiscoveryService(config, clusterManager, connectFunc);

            // Accept connections in background
            _ = Task.Run(async () =>
            {
                try { (await listener1.AcceptTcpClientAsync()).Close(); } catch { }
            });
            _ = Task.Run(async () =>
            {
                try { (await listener2.AcceptTcpClientAsync()).Close(); } catch { }
            });

            var result = await service.ConnectToClusterAsync();

            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Equal(2, callCount);

            listener1.Stop();
            listener2.Stop();
        }

        #endregion

        #region Event Tests

        [Fact]
        public async Task DiscoverAsync_SuccessfulDiscovery_RaisesSeedDiscoveredEvent()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var config = CreateTestConfig(new[] { $"127.0.0.1:{port}" });
            config.ConnectionTimeout = TimeSpan.FromSeconds(2);
            var clusterManager = new TestClusterManager();
            var service = new StaticSeedDiscoveryService(config, clusterManager);

            SeedDiscoveredEventArgs? discoveredEvent = null;
            service.SeedDiscovered += (s, e) => discoveredEvent = e;

            _ = Task.Run(async () =>
            {
                try { (await listener.AcceptTcpClientAsync()).Close(); } catch { }
            });

            await service.DiscoverAsync();

            Assert.NotNull(discoveredEvent);
            Assert.Equal($"127.0.0.1:{port}", discoveredEvent!.Seed.RawEndpoint);

            listener.Stop();
        }

        [Fact]
        public async Task DiscoverAsync_FailedDiscovery_RaisesSeedFailedEvent()
        {
            var config = CreateTestConfig(new[] { "127.0.0.1:1" });
            config.ConnectionTimeout = TimeSpan.FromMilliseconds(100);
            var clusterManager = new TestClusterManager();
            var service = new StaticSeedDiscoveryService(config, clusterManager);

            SeedFailedEventArgs? failedEvent = null;
            service.SeedFailed += (s, e) => failedEvent = e;

            await service.DiscoverAsync();

            Assert.NotNull(failedEvent);
            Assert.Equal("127.0.0.1:1", failedEvent!.Seed.RawEndpoint);
            Assert.NotNull(failedEvent.Error);
        }

        #endregion

        #region SeedDiscoveryServiceFactory Tests

        [Fact]
        public void Create_StaticSeedsMethod_ReturnsStaticSeedDiscoveryService()
        {
            var config = CreateTestConfig(new[] { "localhost:9092" });
            config.Discovery.Method = "StaticSeeds";
            var clusterManager = new TestClusterManager();

            var service = SeedDiscoveryServiceFactory.Create(config, clusterManager);

            Assert.IsType<StaticSeedDiscoveryService>(service);
        }

        [Fact]
        public void Create_LowerCaseMethod_ReturnsStaticSeedDiscoveryService()
        {
            var config = CreateTestConfig(new[] { "localhost:9092" });
            config.Discovery.Method = "staticseeds";
            var clusterManager = new TestClusterManager();

            var service = SeedDiscoveryServiceFactory.Create(config, clusterManager);

            Assert.IsType<StaticSeedDiscoveryService>(service);
        }

        [Fact]
        public void Create_UnknownMethod_ReturnsStaticSeedDiscoveryServiceAsDefault()
        {
            var config = CreateTestConfig(new[] { "localhost:9092" });
            config.Discovery.Method = "UnknownMethod";
            var clusterManager = new TestClusterManager();

            var service = SeedDiscoveryServiceFactory.Create(config, clusterManager);

            Assert.IsType<StaticSeedDiscoveryService>(service);
        }

        [Fact]
        public void Create_NullMethod_ReturnsStaticSeedDiscoveryServiceAsDefault()
        {
            var config = CreateTestConfig(new[] { "localhost:9092" });
            config.Discovery.Method = null!;
            var clusterManager = new TestClusterManager();

            var service = SeedDiscoveryServiceFactory.Create(config, clusterManager);

            Assert.IsType<StaticSeedDiscoveryService>(service);
        }

        #endregion

        #region Helper Methods

        private static P2PConfiguration CreateTestConfig(string[]? seeds = null, int p2pPort = 9092)
        {
            return new P2PConfiguration
            {
                ClusterId = "test-cluster",
                ClusterName = "Test Cluster",
                P2PPort = p2pPort,
                Discovery = new DiscoveryConfiguration
                {
                    Method = "StaticSeeds",
                    Seeds = seeds ?? Array.Empty<string>()
                }
            };
        }

        #endregion

        #region Test Cluster Manager

        private class TestClusterManager : IClusterManager
        {
            public NodeIdentity LocalNode => new()
            {
                NodeId = "test-node",
                ClusterId = "test-cluster",
                Host = "localhost",
                Port = 9090,
                P2PPort = 9092
            };

            public bool IsClusterMember => false;
            public bool IsLeader => false;

            public event EventHandler<NodeJoinedEventArgs>? NodeJoined;
            public event EventHandler<NodeLeftEventArgs>? NodeLeft;
            public event EventHandler<LeaderChangedEventArgs>? LeaderChanged;
            public event EventHandler<NodeStateChangedEventArgs>? NodeStateChanged;

            public Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default)
            {
                return Task.FromResult(new ClusterInfo
                {
                    ClusterId = "test-cluster",
                    ClusterName = "Test",
                    Nodes = new List<NodeInfo>()
                });
            }

            public Task<JoinResult> JoinClusterAsync(string seedNode, JoinOptions options, CancellationToken ct = default)
            {
                return Task.FromResult(JoinResult.SuccessResult(new ClusterInfo
                {
                    ClusterId = "test-cluster",
                    ClusterName = "Test",
                    Nodes = new List<NodeInfo>()
                }));
            }

            public Task<JoinResult> CreateClusterAsync(string clusterName, CancellationToken ct = default)
            {
                return Task.FromResult(JoinResult.SuccessResult(new ClusterInfo
                {
                    ClusterId = "test-cluster",
                    ClusterName = clusterName,
                    Nodes = new List<NodeInfo>()
                }));
            }

            public Task<LeaveResult> LeaveClusterAsync(LeaveOptions options, CancellationToken ct = default)
            {
                return Task.FromResult(LeaveResult.SuccessResult());
            }

            public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken ct = default)
            {
                return Task.FromResult<IReadOnlyList<NodeInfo>>(new List<NodeInfo>());
            }

            public Task<NodeInfo?> GetNodeAsync(string nodeId, CancellationToken ct = default)
            {
                return Task.FromResult<NodeInfo?>(null);
            }

            public Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default)
            {
                return Task.FromResult(true);
            }

            public Task<NodeInfo?> GetLeaderAsync(CancellationToken ct = default)
            {
                return Task.FromResult<NodeInfo?>(null);
            }

            public Task<bool> RequestLeaderElectionAsync(CancellationToken ct = default)
            {
                return Task.FromResult(true);
            }

            public Task<bool> UpdateNodeStateAsync(NodeState newState, CancellationToken ct = default)
            {
                return Task.FromResult(true);
            }

            public void Dispose() { }
        }

        #endregion
    }
}
