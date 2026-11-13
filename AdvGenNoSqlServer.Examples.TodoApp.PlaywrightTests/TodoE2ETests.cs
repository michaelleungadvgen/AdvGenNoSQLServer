// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace AdvGenNoSqlServer.Examples.TodoApp.PlaywrightTests;

[CollectionDefinition(Name)]
public class TodoAppCollection : ICollectionFixture<TodoAppFixture>
{
    public const string Name = "TodoApp E2E";
}

[Collection(TodoAppCollection.Name)]
public class TodoE2ETests
{
    private readonly TodoAppFixture _fixture;

    public TodoE2ETests(TodoAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Home_ShowsServerConnection()
    {
        var page = await NewPageAsync();
        await Expect(page.Locator(".status")).ToContainTextAsync("localhost:19191");
        await Expect(page.Locator(".status")).ToContainTextAsync("todos");
        await Expect(page.Locator(".error")).ToHaveCountAsync(0);
        await page.CloseAsync();
    }

    [Fact]
    public async Task AddTodo_AppearsInList()
    {
        var page = await NewPageAsync();
        var title = UniqueTitle("add");
        await AddTodoAsync(page, title);
        await Expect(TodoRow(page, title).Locator(".title")).ToHaveTextAsync(title);

        await DeleteTodoAsync(page, title);
        await page.CloseAsync();
    }

    [Fact]
    public async Task ToggleTodo_PersistsAcrossReload()
    {
        var page = await NewPageAsync();
        var title = UniqueTitle("toggle");
        await AddTodoAsync(page, title);

        await TodoRow(page, title).Locator("input[type=checkbox]").CheckAsync();
        await Expect(TodoRow(page, title).Locator("input[type=checkbox]")).ToBeCheckedAsync();

        // Reload: the completed state must come back from the server, not the circuit.
        await page.ReloadAsync();
        await Expect(TodoRow(page, title).Locator("input[type=checkbox]")).ToBeCheckedAsync();

        await DeleteTodoAsync(page, title);
        await page.CloseAsync();
    }

    [Fact]
    public async Task DeleteTodo_RemovesFromList()
    {
        var page = await NewPageAsync();
        var title = UniqueTitle("delete");
        await AddTodoAsync(page, title);

        await DeleteTodoAsync(page, title);
        await Expect(page.Locator(".empty")).ToBeVisibleAsync();
        await page.CloseAsync();
    }

    private async Task<IPage> NewPageAsync()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await page.GotoAsync(TodoAppFixture.BaseUrl);
        // Wait for the interactive circuit (the marker renders only after the first
        // interactive render, never during prerender) before driving any inputs.
        await page.Locator("#interactive-ready").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30000
        });
        await Expect(page.Locator(".status")).ToBeVisibleAsync();
        return page;
    }

    private static string UniqueTitle(string kind) => $"E2E {kind} {Guid.NewGuid():N}"[..40];

    private static ILocator TodoRow(IPage page, string title) =>
        page.Locator(".todo-list li").Filter(new LocatorFilterOptions { HasText = title });

    private static async Task AddTodoAsync(IPage page, string title)
    {
        await page.GetByPlaceholder("What needs to be done?").FillAsync(title);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();
        await Expect(TodoRow(page, title)).ToBeVisibleAsync();
    }

    private static async Task DeleteTodoAsync(IPage page, string title)
    {
        await TodoRow(page, title)
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" })
            .ClickAsync();
        await Expect(TodoRow(page, title)).ToHaveCountAsync(0);
    }
}