// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Embedded;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class CrashRecoveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"));

    public CrashRecoveryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string DbPath() => Path.Combine(_dir, "crash.agdb");

    private static Document Doc(string id, long v) =>
        new() { Id = id, Data = new() { ["v"] = v } };

    [Fact]
    public async Task Crash_NoCheckpoint_WalReplayRestoresAll()
    {
        var path = DbPath();
        var store = EmbeddedDocumentStore.OpenFile(path, walCheckpointBytes: long.MaxValue);
        for (int i = 0; i < 50; i++)
            await store.InsertAsync("items", Doc($"i-{i:D2}", i));
        store.SimulateCrash(); // no checkpoint — data lives only in the WAL

        using var reopened = EmbeddedDocumentStore.OpenFile(path);
        Assert.Equal(50, await reopened.CountAsync("items"));
        Assert.Equal(42L, (await reopened.GetAsync("items", "i-42"))!.Data!["v"]);
    }

    [Fact]
    public async Task Crash_TornWalTail_RecoversCompleteBatches()
    {
        var path = DbPath();
        var store = EmbeddedDocumentStore.OpenFile(path, walCheckpointBytes: long.MaxValue);
        for (int i = 0; i < 10; i++)
            await store.InsertAsync("items", Doc($"i-{i:D2}", i));
        store.SimulateCrash();

        // Truncate the WAL mid-frame.
        var walPath = path + ".wal";
        using (var fs = new FileStream(walPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            fs.SetLength(fs.Length - 20);
        }

        using var reopened = EmbeddedDocumentStore.OpenFile(path);
        long count = await reopened.CountAsync("items");
        // At least the earlier complete batches survive; no exception thrown.
        Assert.True(count >= 9 && count <= 10, $"count={count}");
    }

    [Fact]
    public async Task CheckpointThreshold_TruncatesWal_DataInMainFile()
    {
        var path = DbPath();
        // Small threshold so a checkpoint fires quickly.
        using (var store = EmbeddedDocumentStore.OpenFile(path, walCheckpointBytes: 64 * 1024))
        {
            for (int i = 0; i < 40; i++)
                await store.InsertAsync("items", Doc($"i-{i:D2}", i));
            // A checkpoint should have fired (WAL back to 0 at least once).
        }

        // Delete the WAL entirely — data must already be in the main file from checkpoint(s)/dispose.
        var walPath = path + ".wal";
        if (File.Exists(walPath)) File.Delete(walPath);

        using var reopened = EmbeddedDocumentStore.OpenFile(path);
        Assert.Equal(40, await reopened.CountAsync("items"));
    }

    [Fact]
    public async Task FailedOperation_IsAtomic_NoPartialState()
    {
        var path = DbPath();
        using var store = EmbeddedDocumentStore.OpenFile(path, walCheckpointBytes: long.MaxValue);
        await store.InsertAsync("items", Doc("a", 1));

        store.BeforeCommitHook = () => throw new InvalidOperationException("simulated mid-op failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync("items", Doc("a", 999)));
        store.BeforeCommitHook = null;

        // The update was rolled back — original value intact.
        Assert.Equal(1L, (await store.GetAsync("items", "a"))!.Data!["v"]);
    }
}
