using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using AdvGenNoSqlServer.Core.Configuration;

namespace AdvGenNoSqlServer.Benchmarks;

[MemoryDiagnoser]
public class ConfigurationManagerBenchmarks
{
    private string _testConfigPath;
    private ConfigurationManager _manager;

    [GlobalSetup]
    public void Setup()
    {
        _testConfigPath = Path.Combine(Path.GetTempPath(), $"test_config_{Guid.NewGuid()}.json");
        File.WriteAllText(_testConfigPath, "{\"Port\": 1234}");

        _manager = new ConfigurationManager(_testConfigPath, enableHotReload: true);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _manager?.Dispose();
        if (File.Exists(_testConfigPath))
        {
            File.Delete(_testConfigPath);
        }
    }

    [Benchmark]
    public async Task HotReloadTriggered()
    {
        var tcs = new TaskCompletionSource<bool>();

        void OnConfigChanged(object sender, ConfigurationChangedEventArgs e)
        {
            tcs.TrySetResult(true);
        }

        _manager.ConfigurationChanged += OnConfigChanged;

        try
        {
            // Trigger the reload
            File.WriteAllText(_testConfigPath, "{\"Port\": 5678}");

            // Wait for it to process
            await Task.WhenAny(tcs.Task, Task.Delay(2000));
        }
        finally
        {
            _manager.ConfigurationChanged -= OnConfigChanged;
        }
    }
}
