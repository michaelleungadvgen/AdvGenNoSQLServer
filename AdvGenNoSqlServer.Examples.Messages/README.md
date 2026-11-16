# AdvGenNoSQL Cache-Mode Messaging Example

A console **message board / chat log** that demonstrates the AdvGenNoSQL server's
**cache storage mode** (`StorageMode=Cache`). Messages are stored only in server
memory (Redis-style) — nothing is written to disk, and everything is lost when the
server restarts.

> WARNING: quick demonstration prototype. Do not store sensitive information in it.

## How it demonstrates cache mode

- The server runs with `NOSQL_STORAGE_MODE=Cache`, so the `messages` collection is an
  in-memory store. No storage directory or files are created.
- The app connects over the TCP binary protocol (`AdvGenNoSqlServer.Client`), sends
  messages, and reads them back — all served from RAM.
- Restart the server and the messages are gone (that is the point of cache mode).

## Quick start (Windows)

From the repo root, pick a client:

```bat
run-messages-demo.bat     :: console message board (interactive)
run-messages-web.bat      :: Blazor Server web UI  -> http://localhost:5290
run-messages-wpf.bat      :: WPF desktop client
```

Each starts the Host in cache mode and the chosen client. Open the web UI in multiple
browser tabs (or run the WPF client alongside it) to chat — all clients share the same
server-side, memory-only message store. Closing the Host window discards all messages.

The shared message logic lives in this project (`MessageService.cs`) and is reused by the
console, WPF (`AdvGenNoSqlServer.Examples.Messages.Wpf`) and Blazor Server
(`AdvGenNoSqlServer.Examples.Messages.Web`) clients.

## Manual run

1. Start the server in cache mode:

   ```powershell
   # PowerShell
   $env:NOSQL_STORAGE_MODE="Cache"; $env:NOSQL_ENABLE_SSL="false"; $env:NOSQL_MASTER_PASSWORD="admin123"
   dotnet run --project AdvGenNoSqlServer.Host
   ```

   ```bash
   # bash
   NOSQL_STORAGE_MODE=Cache NOSQL_ENABLE_SSL=false NOSQL_MASTER_PASSWORD=admin123 \
     dotnet run --project AdvGenNoSqlServer.Host
   ```

2. Run the messaging app (separate terminal):

   ```bash
   dotnet run --project AdvGenNoSqlServer.Examples.Messages            # interactive
   dotnet run --project AdvGenNoSqlServer.Examples.Messages -- --demo  # scripted tour
   ```

## Interactive commands

| Command | Action |
|---------|--------|
| `<text>` | send a message |
| `/nick <name>` | change your display name |
| `/list` | show all messages |
| `/count` | count messages |
| `/clear` | delete all messages |
| `/stats` | server statistics |
| `/quit` | exit |

## Options

`--host <h>` (default `localhost`), `--port <p>` (default `9191`), `--user <u>`
(default `admin`), `--password <pw>` (default `admin123`), `--collection <c>`
(default `messages`), `--ssl` (enable TCP TLS), `--demo` (scripted demonstration).

## E2E tests

`AdvGenNoSqlServer.Examples.Messages.Wpf.E2ETests` is an xUnit suite that launches the
Host in cache mode plus the WPF client on isolated ports and drives the WPF UI through
Windows UI Automation (connect, send, multiple messages, clear, memory-only/no-disk, and
a cross-client shared-cache check).

```bash
dotnet test AdvGenNoSqlServer.Examples.Messages.Wpf.E2ETests -c Release
```

It requires an interactive Windows desktop session (UI Automation), so it is not run in
Linux CI — like the Playwright suites, run it locally.

## Project layout

| File | Purpose |
|------|---------|
| `Program.cs` | CLI parsing, banner, interactive loop and `--demo` tour |
| `MessageService.cs` | TCP client wrapper: send / list / count / clear / stats |