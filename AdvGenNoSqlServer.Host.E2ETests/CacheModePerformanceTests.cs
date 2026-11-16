// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using AdvGenNoSqlServer.Client;

namespace AdvGenNoSqlServer.Host.E2ETests;

/// <summary>
/// Cache-mode performance measurement against the real Host over TCP. Reports throughput
/// (ops/sec) and latency percentiles for set/get/delete plus concurrent write throughput.
/// Writes a Markdown results file (path printed to the console) for inclusion in the README.
/// </summary>
[Collection(HostE2ECollection.Name)]
public class CacheModePerformanceTests
{
    private readonly HostE2EFixture _fixture;

    public CacheModePerformanceTests(HostE2EFixture fixture) => _fixture = fixture;

    private sealed record Stats(double OpsPerSec, double AvgMs, double P50Ms, double P95Ms, double P99Ms);

    private static async Task<Stats> MeasureAsync(int count, Func<int, Task> operation)
    {
        var latencies = new List<double>(count);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var start = Stopwatch.GetTimestamp();
            await operation(i);
            latencies.Add((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        }
        sw.Stop();

        latencies.Sort();
        double Pct(double p) => latencies[Math.Min(latencies.Count - 1, (int)(p * latencies.Count))];
        return new Stats(
            count / sw.Elapsed.TotalSeconds,
            latencies.Average(),
            Pct(0.50), Pct(0.95), Pct(0.99));
    }

    [Fact]
    public async Task CacheMode_ThroughputAndLatency()
    {
        await using var client = await _fixture.NewClientAsync();
        var collection = "perf_" + Guid.NewGuid().ToString("N")[..8];
        await client.ExecuteCommandAsync("createcollection", collection);

        // Warmup (JIT / connection / cache warm).
        for (var i = 0; i < 100; i++)
            await client.SetAsync(collection, new { _id = $"warm{i}", n = i });

        var set = await MeasureAsync(1000, i => client.SetAsync(collection, new { _id = $"doc{i}", n = i, payload = "cache-mode-perf" }));
        var get = await MeasureAsync(1000, i => client.GetAsync(collection, $"doc{i % 1000}"));
        var delete = await MeasureAsync(500, i => client.DeleteAsync(collection, $"doc{i}"));

        // Concurrent writes: 4 clients x 500 sets.
        const int clients = 4;
        const int perClient = 500;
        var concurrentClients = new List<AdvGenNoSqlClient>();
        for (var c = 0; c < clients; c++)
            concurrentClients.Add(await _fixture.NewClientAsync());

        var concurrentSw = Stopwatch.StartNew();
        await Task.WhenAll(concurrentClients.Select((cl, c) => Task.Run(async () =>
        {
            for (var i = 0; i < perClient; i++)
                await cl.SetAsync(collection, new { _id = $"conc{c}_{i}", n = i });
        })));
        concurrentSw.Stop();
        var concurrentOpsPerSec = clients * perClient / concurrentSw.Elapsed.TotalSeconds;
        foreach (var cl in concurrentClients)
            await cl.DisposeAsync();

        var report = BuildReport(set, get, delete, concurrentOpsPerSec, clients, perClient);
        var path = Path.Combine(Path.GetTempPath(), "cachemode-perf-results.md");
        await File.WriteAllTextAsync(path, report);
        Console.WriteLine(report);
        Console.WriteLine($"RESULTS_FILE={path}");

        // Sanity: the cache-mode server must have handled real traffic.
        Assert.True(set.OpsPerSec > 0);
        Assert.True(get.OpsPerSec > 0);
    }

    private static string BuildReport(Stats set, Stats get, Stats delete, double concurrentOpsPerSec, int clients, int perClient)
    {
        string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.AppendLine("| Operation | ops/sec | avg (ms) | p50 (ms) | p95 (ms) | p99 (ms) |");
        sb.AppendLine("|-----------|---------|----------|----------|----------|----------|");
        sb.AppendLine($"| SET (1000) | {F(set.OpsPerSec)} | {F(set.AvgMs)} | {F(set.P50Ms)} | {F(set.P95Ms)} | {F(set.P99Ms)} |");
        sb.AppendLine($"| GET (1000) | {F(get.OpsPerSec)} | {F(get.AvgMs)} | {F(get.P50Ms)} | {F(get.P95Ms)} | {F(get.P99Ms)} |");
        sb.AppendLine($"| DELETE (500) | {F(delete.OpsPerSec)} | {F(delete.AvgMs)} | {F(delete.P50Ms)} | {F(delete.P95Ms)} | {F(delete.P99Ms)} |");
        sb.AppendLine();
        sb.AppendLine($"Concurrent writes: **{F(concurrentOpsPerSec)} ops/sec** ({clients} clients x {perClient} sets).");
        return sb.ToString();
    }
}