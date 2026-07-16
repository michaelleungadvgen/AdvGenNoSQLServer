// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Buffers.Binary;
using System.IO.Hashing;

namespace AdvGenNoSqlServer.Embedded.Storage;

/// <summary>Type discriminator for a page.</summary>
public enum PageType : byte
{
    /// <summary>File header page (page 0).</summary>
    Header = 0,
    /// <summary>Catalog page (collection/index definitions).</summary>
    Catalog = 1,
    /// <summary>Document data page.</summary>
    Data = 2,
    /// <summary>Overflow page for large records.</summary>
    Overflow = 3,
    /// <summary>Freed page on the free list.</summary>
    Free = 4,
}

/// <summary>
/// An 8 KB slotted page. The 32-byte header lives at the start; records grow up from
/// offset 32; the slot array (4 bytes per slot: offset uint16, length uint16) grows down
/// from the end of the page. A slot with length 0 is a tombstone. All integers are
/// little-endian. Not thread-safe; the engine serializes access.
/// </summary>
public sealed class Page
{
    /// <summary>Fixed page size in bytes.</summary>
    public const int PageSize = 8192;

    private const uint Magic = 0xAD6DB001;
    private const int HeaderSize = 32;
    private const int SlotSize = 4;
    private const int BodyStart = HeaderSize;

    // Header field offsets
    private const int OffMagic = 0;
    private const int OffType = 4;
    private const int OffPageId = 5;
    private const int OffNextPageId = 9;
    private const int OffSlotCount = 13;
    private const int OffFreeBytes = 15;
    private const int OffChecksum = 17;

    /// <summary>The raw 8 KB buffer backing this page.</summary>
    public byte[] Buffer { get; }

    /// <summary>The page's id.</summary>
    public uint PageId { get; private set; }

    /// <summary>The page type.</summary>
    public PageType Type { get; private set; }

    /// <summary>Next page in a chain (0 = none).</summary>
    public uint NextPageId { get; set; }

    /// <summary>Number of slots (including tombstones).</summary>
    public ushort SlotCount { get; private set; }

    /// <summary>Bytes available for a new record body plus its slot entry.</summary>
    public int FreeBytes => SlotArrayStart - _recordsEnd;

    private int _recordsEnd;

    private Page(byte[] buffer)
    {
        Buffer = buffer;
    }

    private int SlotArrayStart => PageSize - SlotCount * SlotSize;

    /// <summary>Creates a new empty page of the given id and type.</summary>
    public static Page CreateNew(uint pageId, PageType type)
    {
        var page = new Page(new byte[PageSize])
        {
            PageId = pageId,
            Type = type,
            NextPageId = 0,
            SlotCount = 0,
        };
        page._recordsEnd = BodyStart;
        return page;
    }

    /// <summary>Reconstructs a page from a raw buffer (does not validate checksum).</summary>
    public static Page FromBuffer(byte[] buffer)
    {
        if (buffer.Length != PageSize)
            throw new ArgumentException($"Page buffer must be {PageSize} bytes", nameof(buffer));

        var span = buffer.AsSpan();
        var page = new Page(buffer)
        {
            Type = (PageType)buffer[OffType],
            PageId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(OffPageId, 4)),
            NextPageId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(OffNextPageId, 4)),
            SlotCount = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(OffSlotCount, 2)),
        };
        ushort freeBytes = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(OffFreeBytes, 2));
        page._recordsEnd = page.SlotArrayStart - freeBytes;
        return page;
    }

    /// <summary>Writes the header and recomputes the checksum. Call before persisting.</summary>
    public void Seal()
    {
        var span = Buffer.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffMagic, 4), Magic);
        Buffer[OffType] = (byte)Type;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffPageId, 4), PageId);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffNextPageId, 4), NextPageId);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(OffSlotCount, 2), SlotCount);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(OffFreeBytes, 2), (ushort)FreeBytes);

        uint checksum = Crc32.HashToUInt32(span.Slice(HeaderSize));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffChecksum, 4), checksum);
    }

    /// <summary>Validates the stored checksum against the current body bytes.</summary>
    public bool Validate()
    {
        var span = Buffer.AsSpan();
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(OffChecksum, 4));
        uint actual = Crc32.HashToUInt32(span.Slice(HeaderSize));
        return stored == actual;
    }

    /// <summary>
    /// Appends a record. Returns the slot index, or -1 if there is not enough free space
    /// for the record body plus its 4-byte slot entry.
    /// </summary>
    public int TryAddRecord(ReadOnlySpan<byte> record)
    {
        int needed = record.Length + SlotSize;
        if (needed > FreeBytes)
            return -1;

        int recordOffset = _recordsEnd;
        record.CopyTo(Buffer.AsSpan(recordOffset, record.Length));
        _recordsEnd += record.Length;

        int slotIndex = SlotCount;
        SlotCount++;
        WriteSlot(slotIndex, (ushort)recordOffset, (ushort)record.Length);
        return slotIndex;
    }

    /// <summary>Reads a record's bytes by slot index. Throws if the slot is tombstoned.</summary>
    public ReadOnlyMemory<byte> ReadRecord(int slot)
    {
        (ushort offset, ushort length) = ReadSlot(slot);
        if (length == 0)
            throw new InvalidOperationException($"Slot {slot} on page {PageId} is deleted");
        return new ReadOnlyMemory<byte>(Buffer, offset, length);
    }

    /// <summary>
    /// Overwrites a record in place. The new record must be no longer than the existing one
    /// (the slot's length is updated to the new, shorter length; trailing bytes are abandoned).
    /// </summary>
    public void OverwriteRecord(int slot, ReadOnlySpan<byte> record)
    {
        (ushort offset, ushort length) = ReadSlot(slot);
        if (record.Length > length)
            throw new ArgumentException("Overwrite record is larger than the existing slot", nameof(record));
        record.CopyTo(Buffer.AsSpan(offset, record.Length));
        WriteSlot(slot, offset, (ushort)record.Length);
    }

    /// <summary>Tombstones a record's slot (length set to 0). Body bytes are retained.</summary>
    public void DeleteRecord(int slot)
    {
        (ushort offset, _) = ReadSlot(slot);
        WriteSlot(slot, offset, 0);
    }

    /// <summary>Returns true if the slot is a tombstone.</summary>
    public bool IsSlotDeleted(int slot)
    {
        (_, ushort length) = ReadSlot(slot);
        return length == 0;
    }

    private int SlotOffset(int slot)
    {
        if (slot < 0 || slot >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));
        return PageSize - (slot + 1) * SlotSize;
    }

    private (ushort Offset, ushort Length) ReadSlot(int slot)
    {
        int at = SlotOffset(slot);
        var span = Buffer.AsSpan();
        return (
            BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(at, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(at + 2, 2)));
    }

    private void WriteSlot(int slot, ushort offset, ushort length)
    {
        int at = PageSize - (slot + 1) * SlotSize;
        var span = Buffer.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(at, 2), offset);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(at + 2, 2), length);
    }
}
