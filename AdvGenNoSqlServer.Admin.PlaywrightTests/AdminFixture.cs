// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace AdvGenNoSqlServer.Admin.PlaywrightTests;

/// <summary>
/// Self-hosting E2E fixture for the Blazor WASM Admin. The app always derives its
/// REST base URL as https://host:(TCP port + 1), so the Host runs its REST API over
/// HTTPS (ASP.NET Core dev cert) on TcpPort+1 and browser contexts ignore cert
/// errors. The WASM app itself is served by the Blazor DevServer over plain HTTP.
/// </summary>
public sealed class AdminFixture : IAsyncLifetime
{
    public const string BaseUrl = "http://localhost:19480";
    public const int TcpPort = 19491;
    public const int ApiPort = TcpPort + 1;

    private Process? _host;
    private Process? _app;
    private string _storageDir = "";
    private string _hostOut = "";
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<IPage> NewPageAsync()
    {
        var browser = _browser ?? throw new InvalidOperationException("Fixture is not initialized.");
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        });
        // The app loads Google Fonts from the network; abort for speed and determinism.
        await context.RouteAsync("**/*", route =>
            route.Request.Url.Contains("fonts.googleapis.com") || route.Request.Url.Contains("fonts.gstatic.com")
                ? route.AbortAsync()
                : route.ContinueAsync());
        return await context.NewPageAsync();
    }

    public async Task InitializeAsync()
    {
        var root = FindRepoRoot();
        _storageDir = Path.Combine(Path.GetTempPath(), "admin-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageDir);

        _hostOut = Path.Combine(Path.GetTempPath(), "admin-e2e-host-" + Guid.NewGuid().ToString("N"));
        await RunDotnetAsync($"build \"{Path.Combine(root, "AdvGenNoSqlServer.Host")}\" -c Release --nologo -v q -o \"{_hostOut}\"", root);
        await RunDotnetAsync($"build \"{Path.Combine(root, "AdvGenNoSqlServer.Admin")}\" -c Release --nologo -v q", root);

        var hostDll = Path.Combine(_hostOut, "AdvGenNoSqlServer.Host.dll");
        _host = Launch(hostDll, Path.Combine(root, "AdvGenNoSqlServer.Host"), new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["NOSQL_PORT"] = TcpPort.ToString(),
            ["NOSQL_ADMIN_HTTP_PORT"] = ApiPort.ToString(),
            ["NOSQL_ADMIN_HTTP_USE_HTTPS"] = "true",
            ["NOSQL_MASTER_PASSWORD"] = "admin123",
            ["NOSQL_REQUIRE_AUTHENTICATION"] = "true",
            ["NOSQL_ENABLE_SSL"] = "false",
            ["NOSQL_STORAGE_PATH"] = _storageDir,
            ["NOSQL_PBKDF2_ITERATIONS"] = "10000"
        });
        await WaitTcpAsync(TcpPort, TimeSpan.FromSeconds(60));
        await WaitHttpsAsync($"https://localhost:{ApiPort}/api/health", TimeSpan.FromSeconds(60));

        _app = LaunchRun(root, Path.Combine(root, "AdvGenNoSqlServer.Admin"), new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = BaseUrl
        });
        await WaitHttpAsync(BaseUrl, TimeSpan.FromSeconds(120));

        _playwright = await Playwright.CreateAsync();
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
        try { Directory.Delete(_hostOut, true); } catch (IOException) { }
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

    private static Process LaunchRun(string root, string projectDir, Dictionary<string, string> env)
    {
        // The standalone WASM app needs the Blazor DevServer (dotnet run), not a dll launch.
        var psi = new ProcessStartInfo("dotnet",
            $"run --project \"{projectDir}\" -c Release --no-build --no-launch-profile")
        {
            WorkingDirectory = root,
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
        await WaitReadyAsync(http, url, timeout);
    }

    private static async Task WaitHttpsAsync(string url, TimeSpan timeout)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        await WaitReadyAsync(http, url, timeout);
    }

    private static async Task WaitReadyAsync(HttpClient http, string url, TimeSpan timeout)
    {
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
        throw new TimeoutException($"Service did not become ready at {url} within {timeout}.");
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