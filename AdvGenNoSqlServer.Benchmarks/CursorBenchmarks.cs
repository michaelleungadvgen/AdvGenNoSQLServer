using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Query.Cursors;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Storage;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace AdvGenNoSqlServer.Benchmarks;

[MemoryDiagnoser]
public class CursorBenchmarks
{
    private DocumentStore _store = null!;
    private FilterEngine _filterEngine = null!;
    private const string CollectionName = "test_cursors";

    [Params(100, 1000)]
    public int DocumentCount { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _store = new DocumentStore();
        _filterEngine = new FilterEngine();

        for (int i = 0; i < DocumentCount; i++)
        {
            await _store.InsertAsync(CollectionName, new Document { Id = $"doc_{i}", Data = new Dictionary<string, object> { ["value"] = i } });
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<int> FetchAllUsingCursor()
    {
        var manager = new CursorManager(_store, _filterEngine);
        var result = await manager.CreateCursorAsync(
            CollectionName,
            null,
            null,
            new AdvGenNoSqlServer.Query.Cursors.CursorOptions { BatchSize = 10 },
            CancellationToken.None);

        int count = result.Documents.Count;
        var hasMore = result.HasMore;
        var cursorId = result.Cursor?.CursorId;

        while (hasMore && cursorId != null)
        {
            var batchResult = await manager.GetMoreAsync(cursorId, 10, CancellationToken.None);
            count += batchResult.Documents.Count;
            hasMore = batchResult.HasMore;
        }

        return count;
    }
}
