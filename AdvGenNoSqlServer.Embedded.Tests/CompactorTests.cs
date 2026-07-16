// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Embedded;
using AdvGenNoSqlServer.Query.Models;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class CompactorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"));

    public CompactorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string DbPath() => Path.Combine(_dir, "compact.agdb");

    [Fact]
    public async Task Compact_ShrinksFile_KeepsSurvivors_IndexesWork()
    {
        var path = DbPath();
        using var db = new AdvGenDatabase(path);
        var col = db.GetCollection("items");
        await col.EnsureIndexAsync("bucket");
        for (int i = 0; i < 1000; i++)
            await col.InsertAsync(new Document { Id = $"i-{i:D4}", Data = new() { ["bucket"] = $"b{i % 10}", ["n"] = (long)i } });

        // Delete 900 (keep i-0900..i-0999).
        for (int i = 0; i < 900; i++)
            await col.DeleteAsync($"i-{i:D4}");

        db.Checkpoint();
        long sizeBefore = new FileInfo(path).Length;

        await db.CompactAsync();

        long sizeAfter = new FileInfo(path).Length;
        Assert.True(sizeAfter <= sizeBefore / 2, $"before={sizeBefore} after={sizeAfter}");

        var col2 = db.GetCollection("items");
        Assert.Equal(100, await col2.CountAsync());
        Assert.NotNull(await col2.FindByIdAsync("i-0950"));
        Assert.Null(await col2.FindByIdAsync("i-0001"));

        // Index still works after compaction.
        var b5 = await col2.FindAsync(QueryFilter.Eq("bucket", "b5"));
        Assert.Equal(10, b5.Count); // i-0905, i-0915, ... i-0995
    }

    [Fact]
    public async Task Compact_InterruptedBeforeSwap_LeavesOriginalValid()
    {
        var path = DbPath();
        var db = new AdvGenDatabase(path);
        var col = db.GetCollection("items");
        for (int i = 0; i < 50; i++)
            await col.InsertAsync(new Document { Id = $"i{i}", Data = new() { ["n"] = (long)i } });

        db.BeforeCompactSwapHook = () => throw new InvalidOperationException("simulated crash before swap");
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.CompactAsync());
        db.Dispose();

        // The original file is intact and valid — reopen and verify all data present.
        using var reopened = new AdvGenDatabase(path);
        Assert.Equal(50, await reopened.GetCollection("items").CountAsync());
    }
}
