// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using System.Net.Sockets;
using ModelContextProtocol.Client;

namespace AdvGenNoSqlServer.Mcp.E2ETests;

/// <summary>
/// Self-hosting E2E fixture: builds and launches the real Host (cache mode, isolated ports)
/// and the MCP server, then connects an MCP client over stdio — the same way an agent host
/// would spawn it.
/// </summary>
public sealed class McpE2EFixture : IAsyncLifetime
{
    public const int TcpPort = 19991;
    public const int RestPort = 19992;

    private Process? _host;
    private string _tempDir = "";
    private string _mcpDll = "";

    public McpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var root = FindRepoRoot();
        _tempDir = Path.Combine(Path.GetTempPath(), "mcp-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var hostOut = Path.Combine(_tempDir, "host");
        var mcpOut = Path.Combine(_tempDir, "mcp");
        await RunDotnetAsync($"build \"{Path.Combine(root, "AdvGenNoSqlServer.Host")}\" -c Release --nologo -v q -o \"{hostOut}\"", root);
        await RunDotnetAsync($"build \"{Path.Combine(root, "AdvGenNoSqlServer.Mcp")}\" -c Release --nologo -v q -o \"{mcpOut}\"", root);
        _mcpDll = Path.Combine(mcpOut, "AdvGenNoSqlServer.Mcp.dll");

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
            ["NOSQL_STORAGE_PATH"] = Path.Combine(_tempDir, "data"),
            ["NOSQL_PBKDF2_ITERATIONS"] = "10000"
        });

        await WaitTcpAsync(TcpPort, TimeSpan.FromSeconds(60));

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "advgen-mcp-e2e",
            Command = "dotnet",
            Arguments = new[] { _mcpDll },
            WorkingDirectory = mcpOut,
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["NOSQL_MCP_SERVER"] = $"localhost:{TcpPort}",
                ["NOSQL_MCP_USER"] = "admin",
                ["NOSQL_MCP_PASSWORD"] = "admin123",
                ["NOSQL_MCP_SSL"] = "false"
            }
        });

        Client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        if (Client != null)
            await Client.DisposeAsync();
        Kill(_host);
        try { Directory.Delete(_tempDir, true); } catch (IOException) { }
    }

    /// <summary>Calls a tool and returns its concatenated text content.</summary>
    public async Task<string> CallAsync(string tool, Dictionary<string, object>? args = null)
    {
        var result = await Client.CallToolAsync(tool, args);
        Assert.False(result.IsError ?? false, $"Tool '{tool}' returned an error: {string.Join(" ", result.Content.Select(c => c.ToString()))}");
        return string.Join("", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(t => t.Text));
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