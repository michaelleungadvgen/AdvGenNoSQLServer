# AdvGenNoSqlServer.Embedded

A local, in-process, single-file document database in the spirit of LiteDB — built on the
AdvGenNoSQL `Document` model, indexing, and query engine, with **no server, TCP, HTTP, or
authentication**. One `.agdb` file on disk, one `AdvGenDatabase` object in code.

## Quick start

```csharp
using AdvGenNoSqlServer.Embedded;

public class Item
{
    public string Id { get; set; } = "";   // string id (assigned automatically if empty)
    public string Name { get; set; } = "";
    public string Barcode { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
}

using var db = new AdvGenDatabase("prices.agdb");   // or ":memory:"
var items = db.GetCollection<Item>("items");
items.EnsureIndex(x => x.Barcode);

items.Insert(new Item { Name = "Milk 2L", Barcode = "93052001", Price = 2.90m, IsActive = true });

var cheap = items.Find(x => x.Price < 3.0m && x.IsActive);
var one   = items.FindOne(x => x.Barcode == "93052001");
var page  = items.Query().Where(x => x.IsActive).OrderBy(x => x.Price).Skip(0).Limit(20).ToList();
```

## Storage & durability

- **Single file** (`.agdb`), 8 KB slotted pages with CRC32 checksums; large documents spill to
  overflow-page chains.
- **Write-ahead log** (`.agdb.wal`): every write is fsync-committed to the WAL before it is
  acknowledged, then flushed to the main file at a checkpoint (WAL over threshold,
  `Checkpoint()`, or dispose). Crash recovery replays committed WAL frames on open; torn or
  corrupt tails are ignored safely.
- **Indexes** (primary id→address and secondary B-trees) live in memory and are **rebuilt by a
  sequential scan on open**. Index *definitions* persist in the file catalog, so rebuild is
  automatic.
- `CompactAsync()` rewrites the file, reclaiming space from deleted documents (atomic swap).

## Typed API (LiteDB-style)

`GetCollection<T>(name)` returns `IEmbeddedCollection<T>` with `Insert`/`InsertBulk`/`Update`/
`Upsert`/`Delete`/`DeleteMany`/`FindById`/`FindOne`/`Find`/`FindAll`/`Count`/`EnsureIndex`/
`Query()`, plus `...Async` variants. Ids come from a public `string Id` property (or one marked
`[EmbeddedId]`).

### Supported query subset (translated to indexed queries)

| Construct | Example |
|---|---|
| Comparisons | `x.Price < 3m`, `x.Age >= min`, `x.Name == "a"`, `x.Qty != 0` |
| Boolean logic | `a && b`, `a || b` |
| Bool members | `x.IsActive`, `!x.IsActive` |
| Membership | `list.Contains(x.Category)` |

Anything outside this subset (string `Contains`/`StartsWith`/`EndsWith`, method calls,
arithmetic) is evaluated **in memory over deserialized candidates** — results are always
correct; only performance differs. `db.Diagnostics.FallbackQueryCount` surfaces hot fallbacks.

## Limitations (v1)

- **Single process** — one handle per file (exclusive lock; a second open throws
  `EmbeddedDatabaseLockedException`).
- **In-memory indexes** — rebuilt on open; very large files pay a cold-open scan.
- **No multi-operation transactions** — each write is its own atomic, durable unit.
- No replication, change streams, full-text/geospatial, or field encryption in the embedded
  build (available in the full server).

## Migrating from LiteDB

Near-mechanical — the typed surface mirrors LiteDB. The one required change is ids:
`[BsonId] ObjectId Id` → plain `string Id`, and `Insert` returns the `string` id directly. See
`docs/superpowers/specs/2026-07-15-embedded-nosql-design.md` → *Migration notes*.
