// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Indexing;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System.Diagnostics;

namespace AdvGenNoSqlServer.Benchmarks;

[SimpleJob(RuntimeMoniker.Net90, launchCount: 1, warmupCount: 2, iterationCount: 3)]
[MemoryDiagnoser]
public class NPlusOneBenchmarks
{
    private DocumentStore _documentStore = null!;
    private IndexManager _indexManager = null!;
    private QueryExecutor _queryExecutor = null!;
    private FilterEngine _filterEngine = null!;
    private const string CollectionName = "nplusone_benchmark";

    [Params(100, 1000)]
    public int DocumentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _documentStore = new DocumentStore();
        _indexManager = new IndexManager();
        _filterEngine = new FilterEngine();
        _queryExecutor = new QueryExecutor(_documentStore, _filterEngine, _indexManager);

        // Create an index on "category"
        _indexManager.CreateIndex<string>(
            CollectionName,
            "category",
            false,
            doc => (string)doc.Data!["category"]);

        // Pre-populate with test data
        for (int i = 0; i < DocumentCount; i++)
        {
            var doc = new Document
            {
                Id = $"doc_{i}",
                Data = new Dictionary<string, object>
                {
                    ["category"] = "selected",
                    ["value"] = i
                }
            };
            _documentStore.InsertAsync(CollectionName, doc).Wait();
            _indexManager.IndexDocument(CollectionName, doc);
        }
    }

    [Benchmark]
    public async Task<long> CountWithIndex()
    {
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = CollectionName,
            Filter = QueryFilter.Eq("category", "selected")
        };
        return await _queryExecutor.CountAsync(query);
    }

    [Benchmark]
    public async Task<QueryResult> ExecuteWithIndex()
    {
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = CollectionName,
            Filter = QueryFilter.Eq("category", "selected")
        };
        return await _queryExecutor.ExecuteAsync(query);
    }
}
