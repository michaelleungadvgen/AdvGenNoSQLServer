// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using Xunit;
using Xunit.Abstractions;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Load testing suite for validating server performance under sustained concurrent load.
    /// These tests measure throughput, latency, and stability with high numbers of concurrent clients.
    /// </summary>
    public class LoadTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly ServerConfiguration _testConfig;
        private TcpServer? _server;
        private readonly List<string> _testDataPaths;
        private readonly ConcurrentDictionary<string, List<double>> _latencyMetrics;
        private readonly ConcurrentDictionary<string, long> _operationCounters;

        public LoadTests(ITestOutputHelper output)
        {
            _output = output;
            _testConfig = new ServerConfiguration
            {
                Host = "127.0.0.1",
                Port = 19091, // Use different port to avoid conflicts
                MaxConcurrentConnections = 1000,
                ConnectionTimeout = TimeSpan.FromSeconds(30),
                KeepAliveInterval = TimeSpan.FromSeconds(60),
                ReceiveBufferSize = 65536,
                SendBufferSize = 65536
            };
            _testDataPaths = new List<string>();
            _latencyMetrics = new ConcurrentDictionary<string, List<double>>();
            _operationCounters = new ConcurrentDictionary<string, long>();
        }

        public void Dispose()
        {
            _server?.StopAsync().Wait(TimeSpan.FromSeconds(5));
            _server?.Dispose();
            
            // Cleanup test data directories
            foreach (var path in _testDataPaths)
            {
                try
                {
                    if (System.IO.Directory.Exists(path))
                    {
                        System.IO.Directory.Delete(path, true);
                    }
                }
                catch { }
            }
        }

        private async Task StartTestServerAsync()
        {
            _server = new TcpServer(_testConfig);
            _server.ConnectionEstablished += (s, e) => 
            {
                _output.WriteLine($"Client connected: {e.ConnectionId}");
            };
            _server.ConnectionClosed += (s, e) => 
            {
                _output.WriteLine($"Client disconnected: {e.ConnectionId}");
            };
            
            // Echo server - respond to messages
            _server.MessageReceived += async (s, e) =>
            {
                switch (e.Message.MessageType)
                {
                    case MessageType.Handshake:
                        await e.SendResponseAsync(NoSqlMessage.Create(MessageType.Response, "{\"success\":true}"));
                        break;
                    case MessageType.Ping:
                        await e.SendResponseAsync(new NoSqlMessage
                        {
                            MessageType = MessageType.Pong,
                            Payload = Array.Empty<byte>(),
                            PayloadLength = 0
                        });
                        break;
                    case MessageType.Command:
                        await e.SendResponseAsync(NoSqlMessage.CreateSuccess());
                        break;
                    default:
                        await e.SendResponseAsync(NoSqlMessage.CreateSuccess());
                        break;
                }
            };
            
            await _server.StartAsync(CancellationToken.None);
            await Task.Delay(100); // Give server time to start
        }

        private void RecordLatency(string operation, double latencyMs)
        {
            var list = _latencyMetrics.GetOrAdd(operation, _ => new List<double>());
            lock (list)
            {
                list.Add(latencyMs);
            }
        }

        private void RecordOperation(string operation)
        {
            _operationCounters.AddOrUpdate(operation, 1, (_, count) => count + 1);
        }

        private void PrintStatistics(string testName, TimeSpan duration)
        {
            _output.WriteLine($"\n=== {testName} Results ===");
            _output.WriteLine($"Duration: {duration.TotalSeconds:F2}s");
            
            foreach (var kvp in _operationCounters)
            {
                var opsPerSec = kvp.Value / duration.TotalSeconds;
                _output.WriteLine($"Operations '{kvp.Key}': {kvp.Value} total, {opsPerSec:F2} ops/sec");
            }

            foreach (var kvp in _latencyMetrics)
            {
                var latencies = kvp.Value;
                if (latencies.Count == 0) continue;
                
                lock (latencies)
                {
                    var sorted = latencies.OrderBy(x => x).ToList();
                    var p50 = sorted[(int)(sorted.Count * 0.5)];
                    var p95 = sorted[(int)(sorted.Count * 0.95)];
                    var p99 = sorted[(int)(sorted.Count * 0.99)];
                    var avg = sorted.Average();
                    var min = sorted.Min();
                    var max = sorted.Max();

                    _output.WriteLine($"\nLatency '{kvp.Key}':");
                    _output.WriteLine($"  Count: {sorted.Count}");
                    _output.WriteLine($"  Min: {min:F2}ms");
                    _output.WriteLine($"  Avg: {avg:F2}ms");
                    _output.WriteLine($"  P50: {p50:F2}ms");
                    _output.WriteLine($"  P95: {p95:F2}ms");
                    _output.WriteLine($"  P99: {p99:F2}ms");
                    _output.WriteLine($"  Max: {max:F2}ms");
                }
            }
            _output.WriteLine("================================\n");
        }

        /// <summary>
        /// Quick smoke test to verify load test infrastructure works.
        /// This test runs with the normal test suite.
        /// </summary>
        [Fact]
        public async Task LoadTest_SmokeTest()
        {
            await StartTestServerAsync();
            Assert.True(_server!.IsRunning, "Server should be running");

            var client = new AdvGenNoSqlClient($"127.0.0.1:{_testConfig.Port}");
            await client.ConnectAsync();
            Assert.True(client.IsConnected, "Client should be connected");

            var pong = await client.PingAsync();
            Assert.True(pong, "Ping should succeed");

            await client.DisconnectAsync();
            Assert.False(client.IsConnected, "Client should be disconnected");
        }

        /// <summary>
        /// Tests server with 100 concurrent clients performing operations.
        /// Validates that the server can handle moderate concurrent load.
        /// </summary>
        [Fact(Skip = "Load test - run manually with: dotnet test --filter FullyQualifiedName~LoadTests --no-skip")]
        public async Task LoadTest_100ConcurrentClients_Sustained()
        {
            await StartTestServerAsync();
            const int clientCount = 100;
            const int operationsPerClient = 100;
            const int durationSeconds = 30;
            
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
            var stopwatch = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, clientCount).Select(async clientId =>
            {
                var client = new AdvGenNoSqlClient($"127.0.0.1:{_testConfig.Port}");
                await client.ConnectAsync();

                try
                {
                    int operationCount = 0;
                    while (!cts.Token.IsCancellationRequested && operationCount < operationsPerClient)
                    {
                        var sw = Stopwatch.StartNew();
                        
                        var pong = await client.PingAsync();
                        if (pong)
                        {
                            RecordOperation("Ping");
                            RecordLatency("Ping", sw.Elapsed.TotalMilliseconds);
                        }

                        operationCount++;
                        
                        // Small delay to simulate realistic operation timing
                        if (operationCount % 10 == 0)
                        {
                            await Task.Delay(10, cts.Token);
                        }
                    }
                }
                finally
                {
                    await client.DisconnectAsync();
                }
            }).ToArray();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Expected when timeout is reached
            }

            stopwatch.Stop();
            PrintStatistics($"100 Concurrent Clients ({durationSeconds}s)", stopwatch.Elapsed);

            // Assert minimum throughput
            var totalPings = _operationCounters.GetValueOrDefault("Ping");
            var pingsPerSec = totalPings / stopwatch.Elapsed.TotalSeconds;
            _output.WriteLine($"Ping throughput: {pingsPerSec:F2}/sec");
            
            Assert.True(pingsPerSec > 50, $"Ping throughput should exceed 50/sec, got {pingsPerSec:F2}");
        }

        /// <summary>
        /// Tests server with high burst of concurrent connections (ramp-up test).
        /// Validates connection handling capacity.
        /// </summary>
        [Fact(Skip = "Load test - run manually with: dotnet test --filter FullyQualifiedName~LoadTests --no-skip")]
        public async Task LoadTest_HighBurstConnections()
        {
            await StartTestServerAsync();
            const int burstSize = 200;
            const int rampUpMs = 5000; // Ramp up over 5 seconds
            
            var stopwatch = Stopwatch.StartNew();
            var connectionTimes = new ConcurrentBag<(int clientId, double connectMs)>();

            // Ramp up connections gradually
            var tasks = Enumerable.Range(0, burstSize).Select(async clientId =>
            {
                // Stagger connections over rampUpMs
                var delayMs = (clientId * rampUpMs) / burstSize;
                await Task.Delay(delayMs);

                var sw = Stopwatch.StartNew();
                var client = new AdvGenNoSqlClient($"127.0.0.1:{_testConfig.Port}");
                
                try
                {
                    await client.ConnectAsync();
                    sw.Stop();
                    connectionTimes.Add((clientId, sw.Elapsed.TotalMilliseconds));
                    RecordOperation("Connection");
                    RecordLatency("Connection", sw.Elapsed.TotalMilliseconds);
                    
                    // Keep connection open briefly
                    await Task.Delay(100);
                    await client.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Client {clientId} failed: {ex.Message}");
                    RecordOperation("ConnectionFailed");
                }
            }).ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            PrintStatistics($"High Burst Connections ({burstSize})", stopwatch.Elapsed);

            // Assert connection success rate
            var successfulConnections = _operationCounters.GetValueOrDefault("Connection");
            var failedConnections = _operationCounters.GetValueOrDefault("ConnectionFailed");
            var successRate = (double)successfulConnections / burstSize;

            _output.WriteLine($"Connection success rate: {successRate:P2} ({successfulConnections}/{burstSize})");
            Assert.True(successRate > 0.95, $"Connection success rate should be >95%, got {successRate:P2}");

            // Assert connection latency
            if (_latencyMetrics.TryGetValue("Connection", out var latencies) && latencies.Count > 0)
            {
                var avgLatency = latencies.Average();
                _output.WriteLine($"Average connection latency: {avgLatency:F2}ms");
                Assert.True(avgLatency < 500, $"Average connection latency should be <500ms, got {avgLatency:F2}ms");
            }
        }

        /// <summary>
        /// Tests sustained high throughput over an extended period.
        /// Validates server stability under continuous load.
        /// </summary>
        [Fact(Skip = "Load test - run manually with: dotnet test --filter FullyQualifiedName~LoadTests --no-skip")]
        public async Task LoadTest_SustainedThroughput_60Seconds()
        {
            await StartTestServerAsync();
            const int clientCount = 50;
            const int durationSeconds = 60;
            
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
            var stopwatch = Stopwatch.StartNew();
            var intervalMetrics = new ConcurrentDictionary<int, long>(); // Second -> Operation count

            var tasks = Enumerable.Range(0, clientCount).Select(async clientId =>
            {
                var client = new AdvGenNoSqlClient($"127.0.0.1:{_testConfig.Port}");
                await client.ConnectAsync();

                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var sw = Stopwatch.StartNew();
                        var pong = await client.PingAsync();
                        sw.Stop();

                        if (pong)
                        {
                            RecordOperation("Ping");
                            RecordLatency("Ping", sw.Elapsed.TotalMilliseconds);
                            
                            var currentSecond = (int)(stopwatch.Elapsed.TotalSeconds);
                            intervalMetrics.AddOrUpdate(currentSecond, 1, (_, count) => count + 1);
                        }

                        // Small delay between operations
                        await Task.Delay(5, cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
                finally
                {
                    await client.DisconnectAsync();
                }
            }).ToArray();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            stopwatch.Stop();
            PrintStatistics($"Sustained Throughput ({durationSeconds}s)", stopwatch.Elapsed);

            // Calculate throughput consistency
            var intervals = intervalMetrics.OrderBy(x => x.Key).ToList();
            if (intervals.Count > 2)
            {
                // Skip first and last intervals as they may be partial
                var middleIntervals = intervals.Skip(1).Take(intervals.Count - 2).Select(x => (double)x.Value).ToList();
                var avgThroughput = middleIntervals.Average();
                var stdDev = Math.Sqrt(middleIntervals.Average(x => Math.Pow(x - avgThroughput, 2)));
                var coefficientOfVariation = stdDev / avgThroughput;

                _output.WriteLine($"Average throughput: {avgThroughput:F2} ops/sec");
                _output.WriteLine($"Standard deviation: {stdDev:F2}");
                _output.WriteLine($"Coefficient of variation: {coefficientOfVariation:P2}");

                // Assert consistency (CV should be low for stable throughput)
                Assert.True(coefficientOfVariation < 0.5, 
                    $"Throughput should be consistent (CV < 50%), got {coefficientOfVariation:P2}");
            }

            // Assert total operations
            var totalOps = _operationCounters.GetValueOrDefault("Ping");
            var overallThroughput = totalOps / stopwatch.Elapsed.TotalSeconds;
            _output.WriteLine($"Overall throughput: {overallThroughput:F2} ops/sec");
            Assert.True(overallThroughput > 100, $"Overall throughput should exceed 100 ops/sec");
        }

        /// <summary>
        /// Tests mixed workload with different operation types.
        /// Simulates realistic usage patterns.
        /// </summary>
        [Fact(Skip = "Load test - run manually with: dotnet test --filter FullyQualifiedName~LoadTests --no-skip")]
        public async Task LoadTest_MixedWorkload()
        {
            await StartTestServerAsync();
            const int clientCount = 30;
            const int durationSeconds = 30;
            var random = new Random();
            
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
            var stopwatch = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, clientCount).Select(async clientId =>
            {
                var client = new AdvGenNoSqlClient($"127.0.0.1:{_testConfig.Port}");
                await client.ConnectAsync();

                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var operation = random.Next(4); // 0-3 different operations
                        var sw = Stopwatch.StartNew();

                        switch (operation)
                        {
                            case 0:
                                await client.PingAsync();
                                RecordOperation("Ping");
                                RecordLatency("Ping", sw.Elapsed.TotalMilliseconds);
                                break;
                            case 1:
                                // Simulate read operation
                                await Task.Delay(random.Next(5, 15), cts.Token);
                                RecordOperation("Read");
                                RecordLatency("Read", sw.Elapsed.TotalMilliseconds);
                                break;
                            case 2:
                                // Simulate write operation
                                await Task.Delay(random.Next(10, 25), cts.Token);
                                RecordOperation("Write");
                                RecordLatency("Write", sw.Elapsed.TotalMilliseconds);
                                break;
                            case 3:
                                // Simulate query operation
                                await Task.Delay(random.Next(15, 40), cts.Token);
                                RecordOperation("Query");
                                RecordLatency("Query", sw.Elapsed.TotalMilliseconds);
                                break;
                        }

                        await Task.Delay(random.Next(10, 50), cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
                finally
                {
                    await client.DisconnectAsync();
                }
            }).ToArray();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            stopwatch.Stop();
            PrintStatistics($"Mixed Workload ({durationSeconds}s)", stopwatch.Elapsed);

            // Assert all operation types were performed
            Assert.True(_operationCounters.ContainsKey("Ping"), "Should have Ping operations");
            Assert.True(_operationCounters.ContainsKey("Read"), "Should have Read operations");
            Assert.True(_operationCounters.ContainsKey("Write"), "Should have Write operations");
            Assert.True(_operationCounters.ContainsKey("Query"), "Should have Query operations");

            // Assert total throughput
            var totalOps = _operationCounters.Values.Sum();
            var throughput = totalOps / stopwatch.Elapsed.TotalSeconds;
            _output.WriteLine($"Total mixed operations: {totalOps}");
            _output.WriteLine($"Mixed workload throughput: {throughput:F2} ops/sec");
            Assert.True(throughput > 20, $"Mixed workload throughput should exceed 20 ops/sec");
        }

        /// <summary>
        /// Tests graceful degradation under overload conditions.
        /// Validates that server remains responsive when overloaded.
        /// </summary>
        [Fact(Skip = "Load test - run manually with: dotnet test --filter FullyQualifiedName~LoadTests --no-skip")]
        public async Task LoadTest_GracefulDegradation()
        {
            await StartTestServerAsync();
            const int overloadClientCount = 150; // More than typical capacity
            const int durationSeconds = 20;
            
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
            var stopwatch = Stopwatch.StartNew();
            var errorCount = 0;
            var successCount = 0;

            var tasks = Enumerable.Range(0, overloadClientCount).Select(async clientId =>
            {
                try
                {
                    var client = new AdvGenNoSqlClient($"127.0.0.1:{_testConfig.Port}");
                    await client.ConnectAsync();

                    try
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                var sw = Stopwatch.StartNew();
                                var pong = await client.PingAsync();
                                sw.Stop();

                                if (pong)
                                {
                                    Interlocked.Increment(ref successCount);
                                    RecordOperation("Success");
                                    RecordLatency("Success", sw.Elapsed.TotalMilliseconds);
                                }
                                else
                                {
                                    Interlocked.Increment(ref errorCount);
                                    RecordOperation("Error");
                                }
                            }
                            catch
                            {
                                Interlocked.Increment(ref errorCount);
                                RecordOperation("Error");
                            }

                            await Task.Delay(10, cts.Token);
                        }
                    }
                    finally
                    {
                        await client.DisconnectAsync();
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errorCount);
                    _output.WriteLine($"Client {clientId} connection failed: {ex.Message}");
                }
            }).ToArray();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            stopwatch.Stop();

            var totalAttempts = successCount + errorCount;
            var successRate = totalAttempts > 0 ? (double)successCount / totalAttempts : 0;

            _output.WriteLine($"\n=== Graceful Degradation Results ===");
            _output.WriteLine($"Duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
            _output.WriteLine($"Success: {successCount}");
            _output.WriteLine($"Errors: {errorCount}");
            _output.WriteLine($"Success rate: {successRate:P2}");

            // Server should maintain >70% success rate even under overload
            Assert.True(successRate > 0.70, $"Success rate under overload should be >70%, got {successRate:P2}");

            // Some operations should succeed
            Assert.True(successCount > 100, $"Should have significant successful operations, got {successCount}");
        }
    }
}
