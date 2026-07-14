# Admin UI Audit — Findings and Fix Design

**Date:** 2026-07-14
**Scope:** `AdvGenNoSqlServer.AdminClient` (Blazor Server + MudBlazor, TCP admin console) and the server/client pieces it depends on.
**Companion plan:** `docs/superpowers/plans/2026-07-14-admin-ui-fixes.md`

## 1. Current state

| Page | Status | Detail |
|---|---|---|
| Login | ✅ Works | Connects over SSL, authenticates, redirects |
| Dashboard | ✅ Works | `stats` command exists; tiles + 30s auto-refresh render |
| Collections — list & counts | ✅ Works | `listcollections` and `count` commands exist |
| Collections — **create** | ❌ **Broken** | Sends `createcollection`; server has no such command → `UNKNOWN_COMMAND` |
| Collections — **delete** | ❌ **Broken** | Sends `dropcollection`; server has no such command |
| Documents — **list** | ❌ **Broken** | Sends `listdocuments`; server has no such command — the whole page is dead |
| Documents — insert/edit/delete | ✅/⚠️ | `set`/`delete` commands exist, but listing is broken so they're unreachable in practice; JSON is edited in a **single-line** text field |
| Query | ❌ **Broken** | Sends the raw query text as the Command payload; server requires JSON with a `command` property → `INVALID_COMMAND` for anything typed. Placeholder advertises SQL (`SELECT * FROM ...`) that does not exist on the TCP path |

The server's full TCP command set is: `get, set, delete, exists, insert, replace, upsert, find_one, touch, listcollections, count, stats, cluster` (`AdvGenNoSqlServer.Server/NoSqlServer.cs:286-302`). The AdminClient calls three commands that are not in it.

## 2. Defects (verified against source)

### D1 — Missing server commands (breaks 3 features)
`TcpAdminService` calls `createcollection`, `dropcollection` (`Services/TcpAdminService.cs:92-110`), and `listdocuments` (`:122-140`). None exist server-side. `IDocumentStore` already exposes `CreateCollectionAsync`, `DropCollectionAsync`, `GetAllAsync`, and `CountAsync` (`AdvGenNoSqlServer.Core/Abstractions/IDocumentStore.cs`), so this is purely a missing command-handler problem.

### D2 — Client `GetAsync` response-shape mismatch (always returns null)
`HandleGetCommand` returns `{ found, value = <Document> }` (`NoSqlServer.cs:335-338`), where `<Document>` serializes with PascalCase `Id`/`Data` properties. The client reads a `document` property and expects a flat JSON document (`AdvGenNoSqlClient.Commands.cs:54`). Against a real server, `GetAsync` **always returns null**. `ClientGetFixTests` encodes the intended contract (`document`, flat) but only tests hand-built JSON — it never hits the real server, so the mismatch survived.

### D3 — `NoSqlMessage.CreateCommand` builds JSON by string concatenation
`MessageProtocol.cs:163-179` interpolates `command` and `collection` into a JSON string without escaping. A collection named `a"b` (or containing a backslash) produces a malformed payload — data-dependent breakage and an injection vector. Every AdminClient command flows through this.

### D4 — Query page cannot work and misleads users
The page routes raw text to `ExecuteQueryAsync`, which sends it as the Command payload verbatim (`Client.cs:301-309`). The server JSON-parses the payload and needs `command`. There is no SQL/query engine wired to the TCP path (the `Query` project is not reachable from `HandleCommandAsync`). The page can never succeed as designed.

### D5 — JSON editing UX
`TextInputDialog` renders a single-line `MudTextField` (no `Lines` parameter). Insert/Edit Document pre-fills **indented multi-line JSON** into it; editing anything nontrivial is impractical. Also, the Query results panel hardcodes `background:#1e1e1e` — unreadable if MudBlazor is in light theme.

## 3. Fix design

### F1 — Add the three missing server commands
New handlers in `NoSqlServer.HandleCommandAsync`, matching the wire shapes `TcpAdminService` already sends (via `NoSqlMessage.CreateCommand`: `{"command","collection","document"}`):

- `createcollection` → `IDocumentStore.CreateCollectionAsync`; responds `{ created: bool, collection }` (`created=false` if it already existed).
- `dropcollection` → `DropCollectionAsync`; responds `{ dropped: bool, collection }`.
- `listdocuments` → reads `skip`/`take` from the `document` property (defaults 0/50, `take` capped at 500); responds `{ documents: [ { "_id": ..., ...data } ], total }` — documents are **flattened** (`_id` + data fields), which is the shape the Documents page renders. Implementation: `GetAllAsync` + in-memory `Skip/Take`, `CountAsync` for total (acceptable for an admin tool).

### F2 — Fix the `get` contract at both ends
Server: `HandleGetCommand` additionally returns a flat `document` property — `{ found, document: { "_id", ...data }, value }` (`value` retained for backward compatibility). Client: `GetAsync` keeps reading `document` (now correct against the real server). `ClientGetFixTests` gets a true end-to-end-shaped test.

### F3 — Serialize `CreateCommand` payloads properly
Replace the `StringBuilder` in `NoSqlMessage.CreateCommand` with `JsonSerializer.Serialize` over an ordered payload object — identical wire shape for well-formed names, correct escaping for the rest.

### F4 — Turn the Query page into an honest Command Console
Rename UI copy to "Command Console". A dropdown of command templates (`get`, `set`, `delete`, `exists`, `find_one`, `count`, `listcollections`, `listdocuments`, `stats`) pre-fills the editor with valid JSON; the page validates the input is JSON with a `command` property before sending; results render pretty-printed in a theme-aware panel. No SQL implied anywhere.

### F5 — Multiline JSON dialogs + theme-aware output
`TextInputDialog` gains a `Lines` parameter (default 1); Documents Insert/Edit pass `Lines=12`. The results panel drops the hardcoded colors and uses `MudPaper` defaults + `<pre>` styling.

## 4. Known limitations accepted as-is (documented, not fixed here)

- **Session lifetime:** `TcpAdminService` is circuit-scoped; a browser refresh drops the TCP connection and returns to login. Acceptable for an admin tool; a reconnect/session-persistence layer is future work.
- **Server-side authorization:** the TCP protocol enforces auth only at the `Authentication` message; commands are not gated per-connection server-side. The admin login is therefore cosmetic from a security standpoint. Fixing this is a server security work item (tracked in README warnings), out of scope for a UI plan.
- **N+1 counts:** the Collections page issues one `count` per collection sequentially. Fine at admin scale.
- **Not built yet (future pages):** cache browser (already planned: `2026-07-14-cache-admin-ui.md`), cluster status (a TCP `cluster` command exists and is unused by the UI), index management, full-text search, import/export, multi-database selection (TCP path exposes a single store).

## 5. Testing strategy

- Server command handlers: xUnit tests via the same reflection-invocation pattern as `ClusterCommandTests`.
- Client fixes: round-trip tests against an in-process server (pattern already exists in the test project), replacing the simulation-only `ClientGetFixTests`.
- `CreateCommand` escaping: pure unit tests (serialize → parse → property equality, including quotes/backslashes/unicode in names).
- Razor pages: no test infrastructure exists (no bUnit); verified by driving the app per the plan's final checklist.
