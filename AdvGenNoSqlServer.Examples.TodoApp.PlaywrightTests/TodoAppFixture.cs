// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace AdvGenNoSqlServer.Examples.TodoApp.PlaywrightTests;

/// <summary>
/// Self-hosting E2E fixture: builds and launches the AdvGenNoSQL Host and the Blazor
/// TodoApp on isolated local ports, then drives them with Playwright through the
/// system Edge browser (no browser download required).
/// </summary>
public sealed class TodoAppFixture : IAsyncLifetime
{
    public const string BaseUrl = "http://localhost:5091";
    private const int TcpPort = 19191;
    private const int AdminPort = 19192;

    private Process? _host;
    private Process? _app;
    private string _storageDir = "";
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IBrowser Browser => _browser ?? throw new InvalidOperationException("Fixture is not initialized.");

    public async Task InitializeAsync()
    {
        var root = FindRepoRoot();
        _storageDir = Path.Combine(Path.GetTempPath(), "todoapp-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageDir);

        await RunDotnetAsync($"build \"{Path.Combine(root, "AdvGenNoSqlServer.Host")}\" -c Release --nologo -v q", root);
        await RunDotnetAsync($"build \"{Path.Combine(root, "AdvGenNoSqlServer.Examples.TodoApp")}\" -c Release --nologo -v q", root);

        var hostDll = Path.Combine(root, "AdvGenNoSqlServer.Host", "bin", "Release", "net9.0", "AdvGenNoSqlServer.Host.dll");
        _host = Launch(hostDll, Path.Combine(root, "AdvGenNoSqlServer.Host"), new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["NOSQL_PORT"] = TcpPort.ToString(),
            ["NOSQL_ADMIN_HTTP_PORT"] = AdminPort.ToString(),
            ["NOSQL_ADMIN_HTTP_USE_HTTPS"] = "false",
            ["NOSQL_MASTER_PASSWORD"] = "admin123",
            ["NOSQL_ENABLE_SSL"] = "false",
            ["NOSQL_STORAGE_PATH"] = _storageDir
        });
        await WaitTcpAsync(TcpPort, TimeSpan.FromSeconds(60));

        var appDll = Path.Combine(root, "AdvGenNoSqlServer.Examples.TodoApp", "bin", "Release", "net9.0", "AdvGenNoSqlServer.Examples.TodoApp.dll");
        _app = Launch(appDll, Path.Combine(root, "AdvGenNoSqlServer.Examples.TodoApp"), new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = BaseUrl,
            ["NoSql__Host"] = "localhost",
            ["NoSql__Port"] = TcpPort.ToString(),
            ["NoSql__UseSsl"] = "false",
            ["NoSql__Username"] = "admin",
            ["NoSql__Password"] = "admin123",
            ["NoSql__Collection"] = "todos"
        });
        await WaitHttpAsync(BaseUrl, TimeSpan.FromSeconds(60));

        _playwright = await Playwright.CreateAsync();
        // Use the Playwright-matched cached Chromium (no download, no system-browser
        // version coupling; the system Edge is too new for this driver's headless mode).
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser != null)
            await _browser.CloseAsync();
        _playwright?.Dispose();
        Kill(_app);
        Kill(_host);
        try { Directory.Delete(_storageDir, true); } catch (IOException) { }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AdvGenNoSqlServer.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (AdvGenNoSqlServer.sln).");
    }

    private static async Task RunDotnetAsync(string arguments, string workingDir)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet {arguments} failed ({process.ExitCode}): {stdout} {stderr}");
    }

    private static Process Launch(string dll, string workingDir, Dictionary<string, string> env)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var (key, value) in env)
            psi.Environment[key] = value;
        return Process.Start(psi)!;
    }

    private static async Task WaitTcpAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("localhost", port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(250);
            }
        }
        throw new TimeoutException($"AdvGenNoSQL Host did not open TCP port {port} within {timeout}.");
    }

    private static async Task WaitHttpAsync(string url, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }
            await Task.Delay(250);
        }
        throw new TimeoutException($"TodoApp did not become ready at {url} within {timeout}.");
    }

    private static void Kill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}