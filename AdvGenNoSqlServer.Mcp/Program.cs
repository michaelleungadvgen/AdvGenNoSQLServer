// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Transport selection: stdio by default (for agent hosts that spawn the process),
// or streamable HTTP with --http / NOSQL_MCP_TRANSPORT=http (for remote agents).
var useHttp = args.Contains("--http")
    || string.Equals(Environment.GetEnvironmentVariable("NOSQL_MCP_TRANSPORT"), "http", StringComparison.OrdinalIgnoreCase);

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOSQL_MCP_PASSWORD")))
{
    await Console.Error.WriteLineAsync(
        "NOSQL_MCP_PASSWORD is not set. Configure the MCP server with the database password " +
        "(and optionally NOSQL_MCP_SERVER, NOSQL_MCP_USER, NOSQL_MCP_SSL).");
    return 1;
}

if (useHttp)
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddSingleton<NoSqlClientService>();
    builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var port = int.TryParse(Environment.GetEnvironmentVariable("NOSQL_MCP_HTTP_PORT"), out var p) ? p : 9292;
    var app = builder.Build();
    app.MapMcp();
    await app.RunAsync($"http://localhost:{port}");
}
else
{
    var builder = Host.CreateApplicationBuilder(args);
    // stdout is reserved for the MCP JSON-RPC stream; all logs go to stderr.
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddSingleton<NoSqlClientService>();
    builder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}

return 0;