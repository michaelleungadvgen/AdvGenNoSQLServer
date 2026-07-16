// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Embedded.Indexing;
using AdvGenNoSqlServer.Embedded.Storage;

namespace AdvGenNoSqlServer.Embedded;

/// <summary>
/// <see cref="IDocumentStore"/> implementation backed by a paged file (or in-memory store).
/// Each collection has a <see cref="RecordFile"/> and an in-memory primary index
/// (id → <see cref="RecordAddress"/>) rebuilt on open. v1 uses a single reader/writer lock
/// around the whole store.
/// </summary>
public sealed class EmbeddedDocumentStore : IDocumentStore, IDisposable
{
    private sealed class CollectionState
    {
        public required RecordFile File { get; init; }
        public Dictionary<string, RecordAddress> PrimaryIndex { get; } = new();
    }

    private readonly IPageStore _store;
    private readonly ITransactionalPageStore? _txn;
    private readonly Catalog _catalog;
    private readonly Action<Page> _persist;
    private readonly Dictionary<string, CollectionState> _collections = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly IndexRegistry _indexes = new();
    private bool _initialized;

    /// <summary>The secondary-index registry (shared with the query executor).</summary>
    internal IndexRegistry Indexes => _indexes;

    /// <summary>Index definitions currently in the catalog (for compaction).</summary>
    internal IReadOnlyList<(string Collection, string Field, bool Unique)> GetIndexDefinitions()
        => _catalog.Indexes.Select(i => (i.Collection, i.Field, i.Unique)).ToList();

    /// <summary>Raised after any collection's first page id changes (WAL/checkpoint hook).</summary>
    internal event Action<string, uint>? CollectionRootChanged;

    /// <summary>Test hook: invoked just before a write op commits (to simulate mid-op failure).</summary>
    internal Action? BeforeCommitHook { get; set; }

    /// <summary>Creates a store over the given page store. Call <see cref="InitializeAsync"/> before use.</summary>
    public EmbeddedDocumentStore(IPageStore store) : this(store, store.WritePage) { }

    /// <summary>Creates a store with a custom page-persist path (e.g. routed through a WAL).</summary>
    public EmbeddedDocumentStore(IPageStore store, Action<Page> persistPage)
    {
        _store = store;
        _txn = store as ITransactionalPageStore;
        _persist = persistPage;
        uint root = store is ICatalogRootStore crs ? crs.CatalogRootPageId : 0;
        _catalog = new Catalog(store, root, persistPage, id =>
        {
            if (store is ICatalogRootStore c) c.CatalogRootPageId = id;
        });
    }

    private void Commit()
    {
        BeforeCommitHook?.Invoke();
        _txn?.CommitTransaction();
    }

    private void Rollback() => _txn?.RollbackTransaction();

    /// <summary>Forces a checkpoint (flush committed pages to the main file).</summary>
    public void Checkpoint() => _txn?.Checkpoint();

    /// <summary>Creates an in-memory store, already initialized.</summary>
    public static EmbeddedDocumentStore CreateInMemory()
    {
        var store = new EmbeddedDocumentStore(new MemoryPageStore());
        store.InitializeAsync().GetAwaiter().GetResult();
        return store;
    }

    /// <summary>
    /// Opens a WAL-backed file store, replaying any committed WAL frames first. The store is
    /// initialized and ready to use.
    /// </summary>
    public static EmbeddedDocumentStore OpenFile(string path, long walCheckpointBytes = 4L * 1024 * 1024)
    {
        var wal = new WalPageStore(new FilePageStore(path), path + ".wal", walCheckpointBytes);
        var store = new EmbeddedDocumentStore(wal);
        store.InitializeAsync().GetAwaiter().GetResult();
        return store;
    }

    /// <summary>Test hook: closes streams without checkpointing, simulating a crash.</summary>
    internal void SimulateCrash()
    {
        if (_store is WalPageStore wal) wal.Abort();
        else _store.Dispose();
        _lock.Dispose();
    }

    /// <summary>Loads the catalog and rebuilds primary indexes from the pages.</summary>
    public Task InitializeAsync()
    {
        _lock.EnterWriteLock();
        try
        {
            _catalog.Load();
            foreach (var def in _catalog.Collections)
            {
                var state = new CollectionState { File = new RecordFile(_store, def.FirstPage, _persist) };
                foreach (var (addr, id, _) in state.File.Enumerate())
                    state.PrimaryIndex[id] = addr;
                _collections[def.Name] = state;
            }

            // Rebuild secondary indexes from catalog definitions, then backfill from documents.
            foreach (var idx in _catalog.Indexes)
                _indexes.CreateIndex(idx.Collection, idx.Field, idx.Unique);
            foreach (var (name, state) in _collections)
            {
                if (!_indexes.HasAnyIndex(name)) continue;
                foreach (var addr in state.PrimaryIndex.Values)
                    _indexes.ApplyInsert(name, DocumentSerializer.Deserialize(state.File.Read(addr).Body));
            }
            _initialized = true;
        }
        finally { _lock.ExitWriteLock(); }
        return Task.CompletedTask;
    }

    private CollectionState GetOrCreate(string collectionName)
    {
        if (_collections.TryGetValue(collectionName, out var existing))
            return existing;
        _catalog.AddCollection(collectionName);
        var state = new CollectionState { File = new RecordFile(_store, 0, _persist) };
        _collections[collectionName] = state;
        return state;
    }

    private void SyncFirstPage(string collectionName, CollectionState state)
    {
        var def = _catalog.Collections.FirstOrDefault(c => c.Name == collectionName);
        if (def != null && def.FirstPage != state.File.FirstPageId)
        {
            _catalog.UpdateCollectionFirstPage(collectionName, state.File.FirstPageId);
            CollectionRootChanged?.Invoke(collectionName, state.File.FirstPageId);
        }
    }

    /// <inheritdoc />
    public Task<Document> InsertAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        ArgumentNullException.ThrowIfNull(document);

        _lock.EnterWriteLock();
        try
        {
            var state = GetOrCreate(collectionName);
            string id = string.IsNullOrWhiteSpace(document.Id) ? Guid.NewGuid().ToString() : document.Id;
            if (state.PrimaryIndex.ContainsKey(id))
                throw new DocumentAlreadyExistsException(collectionName, id);

            var now = DateTime.UtcNow;
            var stored = new Document
            {
                Id = id,
                Data = document.Data ?? new Dictionary<string, object>(),
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            };
            var bytes = DocumentSerializer.Serialize(stored);
            _indexes.CheckInsert(collectionName, stored);
            var addr = state.File.Insert(id, bytes);
            SyncFirstPage(collectionName, state);
            Commit();
            state.PrimaryIndex[id] = addr;
            _indexes.ApplyInsert(collectionName, stored);
            return Task.FromResult(stored);
        }
        catch { Rollback(); throw; }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc />
    public Task<Document?> GetAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        if (string.IsNullOrWhiteSpace(documentId)) return Task.FromResult<Document?>(null);

        _lock.EnterReadLock();
        try
        {
            if (_collections.TryGetValue(collectionName, out var state) &&
                state.PrimaryIndex.TryGetValue(documentId, out var addr))
            {
                var (_, body) = state.File.Read(addr);
                return Task.FromResult<Document?>(DocumentSerializer.Deserialize(body));
            }
            return Task.FromResult<Document?>(null);
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetManyAsync(string collectionName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        if (documentIds == null) return Task.FromResult(Enumerable.Empty<Document>());

        _lock.EnterReadLock();
        try
        {
            var result = new List<Document>();
            if (_collections.TryGetValue(collectionName, out var state))
            {
                var seen = new HashSet<string>();
                foreach (var id in documentIds)
                {
                    if (seen.Add(id) && state.PrimaryIndex.TryGetValue(id, out var addr))
                        result.Add(DocumentSerializer.Deserialize(state.File.Read(addr).Body));
                }
            }
            return Task.FromResult<IEnumerable<Document>>(result);
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetAllAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        _lock.EnterReadLock();
        try
        {
            var result = new List<Document>();
            if (_collections.TryGetValue(collectionName, out var state))
            {
                foreach (var addr in state.PrimaryIndex.Values)
                    result.Add(DocumentSerializer.Deserialize(state.File.Read(addr).Body));
            }
            return Task.FromResult<IEnumerable<Document>>(result);
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc />
    public Task<Document> UpdateAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        ArgumentNullException.ThrowIfNull(document);

        _lock.EnterWriteLock();
        try
        {
            if (!_collections.TryGetValue(collectionName, out var state))
                throw new CollectionNotFoundException(collectionName);
            if (!state.PrimaryIndex.TryGetValue(document.Id, out var addr))
                throw new DocumentNotFoundException(collectionName, document.Id);

            var existing = DocumentSerializer.Deserialize(state.File.Read(addr).Body);
            var updated = new Document
            {
                Id = document.Id,
                Data = document.Data ?? existing.Data,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                Version = existing.Version + 1,
            };
            var bytes = DocumentSerializer.Serialize(updated);
            _indexes.CheckUpdate(collectionName, updated, existing);
            var newAddr = state.File.Update(addr, document.Id, bytes);
            SyncFirstPage(collectionName, state);
            Commit();
            state.PrimaryIndex[document.Id] = newAddr;
            _indexes.ApplyUpdate(collectionName, existing, updated);
            return Task.FromResult(updated);
        }
        catch { Rollback(); throw; }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        if (string.IsNullOrWhiteSpace(documentId)) return Task.FromResult(false);

        _lock.EnterWriteLock();
        try
        {
            if (_collections.TryGetValue(collectionName, out var state) &&
                state.PrimaryIndex.TryGetValue(documentId, out var addr))
            {
                Document? doc = _indexes.HasAnyIndex(collectionName)
                    ? DocumentSerializer.Deserialize(state.File.Read(addr).Body) : null;
                state.File.Delete(addr);
                Commit();
                state.PrimaryIndex.Remove(documentId);
                if (doc != null) _indexes.ApplyDelete(collectionName, doc);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch { Rollback(); throw; }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        if (string.IsNullOrWhiteSpace(documentId)) return Task.FromResult(false);

        _lock.EnterReadLock();
        try
        {
            bool exists = _collections.TryGetValue(collectionName, out var state) &&
                          state.PrimaryIndex.ContainsKey(documentId);
            return Task.FromResult(exists);
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc />
    public Task<long> CountAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        _lock.EnterReadLock();
        try
        {
            long count = _collections.TryGetValue(collectionName, out var state) ? state.PrimaryIndex.Count : 0;
            return Task.FromResult(count);
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc />
    public Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        _lock.EnterWriteLock();
        try { GetOrCreate(collectionName); Commit(); return Task.CompletedTask; }
        catch { Rollback(); throw; }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc />
    public Task<bool> DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        _lock.EnterWriteLock();
        try
        {
            if (!_collections.ContainsKey(collectionName)) return Task.FromResult(false);
            _catalog.RemoveCollection(collectionName);
            Commit();
            _collections.Remove(collectionName);
            _indexes.ApplyDrop(collectionName);
            return Task.FromResult(true);
        }
        catch { Rollback(); throw; }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        _lock.EnterReadLock();
        try { return Task.FromResult<IEnumerable<string>>(_collections.Keys.ToList()); }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc />
    public Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        _lock.EnterWriteLock();
        try
        {
            if (_collections.TryGetValue(collectionName, out var state))
            {
                var docs = _indexes.HasAnyIndex(collectionName)
                    ? state.PrimaryIndex.Values.Select(a => DocumentSerializer.Deserialize(state.File.Read(a).Body)).ToList()
                    : null;
                foreach (var addr in state.PrimaryIndex.Values.ToList())
                    state.File.Delete(addr);
                Commit();
                state.PrimaryIndex.Clear();
                if (docs != null) foreach (var d in docs) _indexes.ApplyDelete(collectionName, d);
            }
            return Task.CompletedTask;
        }
        catch { Rollback(); throw; }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Ensures a secondary index on a field exists, creating and backfilling it if not.
    /// Returns true if the index was created, false if it already existed.
    /// </summary>
    public Task<bool> EnsureIndexAsync(string collectionName, string field, bool unique = false, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field name cannot be empty", nameof(field));

        _lock.EnterWriteLock();
        try
        {
            if (_indexes.HasIndex(collectionName, field)) return Task.FromResult(false);
            var state = GetOrCreate(collectionName);
            _catalog.AddIndex(new IndexDef
            {
                Collection = collectionName,
                Field = field,
                Name = $"{collectionName}_{field}_idx",
                Unique = unique,
            });
            Commit();
            _indexes.CreateIndex(collectionName, field, unique);
            foreach (var addr in state.PrimaryIndex.Values)
                _indexes.ApplyInsert(collectionName, DocumentSerializer.Deserialize(state.File.Read(addr).Body));
            return Task.FromResult(true);
        }
        catch { Rollback(); throw; }
        finally { _lock.ExitWriteLock(); }
    }

    private static void ValidateCollectionName(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));
    }

    /// <summary>Flushes pending page writes.</summary>
    public void Flush() => _store.Flush(true);

    /// <inheritdoc />
    public void Dispose()
    {
        _store.Flush(true);
        _store.Dispose();
        _lock.Dispose();
        _ = _initialized;
    }
}
