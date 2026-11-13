// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace AdvGenNoSqlServer.Admin.PlaywrightTests;

[CollectionDefinition(Name)]
public class AdminCollection : ICollectionFixture<AdminFixture>
{
    public const string Name = "Admin WASM E2E";
}

[Collection(AdminCollection.Name)]
public class AdminE2ETests
{
    private readonly AdminFixture _fixture;

    public AdminE2ETests(AdminFixture fixture) => _fixture = fixture;

    // ---------- Login / logout ----------

    [Fact]
    public async Task Login_ValidCredentials_ShowsDashboard()
    {
        var page = await LoginAsync();
        await Expect(page.GetByText("Total Documents", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("Server Information")).ToBeVisibleAsync();
        await Expect(page.GetByText("Quick Actions")).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Login_WrongPassword_ShowsError()
    {
        var page = await _fixture.NewPageAsync();
        await page.GotoAsync(AdminFixture.BaseUrl + "/login");
        await page.GetByLabel("Server URL").WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await page.GetByLabel("Server URL").FillAsync($"localhost:{AdminFixture.TcpPort}");
        await page.GetByLabel("Username").FillAsync("admin");
        await page.GetByLabel("Password").FillAsync("definitely-wrong");
        await page.Locator("form").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Login", Exact = true }).ClickAsync();
        await ExpectSnackbar(page, "Invalid credentials");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Logout_ReturnsToLogin()
    {
        var page = await LoginAsync();
        await page.Locator(".mud-appbar button").Last.ClickAsync();
        await ExpectSnackbar(page, "Logged out successfully");
        await Expect(page.GetByLabel("Server URL")).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task DeepLink_Unauthenticated_ShowsDisconnected()
    {
        var page = await _fixture.NewPageAsync();
        await page.GotoAsync(AdminFixture.BaseUrl + "/collections");
        await Expect(page.GetByText("Disconnected", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 60000 });
        await page.Context.CloseAsync();
    }

    // ---------- Databases ----------

    [Fact]
    public async Task Database_CreateAndSelect()
    {
        var page = await LoginAsync();
        var name = UniqueName("db");
        await page.Locator("button[title='Create new database']").ClickAsync();
        await FillDialogAsync(page, "Enter the name for the new database", "Database Name", name);
        await ExpectSnackbar(page, $"Database '{name}' created successfully");
        await Expect(page.Locator(".mud-appbar .mud-select input")).ToHaveValueAsync(name);
        await page.Context.CloseAsync();
    }

    // ---------- Collections ----------

    [Fact]
    public async Task Collections_CreateAndDelete()
    {
        var page = await LoginAsync();
        var name = UniqueName("coll");
        await CreateCollectionAsync(page, name);

        var row = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name });
        await row.Locator("button").Last.ClickAsync();
        await ConfirmDeleteAsync(page);
        await ExpectSnackbar(page, $"Collection '{name}' deleted");
        await Expect(row).ToHaveCountAsync(0);
        await page.Context.CloseAsync();
    }

    // ---------- Documents ----------

    [Fact]
    public async Task Documents_CreateViewEditDelete()
    {
        var page = await LoginAsync();
        var collection = UniqueName("docs");
        await CreateCollectionAsync(page, collection);

        await NavLink(page, "Documents").ClickAsync();
        await SelectAsync(page, "Collection", collection);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New", Exact = true }).ClickAsync();

        var createDialog = Dialog(page, "Create Document");
        await createDialog.GetByLabel("Document Data (JSON)").FillAsync("""{"name":"beta","value":1}""");
        await createDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create", Exact = true }).ClickAsync();
        await ExpectSnackbar(page, "Document created with ID:");

        var row = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = "beta" });
        await Expect(row).ToBeVisibleAsync();

        await row.Locator("button").Nth(0).ClickAsync();
        var viewDialog = Dialog(page, "Document:");
        await Expect(viewDialog).ToContainTextAsync("beta");
        await viewDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close", Exact = true }).ClickAsync();

        await row.Locator("button").Nth(1).ClickAsync();
        var editDialog = Dialog(page, "Edit Document");
        await editDialog.GetByLabel("Document Data (JSON)").FillAsync("""{"name":"beta","value":2}""");
        await editDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Update", Exact = true }).ClickAsync();
        await ExpectSnackbar(page, "Document updated successfully");

        await row.Locator("button").Nth(0).ClickAsync();
        await Expect(Dialog(page, "Document:")).ToContainTextAsync("\"value\": 2");
        await Dialog(page, "Document:").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close", Exact = true }).ClickAsync();

        await row.Locator("button").Nth(2).ClickAsync();
        await ConfirmDeleteAsync(page);
        await ExpectSnackbar(page, "deleted");
        await Expect(row).ToHaveCountAsync(0);
        await page.Context.CloseAsync();
    }

    // ---------- Query ----------

    [Fact]
    public async Task Query_Execute_ShowsSuccess()
    {
        var page = await LoginAsync();
        await NavLink(page, "Query").ClickAsync();
        await page.GetByLabel("Query (JSON format)").FillAsync("""{"name":"beta"}""");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Execute Query", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Success", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await page.Context.CloseAsync();
    }

    // ---------- Users & roles ----------

    [Fact]
    public async Task Users_CreateChangeRoleResetPasswordDelete()
    {
        var page = await LoginAsync();
        var user = UniqueName("user");
        await NavLink(page, "Users & Roles").ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New User", Exact = true }).ClickAsync();

        var createDialog = Dialog(page, "New User");
        await createDialog.GetByLabel("Username").FillAsync(user);
        await createDialog.GetByLabel("Password (min 6 chars)").FillAsync("Passw0rd!");
        await createDialog.Locator(".mud-select div.mud-input-slot").ClickAsync();
        await page.Locator(".mud-popover .mud-list-item").Filter(new LocatorFilterOptions { HasText = "readwrite" }).First.ClickAsync();
        await createDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create", Exact = true }).ClickAsync();
        await ExpectSnackbar(page, $"User '{user}' created");

        var row = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = user });
        await Expect(row).ToContainTextAsync("readwrite");

        await row.Locator("button[title='Change role']").ClickAsync();
        var roleDialog = Dialog(page, $"Role — {user}");
        await roleDialog.Locator(".mud-select div.mud-input-slot").ClickAsync();
        await page.Locator(".mud-popover .mud-list-item").Filter(new LocatorFilterOptions { HasText = "readonly" }).First.ClickAsync();
        await roleDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save", Exact = true }).ClickAsync();
        await ExpectSnackbar(page, $"Role updated for '{user}'");
        await Expect(row).ToContainTextAsync("readonly");

        await row.Locator("button[title='Reset password']").ClickAsync();
        var pwDialog = Dialog(page, $"Password — {user}");
        await pwDialog.GetByLabel("New password (min 6 chars)").FillAsync("NewPass456!");
        await pwDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Reset", Exact = true }).ClickAsync();
        await ExpectSnackbar(page, $"Password reset for '{user}'");

        await row.Locator("button[title='Delete user']").ClickAsync();
        var deleteDialog = Dialog(page, "Delete User");
        await deleteDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();
        await ExpectSnackbar(page, $"User '{user}' deleted");
        await Expect(row).ToHaveCountAsync(0);
        await page.Context.CloseAsync();
    }

    // ---------- Settings / About ----------

    [Fact]
    public async Task Settings_Save_PersistsToLocalStorage()
    {
        var page = await LoginAsync();
        await NavLink(page, "Settings").ClickAsync();
        await page.GetByLabel("Default Server URL").FillAsync($"localhost:{AdminFixture.TcpPort}");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save", Exact = true }).First.ClickAsync();
        await ExpectSnackbar(page, "Connection settings saved");
        var stored = await page.EvaluateAsync<string>("localStorage.getItem('advgen-admin-settings')");
        Assert.Contains($"localhost:{AdminFixture.TcpPort}", stored);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task About_ShowsVersion()
    {
        var page = await LoginAsync();
        await NavLink(page, "About").ClickAsync();
        await Expect(page.GetByText("Version 1.0.0")).ToBeVisibleAsync();
        await Expect(page.GetByText("Technology Stack")).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    // ---------- Helpers ----------

    private static string UniqueName(string kind) => $"e2e_{kind}_{Guid.NewGuid():N}"[..24];

    private async Task<IPage> LoginAsync()
    {
        var page = await _fixture.NewPageAsync();
        await page.GotoAsync(AdminFixture.BaseUrl + "/login");
        // The WASM app renders nothing until boot completes; the login form is the ready marker.
        await page.GetByLabel("Server URL").WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await page.GetByLabel("Server URL").FillAsync($"localhost:{AdminFixture.TcpPort}");
        await page.GetByLabel("Username").FillAsync("admin");
        await page.GetByLabel("Password").FillAsync("admin123");
        await page.Locator("form").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Login", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Dashboard", Exact = true }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Expect(page.GetByText("Connected", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        return page;
    }

    private static ILocator Dialog(IPage page, string titleText) =>
        page.Locator(".mud-dialog").Filter(new LocatorFilterOptions { HasText = titleText });

    // Nav links share names with the Dashboard "Quick Actions" anchors, so scope to the nav menu.
    private static ILocator NavLink(IPage page, string name) =>
        page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = name, Exact = true });

    private static async Task FillDialogAsync(IPage page, string dialogText, string label, string value)
    {
        var dialog = Dialog(page, dialogText);
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await dialog.GetByLabel(label).FillAsync(value);
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "OK", Exact = true }).ClickAsync();
    }

    private static async Task ConfirmDeleteAsync(IPage page)
    {
        var dialog = Dialog(page, "Confirm Delete");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();
    }

    private static Task ExpectSnackbar(IPage page, string text) =>
        Expect(page.Locator(".mud-snackbar").Filter(new LocatorFilterOptions { HasText = text }).First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

    private static async Task SelectAsync(IPage page, string label, string itemText)
    {
        // MudSelect's label is not linked to its input, so GetByLabel fails; click the input directly.
        var select = page.Locator(".mud-select").Filter(new LocatorFilterOptions { HasText = label });
        await select.Locator("input").ClickAsync();
        await page.Locator(".mud-popover .mud-list-item")
            .Filter(new LocatorFilterOptions { HasText = itemText }).First.ClickAsync();
    }

    private static async Task CreateCollectionAsync(IPage page, string name)
    {
        await NavLink(page, "Collections").ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New Collection", Exact = true }).ClickAsync();
        await FillDialogAsync(page, "Enter the name for the new collection", "Collection Name", name);
        await ExpectSnackbar(page, $"Collection '{name}' created successfully");
        await Expect(page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name })).ToBeVisibleAsync();
    }
}