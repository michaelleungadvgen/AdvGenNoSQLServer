// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Query.Parsing;
using AdvGenNoSqlServer.Storage;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace AdvGenNoSqlServer.Benchmarks;

[SimpleJob(RuntimeMoniker.Net90, launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
[RankColumn]
public class QueryEngineBenchmarks
{
    private DocumentStore _documentStore = null!;
    private QueryExecutor _queryExecutor = null!;
    private QueryParser _queryParser = null!;
    private FilterEngine _filterEngine = null!;
    private const string CollectionName = "query_benchmark";

    [Params(100, 1000, 10000)]
    public int DocumentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _documentStore = new DocumentStore();
        _queryParser = new QueryParser();
        _filterEngine = new FilterEngine();
        _queryExecutor = new QueryExecutor(_documentStore, _filterEngine, null);

        // Pre-populate with test data
        var random = new Random(42);
        for (int i = 0; i < DocumentCount; i++)
        {
            var doc = new Document
            {
                Id = $"doc_{i}",
                Data = new Dictionary<string, object>
                {
                    ["name"] = $"User {i}",
                    ["email"] = $"user{i}@example.com",
                    ["age"] = random.Next(18, 80),
                    ["score"] = random.NextDouble() * 100,
                    ["active"] = i % 3 == 0,
                    ["category"] = $"Category_{i % 10}"
                }
            };
            _documentStore.InsertAsync(CollectionName, doc).Wait();
        }
    }

    [Benchmark]
    public void ParseSimpleQuery()
    {
        var json = @"{ ""collection"": ""users"", ""filter"": { ""age"": { ""$gte"": 18 } } }";
        _queryParser.Parse(json);
    }

    [Benchmark]
    public void ParseComplexQuery()
    {
        var json = @"{
            ""collection"": ""users"",
            ""filter"": {
                ""$and"": [
                    { ""age"": { ""$gte"": 18, ""$lte"": 65 } },
                    { ""active"": true },
                    { ""category"": { ""$in"": [""Category_1"", ""Category_2""] } }
                ]
            },
            ""sort"": [{ ""field"": ""age"", ""direction"": ""desc"" }],
            ""options"": { ""skip"": 0, ""limit"": 100 }
        }";
        _queryParser.Parse(json);
    }

    [Benchmark]
    public async Task ExecuteSimpleFilter()
    {
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = CollectionName,
            Filter = QueryFilter.Eq("active", true)
        };
        await _queryExecutor.ExecuteAsync(query);
    }

    [Benchmark]
    public async Task ExecuteRangeFilter()
    {
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = CollectionName,
            Filter = QueryFilter.Gte("age", 25).And(QueryFilter.Lte("age", 50))
        };
        await _queryExecutor.ExecuteAsync(query);
    }

    [Benchmark]
    public async Task ExecuteWithSorting()
    {
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = CollectionName,
            Sort = new List<SortField>
            {
                SortField.Descending("score")
            },
            Options = new QueryOptions { Limit = 100 }
        };
        await _queryExecutor.ExecuteAsync(query);
    }

    [Benchmark]
    public async Task ExecuteWithPagination()
    {
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = CollectionName,
            Options = new QueryOptions { Skip = 100, Limit = 50 }
        };
        await _queryExecutor.ExecuteAsync(query);
    }

    [Benchmark]
    public void FilterEngineSimpleMatch()
    {
        var doc = new Document 
        { 
            Id = "test", 
            Data = new Dictionary<string, object> { ["age"] = 25, ["active"] = true } 
        };
        var filter = QueryFilter.Eq("active", true);
        _filterEngine.Matches(doc, filter);
    }

    [Benchmark]
    public void FilterEngineComplexMatch()
    {
        var doc = new Document 
        { 
            Id = "test",
            Data = new Dictionary<string, object>
            {
                ["age"] = 30,
                ["active"] = true,
                ["category"] = "Category_1"
            }
        };
        var filter = QueryFilter.Gte("age", 18).And(QueryFilter.Lte("age", 65))
            .And(QueryFilter.In("category", new List<object> { "Category_1", "Category_2" }));
        _filterEngine.Matches(doc, filter);
    }
}
