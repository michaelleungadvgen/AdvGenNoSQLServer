// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Embedded.Storage;

/// <summary>
/// Durable page store that layers a write-ahead log over a <see cref="FilePageStore"/>.
/// Page writes are buffered in a transaction, appended to the WAL on
/// <see cref="CommitTransaction"/> (fsync), and only flushed to the main file at a
/// checkpoint (WAL over threshold, explicit <see cref="Checkpoint"/>, or dispose). Page
/// allocation and free eagerly extend/trim the underlying file; a crash before commit
/// leaves at most unreferenced pages. On open, committed WAL frames are replayed into the
/// file. Not thread-safe; the engine serializes access.
/// </summary>
public sealed class WalPageStore : IPageStore, ITransactionalPageStore, ICatalogRootStore
{
    private readonly FilePageStore _file;
    private readonly WriteAheadLog _wal;
    private readonly long _checkpointThresholdBytes;

    // Committed-but-not-yet-checkpointed page images.
    private readonly Dictionary<uint, byte[]> _cache = new();
    // Dirty pages in the current (uncommitted) transaction.
    private readonly Dictionary<uint, byte[]> _pending = new();

    /// <summary>Opens a WAL-backed store, replaying any committed WAL frames into the file.</summary>
    public WalPageStore(FilePageStore file, string walPath, long checkpointThresholdBytes)
    {
        _file = file;
        _wal = new WriteAheadLog(walPath);
        _checkpointThresholdBytes = checkpointThresholdBytes;
        RecoverFromWal();
    }

    /// <inheritdoc />
    public uint PageCount => _file.PageCount;

    /// <inheritdoc />
    public uint CatalogRootPageId
    {
        get => _file.CatalogRootPageId;
        set => _file.CatalogRootPageId = value;
    }

    /// <inheritdoc />
    public uint AllocatePage(PageType type) => _file.AllocatePage(type);

    /// <inheritdoc />
    public Page ReadPage(uint pageId)
    {
        if (_pending.TryGetValue(pageId, out var pending))
            return Page.FromBuffer((byte[])pending.Clone());
        if (_cache.TryGetValue(pageId, out var cached))
            return Page.FromBuffer((byte[])cached.Clone());
        return _file.ReadPage(pageId);
    }

    /// <inheritdoc />
    public void WritePage(Page page)
    {
        page.Seal();
        _pending[page.PageId] = (byte[])page.Buffer.Clone();
    }

    /// <inheritdoc />
    public void FreePage(uint pageId)
    {
        _file.FreePage(pageId);
        _pending.Remove(pageId);
        _cache.Remove(pageId);
    }

    /// <inheritdoc />
    public void Flush(bool toDisk) => _file.Flush(toDisk);

    /// <inheritdoc />
    public void CommitTransaction()
    {
        if (_pending.Count == 0)
            return;

        foreach (var (pageId, image) in _pending)
        {
            _wal.Append(Page.FromBuffer((byte[])image.Clone()));
            _cache[pageId] = image;
        }
        _wal.Commit();
        _pending.Clear();

        if (_wal.SizeBytes >= _checkpointThresholdBytes)
            Checkpoint();
    }

    /// <inheritdoc />
    public void RollbackTransaction()
    {
        _wal.DiscardUncommitted();
        _pending.Clear();
    }

    /// <inheritdoc />
    public void Checkpoint()
    {
        if (_cache.Count == 0)
        {
            _wal.Truncate();
            return;
        }
        foreach (var (_, image) in _cache)
            _file.WritePage(Page.FromBuffer((byte[])image.Clone()));
        _file.Flush(true);
        _file.LastCheckpointLsn = _wal.LastCommittedLsn;
        _wal.Truncate();
        _cache.Clear();
    }

    private void RecoverFromWal()
    {
        var pages = _wal.ReadCommittedPages();
        if (pages.Count == 0)
            return;
        foreach (var (_, image) in pages)
            _file.WritePage(Page.FromBuffer((byte[])image.Clone()));
        _file.Flush(true);
        _wal.Truncate();
    }

    /// <summary>Checkpoints and closes both the WAL and the file.</summary>
    public void Dispose()
    {
        Checkpoint();
        _wal.Dispose();
        _file.Dispose();
    }

    /// <summary>
    /// Closes streams WITHOUT checkpointing, simulating a crash. Committed WAL frames remain
    /// on disk and are replayed on the next open. For tests only.
    /// </summary>
    internal void Abort()
    {
        _wal.Dispose();
        _file.Dispose();
    }
}
