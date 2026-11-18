# Feature Suggestions

**Date**: 2026-07-20
**Context**: The server side is already very full-featured — replication, Raft clustering,
sharding, change streams, full-text, geospatial, TTL/capped collections, revisions,
map-reduce, field encryption, and import/export. The highest-value new work is closing the
gaps in the **Embedded** library (consumed by AdvGenPriceComparer) and a few genuinely
missing capabilities.

---

## A. Embedded library — close the v1 limitations

Most useful for the price comparer; these come straight from the Embedded README's
limitations list.

1. **Multi-operation transactions** — `db.BeginTrans()` / commit / rollback across several
   writes, riding on the existing WAL. LiteDB parity, and useful when the price comparer
   imports a catalogue atomically.
2. **Persistent indexes** — write B-tree pages into the `.agdb` file so open doesn't pay a
   full-file scan. Fixes the "cold-open scan" limitation for large files.
3. **Shared read access** — allow a second process (or a viewer tool) to open the file
   read-only while the app holds the write lock.
4. **Embedded full-text search** — port the server's `FullText` module into the embedded
   build; "search products by name" is a core price-comparer operation that currently falls
   back to in-memory scans.
5. **String-method query translation** — `StartsWith`/`Contains` currently fall back to
   in-memory evaluation; `StartsWith` at least can be answered by an index range scan.
6. **Change notifications** — a lightweight `collection.OnChanged` event (the server's
   ChangeStreams, miniaturized) so a WPF/MAUI UI can react to inserts without polling.

## B. Price-comparer-shaped features

7. **Time-series / price-history helpers** — append-only price points per barcode with
   "cheapest in last 30 days" style rollups (capped collections + aggregation already exist
   server-side; this would be a friendly API over them).
8. **Sync between embedded and server** — a small sync client so the desktop app's `.agdb`
   file can push/pull from the server using the existing change streams and revisions. The
   classic "LiteDB + CouchDB-style sync" gap; ties the two products together.
9. **Data-file tooling** — an `agdb` CLI (`dump`, `stats`, `verify`, `compact`,
   `export --json/csv`) for inspecting files outside the app.

## C. Server features that are genuinely missing

10. **Online backup/restore** — import/export exists, but a hot `BACKUP` command producing a
    consistent snapshot while writes continue (plus point-in-time restore from WAL) does not.
11. **Stored queries / views** — named, parameterized queries saved on the server so clients
    call `runquery("cheapest-by-store", …)` instead of shipping filter trees.
12. **Webhooks on change streams** — POST to a URL when a subscribed change fires; makes the
    server usable in integrations without a persistent TCP client.
13. **Vector search** — embeddings + cosine-similarity index would enable "similar products"
    matching (e.g., matching the same product across supermarkets with different names),
    which is *the* hard problem in price comparison.

---

## Recommendation

- **#1 (embedded transactions)** and **#2 (persistent indexes)** are the natural next steps
  for the embedded initiative.
- **#13 (vector search for product matching)** is the most differentiating feature for the
  price comparer.
