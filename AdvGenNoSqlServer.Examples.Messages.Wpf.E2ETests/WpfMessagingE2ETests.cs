// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.IO;
using AdvGenNoSqlServer.Examples.Messages;

namespace AdvGenNoSqlServer.Examples.Messages.Wpf.E2ETests;

[CollectionDefinition(Name)]
public class WpfMessagingCollection : ICollectionFixture<WpfMessagingFixture>
{
    public const string Name = "WPF Messaging E2E";
}

[Collection(WpfMessagingCollection.Name)]
public class WpfMessagingE2ETests
{
    private readonly WpfMessagingFixture _fixture;

    public WpfMessagingE2ETests(WpfMessagingFixture fixture) => _fixture = fixture;

    private static string UniqueText() => "wpf-e2e-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Connect_ShowsConnectedStatus()
    {
        await _fixture.ConnectAsync();
        var status = _fixture.GetStatusText();
        Assert.Contains($"localhost:{WpfMessagingFixture.TcpPort}", status);
        Assert.Contains("cache mode", status);
        Assert.DoesNotContain("Connect failed", status);
    }

    [Fact]
    public async Task SendMessage_AppearsInList()
    {
        await _fixture.ConnectAsync();
        var text = UniqueText();
        await _fixture.SendMessageAsync(text);
        await _fixture.WaitForTextAsync(text, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task MultipleMessages_AllAppear()
    {
        await _fixture.ConnectAsync();
        var first = UniqueText();
        var second = UniqueText();
        await _fixture.SendMessageAsync(first);
        await _fixture.WaitForTextAsync(first, TimeSpan.FromSeconds(20));
        await _fixture.SendMessageAsync(second);
        await _fixture.WaitForTextAsync(second, TimeSpan.FromSeconds(20));
        Assert.True(_fixture.TextExists(first));
        Assert.True(_fixture.TextExists(second));
    }

    [Fact]
    public async Task Clear_RemovesMessages()
    {
        await _fixture.ConnectAsync();
        var text = UniqueText();
        await _fixture.SendMessageAsync(text);
        await _fixture.WaitForTextAsync(text, TimeSpan.FromSeconds(20));

        _fixture.ClickButton("Clear");
        await _fixture.WaitForTextGoneAsync(text, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task CacheMode_WritesNoDiskFiles()
    {
        await _fixture.ConnectAsync();
        var text = UniqueText();
        await _fixture.SendMessageAsync(text);
        await _fixture.WaitForTextAsync(text, TimeSpan.FromSeconds(20));

        // Cache mode is memory-only: the storage directory must stay empty.
        var entries = Directory.Exists(_fixture.StorageDir)
            ? Directory.EnumerateFileSystemEntries(_fixture.StorageDir).ToArray()
            : Array.Empty<string>();
        Assert.Empty(entries);
    }

    [Fact]
    public async Task CrossClient_MessageVisibleInSharedCache()
    {
        await _fixture.ConnectAsync();
        var text = UniqueText();
        await _fixture.SendMessageAsync(text);
        await _fixture.WaitForTextAsync(text, TimeSpan.FromSeconds(20));

        // A second, independent TCP client must see the same message in the server cache.
        await using var service = new MessageService("localhost", WpfMessagingFixture.TcpPort, "admin", "admin123", useSsl: false, collection: "messages");
        await service.ConnectAsync();
        var messages = await service.ListAsync();
        Assert.Contains(messages, m => m.Text == text);
    }
}