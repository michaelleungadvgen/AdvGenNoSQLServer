// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace AdvGenNoSqlServer.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("=== AdvGenNoSQL Server Performance Benchmarks ===");
        Console.WriteLine();
        Console.WriteLine("Available benchmark suites:");
        Console.WriteLine("  1. DocumentStoreBenchmarks - CRUD operation performance");
        Console.WriteLine("  2. QueryEngineBenchmarks - Query parsing and execution performance");
        Console.WriteLine("  3. BTreeIndexBenchmarks - Index operation performance");
        Console.WriteLine("  4. CacheBenchmarks - Cache hit/miss and eviction performance");
        Console.WriteLine("  5. SerializationBenchmarks - JSON and message serialization performance");
        Console.WriteLine("  6. All - Run all benchmarks");
        Console.WriteLine();

        var config = DefaultConfig.Instance
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run -- [benchmark-name|all]");
            Console.WriteLine("Example: dotnet run -- DocumentStore");
            Console.WriteLine("Example: dotnet run -- all");
            Console.WriteLine();
            Console.WriteLine("Running all benchmarks by default...");
            Console.WriteLine();
            BenchmarkRunner.Run(typeof(Program).Assembly, config);
        }
        else
        {
            string benchmarkName = args[0].ToLowerInvariant();
            
            switch (benchmarkName)
            {
                case "documentstore":
                case "1":
                    BenchmarkRunner.Run<DocumentStoreBenchmarks>(config);
                    break;
                    
                case "queryengine":
                case "2":
                    BenchmarkRunner.Run<QueryEngineBenchmarks>(config);
                    break;
                    
                case "btreeindex":
                case "index":
                case "3":
                    BenchmarkRunner.Run<BTreeIndexBenchmarks>(config);
                    break;
                    
                case "cache":
                case "4":
                    BenchmarkRunner.Run<CacheBenchmarks>(config);
                    break;
                    
                case "serialization":
                case "5":
                    BenchmarkRunner.Run<SerializationBenchmarks>(config);
                    break;
                    
                case "all":
                case "6":
                    BenchmarkRunner.Run(typeof(Program).Assembly, config);
                    break;
                    
                default:
                    Console.WriteLine($"Unknown benchmark: {args[0]}");
                    Console.WriteLine("Available options: DocumentStore, QueryEngine, BTreeIndex, Cache, Serialization, All");
                    break;
            }
        }
    }
}
