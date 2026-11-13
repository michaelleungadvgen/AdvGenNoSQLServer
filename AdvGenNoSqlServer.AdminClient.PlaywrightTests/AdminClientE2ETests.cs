// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace AdvGenNoSqlServer.AdminClient.PlaywrightTests;

[CollectionDefinition(Name)]
public class AdminClientCollection : ICollectionFixture<AdminClientFixture>
{
    public const string Name = "AdminClient E2E";
}

[Collection(AdminClientCollection.Name)]
public class AdminClientE2ETests
{
    private readonly AdminClientFixture _fixture;

    public AdminClientE2ETests(AdminClientFixture fixture) => _fixture = fixture;

    // ---------- Login / logout ----------

    [Fact]
    public async Task Login_ValidCredentials_ShowsDashboard()
    {
        var page = await LoginAsync();
        await Expect(page.Locator(".mud-appbar")).ToContainTextAsync($"localhost:{AdminClientFixture.TcpPort}");
        await Expect(page.Locator(".mud-appbar")).ToContainTextAsync("admin");
        await Expect(page.GetByText("Version", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("Total Documents", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Login_WrongPassword_ShowsError()
    {
        var page = await NewLoginPageAsync();
        await page.GetByLabel("Port").FillAsync(AdminClientFixture.TcpPort.ToString());
        await page.GetByLabel("Username").FillAsync("admin");
        await page.GetByLabel("Password").FillAsync("definitely-wrong");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Connect", Exact = true }).ClickAsync();
        await ExpectSnackbar(page, "Connection failed");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Login_EmptyFields_ShowsValidation()
    {
        var page = await NewLoginPageAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Connect", Exact = true }).ClickAsync();
        await ExpectSnackbar(page, "All fields are required");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Logout_ReturnsToLogin()
    {
        var page = await LoginAsync();
        await page.Locator(".mud-appbar button").Last.ClickAsync();
        await ExpectSnackbar(page, "Logged out");
        await Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Connect", Exact = true })).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    // ---------- Collections ----------

    [Fact]
    public async Task Collections_CreateBrowseDelete()
    {
        var page = await LoginAsync();
        var name = UniqueName("coll");
        await CreateCollectionAsync(page, name);

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = name, Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = name })).ToBeVisibleAsync();
        await Expect(page.GetByText("No documents found.")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Collections", Exact = true }).ClickAsync();
        await DropCollectionAsync(page, name);
        await page.Context.CloseAsync();
    }

    // ---------- Documents ----------

    [Fact]
    public async Task Documents_InsertViewEditDelete()
    {
        var page = await LoginAsync();
        var collection = UniqueName("docs");
        await CreateCollectionAsync(page, collection);
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = collection, Exact = true }).ClickAsync();

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Insert", Exact = true }).ClickAsync();
        await FillDialogAsync(page, "Insert Document", "JSON", """{"name":"alpha","value":1}""");
        await ExpectSnackbar(page, "Document inserted with ID:");
        var row = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = "alpha" });
        await Expect(row).ToBeVisibleAsync();

        await row.Locator("button[title='View']").ClickAsync();
        var viewDialog = Dialog(page, "Document");
        await Expect(viewDialog).ToContainTextAsync("alpha");
        await viewDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close", Exact = true }).ClickAsync();

        await row.Locator("button[title='Edit']").ClickAsync();
        var editDialog = Dialog(page, "Edit Document");
        var json = await editDialog.GetByLabel("JSON").InputValueAsync();
        var id = JsonDocument.Parse(json).RootElement.GetProperty("_id").GetString();
        await editDialog.GetByLabel("JSON").FillAsync($$"""{"_id":"{{id}}","name":"alpha","value":2}""");
        await editDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "OK", Exact = true }).ClickAsync();
        await ExpectSnackbar(page, "Document updated");

        await row.Locator("button[title='View']").ClickAsync();
        await Expect(Dialog(page, "Document")).ToContainTextAsync("\"value\":2");
        await Dialog(page, "Document").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close", Exact = true }).ClickAsync();

        await row.Locator("button[title='Delete']").ClickAsync();
        await ConfirmDialogAsync(page, "Delete Document");
        await ExpectSnackbar(page, "Document deleted");
        await Expect(page.GetByText("No documents found.")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Collections", Exact = true }).ClickAsync();
        await DropCollectionAsync(page, collection);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Attachments_UploadDownloadDelete()
    {
        var page = await LoginAsync();
        var collection = UniqueName("attach");
        await CreateCollectionAsync(page, collection);
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = collection, Exact = true }).ClickAsync();
        await InsertDocumentAsync(page, """{"name":"with-file"}""");
        var row = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = "with-file" });
        await Expect(row).ToBeVisibleAsync();

        await row.Locator("button[title='Attachments']").ClickAsync();
        var dialog = Dialog(page, "Attachments for");
        await Expect(dialog.GetByText("No attachments.")).ToBeVisibleAsync();

        var fileName = $"e2e-{Guid.NewGuid():N}.txt";
        var filePath = Path.Combine(Path.GetTempPath(), fileName);
        await File.WriteAllTextAsync(filePath, "attachment payload");
        try
        {
            await dialog.Locator("input[type=file]").SetInputFilesAsync(filePath);
            await ExpectSnackbar(page, $"Uploaded {fileName}");
            var fileRow = dialog.Locator("tr").Filter(new LocatorFilterOptions { HasText = fileName });
            await Expect(fileRow).ToBeVisibleAsync();

            var download = await page.RunAndWaitForDownloadAsync(async () =>
                await fileRow.Locator("button[title='Download']").ClickAsync());
            Assert.Equal(fileName, download.SuggestedFilename);

            await fileRow.Locator("button[title='Delete']").ClickAsync();
            await ConfirmDialogAsync(page, "Delete Attachment");
            await Expect(dialog.GetByText("No attachments.")).ToBeVisibleAsync();
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close", Exact = true }).ClickAsync();
        }
        finally
        {
            File.Delete(filePath);
        }

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Collections", Exact = true }).ClickAsync();
        await DropCollectionAsync(page, collection);
        await page.Context.CloseAsync();
    }

    // ---------- Console ----------

    [Fact]
    public async Task Console_StatsTemplate_ReturnsServerStats()
    {
        var page = await LoginAsync();
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Console", Exact = true }).ClickAsync();
        await SelectAsync(page, "Template", "stats");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send Command", Exact = true }).ClickAsync();
        await Expect(page.Locator("pre")).ToContainTextAsync("uptimeSeconds", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Console_InvalidPayload_ShowsValidation()
    {
        var page = await LoginAsync();
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Console", Exact = true }).ClickAsync();
        var send = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send Command", Exact = true });

        await page.GetByLabel("Command JSON").FillAsync("not-json");
        await send.ClickAsync();
        await ExpectSnackbar(page, "Invalid JSON");

        await page.GetByLabel("Command JSON").FillAsync("""{"foo":1}""");
        await send.ClickAsync();
        await ExpectSnackbar(page, "Payload must be a JSON object");
        await page.Context.CloseAsync();
    }

    // ---------- Users & roles ----------

    [Fact]
    public async Task Users_CreateChangeRoleResetPasswordDelete()
    {
        var page = await LoginAsync();
        var user = UniqueName("user");
        await CreateUserAsync(page, user, "Passw0rd!", "readonly");

        var row = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = user });
        await Expect(row).ToContainTextAsync("readonly");

        await row.Locator("button[title='Change role']").ClickAsync();
        await FillDialogAsync(page, $"Role for {user}", "Role", "readwrite");
        await ExpectSnackbar(page, "Role updated");
        await Expect(row).ToContainTextAsync("readwrite");

        await row.Locator("button[title='Reset password']").ClickAsync();
        await FillDialogAsync(page, $"Reset password for {user}", "New password", "NewPass456!");
        await ExpectSnackbar(page, "Password reset");

        await row.Locator("button[title='Delete']").ClickAsync();
        await ConfirmDialogAsync(page, "Delete User");
        await ExpectSnackbar(page, "User deleted");
        await Expect(row).ToHaveCountAsync(0);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task ReadOnlyUser_RestrictedNavAndCommands()
    {
        var admin = await LoginAsync();
        var user = UniqueName("ro");
        await CreateUserAsync(admin, user, "Passw0rd!", "readonly");

        var page = await LoginAsync(user, "Passw0rd!");
        await Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Users", Exact = true })).ToHaveCountAsync(0);

        // A full-page GotoAsync would tear down the Blazor circuit and log the user out,
        // so navigate client-side by clicking an injected anchor (Blazor intercepts it).
        await page.EvaluateAsync("() => { const a = document.createElement('a'); a.href = 'users'; document.body.appendChild(a); a.click(); }");
        await Expect(page.GetByText("User management requires the admin role.")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Collections", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New Collection", Exact = true }).ClickAsync();
        await FillDialogAsync(page, "Create Collection", "Collection Name", UniqueName("forbidden"));
        await ExpectSnackbar(page, "Failed to create collection");
        await page.Context.CloseAsync();

        await DeleteUserAsync(admin, user);
        await admin.Context.CloseAsync();
    }

    [Fact]
    public async Task ChangeMyPassword_NewPasswordWorks()
    {
        var admin = await LoginAsync();
        var user = UniqueName("pw");
        await CreateUserAsync(admin, user, "OldPass123", "readwrite");

        var page = await LoginAsync(user, "OldPass123");
        await page.Locator("button[title='Change my password']").ClickAsync();
        await FillDialogAsync(page, "Current password:", "Current password", "OldPass123");
        await FillDialogAsync(page, "New password (min 6 chars):", "New password", "NewPass456");
        await ExpectSnackbar(page, "Password changed");

        await page.Locator(".mud-appbar button").Last.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Connect", Exact = true })).ToBeVisibleAsync();
        await page.Context.CloseAsync();

        var relogged = await LoginAsync(user, "NewPass456");
        await Expect(relogged.Locator(".mud-appbar")).ToContainTextAsync(user);
        await relogged.Context.CloseAsync();

        await DeleteUserAsync(admin, user);
        await admin.Context.CloseAsync();
    }

    // ---------- Helpers ----------

    private static string UniqueName(string kind) => $"e2e_{kind}_{Guid.NewGuid():N}"[..24];

    private async Task<IPage> NewLoginPageAsync()
    {
        var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(AdminClientFixture.BaseUrl + "/login");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Connect", Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        // The page is prerendered; wait for blazor.server.js and give the circuit
        // a moment so input events are not lost before it starts.
        await page.WaitForFunctionAsync("() => !!window.Blazor", null, new PageWaitForFunctionOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private async Task<IPage> LoginAsync(string username = "admin", string password = "admin123")
    {
        var page = await NewLoginPageAsync();
        await page.GetByLabel("Port").FillAsync(AdminClientFixture.TcpPort.ToString());
        await page.GetByLabel("Username").FillAsync(username);
        await page.GetByLabel("Password").FillAsync(password);
        var connect = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Connect", Exact = true });
        await connect.ClickAsync();
        var dashboard = page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Server Dashboard" });
        try
        {
            await Expect(dashboard).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        }
        catch (PlaywrightException)
        {
            // The first click can land before the circuit is interactive; retry once.
            await connect.ClickAsync();
            await Expect(dashboard).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        }
        return page;
    }

    private static ILocator Dialog(IPage page, string titleText) =>
        page.Locator(".mud-dialog").Filter(new LocatorFilterOptions { HasText = titleText });

    private static async Task FillDialogAsync(IPage page, string dialogText, string label, string value)
    {
        var dialog = Dialog(page, dialogText);
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await dialog.GetByLabel(label).FillAsync(value);
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "OK", Exact = true }).ClickAsync();
    }

    private static async Task ConfirmDialogAsync(IPage page, string dialogText)
    {
        var dialog = Dialog(page, dialogText);
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();
    }

    private static Task ExpectSnackbar(IPage page, string text) =>
        Expect(page.Locator(".mud-snackbar").Filter(new LocatorFilterOptions { HasText = text }).First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

    private static async Task SelectAsync(IPage page, string label, string itemText)
    {
        await page.GetByLabel(label).ClickAsync();
        await page.Locator(".mud-popover .mud-list-item")
            .Filter(new LocatorFilterOptions { HasText = itemText }).First.ClickAsync();
    }

    private static async Task CreateCollectionAsync(IPage page, string name)
    {
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Collections", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New Collection", Exact = true }).ClickAsync();
        await FillDialogAsync(page, "Create Collection", "Collection Name", name);
        await ExpectSnackbar(page, $"Collection '{name}' created");
        await Expect(page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name })).ToBeVisibleAsync();
    }

    private static async Task DropCollectionAsync(IPage page, string name)
    {
        var row = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name });
        await row.Locator("button").Last.ClickAsync();
        await ConfirmDialogAsync(page, "Delete Collection");
        await ExpectSnackbar(page, $"Collection '{name}' deleted");
        await Expect(row).ToHaveCountAsync(0);
    }

    private static async Task InsertDocumentAsync(IPage page, string json)
    {
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Insert", Exact = true }).ClickAsync();
        await FillDialogAsync(page, "Insert Document", "JSON", json);
        await ExpectSnackbar(page, "Document inserted with ID:");
    }

    private static async Task CreateUserAsync(IPage page, string username, string password, string role)
    {
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Users", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New User", Exact = true }).ClickAsync();
        await FillDialogAsync(page, "Enter the username:", "Username", username);
        await FillDialogAsync(page, "Enter a password (min 6 chars):", "Password", password);
        await FillDialogAsync(page, "Role: admin, readwrite, or readonly", "Role", role);
        await ExpectSnackbar(page, $"User '{username}' created");
        await Expect(page.Locator("tr").Filter(new LocatorFilterOptions { HasText = username })).ToBeVisibleAsync();
    }

    private static async Task DeleteUserAsync(IPage page, string username)
    {
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Users", Exact = true }).ClickAsync();
        var row = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = username });
        await row.Locator("button[title='Delete']").ClickAsync();
        await ConfirmDialogAsync(page, "Delete User");
        await ExpectSnackbar(page, "User deleted");
    }
}