// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;
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
    private readonly Catalog _catalog;
    private readonly Action<Page> _persist;
    private readonly Dictionary<string, CollectionState> _collections = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private bool _initialized;

    /// <summary>Raised after any collection's first page id changes (WAL/checkpoint hook).</summary>
    internal event Action<string, uint>? CollectionRootChanged;

    /// <summary>Creates a store over the given page store. Call <see cref="InitializeAsync"/> before use.</summary>
    public EmbeddedDocumentStore(IPageStore store) : this(store, store.WritePage) { }

    /// <summary>Creates a store with a custom page-persist path (e.g. routed through a WAL).</summary>
    public EmbeddedDocumentStore(IPageStore store, Action<Page> persistPage)
    {
        _store = store;
        _persist = persistPage;
        uint root = store is FilePageStore fps ? fps.CatalogRootPageId : 0;
        _catalog = new Catalog(store, root, persistPage, id =>
        {
            if (store is FilePageStore f) f.CatalogRootPageId = id;
        });
    }

    /// <summary>Creates an in-memory store, already initialized.</summary>
    public static EmbeddedDocumentStore CreateInMemory()
    {
        var store = new EmbeddedDocumentStore(new MemoryPageStore());
        store.InitializeAsync().GetAwaiter().GetResult();
        return store;
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
            var addr = state.File.Insert(id, DocumentSerializer.Serialize(stored));
            state.PrimaryIndex[id] = addr;
            SyncFirstPage(collectionName, state);
            return Task.FromResult(stored);
        }
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
                foreach (var id in documentIds)
                {
                    if (state.PrimaryIndex.TryGetValue(id, out var addr))
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
            var newAddr = state.File.Update(addr, document.Id, DocumentSerializer.Serialize(updated));
            state.PrimaryIndex[document.Id] = newAddr;
            SyncFirstPage(collectionName, state);
            return Task.FromResult(updated);
        }
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
                state.File.Delete(addr);
                state.PrimaryIndex.Remove(documentId);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
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
        try { GetOrCreate(collectionName); return Task.CompletedTask; }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc />
    public Task<bool> DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ValidateCollectionName(collectionName);
        _lock.EnterWriteLock();
        try
        {
            if (!_collections.Remove(collectionName)) return Task.FromResult(false);
            _catalog.RemoveCollection(collectionName);
            return Task.FromResult(true);
        }
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
                foreach (var addr in state.PrimaryIndex.Values.ToList())
                    state.File.Delete(addr);
                state.PrimaryIndex.Clear();
            }
            return Task.CompletedTask;
        }
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
