// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using AdvGenNoSqlServer.Client;

namespace AdvGenNoSqlServer.Host.E2ETests;

/// <summary>
/// Self-hosting E2E fixture for the production Host. Builds and launches the real Host
/// (TCP binary protocol + REST API) on isolated ports in cache mode, then exercises it
/// end-to-end via the TCP client library and raw HTTP REST calls.
/// </summary>
public sealed class HostE2EFixture : IAsyncLifetime
{
    public const int TcpPort = 19791;
    public const int RestPort = 19792;
    public static readonly string RestBase = $"http://localhost:{RestPort}";

    private Process? _host;
    private string _tempDir = "";

    public string StorageDir => Path.Combine(_tempDir, "data");

    public async Task InitializeAsync()
    {
        var root = FindRepoRoot();
        _tempDir = Path.Combine(Path.GetTempPath(), "host-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(StorageDir);

        var hostOut = Path.Combine(_tempDir, "host");
        await RunDotnetAsync($"build \"{Path.Combine(root, "AdvGenNoSqlServer.Host")}\" -c Release --nologo -v q -o \"{hostOut}\"", root);

        _host = Launch(Path.Combine(hostOut, "AdvGenNoSqlServer.Host.dll"), Path.Combine(root, "AdvGenNoSqlServer.Host"), new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["NOSQL_STORAGE_MODE"] = "Cache",
            ["NOSQL_PORT"] = TcpPort.ToString(),
            ["NOSQL_ADMIN_HTTP_PORT"] = RestPort.ToString(),
            ["NOSQL_ADMIN_HTTP_USE_HTTPS"] = "false",
            ["NOSQL_MASTER_PASSWORD"] = "admin123",
            ["NOSQL_REQUIRE_AUTHENTICATION"] = "true",
            ["NOSQL_ENABLE_SSL"] = "false",
            ["NOSQL_STORAGE_PATH"] = StorageDir,
            ["NOSQL_PBKDF2_ITERATIONS"] = "10000"
        });

        await WaitTcpAsync(TcpPort, TimeSpan.FromSeconds(60));
        await WaitHttpAsync($"{RestBase}/api/health", TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync()
    {
        Kill(_host);
        try { Directory.Delete(_tempDir, true); } catch (IOException) { }
    }

    /// <summary>A connected + authenticated TCP client.</summary>
    public async Task<AdvGenNoSqlClient> NewClientAsync(string username = "admin", string password = "admin123")
    {
        var client = new AdvGenNoSqlClient($"localhost:{TcpPort}", new AdvGenNoSqlClientOptions
        {
            UseSsl = false,
            CheckCertificateRevocation = false
        });
        await client.ConnectAsync();
        if (!await client.AuthenticateAsync(username, password))
            throw new InvalidOperationException("TCP authentication failed");
        return client;
    }

    public HttpClient NewRestClient() => new() { BaseAddress = new Uri(RestBase), Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Logs in over REST and returns a client with the Bearer token set.</summary>
    public async Task<HttpClient> NewAuthenticatedRestClientAsync(string username = "admin", string password = "admin123")
    {
        var client = NewRestClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ---------- helpers ----------

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AdvGenNoSqlServer.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
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
            catch (SocketException) { await Task.Delay(250); }
        }
        throw new TimeoutException($"Host did not open TCP port {port} within {timeout}.");
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
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(250);
        }
        throw new TimeoutException($"Host REST did not become ready at {url} within {timeout}.");
    }

    private static void Kill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }
}