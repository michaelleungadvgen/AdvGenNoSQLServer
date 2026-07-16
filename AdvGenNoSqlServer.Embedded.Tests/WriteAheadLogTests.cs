// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text;
using AdvGenNoSqlServer.Embedded.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class WriteAheadLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"));

    public WriteAheadLogTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string WalPath() => Path.Combine(_dir, "test.wal");

    private static Page MakePage(uint id, string content)
    {
        var page = Page.CreateNew(id, PageType.Data);
        page.TryAddRecord(Encoding.UTF8.GetBytes(content));
        return page;
    }

    [Fact]
    public void CommittedPages_ReplayedOnFreshInstance()
    {
        var path = WalPath();
        using (var wal = new WriteAheadLog(path))
        {
            wal.Append(MakePage(1, "one"));
            wal.Append(MakePage(2, "two"));
            wal.Append(MakePage(3, "three"));
            wal.Commit();
        }
        using (var wal = new WriteAheadLog(path))
        {
            var pages = wal.ReadCommittedPages();
            Assert.Equal(3, pages.Count);
            Assert.Contains(1u, pages.Keys);
            Assert.Contains(2u, pages.Keys);
            Assert.Contains(3u, pages.Keys);
        }
    }

    [Fact]
    public void UncommittedPages_NotReplayed()
    {
        var path = WalPath();
        using (var wal = new WriteAheadLog(path))
        {
            wal.Append(MakePage(1, "one"));
            wal.Append(MakePage(2, "two"));
            // no commit
        }
        using (var wal = new WriteAheadLog(path))
        {
            Assert.Empty(wal.ReadCommittedPages());
        }
    }

    [Fact]
    public void LaterImageWins_AcrossBatches()
    {
        var path = WalPath();
        using (var wal = new WriteAheadLog(path))
        {
            wal.Append(MakePage(7, "old"));
            wal.Commit();
            wal.Append(MakePage(7, "new"));
            wal.Commit();
        }
        using (var wal = new WriteAheadLog(path))
        {
            var pages = wal.ReadCommittedPages();
            var page = Page.FromBuffer(pages[7]);
            Assert.Equal("new", Encoding.UTF8.GetString(page.ReadRecord(0).ToArray()));
        }
    }

    [Fact]
    public void TornTail_ReturnsOnlyCompleteBatches()
    {
        var path = WalPath();
        using (var wal = new WriteAheadLog(path))
        {
            wal.Append(MakePage(1, "committed"));
            wal.Commit();
            wal.Append(MakePage(2, "uncommitted"));
            wal.Commit();
        }
        // Truncate mid-frame in the last batch.
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            fs.SetLength(fs.Length - 10);
        }
        using (var wal = new WriteAheadLog(path))
        {
            var pages = wal.ReadCommittedPages();
            Assert.Contains(1u, pages.Keys);
            Assert.DoesNotContain(2u, pages.Keys);
        }
    }

    [Fact]
    public void CorruptFrame_DiscardsThatBatchAndAfter()
    {
        var path = WalPath();
        using (var wal = new WriteAheadLog(path))
        {
            wal.Append(MakePage(1, "batchA"));
            wal.Commit();
            wal.Append(MakePage(2, "batchB"));
            wal.Commit();
        }
        // Corrupt a byte inside batch B's page frame (after batch A's page+commit frames).
        // Batch A = 8209 (page) + 13 (commit) = 8222; corrupt at 8222 + 50.
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            long pos = 8222 + 50;
            fs.Seek(pos, SeekOrigin.Begin);
            int b = fs.ReadByte();
            fs.Seek(pos, SeekOrigin.Begin);
            fs.WriteByte((byte)(b ^ 0xFF));
        }
        using (var wal = new WriteAheadLog(path))
        {
            var pages = wal.ReadCommittedPages();
            Assert.Contains(1u, pages.Keys);
            Assert.DoesNotContain(2u, pages.Keys);
        }
    }

    [Fact]
    public void Truncate_EmptiesLog()
    {
        var path = WalPath();
        using var wal = new WriteAheadLog(path);
        wal.Append(MakePage(1, "x"));
        wal.Commit();
        Assert.True(wal.SizeBytes > 0);
        wal.Truncate();
        Assert.Equal(0, wal.SizeBytes);
        Assert.Empty(wal.ReadCommittedPages());
    }
}
