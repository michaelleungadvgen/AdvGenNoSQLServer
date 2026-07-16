// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage.Indexing;

namespace AdvGenNoSqlServer.Embedded.Indexing;

/// <summary>
/// Thin adapter over the reused <see cref="IndexManager"/>. Maintains string-keyed secondary
/// indexes for the embedded store: creates them from catalog definitions on open, performs
/// unique-constraint pre-checks before a write commits, and applies committed mutations to
/// the in-memory B-tree indexes.
/// </summary>
internal sealed class IndexRegistry
{
    private readonly List<(string Collection, string Field, bool Unique)> _defs = new();

    /// <summary>The underlying index manager (shared with the query executor).</summary>
    public IndexManager Manager { get; } = new();

    /// <summary>True if an index exists for the collection field.</summary>
    public bool HasIndex(string collection, string field) => Manager.HasIndex(collection, field);

    /// <summary>True if the collection has any secondary index.</summary>
    public bool HasAnyIndex(string collection) => _defs.Any(d => d.Collection == collection);

    /// <summary>Creates a string-keyed index in the manager.</summary>
    public void CreateIndex(string collection, string field, bool unique)
    {
        Manager.CreateIndex<string>(collection, field, unique, doc => KeyOf(doc, field));
        _defs.Add((collection, field, unique));
    }

    /// <summary>Pre-commit unique check for an insert. Throws <see cref="DuplicateKeyException"/>.</summary>
    public void CheckInsert(string collection, Document doc)
    {
        foreach (var d in _defs)
        {
            if (d.Collection != collection || !d.Unique) continue;
            string key = KeyOf(doc, d.Field);
            var idx = Manager.GetIndex<string>(collection, d.Field);
            if (idx != null && idx.ContainsKey(key))
                throw new DuplicateKeyException(collection, d.Field, key);
        }
    }

    /// <summary>Pre-commit unique check for an update (ignores the document's current key).</summary>
    public void CheckUpdate(string collection, Document newDoc, Document oldDoc)
    {
        foreach (var d in _defs)
        {
            if (d.Collection != collection || !d.Unique) continue;
            string newKey = KeyOf(newDoc, d.Field);
            string oldKey = KeyOf(oldDoc, d.Field);
            if (newKey == oldKey) continue;
            var idx = Manager.GetIndex<string>(collection, d.Field);
            if (idx != null && idx.ContainsKey(newKey))
                throw new DuplicateKeyException(collection, d.Field, newKey);
        }
    }

    /// <summary>Applies a committed insert to the indexes.</summary>
    public void ApplyInsert(string collection, Document doc) => Manager.IndexDocument(collection, doc);

    /// <summary>Applies a committed update to the indexes.</summary>
    public void ApplyUpdate(string collection, Document oldDoc, Document newDoc)
        => Manager.UpdateDocument(collection, oldDoc, newDoc);

    /// <summary>Applies a committed delete to the indexes.</summary>
    public void ApplyDelete(string collection, Document doc) => Manager.RemoveDocument(collection, doc);

    /// <summary>Drops all indexes for a collection.</summary>
    public void ApplyDrop(string collection)
    {
        Manager.DropCollectionIndexes(collection);
        _defs.RemoveAll(d => d.Collection == collection);
    }

    internal static string KeyOf(Document doc, string field)
        => doc.Data != null && doc.Data.TryGetValue(field, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
}
