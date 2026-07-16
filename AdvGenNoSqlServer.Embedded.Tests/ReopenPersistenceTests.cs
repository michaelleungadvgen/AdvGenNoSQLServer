// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Embedded;
using AdvGenNoSqlServer.Embedded.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class ReopenPersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"));

    public ReopenPersistenceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public async Task Insert100_Reopen_AllPresent()
    {
        var path = Path.Combine(_dir, "persist.agdb");

        using (var store = new EmbeddedDocumentStore(new FilePageStore(path)))
        {
            await store.InitializeAsync();
            for (int i = 0; i < 100; i++)
                await store.InsertAsync("items", new Document
                {
                    Id = $"item-{i:D3}",
                    Data = new() { ["index"] = (long)i, ["name"] = $"name-{i}" }
                });
        }

        using (var reopened = new EmbeddedDocumentStore(new FilePageStore(path)))
        {
            await reopened.InitializeAsync();
            Assert.Equal(100, await reopened.CountAsync("items"));
            var d = await reopened.GetAsync("items", "item-042");
            Assert.NotNull(d);
            Assert.Equal(42L, d!.Data!["index"]);
            Assert.Equal("name-42", d.Data["name"]);
        }
    }
}
