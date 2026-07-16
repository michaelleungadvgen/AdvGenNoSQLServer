// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Buffers.Binary;

namespace AdvGenNoSqlServer.Embedded.Storage;

/// <summary>
/// Single-file page store. Page 0 holds the file header (format version, catalog root,
/// free-list head, last checkpoint LSN). Free pages form a chain via their
/// <see cref="Page.NextPageId"/>. The file is opened with an exclusive lock; a second
/// opener gets an <see cref="EmbeddedDatabaseLockedException"/>. Not thread-safe; the
/// engine serializes access.
/// </summary>
public sealed class FilePageStore : IPageStore
{
    private const ushort FormatVersion = 1;

    // Header body offsets (relative to the start of the buffer, after the 32-byte page header)
    private const int OffFormatVersion = 32;
    private const int OffCatalogRoot = 34;
    private const int OffFreeListHead = 38;
    private const int OffLastCheckpointLsn = 42;

    private readonly FileStream _stream;
    private uint _catalogRootPageId;
    private uint _freeListHeadPageId;
    private ulong _lastCheckpointLsn;

    /// <summary>Opens or creates a page-store file at the given path.</summary>
    public FilePageStore(string path)
    {
        try
        {
            _stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 4096, FileOptions.None);
        }
        catch (IOException ex)
        {
            throw new EmbeddedDatabaseLockedException($"Database file '{path}' is locked or unavailable", ex);
        }

        if (_stream.Length == 0)
        {
            InitializeHeader();
        }
        else
        {
            LoadHeader();
        }
    }

    /// <summary>Root page id of the catalog chain (0 = none).</summary>
    public uint CatalogRootPageId
    {
        get => _catalogRootPageId;
        set { _catalogRootPageId = value; WriteHeader(); }
    }

    /// <summary>Last checkpoint LSN persisted in the header.</summary>
    public ulong LastCheckpointLsn
    {
        get => _lastCheckpointLsn;
        set { _lastCheckpointLsn = value; WriteHeader(); }
    }

    /// <inheritdoc />
    public uint PageCount => (uint)(_stream.Length / Page.PageSize);

    /// <inheritdoc />
    public uint AllocatePage(PageType type)
    {
        uint id;
        if (_freeListHeadPageId != 0)
        {
            id = _freeListHeadPageId;
            var freed = ReadPageRaw(id);
            _freeListHeadPageId = freed.NextPageId;
            WriteHeader();
        }
        else
        {
            id = PageCount;
        }

        var page = Page.CreateNew(id, type);
        WritePage(page);
        return id;
    }

    /// <inheritdoc />
    public Page ReadPage(uint pageId)
    {
        var page = ReadPageRaw(pageId);
        if (!page.Validate())
            throw new EmbeddedDataCorruptionException($"Page {pageId} failed checksum validation");
        return page;
    }

    /// <inheritdoc />
    public void WritePage(Page page)
    {
        page.Seal();
        _stream.Seek((long)page.PageId * Page.PageSize, SeekOrigin.Begin);
        _stream.Write(page.Buffer, 0, Page.PageSize);
    }

    /// <inheritdoc />
    public void FreePage(uint pageId)
    {
        if (pageId == 0) throw new ArgumentException("Cannot free the header page", nameof(pageId));
        var page = Page.CreateNew(pageId, PageType.Free);
        page.NextPageId = _freeListHeadPageId;
        WritePage(page);
        _freeListHeadPageId = pageId;
        WriteHeader();
    }

    /// <inheritdoc />
    public void Flush(bool toDisk) => _stream.Flush(toDisk);

    /// <inheritdoc />
    public void Dispose()
    {
        _stream.Flush(true);
        _stream.Dispose();
    }

    private Page ReadPageRaw(uint pageId)
    {
        long offset = (long)pageId * Page.PageSize;
        if (offset + Page.PageSize > _stream.Length)
            throw new ArgumentOutOfRangeException(nameof(pageId), $"Page {pageId} is beyond the file");

        var buffer = new byte[Page.PageSize];
        _stream.Seek(offset, SeekOrigin.Begin);
        int read = 0;
        while (read < Page.PageSize)
        {
            int n = _stream.Read(buffer, read, Page.PageSize - read);
            if (n == 0) throw new EmbeddedDataCorruptionException($"Unexpected EOF reading page {pageId}");
            read += n;
        }
        return Page.FromBuffer(buffer);
    }

    private void InitializeHeader()
    {
        _catalogRootPageId = 0;
        _freeListHeadPageId = 0;
        _lastCheckpointLsn = 0;
        var header = Page.CreateNew(0, PageType.Header);
        WriteHeaderFields(header);
        WritePage(header);
        _stream.Flush(true);
    }

    private void LoadHeader()
    {
        var header = ReadPageRaw(0);
        var span = header.Buffer.AsSpan();
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(OffFormatVersion, 2));
        if (version != FormatVersion)
            throw new EmbeddedDataCorruptionException($"Unsupported format version {version}");
        _catalogRootPageId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(OffCatalogRoot, 4));
        _freeListHeadPageId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(OffFreeListHead, 4));
        _lastCheckpointLsn = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(OffLastCheckpointLsn, 8));
    }

    private void WriteHeader()
    {
        var header = ReadPageRaw(0);
        WriteHeaderFields(header);
        WritePage(header);
    }

    private void WriteHeaderFields(Page header)
    {
        var span = header.Buffer.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(OffFormatVersion, 2), FormatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffCatalogRoot, 4), _catalogRootPageId);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffFreeListHead, 4), _freeListHeadPageId);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(OffLastCheckpointLsn, 8), _lastCheckpointLsn);
    }
}
