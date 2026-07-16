// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Embedded;
using LiteDB;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

/// <summary>
/// Ports the shape of AdvGenPriceComparer's AlertRepository to both LiteDB and AdvGenDatabase
/// and asserts identical observable results for the same operation script — proving the typed
/// API is a near-mechanical migration target. The only call-site change is the id type
/// (LiteDB ObjectId → string); see the spec's Migration notes.
/// </summary>
public class MigrationSmokeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"));
    public MigrationSmokeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    // Entity with a plain string Id — works with LiteDB's Id convention AND the embedded mapper.
    public sealed class Alert
    {
        public string Id { get; set; } = "";
        public string ItemId { get; set; } = "";
        public bool IsActive { get; set; }
        public bool IsRead { get; set; }
        public bool IsDismissed { get; set; }
        public long Priority { get; set; }
    }

    private interface IAlertRepo
    {
        void EnsureIndexes();
        void Add(Alert a);
        bool Update(Alert a);
        bool Delete(string id);
        Alert? GetById(string id);
        List<string> ActiveIds();
        int UnreadCount();
        bool MarkAsRead(string id);
        List<string> ByItem(string itemId);
        int TotalCount();
    }

    private sealed class LiteRepo : IAlertRepo, IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<Alert> _col;
        public LiteRepo() { _db = new LiteDatabase(new MemoryStream()); _col = _db.GetCollection<Alert>("alerts"); }
        public void EnsureIndexes() => _col.EnsureIndex(x => x.ItemId);
        public void Add(Alert a) => _col.Insert(a);
        public bool Update(Alert a) => _col.Update(a);
        public bool Delete(string id) => _col.Delete(id);
        public Alert? GetById(string id) => _col.FindById(id);
        public List<string> ActiveIds() => _col.Find(x => x.IsActive && !x.IsDismissed).Select(x => x.Id).OrderBy(x => x).ToList();
        public int UnreadCount() => _col.Count(x => x.IsActive && !x.IsRead && !x.IsDismissed);
        public bool MarkAsRead(string id) { var e = _col.FindById(id); if (e == null) return false; e.IsRead = true; return _col.Update(e); }
        public List<string> ByItem(string itemId) => _col.Find(x => x.ItemId == itemId && x.IsActive).Select(x => x.Id).OrderBy(x => x).ToList();
        public int TotalCount() => _col.Count();
        public void Dispose() => _db.Dispose();
    }

    private sealed class EmbeddedRepo : IAlertRepo, IDisposable
    {
        private readonly AdvGenDatabase _db;
        private readonly AdvGenNoSqlServer.Embedded.Typed.IEmbeddedCollection<Alert> _col;
        public EmbeddedRepo(string path) { _db = new AdvGenDatabase(path); _col = _db.GetCollection<Alert>("alerts"); }
        public void EnsureIndexes() => _col.EnsureIndex(x => x.ItemId);
        public void Add(Alert a) => _col.Insert(a);
        public bool Update(Alert a) => _col.Update(a);
        public bool Delete(string id) => _col.Delete(id);
        public Alert? GetById(string id) => _col.FindById(id);
        public List<string> ActiveIds() => _col.Find(x => x.IsActive && !x.IsDismissed).Select(x => x.Id).OrderBy(x => x).ToList();
        public int UnreadCount() => (int)_col.Count(x => x.IsActive && !x.IsRead && !x.IsDismissed);
        public bool MarkAsRead(string id) { var e = _col.FindById(id); if (e == null) return false; e.IsRead = true; return _col.Update(e); }
        public List<string> ByItem(string itemId) => _col.Find(x => x.ItemId == itemId && x.IsActive).Select(x => x.Id).OrderBy(x => x).ToList();
        public int TotalCount() => (int)_col.Count();
        public void Dispose() => _db.Dispose();
    }

    private static List<object> RunScript(IAlertRepo repo)
    {
        var results = new List<object>();
        repo.EnsureIndexes();

        void Seed(string id, string item, bool active, bool read, bool dismissed, long pri)
            => repo.Add(new Alert { Id = id, ItemId = item, IsActive = active, IsRead = read, IsDismissed = dismissed, Priority = pri });

        Seed("a1", "itemX", true, false, false, 1);
        Seed("a2", "itemX", true, true, false, 2);
        Seed("a3", "itemY", true, false, true, 3);   // dismissed
        Seed("a4", "itemY", false, false, false, 4); // inactive
        Seed("a5", "itemX", true, false, false, 5);

        results.Add(repo.TotalCount());
        results.Add(string.Join(",", repo.ActiveIds()));   // a1,a2,a5
        results.Add(repo.UnreadCount());                    // a1,a5 -> 2
        results.Add(string.Join(",", repo.ByItem("itemX"))); // a1,a2,a5

        results.Add(repo.MarkAsRead("a1"));
        results.Add(repo.UnreadCount());                    // a5 -> 1

        // Update a5 to inactive.
        var a5 = repo.GetById("a5")!;
        a5.IsActive = false;
        results.Add(repo.Update(a5));
        results.Add(string.Join(",", repo.ActiveIds()));    // a1,a2

        results.Add(repo.Delete("a2"));
        results.Add(repo.TotalCount());                     // 4
        results.Add(string.Join(",", repo.ActiveIds()));    // a1
        results.Add(repo.GetById("a2") == null);            // true

        return results;
    }

    [Fact]
    public void EmbeddedResults_MatchLiteDb()
    {
        List<object> lite, embedded;
        using (var l = new LiteRepo()) lite = RunScript(l);
        using (var e = new EmbeddedRepo(Path.Combine(_dir, "alerts.agdb"))) embedded = RunScript(e);

        Assert.Equal(lite.Count, embedded.Count);
        for (int i = 0; i < lite.Count; i++)
            Assert.Equal(lite[i], embedded[i]);
    }
}
