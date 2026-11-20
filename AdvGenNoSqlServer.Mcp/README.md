# AdvGenNoSQL MCP Server

A [Model Context Protocol](https://modelcontextprotocol.io) server that exposes AdvGenNoSQL
Server to AI agents. It wraps the official `AdvGenNoSqlServer.Client` TCP library, so agents
get the same authenticated, RBAC-enforced access as any .NET client — 21 tools covering
collections, documents, users, and attachments.

Two transports:

- **stdio** (default) — the agent host spawns the process and talks JSON-RPC over stdin/stdout.
- **Streamable HTTP** (`--http`) — for remote agents; serves MCP at `http://localhost:9292/`.

## Build

```powershell
dotnet build AdvGenNoSqlServer.Mcp/AdvGenNoSqlServer.Mcp.csproj -c Release
```

## Configure

Configuration is environment-variable based (agent hosts inject these into the server process):

| Variable | Default | Meaning |
|----------|---------|---------|
| `NOSQL_MCP_PASSWORD` | — (**required**) | Database password; startup fails fast without it |
| `NOSQL_MCP_SERVER` | `localhost:9191` | Host TCP address `host:port` |
| `NOSQL_MCP_USER` | `admin` | Database user (role determines what tools can do) |
| `NOSQL_MCP_SSL` | `false` | `true` when the Host runs with TCP SSL enabled |
| `NOSQL_MCP_TRANSPORT` | `stdio` | `http` to select HTTP without the `--http` flag |
| `NOSQL_MCP_HTTP_PORT` | `9292` | HTTP listen port (localhost only) |

The server opens one shared TCP session, authenticating lazily on the first tool call.

## Run

```powershell
# stdio (normally spawned by the agent host, not run by hand)
$env:NOSQL_MCP_PASSWORD = "admin123"
dotnet run --project AdvGenNoSqlServer.Mcp -c Release

# HTTP for remote agents
$env:NOSQL_MCP_PASSWORD = "admin123"
dotnet run --project AdvGenNoSqlServer.Mcp -c Release -- --http
```

The AdvGenNoSQL Host must be running (see the repo root README / QuickStart).

## Connect an agent

### Qoder / Claude Desktop / any stdio MCP host

Add to the MCP config (Qoder: Settings → MCP; Claude Desktop: `claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "advgen-nosql": {
      "command": "dotnet",
      "args": [
        "E:/Projects/AdvGenNoSQLServer/AdvGenNoSqlServer.Mcp/bin/Release/net9.0/AdvGenNoSqlServer.Mcp.dll"
      ],
      "env": {
        "NOSQL_MCP_SERVER": "localhost:9191",
        "NOSQL_MCP_USER": "admin",
        "NOSQL_MCP_PASSWORD": "admin123",
        "NOSQL_MCP_SSL": "false"
      }
    }
  }
}
```

Use a `readwrite` or `readonly` account instead of `admin` for least privilege — the Host
enforces the role on every command regardless of which tools the agent calls.

### Remote agents (HTTP)

Start the server with `--http`, then point the agent's MCP client at
`http://<machine>:9292/` (streamable HTTP). Example with the official C# SDK:

```csharp
using ModelContextProtocol.Client;

var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri("http://localhost:9292/")
});
await using var client = await McpClient.CreateAsync(transport);
var result = await client.CallToolAsync("list_collections");
```

> The HTTP endpoint binds to localhost and has no transport-level auth of its own — the
> database credentials live in the server's environment. Put it behind a reverse proxy with
> TLS + access control before exposing it beyond the machine.

## Tools

| Group | Tools |
|-------|-------|
| Collections | `list_collections`, `create_collection`, `drop_collection` (admin) |
| Documents | `get_document`, `set_document` (upsert), `delete_document`, `document_exists`, `count_documents`, `list_documents` (skip/take paging) |
| Server | `server_stats` |
| Users (admin) | `list_users`, `create_user`, `delete_user`, `set_user_role`, `set_user_password` |
| Attachments | `list_attachments`, `attachment_info`, `upload_attachment` (base64), `download_attachment` (base64), `delete_attachment`, `total_storage` |

Documents use the flat wire shape `{ "_id": ..., ...fields }`. `set_document` takes fields as
a JSON string (`documentJson`) plus an optional `id`; omit both to get a generated ID.

## Tests

Whole-system E2E suite (builds and launches the real Host + MCP server on isolated ports,
drives them with a real MCP client over stdio):

```powershell
dotnet test AdvGenNoSqlServer.Mcp.E2ETests -c Release
```

## Security notes

- Prototype-grade server: do not point agents at sensitive data (see repo root README).
- Never bake real passwords into shared config files; inject `NOSQL_MCP_PASSWORD` via the
  agent host's secret mechanism.
- Prefer a least-privilege database account; `drop_collection` and user tools need `admin`.