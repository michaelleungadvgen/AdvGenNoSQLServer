// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Client;

namespace AdvGenNoSqlServer.Mcp;

/// <summary>
/// Owns the single TCP connection to the AdvGenNoSQL Host shared by all MCP tools.
/// Configuration comes from environment variables so agent hosts can inject it:
/// NOSQL_MCP_SERVER (host:port, default localhost:9191), NOSQL_MCP_USER (default admin),
/// NOSQL_MCP_PASSWORD (required), NOSQL_MCP_SSL (true/false, default false).
/// </summary>
public sealed class NoSqlClientService : IAsyncDisposable
{
    private readonly AdvGenNoSqlClient _client;
    private readonly string _username;
    private readonly string _password;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _authenticated;

    public NoSqlClientService()
    {
        var server = Environment.GetEnvironmentVariable("NOSQL_MCP_SERVER") ?? "localhost:9191";
        _username = Environment.GetEnvironmentVariable("NOSQL_MCP_USER") ?? "admin";
        _password = Environment.GetEnvironmentVariable("NOSQL_MCP_PASSWORD")
            ?? throw new InvalidOperationException(
                "NOSQL_MCP_PASSWORD is not set. Configure the MCP server with the database password.");
        var useSsl = string.Equals(
            Environment.GetEnvironmentVariable("NOSQL_MCP_SSL"), "true", StringComparison.OrdinalIgnoreCase);

        _client = new AdvGenNoSqlClient(server, new AdvGenNoSqlClientOptions
        {
            UseSsl = useSsl,
            CheckCertificateRevocation = false,
            AutoReconnect = true
        });
    }

    public string ServerAddress => Environment.GetEnvironmentVariable("NOSQL_MCP_SERVER") ?? "localhost:9191";

    /// <summary>Returns a connected, authenticated client, establishing the session on first use.</summary>
    public async Task<AdvGenNoSqlClient> GetClientAsync(CancellationToken ct = default)
    {
        if (_authenticated && _client.IsConnected)
            return _client;

        await _gate.WaitAsync(ct);
        try
        {
            if (!_client.IsConnected)
            {
                await _client.ConnectAsync(ct);
                _authenticated = false;
            }

            if (!_authenticated)
            {
                if (!await _client.AuthenticateAsync(_username, _password, ct))
                    throw new InvalidOperationException(
                        $"Authentication failed for user '{_username}'. Check NOSQL_MCP_USER / NOSQL_MCP_PASSWORD.");
                _authenticated = true;
            }

            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _gate.Dispose();
    }
}