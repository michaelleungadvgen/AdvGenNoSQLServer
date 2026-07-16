// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Embedded.Storage;

/// <summary>
/// Low-level page storage abstraction. Page 0 is reserved for the file header;
/// allocatable page ids start at 1. Implementations are not required to be thread-safe;
/// the engine serializes access.
/// </summary>
public interface IPageStore : IDisposable
{
    /// <summary>Total number of pages currently in the store (including page 0).</summary>
    uint PageCount { get; }

    /// <summary>Allocates a page, reusing the free list before growing the store.</summary>
    uint AllocatePage(PageType type);

    /// <summary>Reads a page. Throws <see cref="EmbeddedDataCorruptionException"/> on bad checksum.</summary>
    Page ReadPage(uint pageId);

    /// <summary>Seals and persists a page.</summary>
    void WritePage(Page page);

    /// <summary>Returns a page to the free list.</summary>
    void FreePage(uint pageId);

    /// <summary>Flushes buffered writes; when <paramref name="toDisk"/> is true, fsyncs.</summary>
    void Flush(bool toDisk);
}
