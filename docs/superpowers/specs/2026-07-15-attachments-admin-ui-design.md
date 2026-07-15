# Document Attachments over TCP + Admin UI — Design

**Date:** 2026-07-15
**Status:** Approved by user (brainstorming session)
**Companion plan:** `docs/superpowers/plans/2026-07-15-attachments-admin-ui.md` (written after spec approval)

## Goal

Make document attachments (files attached to a document) usable end to end: expose the existing `AttachmentStore` over the TCP protocol, add a client API, and surface files in the Admin UI on the Documents page, with total storage on the Dashboard.

## Background (verified against source)

- `AdvGenNoSqlServer.Storage/Attachments/AttachmentStore.cs` implements `IAttachmentStore` (`AdvGenNoSqlServer.Core/Attachments/IAttachmentStore.cs`): `StoreAsync`, `GetAsync`, `GetInfoAsync`, `ListAsync`, `DeleteAsync`, `DeleteAllAsync`, `ExistsAsync`, `GetTotalStorageSizeAsync`. Constructor takes `AttachmentStoreOptions { BasePath, MaxAttachmentSize=100MB, MaxTotalStorage, BlockedContentTypes=[exe types], ... }` and creates `BasePath`. Files live at `basePath/collection/documentId/name` with a `_metadata.json` sidecar; each attachment carries a SHA-256 `Hash`.
- **Not wired anywhere in production**: `AttachmentStore` is never constructed by the Host or Server dispatcher; neither dispatcher has attachment commands; the client library has no attachment methods; the Admin UI has no file surface.
- Both dispatchers now enforce RBAC via `CommandAuthorizer` (command→access map) when `RequireAuthentication=true`, and both resolve `StoragePath` to an absolute path at startup. The Documents page (`AdvGenNoSqlServer.AdminClient/Pages/Documents.razor`) lists documents per collection with a `_id` action column; the Dashboard (`Index.razor`) shows stat tiles.
- The command protocol is length-framed JSON (`NoSqlMessage`), max payload 100MB (`MessageProtocol.ValidateHeader`). `NoSqlMessage.CreateCommand` now serializes payloads with `JsonSerializer` (safe escaping).

## Decisions log

| Question | Decision |
|---|---|
| Meaning of "file" | Document attachments (the existing AttachmentStore) |
| Operations | Full: list + upload + download + delete (+ info, total storage) |
| Binary transport | Base64 inside the existing JSON command envelope (no new message type) |
| RBAC mapping | list/info/download/totalstorage = Read; upload/delete = Write |
| UI placement | Attachments dialog per Documents row; "Files" total-storage tile on Dashboard |

## Design

### 1. Server: construct AttachmentStore + six commands (both dispatchers)

Both `NoSqlServerHost` (`Host/Program.cs`) and `Server/NoSqlServer.cs` construct one `AttachmentStore` at startup, after the storage path is resolved:

```
BasePath = <resolved StoragePath>/attachments
MaxAttachmentSize = 25 MB   // UI-facing cap; well under the 100MB frame limit even after base64
```

A `MaxAttachmentSizeMB` (default 25) is added to `ServerConfiguration`. The store is disposed on shutdown. Attachments are keyed by `(collection, documentId, name)`; the store does not require the document to exist (it manages its own directory tree), so commands validate only their own inputs.

Six JSON commands (added to both dispatchers' command switches; the command name is lower-cased before dispatch and before authorization):

| Command | Request | Success response | Errors |
|---|---|---|---|
| `listattachments` | `{collection, id}` | `{ attachments: [ {name, contentType, size, hash, createdAt, updatedAt} ] }` | `INVALID_COMMAND` |
| `attachmentinfo` | `{collection, id, name}` | `{ found, info: {…} }` | `INVALID_COMMAND` |
| `uploadattachment` | `{collection, id, name, contentType, contentBase64}` | `{ stored: true, name, hash, size }` | `INVALID_COMMAND`, `CONTENT_TYPE_BLOCKED`, `ATTACHMENT_TOO_LARGE`, `INVALID_CONTENT` (bad base64) |
| `downloadattachment` | `{collection, id, name}` | `{ found: true, name, contentType, size, contentBase64 }` or `{ found: false }` | `INVALID_COMMAND` |
| `deleteattachment` | `{collection, id, name}` | `{ deleted: bool }` | `INVALID_COMMAND` |
| `totalstorage` | — | `{ bytes }` | — |

- Validation: `collection`, `id`, `name` non-empty; `name` ≤ 255 chars; `contentType` defaults to `application/octet-stream` when absent; decoded content length ≤ `MaxAttachmentSizeMB` (checked before calling the store, so the UI gets `ATTACHMENT_TOO_LARGE` rather than the store's generic message). Base64 decode failure → `INVALID_CONTENT`.
- `uploadattachment` maps the store's `AttachmentResult.Success=false` to `CONTENT_TYPE_BLOCKED` (message contains "not allowed") or `COMMAND_ERROR` otherwise.
- The commands live in a small partial/region so both dispatchers stay in lockstep (audit D6 debt acknowledged; kept identical, not consolidated).
- `IAttachmentStore.DeleteAllAsync` is intentionally **not** surfaced as a command in this version (no per-document bulk-delete UI need yet); only the six commands above are exposed.

### 2. RBAC

`CommandAuthorizer.Map` gains:
- Read: `listattachments`, `attachmentinfo`, `downloadattachment`, `totalstorage`
- Write: `uploadattachment`, `deleteattachment`

So `readonly` can view and download, `readwrite`+ can upload and delete, `admin` everything. Enforcement only when `RequireAuthentication=true` (unchanged rule). Covered by extending the existing `CommandAuthorizerTests` matrix.

### 3. Client library

New partial `AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Attachments.cs`, exposed as `client.Attachments`:

```csharp
public record AttachmentMetadata(string Name, string ContentType, long Size, string Hash, DateTime CreatedAt, DateTime UpdatedAt);

client.Attachments.ListAsync(collection, id)            // IReadOnlyList<AttachmentMetadata>
client.Attachments.InfoAsync(collection, id, name)      // AttachmentMetadata? (null if not found)
client.Attachments.UploadAsync(collection, id, name, contentType, byte[] content)  // AttachmentMetadata (throws on blocked/oversize)
client.Attachments.DownloadAsync(collection, id, name)  // byte[]? (null if not found)
client.Attachments.DeleteAsync(collection, id, name)    // bool
client.Attachments.TotalStorageBytesAsync()             // long
```

Bytes are base64-encoded/decoded inside the client; callers pass/receive `byte[]`. Server error codes surface as `NoSqlClientException` with the code+message (upload throws on `CONTENT_TYPE_BLOCKED`/`ATTACHMENT_TOO_LARGE`); `NotFound` maps to null/false, never an exception.

### 4. Admin UI

**Dashboard** (`Index.razor`): a "Files" tile showing total attachment storage. Note: AdminClient has no `FormatBytes` helper today (only `FormatUptime`) — the plan adds a small `FormatBytes(long)` helper (reused by the attachments dialog for sizes). `TcpAdminService.GetStatsAsync` is unaffected; a separate `GetTotalAttachmentStorageAsync` feeds the tile (fails soft — tile shows "—" on error so older servers don't break the dashboard).

**Documents page** (`Documents.razor`): each row gains a paperclip `MudIconButton` opening an **attachments dialog** for that `(collection, _id)`:
- Lists attachments in a `MudTable`: name, content type, size (`FormatBytes`), short hash, created date.
- **Upload** (hidden for `readonly`): `MudFileUpload` (single file, client-side ≤ 25 MB guard with a clear message); reads bytes, calls `UploadAsync` with the file name and browser-provided content type; refreshes the list.
- **Download**: per row, fetches bytes via `DownloadAsync` and triggers a browser download using a JS `saveAsFile` interop helper (base64 → Blob) added to `_Host`/wwwroot.
- **Delete** (hidden for `readonly`): `ConfirmDialog`, then `DeleteAsync`, refresh.
- Role gating uses `AdminService.CurrentRole` (already available from the RBAC work).

**TcpAdminService**: thin wrappers — `ListAttachmentsAsync`, `UploadAttachmentAsync`, `DownloadAttachmentAsync`, `DeleteAttachmentAsync`, `GetTotalAttachmentStorageAsync` — each `EnsureConnected()` then delegate to `client.Attachments`.

### 5. Error handling summary

- New error codes: `CONTENT_TYPE_BLOCKED`, `ATTACHMENT_TOO_LARGE`, `INVALID_CONTENT`. UI maps each to a specific snackbar.
- Blocked types (default `application/x-msdownload`, `x-executable`, `x-dosexec`) rejected server-side; the UI surfaces the reason.
- Oversize caught client-side (fast feedback) and server-side (authoritative).
- Missing attachment/document → empty list or `found:false`, never an error.

### 6. Testing

- **Unit — AttachmentStore round-trip** (may already exist as `AttachmentStoreTests`; add coverage only if gaps): store→list→get hash match→delete.
- **Dispatcher — Server project, reflection pattern** (`RequireAuthentication=false` for the plain command tests, `=true` for the RBAC ones): upload→list shows it→download returns identical bytes (hash match)→delete removes it; `attachmentinfo` found/not-found; `totalstorage` increases after upload; oversize→`ATTACHMENT_TOO_LARGE`; blocked content type→`CONTENT_TYPE_BLOCKED`; bad base64→`INVALID_CONTENT`.
- **RBAC**: `readonly` may `listattachments`/`downloadattachment` but gets `FORBIDDEN` on `uploadattachment`/`deleteattachment`; `readwrite` may upload.
- **CommandAuthorizer**: extend the matrix with the six commands × three roles.
- **Client round-trip** (real server): `UploadAsync`→`DownloadAsync` byte-identical; `ListAsync`/`InfoAsync`/`DeleteAsync`/`TotalStorageBytesAsync`; blocked/oversize throw.
- **E2E over live Host (TCP+SSL)**: as admin, upload a file to a document, download it and assert the SHA-256 matches, list shows it, delete removes it; as a `readonly` user, download works but upload is `FORBIDDEN`. AdminClient serves.

Note: no automated UI tests exist for AdminClient (no bUnit); Razor changes are verified by driving the app.
