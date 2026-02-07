// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace AdvGenNoSqlServer.Benchmarks;

[SimpleJob(RuntimeMoniker.Net90, launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
[RankColumn]
public class DocumentStoreBenchmarks
{
    private DocumentStore _documentStore = null!;
    private readonly List<string> _documentIds = new();
    private const string CollectionName = "benchmark_collection";

    [Params(100, 1000)]
    public int DocumentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _documentStore = new DocumentStore();

        // Pre-populate with documents
        for (int i = 0; i < DocumentCount; i++)
        {
            var doc = new Document
            {
                Id = $"doc_{i}",
                Data = new Dictionary<string, object>
                {
                    ["name"] = $"User {i}",
                    ["email"] = $"user{i}@example.com",
                    ["index"] = i
                }
            };
            _documentStore.InsertAsync(CollectionName, doc).Wait();
            _documentIds.Add(doc.Id);
        }
    }

    [Benchmark]
    public async Task InsertDocument()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid().ToString(),
            Data = new Dictionary<string, object>
            {
                ["name"] = "New User",
                ["email"] = "newuser@example.com",
                ["timestamp"] = DateTime.UtcNow
            }
        };
        await _documentStore.InsertAsync(CollectionName, doc);
    }

    [Benchmark]
    public async Task GetDocument()
    {
        var id = _documentIds[0];
        await _documentStore.GetAsync(CollectionName, id);
    }

    [Benchmark]
    public async Task UpdateDocument()
    {
        var id = _documentIds[1];
        var doc = await _documentStore.GetAsync(CollectionName, id);
        if (doc != null)
        {
            doc.Data ??= new Dictionary<string, object>();
            doc.Data["updated"] = DateTime.UtcNow;
            await _documentStore.UpdateAsync(CollectionName, doc);
        }
    }

    [Benchmark]
    public async Task DeleteDocument()
    {
        // Use a unique collection to avoid affecting other benchmarks
        var collectionName = $"delete_benchmark_{Guid.NewGuid():N}";
        var doc = new Document 
        { 
            Id = "temp-doc",
            Data = new Dictionary<string, object> { ["temp"] = true }
        };
        await _documentStore.InsertAsync(collectionName, doc);
        await _documentStore.DeleteAsync(collectionName, doc.Id);
    }

    [Benchmark]
    public async Task GetAllDocuments()
    {
        var results = await _documentStore.GetAllAsync(CollectionName);
        _ = results.ToList();
    }

    [Benchmark]
    public async Task CountDocuments()
    {
        await _documentStore.CountAsync(CollectionName);
    }
}
