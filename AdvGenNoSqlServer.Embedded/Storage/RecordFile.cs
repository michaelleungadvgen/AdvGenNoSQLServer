// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Buffers.Binary;
using System.Text;

namespace AdvGenNoSqlServer.Embedded.Storage;

/// <summary>Location of a record: the data page and its slot index.</summary>
public readonly record struct RecordAddress(uint PageId, int Slot);

/// <summary>
/// Stores opaque (id, body) records for one collection across a chain of data pages.
/// Records too large to fit a single page spill into an overflow-page chain. Deletes
/// tombstone the slot (space is reclaimed only by compaction). All page mutations are
/// routed through a caller-supplied persist delegate so the record file stays agnostic of
/// whether writes go straight to the store or through a WAL.
/// </summary>
public sealed class RecordFile
{
    private const byte FlagOverflow = 0x01;

    private readonly IPageStore _store;
    private readonly Action<Page> _persist;
    private uint _lastPageId;

    /// <summary>
    /// Creates a record file over the given store. <paramref name="firstPageId"/> is the
    /// head of the collection's data-page chain, or 0 if the collection has no pages yet.
    /// </summary>
    public RecordFile(IPageStore store, uint firstPageId, Action<Page> persistPage)
    {
        _store = store;
        _persist = persistPage;
        FirstPageId = firstPageId;
        _lastPageId = firstPageId == 0 ? 0 : FindLastPageId(firstPageId);
    }

    /// <summary>Head of the data-page chain (0 until the first insert). Persist on change.</summary>
    public uint FirstPageId { get; private set; }

    /// <summary>Inserts a record and returns its address.</summary>
    public RecordAddress Insert(string id, ReadOnlySpan<byte> body)
    {
        byte[] inline = EncodeInline(id, body);
        // A fresh page's usable space (FreeBytes) is 8160; account for the 4-byte slot.
        if (inline.Length + 4 <= Page.PageSize - 32 - 4)
        {
            return InsertInline(inline);
        }
        return InsertOverflow(id, body);
    }

    /// <summary>Reads the (id, body) at an address.</summary>
    public (string Id, byte[] Body) Read(RecordAddress addr)
    {
        var page = _store.ReadPage(addr.PageId);
        var record = page.ReadRecord(addr.Slot).ToArray();
        return DecodeRecord(record);
    }

    /// <summary>
    /// Updates a record. If the new encoding fits in place (inline and not larger), the
    /// address is unchanged; otherwise the old record is tombstoned and a new one inserted.
    /// </summary>
    public RecordAddress Update(RecordAddress addr, string id, ReadOnlySpan<byte> body)
    {
        var page = _store.ReadPage(addr.PageId);
        var oldRecord = page.ReadRecord(addr.Slot).ToArray();
        bool oldHadOverflow = (ReadFlags(oldRecord) & FlagOverflow) != 0;

        byte[] inline = EncodeInline(id, body);
        bool fitsInline = inline.Length + 4 <= Page.PageSize - 32 - 4;

        if (fitsInline && !oldHadOverflow && inline.Length <= oldRecord.Length)
        {
            page.OverwriteRecord(addr.Slot, inline);
            _persist(page);
            return addr;
        }

        // Tombstone old (freeing its overflow chain) and insert fresh.
        Delete(addr);
        return Insert(id, body);
    }

    /// <summary>Tombstones a record and frees any overflow pages it owned.</summary>
    public void Delete(RecordAddress addr)
    {
        var page = _store.ReadPage(addr.PageId);
        var record = page.ReadRecord(addr.Slot).ToArray();
        if ((ReadFlags(record) & FlagOverflow) != 0)
        {
            uint overflowId = ReadOverflowPointer(record);
            FreeOverflowChain(overflowId);
        }
        page.DeleteRecord(addr.Slot);
        _persist(page);
    }

    /// <summary>Enumerates all live records in insertion order.</summary>
    public IEnumerable<(RecordAddress Address, string Id, byte[] Body)> Enumerate()
    {
        uint pageId = FirstPageId;
        while (pageId != 0)
        {
            var page = _store.ReadPage(pageId);
            for (int slot = 0; slot < page.SlotCount; slot++)
            {
                if (page.IsSlotDeleted(slot)) continue;
                var record = page.ReadRecord(slot).ToArray();
                var (id, body) = DecodeRecord(record);
                yield return (new RecordAddress(pageId, slot), id, body);
            }
            pageId = page.NextPageId;
        }
    }

    // --- inline path ---

    private RecordAddress InsertInline(byte[] inline)
    {
        if (FirstPageId == 0)
        {
            uint firstId = _store.AllocatePage(PageType.Data);
            FirstPageId = firstId;
            _lastPageId = firstId;
        }

        var page = _store.ReadPage(_lastPageId);
        int slot = page.TryAddRecord(inline);
        if (slot < 0)
        {
            uint newId = _store.AllocatePage(PageType.Data);
            page.NextPageId = newId;
            _persist(page);

            var newPage = _store.ReadPage(newId);
            slot = newPage.TryAddRecord(inline);
            _persist(newPage);
            _lastPageId = newId;
            return new RecordAddress(newId, slot);
        }

        _persist(page);
        return new RecordAddress(_lastPageId, slot);
    }

    // --- overflow path ---

    private RecordAddress InsertOverflow(string id, ReadOnlySpan<byte> body)
    {
        uint firstOverflow = WriteOverflowChain(body);
        byte[] record = EncodeOverflow(id, body.Length, firstOverflow);
        return InsertInline(record);
    }

    private uint WriteOverflowChain(ReadOnlySpan<byte> body)
    {
        // Chunk size = a fresh page's usable space minus one slot entry.
        int chunkSize = Page.PageSize - 32 - 4;
        int offset = 0;
        uint firstId = 0;
        uint prevId = 0;
        while (offset < body.Length)
        {
            int len = Math.Min(chunkSize, body.Length - offset);
            uint id = _store.AllocatePage(PageType.Overflow);
            var page = _store.ReadPage(id);
            page.TryAddRecord(body.Slice(offset, len));
            _persist(page);

            if (firstId == 0) firstId = id;
            if (prevId != 0)
            {
                var prev = _store.ReadPage(prevId);
                prev.NextPageId = id;
                _persist(prev);
            }
            prevId = id;
            offset += len;
        }
        return firstId;
    }

    private void FreeOverflowChain(uint firstOverflowId)
    {
        uint pageId = firstOverflowId;
        while (pageId != 0)
        {
            var page = _store.ReadPage(pageId);
            uint next = page.NextPageId;
            _store.FreePage(pageId);
            pageId = next;
        }
    }

    private byte[] ReadOverflowBody(uint firstOverflowId, int bodyLen)
    {
        var body = new byte[bodyLen];
        int written = 0;
        uint pageId = firstOverflowId;
        while (pageId != 0 && written < bodyLen)
        {
            var page = _store.ReadPage(pageId);
            var chunk = page.ReadRecord(0);
            chunk.Span.CopyTo(body.AsSpan(written));
            written += chunk.Length;
            pageId = page.NextPageId;
        }
        return body;
    }

    // --- encoding ---

    private static byte[] EncodeInline(string id, ReadOnlySpan<byte> body)
    {
        var idBytes = Encoding.UTF8.GetBytes(id);
        using var ms = new MemoryStream();
        WriteVarint(ms, (uint)idBytes.Length);
        ms.Write(idBytes);
        ms.WriteByte(0); // flags
        WriteVarint(ms, (uint)body.Length);
        ms.Write(body);
        return ms.ToArray();
    }

    private static byte[] EncodeOverflow(string id, int bodyLen, uint firstOverflowId)
    {
        var idBytes = Encoding.UTF8.GetBytes(id);
        using var ms = new MemoryStream();
        WriteVarint(ms, (uint)idBytes.Length);
        ms.Write(idBytes);
        ms.WriteByte(FlagOverflow);
        WriteVarint(ms, (uint)bodyLen);
        Span<byte> ptr = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(ptr, firstOverflowId);
        ms.Write(ptr);
        return ms.ToArray();
    }

    private (string Id, byte[] Body) DecodeRecord(byte[] record)
    {
        int pos = 0;
        uint idLen = ReadVarint(record, ref pos);
        string id = Encoding.UTF8.GetString(record, pos, (int)idLen);
        pos += (int)idLen;
        byte flags = record[pos++];
        uint bodyLen = ReadVarint(record, ref pos);
        if ((flags & FlagOverflow) != 0)
        {
            uint overflowId = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(pos, 4));
            return (id, ReadOverflowBody(overflowId, (int)bodyLen));
        }
        var body = new byte[bodyLen];
        Array.Copy(record, pos, body, 0, (int)bodyLen);
        return (id, body);
    }

    private static byte ReadFlags(byte[] record)
    {
        int pos = 0;
        uint idLen = ReadVarint(record, ref pos);
        pos += (int)idLen;
        return record[pos];
    }

    private static uint ReadOverflowPointer(byte[] record)
    {
        int pos = 0;
        uint idLen = ReadVarint(record, ref pos);
        pos += (int)idLen;
        pos++; // flags
        ReadVarint(record, ref pos); // bodyLen
        return BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(pos, 4));
    }

    private uint FindLastPageId(uint firstPageId)
    {
        uint pageId = firstPageId;
        while (true)
        {
            var page = _store.ReadPage(pageId);
            if (page.NextPageId == 0) return pageId;
            pageId = page.NextPageId;
        }
    }

    private static void WriteVarint(Stream s, uint value)
    {
        while (value >= 0x80)
        {
            s.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        s.WriteByte((byte)value);
    }

    private static uint ReadVarint(byte[] buffer, ref int pos)
    {
        uint result = 0;
        int shift = 0;
        while (true)
        {
            byte b = buffer[pos++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return result;
    }
}
