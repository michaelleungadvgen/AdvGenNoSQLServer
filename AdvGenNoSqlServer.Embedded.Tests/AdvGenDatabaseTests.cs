// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Embedded;
using AdvGenNoSqlServer.Query.Models;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class AdvGenDatabaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"));

    public AdvGenDatabaseTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string DbPath() => Path.Combine(_dir, "facade.agdb");

    [Fact]
    public void Memory_Works_PersistsNothing()
    {
        using var db = new AdvGenDatabase(":memory:");
        var col = db.GetCollection("items");
        col.InsertAsync(new Document { Id = "a", Data = new() { ["x"] = 1L } }).GetAwaiter().GetResult();
        Assert.Equal(1, col.CountAsync().GetAwaiter().GetResult());
    }

    [Fact]
    public void DoubleOpen_SamePath_Throws()
    {
        var path = DbPath();
        using var first = new AdvGenDatabase(path);
        Assert.Throws<EmbeddedDatabaseLockedException>(() => new AdvGenDatabase(path));
    }

    [Fact]
    public void GetCollection_SameInstancePerName()
    {
        using var db = new AdvGenDatabase(":memory:");
        Assert.Same(db.GetCollection("a"), db.GetCollection("a"));
        Assert.NotSame(db.GetCollection("a"), db.GetCollection("b"));
    }

    [Fact]
    public void Dispose_Checkpoints_WalEmpty()
    {
        var path = DbPath();
        using (var db = new AdvGenDatabase(path))
        {
            var col = db.GetCollection("items");
            for (int i = 0; i < 20; i++)
                col.InsertAsync(new Document { Id = $"i{i}", Data = new() { ["x"] = (long)i } }).GetAwaiter().GetResult();
        }
        var walPath = path + ".wal";
        Assert.True(!File.Exists(walPath) || new FileInfo(walPath).Length == 0);
    }

    [Fact]
    public async Task EndToEnd_Reopen_DataPresent()
    {
        var path = DbPath();
        using (var db = new AdvGenDatabase(path))
        {
            var col = db.GetCollection("items");
            await col.EnsureIndexAsync("category");
            for (int i = 0; i < 100; i++)
                await col.InsertAsync(new Document
                {
                    Id = $"i-{i:D3}",
                    Data = new() { ["price"] = (long)i, ["category"] = (i % 2 == 0) ? "even" : "odd" }
                });
        }

        using (var db = new AdvGenDatabase(path))
        {
            var col = db.GetCollection("items");
            Assert.Equal(100, await col.CountAsync());
            var evens = await col.FindAsync(QueryFilter.Eq("category", "even"));
            Assert.Equal(50, evens.Count);
            Assert.Contains("items", db.GetCollectionNames());
        }
    }

    [Fact]
    public void DropCollection_Works()
    {
        using var db = new AdvGenDatabase(":memory:");
        var col = db.GetCollection("temp");
        col.InsertAsync(new Document { Id = "a", Data = new() }).GetAwaiter().GetResult();
        Assert.True(db.DropCollection("temp"));
        Assert.DoesNotContain("temp", db.GetCollectionNames());
    }
}
