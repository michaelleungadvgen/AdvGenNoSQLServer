// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Embedded.Storage;

/// <summary>
/// Durable page store that layers a write-ahead log over a <see cref="FilePageStore"/>.
/// Page writes AND metadata changes (page allocation, free list, catalog root) are
/// buffered in a transaction and appended to the WAL on <see cref="CommitTransaction"/>
/// (fsync); the header page is WAL-logged whenever metadata changed, so recovery
/// restores metadata atomically with the data. Nothing touches the main file before
/// commit except checkpoint flushes, so rollback discards everything and a crash can
/// never leave the free list or catalog root pointing at uncommitted content.
/// Not thread-safe; the engine serializes access.
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

    // Transactional metadata overlays (null = fall through to committed/file value).
    private uint? _txFreeListHead;
    private uint? _txCatalogRoot;
    // Committed-but-not-yet-checkpointed metadata. The main file is only updated at
    // checkpoint, so it always stays consistent with the pages it actually holds.
    private uint? _committedFreeListHead;
    private uint? _committedCatalogRoot;
    private uint _logicalPageCount;

    private uint EffectiveFreeListHead => _txFreeListHead ?? _committedFreeListHead ?? _file.FreeListHeadPageId;
    private uint EffectiveCatalogRoot => _txCatalogRoot ?? _committedCatalogRoot ?? _file.CatalogRootPageId;

    /// <summary>Opens a WAL-backed store, replaying any committed WAL frames into the file.</summary>
    public WalPageStore(FilePageStore file, string walPath, long checkpointThresholdBytes)
    {
        _file = file;
        _wal = new WriteAheadLog(walPath);
        _checkpointThresholdBytes = checkpointThresholdBytes;
        RecoverFromWal();
        _logicalPageCount = _file.PageCount;
    }

    /// <inheritdoc />
    public uint PageCount => _logicalPageCount;

    /// <inheritdoc />
    public uint CatalogRootPageId
    {
        get => EffectiveCatalogRoot;
        set => _txCatalogRoot = value;
    }

    /// <inheritdoc />
    public uint AllocatePage(PageType type)
    {
        uint id;
        uint head = EffectiveFreeListHead;
        if (head != 0)
        {
            var freed = ReadPage(head);
            id = head;
            _txFreeListHead = freed.NextPageId;
        }
        else
        {
            id = _logicalPageCount;
            _logicalPageCount++;
        }

        var page = Page.CreateNew(id, type);
        page.Seal();
        _pending[id] = (byte[])page.Buffer.Clone();
        return id;
    }

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
        if (pageId == 0) throw new ArgumentException("Cannot free the header page", nameof(pageId));
        // Keep the committed image in _cache: with deferred checkpointing it may be
        // the only copy, and a rollback of this free must restore it. The pending
        // free marker shadows reads until commit replaces the cache entry.
        _pending.Remove(pageId);

        var page = Page.CreateNew(pageId, PageType.Free);
        page.NextPageId = EffectiveFreeListHead;
        page.Seal();
        _pending[pageId] = (byte[])page.Buffer.Clone();
        _txFreeListHead = pageId;
    }

    /// <inheritdoc />
    public void Flush(bool toDisk) => _file.Flush(toDisk);

    /// <inheritdoc />
    public void CommitTransaction()
    {
        bool metadataChanged = _txFreeListHead.HasValue || _txCatalogRoot.HasValue;
        if (_pending.Count == 0 && !metadataChanged)
            return;

        foreach (var (pageId, image) in _pending)
        {
            _wal.Append(Page.FromBuffer((byte[])image.Clone()));
            _cache[pageId] = image;
        }

        if (metadataChanged)
        {
            // WAL-log the header so recovery restores metadata atomically with the data.
            var header = _file.BuildHeaderPage(EffectiveFreeListHead, EffectiveCatalogRoot);
            _wal.Append(header);
            _cache[0] = (byte[])header.Buffer.Clone();
        }

        _wal.Commit();

        // Durable in the WAL. The main file is deliberately NOT touched here: it must
        // only ever reference pages it physically holds (checkpoint writes them together).
        if (_txFreeListHead.HasValue)
            _committedFreeListHead = _txFreeListHead;
        if (_txCatalogRoot.HasValue)
            _committedCatalogRoot = _txCatalogRoot;

        _pending.Clear();
        _txFreeListHead = null;
        _txCatalogRoot = null;

        if (_wal.SizeBytes >= _checkpointThresholdBytes)
            Checkpoint();
    }

    /// <inheritdoc />
    public void RollbackTransaction()
    {
        _wal.DiscardUncommitted();
        _pending.Clear();
        _txFreeListHead = null;
        _txCatalogRoot = null;

        // Discard id space reserved by uncommitted file extensions.
        _logicalPageCount = _file.PageCount;
        foreach (var id in _cache.Keys)
        {
            if (id >= _logicalPageCount)
                _logicalPageCount = id + 1;
        }
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
        _file.ReloadHeader();
        _committedFreeListHead = null;
        _committedCatalogRoot = null;
        _file.LastCheckpointLsn = _wal.LastCommittedLsn;
        _wal.Truncate();
        _cache.Clear();
        if (_file.PageCount > _logicalPageCount)
            _logicalPageCount = _file.PageCount;
    }

    private void RecoverFromWal()
    {
        var pages = _wal.ReadCommittedPages();
        foreach (var (_, image) in pages)
            _file.WritePage(Page.FromBuffer((byte[])image.Clone()));
        if (pages.Count > 0)
        {
            _file.Flush(true);
            _file.ReloadHeader();
        }
        // Always truncate, even with zero committed pages: otherwise a torn or
        // uncommitted tail stays on disk and new commits append after it, only to
        // be silently dropped by the next recovery scan.
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