// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace AdvGenNoSqlServer.AdminClient.PlaywrightTests;

/// <summary>
/// Self-hosting E2E fixture: launches the AdvGenNoSQL Host (TCP + SSL) and the
/// Blazor Server AdminClient on isolated local ports, then drives them with
/// headless Chromium. The Host uses a fresh export of the trusted ASP.NET Core
/// dev certificate because AdminClient always connects with SSL and the client
/// rejects untrusted certificate chains.
/// </summary>
public sealed class AdminClientFixture : IAsyncLifetime
{
    public const string BaseUrl = "http://localhost:19380";
    public const int TcpPort = 19391;
    private const int RestPort = 19392;

    private Process? _host;
    private Process? _app;
    private string _tempDir = "";
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IBrowser Browser => _browser ?? throw new InvalidOperationException("Fixture is not initialized.");

    public async Task InitializeAsync()
    {
        var root = FindRepoRoot();
        _tempDir = Path.Combine(Path.GetTempPath(), "adminclient-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var storageDir = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(storageDir);

        var certPath = Path.Combine(_tempDir, "advgen.pfx");
        await RunDotnetAsync($"dev-certs https -ep \"{certPath}\" -p devpassword", root);

        await RunDotnetAsync($"build \"{Path.Combine(root, "AdvGenNoSqlServer.Host")}\" -c Release --nologo -v q", root);
        await RunDotnetAsync($"build \"{Path.Combine(root, "AdvGenNoSqlServer.AdminClient")}\" -c Release --nologo -v q", root);

        var hostDll = Path.Combine(root, "AdvGenNoSqlServer.Host", "bin", "Release", "net9.0", "AdvGenNoSqlServer.Host.dll");
        _host = Launch(hostDll, Path.Combine(root, "AdvGenNoSqlServer.Host"), new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["NOSQL_PORT"] = TcpPort.ToString(),
            ["NOSQL_ADMIN_HTTP_PORT"] = RestPort.ToString(),
            ["NOSQL_ADMIN_HTTP_USE_HTTPS"] = "false",
            ["NOSQL_MASTER_PASSWORD"] = "admin123",
            ["NOSQL_REQUIRE_AUTHENTICATION"] = "true",
            ["NOSQL_ENABLE_SSL"] = "true",
            ["NOSQL_SSL_CERT_PATH"] = certPath,
            ["NOSQL_SSL_CERT_PASSWORD"] = "devpassword",
            ["NOSQL_STORAGE_PATH"] = storageDir,
            ["NOSQL_PBKDF2_ITERATIONS"] = "10000"
        });
        await WaitTcpAsync(TcpPort, TimeSpan.FromSeconds(60));

        var appDll = Path.Combine(root, "AdvGenNoSqlServer.AdminClient", "bin", "Release", "net9.0", "AdvGenNoSqlServer.AdminClient.dll");
        _app = Launch(appDll, Path.Combine(root, "AdvGenNoSqlServer.AdminClient"), new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = BaseUrl
        });
        await WaitHttpAsync(BaseUrl, TimeSpan.FromSeconds(60));

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
        try { Directory.Delete(_tempDir, true); } catch (IOException) { }
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
        throw new TimeoutException($"AdminClient did not become ready at {url} within {timeout}.");
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