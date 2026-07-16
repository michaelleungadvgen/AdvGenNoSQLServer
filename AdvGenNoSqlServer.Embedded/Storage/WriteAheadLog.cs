// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Buffers.Binary;
using System.IO.Hashing;

namespace AdvGenNoSqlServer.Embedded.Storage;

/// <summary>
/// Append-only write-ahead log. Mutating operations <see cref="Append"/> dirty page
/// images then <see cref="Commit"/> (which fsyncs). Only committed batches are visible to
/// <see cref="ReadCommittedPages"/>; a torn or corrupt tail is silently ignored so recovery
/// is safe. All integers are little-endian. Single-writer; the engine serializes access.
/// </summary>
public sealed class WriteAheadLog : IDisposable
{
    private const byte FrameTypePage = 1;
    private const byte FrameTypeCommit = 2;
    private const int PageFrameSize = 1 + 8 + 4 + Page.PageSize + 4; // type,lsn,pageId,image,crc
    private const int CommitFrameSize = 1 + 8 + 4;                   // type,lsn,crc

    private readonly string _walPath;
    private readonly FileStream _stream;
    private readonly List<(uint PageId, byte[] Image)> _pending = new();
    private ulong _nextLsn;
    private ulong _lastCommittedLsn;

    /// <summary>Opens or creates the WAL file at the given path.</summary>
    public WriteAheadLog(string walPath)
    {
        _walPath = walPath;
        _stream = new FileStream(walPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        // Scan existing committed frames to resume the LSN counter.
        ScanExisting();
        _stream.Seek(0, SeekOrigin.End);
    }

    /// <summary>Current on-disk size of the WAL in bytes.</summary>
    public long SizeBytes => _stream.Length;

    /// <summary>The LSN of the last committed batch (0 if none).</summary>
    public ulong LastCommittedLsn => _lastCommittedLsn;

    /// <summary>Buffers a dirty page image for the current (uncommitted) batch.</summary>
    public void Append(Page page)
    {
        page.Seal();
        _pending.Add((page.PageId, (byte[])page.Buffer.Clone()));
    }

    /// <summary>Writes all buffered frames plus a commit frame and fsyncs.</summary>
    public void Commit()
    {
        if (_pending.Count == 0)
            return;

        _stream.Seek(0, SeekOrigin.End);
        foreach (var (pageId, image) in _pending)
        {
            ulong lsn = _nextLsn++;
            var frame = new byte[PageFrameSize];
            frame[0] = FrameTypePage;
            BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(1, 8), lsn);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(9, 4), pageId);
            image.CopyTo(frame.AsSpan(13, Page.PageSize));
            uint crc = Crc32.HashToUInt32(frame.AsSpan(0, PageFrameSize - 4));
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(PageFrameSize - 4, 4), crc);
            _stream.Write(frame, 0, PageFrameSize);
        }

        ulong commitLsn = _nextLsn++;
        var commit = new byte[CommitFrameSize];
        commit[0] = FrameTypeCommit;
        BinaryPrimitives.WriteUInt64LittleEndian(commit.AsSpan(1, 8), commitLsn);
        uint commitCrc = Crc32.HashToUInt32(commit.AsSpan(0, CommitFrameSize - 4));
        BinaryPrimitives.WriteUInt32LittleEndian(commit.AsSpan(CommitFrameSize - 4, 4), commitCrc);
        _stream.Write(commit, 0, CommitFrameSize);

        _stream.Flush(true);
        _lastCommittedLsn = commitLsn;
        _pending.Clear();
    }

    /// <summary>Drops buffered frames from an operation that failed before commit.</summary>
    public void DiscardUncommitted() => _pending.Clear();

    /// <summary>
    /// Returns the latest committed image of every page, in commit order (later wins).
    /// Uncommitted trailing frames and any torn/corrupt tail are ignored.
    /// </summary>
    public IReadOnlyDictionary<uint, byte[]> ReadCommittedPages()
    {
        var result = new Dictionary<uint, byte[]>();
        _stream.Flush();
        long length = _stream.Length;
        var all = new byte[length];
        _stream.Seek(0, SeekOrigin.Begin);
        int total = 0;
        while (total < length)
        {
            int n = _stream.Read(all, total, (int)(length - total));
            if (n == 0) break;
            total += n;
        }

        var pending = new List<(uint PageId, byte[] Image)>();
        long pos = 0;
        while (pos < total)
        {
            byte type = all[pos];
            if (type == FrameTypePage)
            {
                if (pos + PageFrameSize > total) break; // torn tail
                var span = all.AsSpan((int)pos, PageFrameSize);
                uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(PageFrameSize - 4, 4));
                uint actualCrc = Crc32.HashToUInt32(span.Slice(0, PageFrameSize - 4));
                if (storedCrc != actualCrc) break; // corrupt: discard this batch and everything after
                uint pageId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(9, 4));
                var image = span.Slice(13, Page.PageSize).ToArray();
                pending.Add((pageId, image));
                pos += PageFrameSize;
            }
            else if (type == FrameTypeCommit)
            {
                if (pos + CommitFrameSize > total) break; // torn tail
                var span = all.AsSpan((int)pos, CommitFrameSize);
                uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(CommitFrameSize - 4, 4));
                uint actualCrc = Crc32.HashToUInt32(span.Slice(0, CommitFrameSize - 4));
                if (storedCrc != actualCrc) break;
                foreach (var (pageId, image) in pending)
                    result[pageId] = image;
                pending.Clear();
                pos += CommitFrameSize;
            }
            else
            {
                break; // unknown frame type: stop
            }
        }

        _stream.Seek(0, SeekOrigin.End);
        return result;
    }

    /// <summary>Empties the WAL after a checkpoint.</summary>
    public void Truncate()
    {
        _stream.SetLength(0);
        _stream.Flush(true);
        _pending.Clear();
    }

    private void ScanExisting()
    {
        // Recompute _nextLsn and _lastCommittedLsn from the highest valid frame lsn.
        long length = _stream.Length;
        if (length == 0) { _nextLsn = 1; return; }

        var all = new byte[length];
        _stream.Seek(0, SeekOrigin.Begin);
        int total = 0;
        while (total < length)
        {
            int n = _stream.Read(all, total, (int)(length - total));
            if (n == 0) break;
            total += n;
        }

        ulong maxLsn = 0;
        long pos = 0;
        while (pos < total)
        {
            byte type = all[pos];
            if (type == FrameTypePage)
            {
                if (pos + PageFrameSize > total) break;
                var span = all.AsSpan((int)pos, PageFrameSize);
                uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(PageFrameSize - 4, 4));
                uint actualCrc = Crc32.HashToUInt32(span.Slice(0, PageFrameSize - 4));
                if (storedCrc != actualCrc) break;
                maxLsn = Math.Max(maxLsn, BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(1, 8)));
                pos += PageFrameSize;
            }
            else if (type == FrameTypeCommit)
            {
                if (pos + CommitFrameSize > total) break;
                var span = all.AsSpan((int)pos, CommitFrameSize);
                uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(CommitFrameSize - 4, 4));
                uint actualCrc = Crc32.HashToUInt32(span.Slice(0, CommitFrameSize - 4));
                if (storedCrc != actualCrc) break;
                ulong lsn = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(1, 8));
                maxLsn = Math.Max(maxLsn, lsn);
                _lastCommittedLsn = lsn;
                pos += CommitFrameSize;
            }
            else break;
        }
        _nextLsn = maxLsn + 1;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stream.Flush(true);
        _stream.Dispose();
    }
}
