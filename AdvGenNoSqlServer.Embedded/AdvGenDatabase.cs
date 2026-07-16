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

    private EmbeddedDocumentStore _store;
    private QueryExecutor _executor;
    private readonly EmbeddedDatabaseOptions _options;
    private readonly string? _path;
    private readonly Dictionary<string, EmbeddedCollection> _untyped = new();
    private readonly Dictionary<string, object> _typed = new();
    private bool _disposed;

    /// <summary>Test hook: invoked during compaction just before the atomic file swap.</summary>
    internal Action? BeforeCompactSwapHook { get; set; }

    /// <summary>Runtime diagnostics (e.g. typed-query fallback counter).</summary>
    public EmbeddedDiagnostics Diagnostics { get; } = new();

    /// <summary>Opens (or creates) a database at the given path. Use <c>":memory:"</c> for a volatile store.</summary>
    public AdvGenDatabase(string path, EmbeddedDatabaseOptions? options = null)
    {
        _options = options ?? new EmbeddedDatabaseOptions();
        if (path == MemoryPath)
        {
            _path = null;
            _store = EmbeddedDocumentStore.CreateInMemory();
        }
        else
        {
            _path = path;
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

    /// <summary>Gets (or creates) the typed collection with the given name. Same instance per name.</summary>
    public Typed.IEmbeddedCollection<T> GetCollection<T>(string name) where T : class
    {
        EnsureNotDisposed();
        if (_typed.TryGetValue(name, out var existing)) return (Typed.IEmbeddedCollection<T>)existing;
        var col = new Typed.EmbeddedCollection<T>(name, _store, _executor, _options, Diagnostics);
        _typed[name] = col;
        return col;
    }

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
        _typed.Remove(name);
        return dropped;
    }

    /// <summary>Forces a WAL checkpoint (flush committed pages to the main file).</summary>
    public void Checkpoint()
    {
        EnsureNotDisposed();
        _store.Checkpoint();
    }

    /// <summary>
    /// Rewrites the database file, reclaiming space from deleted documents. No-op for
    /// <c>:memory:</c>. Blocks all other access for its duration. Re-obtain collections via
    /// <see cref="GetCollection(string)"/> / <see cref="GetCollection{T}(string)"/> afterward.
    /// </summary>
    public async Task CompactAsync(CancellationToken ct = default)
    {
        EnsureNotDisposed();
        if (_path == null) return; // nothing to compact for :memory:

        var compactPath = _path + ".compact";
        await Compactor.WriteCompactedCopyAsync(_store, compactPath, _options.WalCheckpointBytes, ct);

        BeforeCompactSwapHook?.Invoke();

        // Release the original file, then atomically replace it with the compacted copy.
        _store.Dispose();
        if (File.Exists(_path + ".wal")) File.Delete(_path + ".wal");
        File.Replace(compactPath, _path, null);
        if (File.Exists(compactPath + ".wal")) File.Delete(compactPath + ".wal");

        // Reopen on the compacted file; drop cached collection wrappers bound to the old store.
        var wal = new WalPageStore(new FilePageStore(_path), _path + ".wal", _options.WalCheckpointBytes);
        _store = new EmbeddedDocumentStore(wal);
        await _store.InitializeAsync();
        _executor = new QueryExecutor(_store, new FilterEngine(), _store.Indexes.Manager);
        _untyped.Clear();
        _typed.Clear();
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
