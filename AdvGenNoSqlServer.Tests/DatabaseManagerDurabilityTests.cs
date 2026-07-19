// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Regression tests for graceful-shutdown durability: pending write-behind operations
/// must survive a server stop (DatabaseManager.DisposeDatabasesAsync).
/// </summary>
public class DatabaseManagerDurabilityTests
{
    [Fact]
    public async Task DisposeDatabases_PendingWrites_ArePersisted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "advgen-durability-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Insert without an explicit flush — the write sits in the write-behind queue
            var manager = new DatabaseManager(dir);
            var db = manager.GetDatabase("default");
            await db.CreateCollectionAsync("items");
            await db.InsertAsync("items", new Document
            {
                Id = "d1",
                Data = new Dictionary<string, object> { ["v"] = "hello" }
            });

            // Simulate graceful shutdown: flush + dispose all stores
            await manager.DisposeDatabasesAsync();

            // Reopen: the document must be on disk
            var manager2 = new DatabaseManager(dir);
            var db2 = manager2.GetDatabase("default");
            var doc = await db2.GetAsync("items", "d1");

            Assert.NotNull(doc);
            Assert.Equal("hello", doc!.Data?["v"]?.ToString());

            await manager2.DisposeDatabasesAsync();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
