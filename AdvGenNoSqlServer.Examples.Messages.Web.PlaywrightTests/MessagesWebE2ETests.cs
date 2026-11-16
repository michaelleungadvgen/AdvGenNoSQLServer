// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace AdvGenNoSqlServer.Examples.Messages.Web.PlaywrightTests;

[CollectionDefinition(Name)]
public class MessagesWebCollection : ICollectionFixture<MessagesWebFixture>
{
    public const string Name = "Messages Web E2E";
}

[Collection(MessagesWebCollection.Name)]
public class MessagesWebE2ETests
{
    private readonly MessagesWebFixture _fixture;

    public MessagesWebE2ETests(MessagesWebFixture fixture) => _fixture = fixture;

    private static string UniqueText() => "web-e2e-" + Guid.NewGuid().ToString("N");

    private async Task<IPage> NewConnectedPageAsync()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await page.GotoAsync(MessagesWebFixture.BaseUrl);
        // Wait for the Blazor circuit to connect to the cache-mode server.
        await Expect(page.GetByText("Connected to")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        return page;
    }

    private static async Task SendAsync(IPage page, string text)
    {
        var input = page.GetByLabel("Message");
        var send = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send", Exact = true });

        // The page polls every 2s and re-renders, which can reset the input mid-fill.
        // Retry fill + explicit input event until the Send button enables.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await input.FillAsync(text);
            await input.DispatchEventAsync("input");
            try
            {
                await Expect(send).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 2500 });
                await send.ClickAsync();
                return;
            }
            catch (PlaywrightException)
            {
                // re-render likely cleared the input; retry
            }
        }
        throw new TimeoutException("Send button never enabled; message input binding did not update.");
    }

    [Fact]
    public async Task PageLoads_ShowsConnectedStatus()
    {
        var page = await NewConnectedPageAsync();
        await Expect(page.GetByText("messages live in server memory")).ToBeVisibleAsync();
        await page.CloseAsync();
    }

    [Fact]
    public async Task SendMessage_AppearsOnBoard()
    {
        var page = await NewConnectedPageAsync();
        var text = UniqueText();
        await SendAsync(page, text);
        await Expect(page.GetByText(text)).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await page.CloseAsync();
    }

    [Fact]
    public async Task MultipleMessages_AllAppear()
    {
        var page = await NewConnectedPageAsync();
        var first = UniqueText();
        var second = UniqueText();
        await SendAsync(page, first);
        await Expect(page.GetByText(first)).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await SendAsync(page, second);
        await Expect(page.GetByText(second)).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await page.CloseAsync();
    }

    [Fact]
    public async Task Clear_RemovesMessages()
    {
        var page = await NewConnectedPageAsync();
        var text = UniqueText();
        await SendAsync(page, text);
        await Expect(page.GetByText(text)).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Clear all", Exact = true }).ClickAsync();
        await Expect(page.GetByText(text)).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await page.CloseAsync();
    }
}