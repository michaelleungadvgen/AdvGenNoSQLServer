// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NoSqlServer.Tests
{
    /// <summary>
    /// Stress tests for the NoSQL server to validate performance under high load.
    /// These tests are disabled by default as they may take significant time to run.
    /// </summary>
    [CollectionDefinition("StressTests", DisableParallelization = true)]
    public class StressTestsCollection { }

    [Collection("StressTests")]
    public class StressTests : IDisposable
    {
        private TcpServer? _server;
        private readonly List<AdvGenNoSqlClient> _clients = new();
        private static int _currentPort = 19500;
        private static readonly object _portLock = new();
        private readonly ConcurrentBag<TimeSpan> _responseTimes = new();

        public void Dispose()
        {
            foreach (var client in _clients)
            {
                try { client.Dispose(); } catch { /* Best effort */ }
            }
            try { _server?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); } catch { /* Best effort */ }

            Thread.Sleep(100);
        }

        private int GetNextPort()
        {
            lock (_portLock)
            {
                return _currentPort++;
            }
        }

        private async Task<TcpServer> CreateTestServer(int port, int maxConnections = 1000)
        {
            var config = new ServerConfiguration
            {
                Host = "127.0.0.1",
                Port = port,
                MaxConcurrentConnections = maxConnections
            };

            var server = new TcpServer(config);

            server.MessageReceived += async (_, args) =>
            {
                switch (args.Message.MessageType)
                {
                    case MessageType.Handshake:
                        await args.SendResponseAsync(NoSqlMessage.CreateSuccess(new { version = "1.0", server = "AdvGenNoSqlServer" }));
                        break;
                    case MessageType.Ping:
                        await args.SendResponseAsync(new NoSqlMessage
                        {
                            MessageType = MessageType.Pong,
                            Payload = Array.Empty<byte>(),
                            PayloadLength = 0
                        });
                        break;
                    case MessageType.Command:
                        // Simulate some processing time (1-5ms)
                        await Task.Delay(Random.Shared.Next(1, 5));
                        await args.SendResponseAsync(NoSqlMessage.CreateSuccess(new { id = Guid.NewGuid().ToString(), status = "ok" }));
                        break;
                    default:
                        await args.SendResponseAsync(NoSqlMessage.CreateError("UNSUPPORTED_TYPE", "Unsupported message type"));
                        break;
                }
            };

            await server.StartAsync();
            await Task.Delay(100); // Give server time to start

            return server;
        }

        /// <summary>
        /// Stress test: Multiple concurrent connections to validate connection handling.
        /// Target: 100+ concurrent connections
        /// </summary>
        [Fact(Skip = "Stress test - run manually")]
        public async Task StressTest_ConcurrentConnections_ShouldHandleMultipleClients()
        {
            // Arrange
            const int concurrentClients = 100;
            const int operationsPerClient = 10;
            var port = GetNextPort();
            _server = await CreateTestServer(port, concurrentClients + 50);

            var clients = new List<AdvGenNoSqlClient>();
            var successCount = 0;
            var failureCount = 0;
            var lockObj = new object();

            // Act - Create multiple clients concurrently
            var stopwatch = Stopwatch.StartNew();
            var tasks = new List<Task>();

            for (int i = 0; i < concurrentClients; i++)
            {
                var clientIndex = i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
                        lock (lockObj) { clients.Add(client); }

                        await client.ConnectAsync();

                        for (int op = 0; op < operationsPerClient; op++)
                        {
                            var opStopwatch = Stopwatch.StartNew();
                            await client.PingAsync();
                            opStopwatch.Stop();
                            _responseTimes.Add(opStopwatch.Elapsed);
                        }

                        await client.DisconnectAsync();

                        lock (lockObj) { successCount++; }
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj) { failureCount++; }
                        Debug.WriteLine($"Client {clientIndex} failed: {ex.Message}");
                    }
                }));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Cleanup
            foreach (var client in clients)
            {
                try { client.Dispose(); } catch { }
            }

            // Assert
            Assert.True(successCount >= concurrentClients * 0.95, 
                $"Expected at least 95% success rate, got {successCount}/{concurrentClients}");
            Assert.True(failureCount <= concurrentClients * 0.05, 
                $"Failure count {failureCount} exceeds 5% threshold");

            // Performance metrics
            var avgResponseTime = _responseTimes.Count > 0 
                ? TimeSpan.FromMilliseconds(_responseTimes.Average(t => t.TotalMilliseconds)) 
                : TimeSpan.Zero;
            Debug.WriteLine($"Concurrent Connections Test: {successCount}/{concurrentClients} succeeded");
            Debug.WriteLine($"Total time: {stopwatch.Elapsed.TotalSeconds:F2}s");
            Debug.WriteLine($"Average response time: {avgResponseTime.TotalMilliseconds:F2}ms");
            Debug.WriteLine($"Operations per second: {concurrentClients * operationsPerClient / stopwatch.Elapsed.TotalSeconds:F0}");
        }

        /// <summary>
        /// Stress test: High throughput with many rapid operations.
        /// Target: 1000+ operations per second
        /// </summary>
        [Fact(Skip = "Stress test - run manually")]
        public async Task StressTest_HighThroughput_ShouldProcessManyOperations()
        {
            // Arrange
            const int totalOperations = 1000;
            const int concurrentClients = 10;
            var port = GetNextPort();
            _server = await CreateTestServer(port, concurrentClients + 10);

            var clients = new AdvGenNoSqlClient[concurrentClients];
            for (int i = 0; i < concurrentClients; i++)
            {
                clients[i] = new AdvGenNoSqlClient($"127.0.0.1:{port}");
                _clients.Add(clients[i]);
                await clients[i].ConnectAsync();
            }

            var successCount = 0;
            var failureCount = 0;
            var lockObj = new object();

            // Act
            var stopwatch = Stopwatch.StartNew();
            var tasks = new List<Task>();

            for (int i = 0; i < totalOperations; i++)
            {
                var clientIndex = i % concurrentClients;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var opStopwatch = Stopwatch.StartNew();
                        await clients[clientIndex].ExecuteCommandAsync("TEST", "items", new { data = Guid.NewGuid().ToString() });
                        opStopwatch.Stop();
                        _responseTimes.Add(opStopwatch.Elapsed);
                        lock (lockObj) { successCount++; }
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj) { failureCount++; }
                        Debug.WriteLine($"Operation failed: {ex.Message}");
                    }
                }));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var throughput = totalOperations / stopwatch.Elapsed.TotalSeconds;
            Assert.True(throughput >= 100, $"Throughput {throughput:F0} ops/sec below minimum 100 ops/sec");
            Assert.True(successCount >= totalOperations * 0.98, 
                $"Success rate {(double)successCount/totalOperations:P} below 98%");

            Debug.WriteLine($"High Throughput Test: {successCount}/{totalOperations} succeeded");
            Debug.WriteLine($"Total time: {stopwatch.Elapsed.TotalSeconds:F2}s");
            Debug.WriteLine($"Throughput: {throughput:F0} ops/sec");
        }

        /// <summary>
        /// Stress test: Connection storm - rapid connect/disconnect cycles.
        /// Validates connection pool and socket handling.
        /// </summary>
        [Fact(Skip = "Stress test - run manually")]
        public async Task StressTest_ConnectionStorm_ShouldHandleRapidConnections()
        {
            // Arrange
            const int cycles = 50;
            var port = GetNextPort();
            _server = await CreateTestServer(port, 100);

            var successCount = 0;
            var failureCount = 0;
            var lockObj = new object();

            // Act
            var stopwatch = Stopwatch.StartNew();

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
                try
                {
                    await client.ConnectAsync();
                    await client.PingAsync();
                    await client.DisconnectAsync();
                    lock (lockObj) { successCount++; }
                }
                catch (Exception ex)
                {
                    lock (lockObj) { failureCount++; }
                    Debug.WriteLine($"Cycle {cycle} failed: {ex.Message}");
                }
                finally
                {
                    client.Dispose();
                }

                // Small delay to prevent overwhelming the system
                if (cycle % 10 == 0)
                {
                    await Task.Delay(10);
                }
            }

            stopwatch.Stop();

            // Assert
            Assert.True(successCount >= cycles * 0.95, 
                $"Expected at least 95% success rate, got {successCount}/{cycles}");

            Debug.WriteLine($"Connection Storm Test: {successCount}/{cycles} succeeded");
            Debug.WriteLine($"Total time: {stopwatch.Elapsed.TotalSeconds:F2}s");
            Debug.WriteLine($"Average cycle time: {stopwatch.Elapsed.TotalMilliseconds / cycles:F2}ms");
        }

        /// <summary>
        /// Stress test: Sustained load over a period of time.
        /// Validates stability and absence of memory leaks.
        /// </summary>
        [Fact(Skip = "Stress test - run manually")]
        public async Task StressTest_SustainedLoad_ShouldRemainStable()
        {
            // Arrange
            const int durationSeconds = 10;
            const int operationsPerSecond = 50;
            var port = GetNextPort();
            _server = await CreateTestServer(port, 50);

            var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
            _clients.Add(client);
            await client.ConnectAsync();

            var successCount = 0;
            var failureCount = 0;
            var lockObj = new object();
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));

            // Act
            var stopwatch = Stopwatch.StartNew();
            var tasks = new List<Task>();

            try
            {
                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    for (int i = 0; i < operationsPerSecond; i++)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                await client.PingAsync();
                                lock (lockObj) { successCount++; }
                            }
                            catch (Exception ex)
                            {
                                lock (lockObj) { failureCount++; }
                                Debug.WriteLine($"Operation failed: {ex.Message}");
                            }
                        }));
                    }

                    await Task.Delay(1000, cancellationTokenSource.Token); // 1 second delay between batches
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when timeout is reached
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalOperations = successCount + failureCount;
            var successRate = totalOperations > 0 ? (double)successCount / totalOperations : 0;
            Assert.True(successRate >= 0.98, $"Success rate {successRate:P} below 98% during sustained load");

            Debug.WriteLine($"Sustained Load Test: {successCount}/{totalOperations} succeeded");
            Debug.WriteLine($"Duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
            Debug.WriteLine($"Average throughput: {totalOperations / stopwatch.Elapsed.TotalSeconds:F0} ops/sec");
        }

        /// <summary>
        /// Quick smoke test that runs with the normal test suite to verify stress test infrastructure.
        /// </summary>
        [Fact]
        public async Task StressTest_SmokeTest_ShouldPassQuickValidation()
        {
            // Arrange
            const int concurrentClients = 5;
            const int operationsPerClient = 5;
            var port = GetNextPort();
            _server = await CreateTestServer(port, concurrentClients + 10);

            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < concurrentClients; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
                    try
                    {
                        await client.ConnectAsync();
                        for (int op = 0; op < operationsPerClient; op++)
                        {
                            await client.PingAsync();
                        }
                        await client.DisconnectAsync();
                    }
                    finally
                    {
                        client.Dispose();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - All operations should succeed
            Assert.True(tasks.All(t => t.IsCompletedSuccessfully), "All concurrent operations should complete successfully");
        }
    }
}
