// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text;
using System.Text.Json;

namespace AdvGenNoSqlServer.Embedded.Storage;

/// <summary>Definition of a collection (name + head of its data-page chain).</summary>
public sealed class CollectionDef
{
    /// <summary>Collection name.</summary>
    public required string Name { get; set; }
    /// <summary>First data page id (0 until the first document is written).</summary>
    public uint FirstPage { get; set; }
}

/// <summary>Definition of a secondary index.</summary>
public sealed class IndexDef
{
    /// <summary>Owning collection.</summary>
    public required string Collection { get; set; }
    /// <summary>Indexed field name.</summary>
    public required string Field { get; set; }
    /// <summary>Index name (unique within the database).</summary>
    public required string Name { get; set; }
    /// <summary>Whether the index enforces uniqueness.</summary>
    public bool Unique { get; set; }
}

/// <summary>
/// On-file catalog of collections and index definitions, stored as compact-JSON records on
/// a chain of catalog pages (reusing <see cref="RecordFile"/>). The chain root is reported
/// through <c>onRootChanged</c> so the owner can persist it in the file header.
/// </summary>
public sealed class Catalog
{
    private sealed class Entry
    {
        public string t { get; set; } = "";   // "col" | "idx"
        public string? name { get; set; }
        public uint firstPage { get; set; }
        public string? col { get; set; }
        public string? field { get; set; }
        public bool unique { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new() { };

    private readonly RecordFile _records;
    private readonly Action<uint> _onRootChanged;
    private uint _root;

    private readonly Dictionary<string, (RecordAddress Addr, CollectionDef Def)> _collections = new();
    private readonly Dictionary<string, (RecordAddress Addr, IndexDef Def)> _indexes = new();

    /// <summary>Opens a catalog rooted at <paramref name="rootPageId"/> (0 = empty).</summary>
    public Catalog(IPageStore store, uint rootPageId, Action<Page> persist, Action<uint> onRootChanged)
    {
        _root = rootPageId;
        _onRootChanged = onRootChanged;
        _records = new RecordFile(store, rootPageId, persist);
    }

    /// <summary>All collection definitions.</summary>
    public IReadOnlyList<CollectionDef> Collections => _collections.Values.Select(v => v.Def).ToList();

    /// <summary>All index definitions.</summary>
    public IReadOnlyList<IndexDef> Indexes => _indexes.Values.Select(v => v.Def).ToList();

    /// <summary>Loads catalog entries from the store into memory.</summary>
    public void Load()
    {
        _collections.Clear();
        _indexes.Clear();
        foreach (var (addr, id, body) in _records.Enumerate())
        {
            var entry = JsonSerializer.Deserialize<Entry>(Encoding.UTF8.GetString(body), Json)!;
            if (entry.t == "col")
            {
                _collections[entry.name!] = (addr, new CollectionDef { Name = entry.name!, FirstPage = entry.firstPage });
            }
            else if (entry.t == "idx")
            {
                _indexes[entry.name!] = (addr, new IndexDef
                {
                    Collection = entry.col!,
                    Field = entry.field!,
                    Name = entry.name!,
                    Unique = entry.unique
                });
            }
            _ = id;
        }
    }

    /// <summary>Adds a collection (idempotent — returns the existing def if present).</summary>
    public CollectionDef AddCollection(string name)
    {
        if (_collections.TryGetValue(name, out var existing))
            return existing.Def;

        var def = new CollectionDef { Name = name, FirstPage = 0 };
        var addr = WriteEntry(new Entry { t = "col", name = name, firstPage = 0 });
        _collections[name] = (addr, def);
        return def;
    }

    /// <summary>Records the first data page id for a collection.</summary>
    public void UpdateCollectionFirstPage(string name, uint firstPage)
    {
        if (!_collections.TryGetValue(name, out var cur)) return;
        cur.Def.FirstPage = firstPage;
        var addr = _records.Update(cur.Addr, CollectionRecordId(name),
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Entry { t = "col", name = name, firstPage = firstPage }, Json)));
        _collections[name] = (addr, cur.Def);
        NotifyRootIfChanged();
    }

    /// <summary>Removes a collection and all of its index definitions.</summary>
    public bool RemoveCollection(string name)
    {
        if (!_collections.TryGetValue(name, out var cur)) return false;
        _records.Delete(cur.Addr);
        _collections.Remove(name);
        RemoveIndexes(name);
        NotifyRootIfChanged();
        return true;
    }

    /// <summary>Adds an index definition (idempotent by index name).</summary>
    public IndexDef AddIndex(IndexDef def)
    {
        if (_indexes.TryGetValue(def.Name, out var existing))
            return existing.Def;

        var addr = WriteEntry(new Entry
        {
            t = "idx", name = def.Name, col = def.Collection, field = def.Field, unique = def.Unique
        });
        _indexes[def.Name] = (addr, def);
        return def;
    }

    /// <summary>Removes all index definitions for a collection.</summary>
    public void RemoveIndexes(string collection)
    {
        foreach (var key in _indexes.Where(kv => kv.Value.Def.Collection == collection).Select(kv => kv.Key).ToList())
        {
            _records.Delete(_indexes[key].Addr);
            _indexes.Remove(key);
        }
        NotifyRootIfChanged();
    }

    private RecordAddress WriteEntry(Entry entry)
    {
        string recordId = entry.t == "col" ? CollectionRecordId(entry.name!) : IndexRecordId(entry.name!);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, Json));
        var addr = _records.Insert(recordId, body);
        NotifyRootIfChanged();
        return addr;
    }

    private void NotifyRootIfChanged()
    {
        if (_records.FirstPageId != _root)
        {
            _root = _records.FirstPageId;
            _onRootChanged(_root);
        }
    }

    private static string CollectionRecordId(string name) => "col:" + name;
    private static string IndexRecordId(string name) => "idx:" + name;
}
