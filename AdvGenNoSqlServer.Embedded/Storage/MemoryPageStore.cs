// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Embedded.Storage;

/// <summary>
/// In-memory page store backed by a dictionary. Used for <c>:memory:</c> databases and
/// tests. Not thread-safe on its own; the engine provides locking.
/// </summary>
public sealed class MemoryPageStore : IPageStore
{
    private readonly Dictionary<uint, byte[]> _pages = new();
    private readonly Stack<uint> _freeList = new();
    private uint _nextPageId = 1; // page 0 reserved for header

    /// <summary>Creates an empty store with a header page (page 0) allocated.</summary>
    public MemoryPageStore()
    {
        var header = Page.CreateNew(0, PageType.Header);
        header.Seal();
        _pages[0] = header.Buffer;
    }

    /// <inheritdoc />
    public uint PageCount => (uint)_pages.Count;

    /// <inheritdoc />
    public uint AllocatePage(PageType type)
    {
        uint id = _freeList.Count > 0 ? _freeList.Pop() : _nextPageId++;
        var page = Page.CreateNew(id, type);
        page.Seal();
        _pages[id] = page.Buffer;
        return id;
    }

    /// <inheritdoc />
    public Page ReadPage(uint pageId)
    {
        if (!_pages.TryGetValue(pageId, out var buffer))
            throw new ArgumentOutOfRangeException(nameof(pageId), $"Page {pageId} is not allocated");
        // Return a copy so callers cannot mutate stored bytes without WritePage.
        var page = Page.FromBuffer((byte[])buffer.Clone());
        if (!page.Validate())
            throw new EmbeddedDataCorruptionException($"Page {pageId} failed checksum validation");
        return page;
    }

    /// <inheritdoc />
    public void WritePage(Page page)
    {
        page.Seal();
        _pages[page.PageId] = (byte[])page.Buffer.Clone();
    }

    /// <inheritdoc />
    public void FreePage(uint pageId)
    {
        if (pageId == 0) throw new ArgumentException("Cannot free the header page", nameof(pageId));
        if (_pages.Remove(pageId))
            _freeList.Push(pageId);
    }

    /// <inheritdoc />
    public void Flush(bool toDisk) { /* no-op for memory */ }

    /// <inheritdoc />
    public void Dispose() { _pages.Clear(); }
}
