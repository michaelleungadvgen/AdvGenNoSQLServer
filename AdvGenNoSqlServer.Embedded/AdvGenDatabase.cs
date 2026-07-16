// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Embedded.Storage;
using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Filtering;

namespace AdvGenNoSqlServer.Embedded;

/// <summary>
/// Single-file, in-process, LiteDB-style embedded document database. Open a database with a
/// file path (or <c>":memory:"</c>), obtain untyped or typed collections, query, and dispose
/// (which checkpoints the WAL). One process may hold a given file open at a time.
/// </summary>
public sealed class AdvGenDatabase : IDisposable, IAsyncDisposable
{
    private const string MemoryPath = ":memory:";

    private readonly EmbeddedDocumentStore _store;
    private readonly QueryExecutor _executor;
    private readonly EmbeddedDatabaseOptions _options;
    private readonly Dictionary<string, EmbeddedCollection> _untyped = new();
    private bool _disposed;

    /// <summary>Runtime diagnostics (e.g. typed-query fallback counter).</summary>
    public EmbeddedDiagnostics Diagnostics { get; } = new();

    /// <summary>Opens (or creates) a database at the given path. Use <c>":memory:"</c> for a volatile store.</summary>
    public AdvGenDatabase(string path, EmbeddedDatabaseOptions? options = null)
    {
        _options = options ?? new EmbeddedDatabaseOptions();
        if (path == MemoryPath)
        {
            _store = EmbeddedDocumentStore.CreateInMemory();
        }
        else
        {
            var wal = new WalPageStore(new FilePageStore(path), path + ".wal", _options.WalCheckpointBytes);
            _store = new EmbeddedDocumentStore(wal);
            _store.InitializeAsync().GetAwaiter().GetResult();
        }
        _executor = new QueryExecutor(_store, new FilterEngine(), _store.Indexes.Manager);
    }

    /// <summary>Gets (or creates) the untyped collection with the given name. Same instance per name.</summary>
    public EmbeddedCollection GetCollection(string name)
    {
        EnsureNotDisposed();
        if (_untyped.TryGetValue(name, out var existing)) return existing;
        var col = new EmbeddedCollection(name, _store, _executor);
        _untyped[name] = col;
        return col;
    }

    // GetCollection<T> (typed layer) is added in Task 15; CompactAsync in Task 17.

    /// <summary>Lists all collection names currently in the database.</summary>
    public IReadOnlyList<string> GetCollectionNames()
    {
        EnsureNotDisposed();
        return _store.GetCollectionsAsync().GetAwaiter().GetResult().ToList();
    }

    /// <summary>Drops a collection and its documents. Returns true if it existed.</summary>
    public bool DropCollection(string name)
    {
        EnsureNotDisposed();
        bool dropped = _store.DropCollectionAsync(name).GetAwaiter().GetResult();
        _untyped.Remove(name);
        return dropped;
    }

    /// <summary>Forces a WAL checkpoint (flush committed pages to the main file).</summary>
    public void Checkpoint()
    {
        EnsureNotDisposed();
        _store.Checkpoint();
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _store.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
