// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Authentication;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class FileUserStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "advgen-userstore-" + Guid.NewGuid().ToString("N"));
    private string PathFor() => Path.Combine(_dir, "users.json");

    public FileUserStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Roundtrip_SaveThenLoad_PreservesUsers()
    {
        var store = new FileUserStore(PathFor());
        var users = new List<PersistedUser>
        {
            new() { Username = "admin", PasswordHash = "h1", Salt = "s1", Role = "admin", CreatedAt = DateTime.UtcNow },
            new() { Username = "bob", PasswordHash = "h2", Salt = "s2", Role = "readonly", CreatedAt = DateTime.UtcNow },
        };
        store.Save(users);

        var loaded = new FileUserStore(PathFor()).Load();
        Assert.Equal(2, loaded.Count);
        var bob = loaded.Single(u => u.Username == "bob");
        Assert.Equal("readonly", bob.Role);
        Assert.Equal("h2", bob.PasswordHash);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
        => Assert.Empty(new FileUserStore(PathFor()).Load());

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty_AndBacksUp()
    {
        File.WriteAllText(PathFor(), "{ this is not valid json");
        var loaded = new FileUserStore(PathFor()).Load();
        Assert.Empty(loaded);
        Assert.Contains(Directory.GetFiles(_dir), f => f.Contains("users.json.corrupt-"));
    }

    [Fact]
    public void Save_IsAtomic_LeavesNoTempFile()
    {
        var store = new FileUserStore(PathFor());
        store.Save(new List<PersistedUser> { new() { Username = "a", PasswordHash = "h", Salt = "s", Role = "admin", CreatedAt = DateTime.UtcNow } });
        Assert.DoesNotContain(Directory.GetFiles(_dir), f => f.EndsWith(".tmp"));
        Assert.True(File.Exists(PathFor()));
    }
}
