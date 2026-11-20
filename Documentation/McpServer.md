# MCP Server — Agent Integration Guide

`AdvGenNoSqlServer.Mcp` lets any MCP-capable AI agent (Qoder, Claude, Cursor, custom SDK
clients) operate an AdvGenNoSQL Server database through standard Model Context Protocol tools.

Project README with build/run basics: [AdvGenNoSqlServer.Mcp/README.md](../AdvGenNoSqlServer.Mcp/README.md).

## Architecture

```
Agent (MCP client)
   │  JSON-RPC 2.0 over stdio  ── or ──  Streamable HTTP (:9292)
   ▼
AdvGenNoSqlServer.Mcp          ← this project; 21 tools, env-var config
   │  one shared, authenticated TCP session (AdvGenNoSqlServer.Client)
   ▼
AdvGenNoSqlServer.Host         ← TCP binary protocol (:9191), RBAC per command
```

- The MCP server is a thin adapter: every tool maps to a TCP command the Host already
  supports, so authentication, RBAC (`admin`/`readwrite`/`readonly`), audit logging, and
  metrics apply exactly as for native clients.
- One TCP connection is shared by all tool calls; it is established and authenticated lazily
  on first use and re-established if dropped (`AutoReconnect`).
- stdout is reserved for JSON-RPC in stdio mode; all logs go to stderr.
- Built with the official C# MCP SDK (`ModelContextProtocol` 2.4.0, Apache-2.0).

## Transports

| Transport | Select | Endpoint | Use case |
|-----------|--------|----------|----------|
| stdio | default | process stdin/stdout | Agent host spawns the server locally (Qoder, Claude Desktop, Cursor) |
| Streamable HTTP | `--http` or `NOSQL_MCP_TRANSPORT=http` | `http://localhost:9292/` (`NOSQL_MCP_HTTP_PORT`) | Remote agents, shared team server |

HTTP mode is stateless streamable HTTP: POST JSON-RPC to `/` with
`Accept: application/json, text/event-stream`; responses arrive as SSE `message` events.
It binds to localhost only and relies on the database credentials in the server environment —
front it with a TLS reverse proxy and access control before exposing it to a network.

## Environment reference

| Variable | Default | Notes |
|----------|---------|-------|
| `NOSQL_MCP_PASSWORD` | required | Fail-fast at startup when missing |
| `NOSQL_MCP_SERVER` | `localhost:9191` | Host TCP `host:port` |
| `NOSQL_MCP_USER` | `admin` | Use least-privilege accounts where possible |
| `NOSQL_MCP_SSL` | `false` | Must match the Host's `EnableSsl` |
| `NOSQL_MCP_TRANSPORT` | `stdio` | `http` selects HTTP |
| `NOSQL_MCP_HTTP_PORT` | `9292` | HTTP mode only |

## Tool reference

All tools return JSON text. Failures return MCP `isError: true` with a message.

### Collections
| Tool | Arguments | Returns |
|------|-----------|---------|
| `list_collections` | — | `{ collections: [...] }` |
| `create_collection` | `name` | `{ created, name }` |
| `drop_collection` | `name` | server result (admin role required) |

### Documents
| Tool | Arguments | Returns |
|------|-----------|---------|
| `get_document` | `collection`, `id` | `{ found, document }` — document is flat `{_id, ...fields}` or null |
| `set_document` | `collection`, `documentJson` (JSON object string), `id?` | `{ id }` — upsert; ID generated when omitted |
| `delete_document` | `collection`, `id` | `{ deleted }` |
| `document_exists` | `collection`, `id` | `{ exists }` |
| `count_documents` | `collection` | `{ count }` |
| `list_documents` | `collection`, `skip=0`, `take=50` | `{ documents, total, collection }` — ordered by `_id`, take capped at 200 |

### Server
| Tool | Arguments | Returns |
|------|-----------|---------|
| `server_stats` | — | `{ version, uptimeSeconds, memoryUsageMB, totalDocuments, totalCollections, activeConnections }` |

### Users (admin role)
| Tool | Arguments | Returns |
|------|-----------|---------|
| `list_users` | — | array of `{ username, role, ... }` |
| `create_user` | `username`, `password`, `role` (`admin`/`readwrite`/`readonly`) | `{ created }` |
| `delete_user` | `username` | `{ deleted }` |
| `set_user_role` | `username`, `role` | `{ changed }` |
| `set_user_password` | `username`, `password` | `{ changed }` |

### Attachments
| Tool | Arguments | Returns |
|------|-----------|---------|
| `list_attachments` | `collection`, `id` | array of metadata |
| `attachment_info` | `collection`, `id`, `name` | `{ name, size, contentType, sha256, ... }` |
| `upload_attachment` | `collection`, `id`, `name`, `contentType`, `base64Content` | metadata (server caps size via `MaxAttachmentSizeMB`) |
| `download_attachment` | `collection`, `id`, `name` | `{ found, name, base64Content }` |
| `delete_attachment` | `collection`, `id`, `name` | `{ deleted }` |
| `total_storage` | — | `{ totalStorageBytes }` |

## Connecting agents

### Qoder / Claude Desktop / Cursor (stdio)

MCP config entry (adjust the DLL path and credentials):

```json
{
  "mcpServers": {
    "advgen-nosql": {
      "command": "dotnet",
      "args": ["E:/Projects/AdvGenNoSQLServer/AdvGenNoSqlServer.Mcp/bin/Release/net9.0/AdvGenNoSqlServer.Mcp.dll"],
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

Build once first: `dotnet build AdvGenNoSqlServer.Mcp -c Release`. The Host must be running.

### Python agent (official `mcp` SDK, stdio)

```python
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

params = StdioServerParameters(
    command="dotnet",
    args=[r"E:/Projects/AdvGenNoSQLServer/AdvGenNoSqlServer.Mcp/bin/Release/net9.0/AdvGenNoSqlServer.Mcp.dll"],
    env={"NOSQL_MCP_PASSWORD": "admin123", "NOSQL_MCP_SERVER": "localhost:9191"},
)
async with stdio_client(params) as (read, write):
    async with ClientSession(read, write) as session:
        await session.initialize()
        tools = await session.list_tools()
        result = await session.call_tool("set_document", {
            "collection": "products",
            "documentJson": '{"name": "Milk 2L", "price": 3.5}',
            "id": "milk-2l",
        })
```

### Remote agent (HTTP, any MCP client)

```bash
NOSQL_MCP_PASSWORD=admin123 dotnet AdvGenNoSqlServer.Mcp.dll --http
# endpoint: http://localhost:9292/  (streamable HTTP)
```

C# client example and raw-HTTP details: see the project README.

## Verification

- E2E suite (real Host + real MCP server + real MCP client, isolated ports, cache mode):
  `dotnet test AdvGenNoSqlServer.Mcp.E2ETests -c Release` — covers tool listing, document
  roundtrip, generated IDs, paging, collection drop, user lifecycle, attachment
  upload/download/delete, stats, and error propagation.
- Manual stdio probe: pipe newline-delimited JSON-RPC (`initialize`, `notifications/initialized`,
  `tools/list`, `tools/call`) into the process with the env vars set; keep stdin open until
  responses arrive (closing stdin immediately shuts the server down).

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| Startup exits with code 1 | `NOSQL_MCP_PASSWORD` not set |
| `Failed to connect to host:port` | Host not running, wrong `NOSQL_MCP_SERVER`, or port blocked |
| TLS/handshake errors | `NOSQL_MCP_SSL` must match the Host's `EnableSsl` |
| `Authentication failed` | Wrong `NOSQL_MCP_USER`/`NOSQL_MCP_PASSWORD` |
| Tool returns permission error | Account role lacks the permission (e.g. `drop_collection` needs admin) |
| No stdout responses in a manual pipe test | stdin EOF shut the server down before it flushed — keep stdin open |