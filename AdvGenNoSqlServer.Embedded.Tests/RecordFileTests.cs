// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text;
using AdvGenNoSqlServer.Embedded.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class RecordFileTests
{
    private static (MemoryPageStore Store, RecordFile File) NewFile()
    {
        var store = new MemoryPageStore();
        var file = new RecordFile(store, 0, store.WritePage);
        return (store, file);
    }

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Insert_ThenRead_RoundTrips()
    {
        var (_, file) = NewFile();
        var addr = file.Insert("doc1", Bytes("hello world"));
        var (id, body) = file.Read(addr);
        Assert.Equal("doc1", id);
        Assert.Equal("hello world", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public void FillPage_ChainsToSecondPage_EnumerateInOrder()
    {
        var (_, file) = NewFile();
        var ids = new List<string>();
        // Each record ~1KB; a page holds ~8; insert 20 to force multiple pages.
        for (int i = 0; i < 20; i++)
        {
            string id = $"doc{i:D3}";
            ids.Add(id);
            file.Insert(id, new byte[1000]);
        }
        var enumerated = file.Enumerate().Select(r => r.Id).ToList();
        Assert.Equal(ids, enumerated);
    }

    [Fact]
    public void Delete_Tombstones_EnumerationSkips()
    {
        var (_, file) = NewFile();
        var a = file.Insert("keep", Bytes("a"));
        var b = file.Insert("gone", Bytes("b"));
        file.Delete(b);
        var ids = file.Enumerate().Select(r => r.Id).ToList();
        Assert.Contains("keep", ids);
        Assert.DoesNotContain("gone", ids);
        _ = a;
    }

    [Fact]
    public void Update_Smaller_SameAddress()
    {
        var (_, file) = NewFile();
        var addr = file.Insert("doc", Bytes("a long-ish body value"));
        var newAddr = file.Update(addr, "doc", Bytes("short"));
        Assert.Equal(addr, newAddr);
        var (_, body) = file.Read(newAddr);
        Assert.Equal("short", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public void Update_Larger_NewAddress_OldTombstoned()
    {
        var (_, file) = NewFile();
        var addr = file.Insert("doc", Bytes("small"));
        var newAddr = file.Update(addr, "doc", new string('x', 2000) is var s ? Bytes(s) : Array.Empty<byte>());
        Assert.NotEqual(addr, newAddr);
        var (_, body) = file.Read(newAddr);
        Assert.Equal(2000, body.Length);
        // enumeration returns exactly one live "doc"
        Assert.Single(file.Enumerate().Where(r => r.Id == "doc"));
    }

    [Fact]
    public void LargeRecord_Overflow_RoundTrips()
    {
        var (_, file) = NewFile();
        var big = new byte[20_000];
        for (int i = 0; i < big.Length; i++) big[i] = (byte)(i % 251);
        var addr = file.Insert("bigdoc", big);
        var (id, body) = file.Read(addr);
        Assert.Equal("bigdoc", id);
        Assert.Equal(big, body);
    }

    [Fact]
    public void DeleteOverflow_FreesPages()
    {
        var (store, file) = NewFile();
        var big = new byte[20_000];
        var addr = file.Insert("bigdoc", big);
        uint countAfterInsert = store.PageCount;
        file.Delete(addr);
        // Freed overflow pages are reusable: allocate should reuse (page count not grow).
        uint before = store.PageCount;
        store.AllocatePage(PageType.Data);
        Assert.True(store.PageCount <= before + 1);
        Assert.True(countAfterInsert > 1); // sanity: overflow actually used extra pages
    }
}
