# User Management + RBAC — Design

**Date:** 2026-07-15
**Status:** Approved by user (brainstorming session)
**Companion plan:** `docs/superpowers/plans/2026-07-15-user-management-rbac.md` (written after spec approval)

## Goal

Give AdvGenNoSqlServer real user management with role-based access control, managed from the Admin UI:

1. **Persistent user accounts** — users survive server restarts (today `AuthenticationManager` is in-memory; after restart only `admin` exists).
2. **Three built-in roles enforced on every TCP command** — `admin`, `readwrite`, `readonly`.
3. **User-management TCP commands + Admin UI page** — list/create/delete users, change roles, reset passwords, self-service password change.

Non-goals: custom roles / per-permission grants (the existing `RoleManager`/`PermissionRegistry` stays unused for now), HTTP-API user management, per-collection ACLs, dispatcher consolidation (tracked separately as tech debt D6 in the 2026-07-14 admin UI audit).

## Background (verified against source)

- `Core/Authentication/AuthenticationManager.cs`: PBKDF2 (100k iterations, SHA-256) hashing, `ConcurrentDictionary` of `UserCredentials` (no `Role` field), token sessions, `RegisterUser`/`Authenticate`/`ChangePassword`/`RemoveUser`/`GetUsers`. Seeds `admin` from `ServerConfiguration.MasterPassword`. **No persistence.**
- The Host's TCP `HandleAuthenticationAsync` (`Host/Program.cs:711`) authenticates via `AuthenticationManager` and returns a token — but **no command checks authentication or roles**; the token is never consulted again.
- The Server project's dispatcher (`Server/NoSqlServer.cs`) has its own simplified auth (username==admin && password==MasterPassword) and likewise enforces nothing.
- Two duplicated dispatchers exist (audit D6): Host `NoSqlServerHost` (production) and Server `NoSqlServer` (test suite). Both must be wired.

## Decisions log

| Question | Decision |
|---|---|
| Scope | Full RBAC including per-command enforcement |
| Persistence | Dedicated `users.json` file (works under future CacheOnly mode; not a document collection) |
| Role model | Three built-in roles: `admin`, `readwrite`, `readonly` |
| Enforcement trigger | Only when `RequireAuthentication=true`; `false` preserves today's anonymous full access (tests/examples unchanged) |
| Architecture | Shared Core components wired identically into both dispatchers |

## Design

### 1. Core components (`AdvGenNoSqlServer.Core/Authentication/`)

**`UserRole`** — string constants `admin`, `readwrite`, `readonly` + validation helper. Stored as strings for JSON friendliness.

**`IUserStore` / `FileUserStore`** (new):
- Path: `ServerConfiguration.UserStorePath`; default `<StoragePath>/users.json` (resolved absolute the same way `StoragePath` is).
- JSON shape: `{ "users": [ { "username", "passwordHash", "salt", "role", "createdAt" } ] }` — never plaintext passwords.
- `Load()` returns the user list; missing file → empty list; corrupt file → empty list + warning log (server still starts; admin re-seeded from `MasterPassword`).
- `Save(users)` writes atomically: serialize to `users.json.tmp`, then `File.Move(tmp, path, overwrite: true)`.
- Thread safety: `AuthenticationManager` serializes calls through a lock around mutations.

**`AuthenticationManager` changes**:
- `UserCredentials` gains `public string Role { get; set; } = UserRole.ReadWrite;`.
- Constructor takes optional `IUserStore`; loads users from it, then seeds `admin` (role `admin`) from `MasterPassword` **only if no user with role `admin` was loaded** (so a deliberately deleted `admin` account stays deleted while another admin exists, and a lost-all-admins store still recovers via `MasterPassword`).
- `RegisterUser(username, password, role)` (old 2-arg overload kept, defaults `readwrite`).
- New: `SetPassword(username, newPassword)` — admin reset, no old password, revokes the user's tokens. `SetRole(username, role)` — takes effect on the user's **next authentication**; already-connected sessions keep their old role until they reconnect (documented behavior, acceptable for an admin tool). `ListUsers()` → `(Username, Role, CreatedAt)` projections, no hashes.
- `AuthToken` gains a `Role` property so dispatchers learn the role directly from the `Authenticate` result.
- Every mutation (`RegisterUser`, `RemoveUser`, `ChangePassword`, `SetPassword`, `SetRole`) persists via `IUserStore.Save`.
- Guards: `RemoveUser`/`SetRole` fail with a distinct result when the target is the **last admin** (count of users with role admin == 1 and target is it).

**`CommandAuthorizer`** (new):
- `enum CommandAccess { Read, Write, Admin }`.
- Static map command→access:
  - Read: `get, exists, count, find_one, listcollections, listdocuments, stats`
  - Write: `set, delete, insert, replace, upsert, touch, createcollection, dropcollection`
  - Admin: `listusers, createuser, deleteuser, setpassword, setrole, cluster`
  - Special: `changepassword` requires only an authenticated identity (any role).
- `bool IsAllowed(string command, string role)` — `readonly`→Read; `readwrite`→Read+Write; `admin`→all. Unknown commands return allowed (they fall through to the dispatcher's `UNKNOWN_COMMAND` handling; authz must not mask that error).

### 2. Dispatcher wiring (both `NoSqlServerHost` and `Server.NoSqlServer`)

Identical hooks in both:
- `ConcurrentDictionary<string, (string Username, string Role)> _authenticatedConnections` keyed by connectionId. Set on successful `Authentication`; removed in the `ConnectionClosed` handler.
- The Server project's `HandleAuthenticationAsync` is upgraded to use a real `AuthenticationManager` (replacing the hardcoded admin/MasterPassword check) so both dispatchers share semantics; it gets the manager via its constructor (tests construct it directly).
- Auth success responses gain `role`: `{ authenticated, token, username, role }`.
- In `HandleCommandAsync`, after parsing the command name and **only when `config.RequireAuthentication` is true**:
  1. connection not in `_authenticatedConnections` → error `AUTH_REQUIRED` ("Authenticate before sending commands").
  2. `!CommandAuthorizer.IsAllowed(command, role)` → error `FORBIDDEN` ("Role '<role>' may not run '<command>'").
- `Handshake`, `Ping`, `Authentication` message types are never gated.
- With `RequireAuthentication=false`, commands run anonymously exactly as today — including the user-management commands (dev mode). Exception: `changepassword` still returns `AUTH_REQUIRED` without an authenticated identity (see §3). The anonymous auth success response reports `role: "admin"` so the Admin UI (which keys its Users page on the role) remains fully usable against a dev server.

### 3. User-management commands (JSON command family, both dispatchers)

| Command | Payload | Success response | Errors |
|---|---|---|---|
| `listusers` | — | `{ users: [ { username, role, createdAt } ] }` | — |
| `createuser` | `{ username, password, role }` | `{ created: true, username }` | `USER_EXISTS`, `INVALID_ROLE`, `WEAK_PASSWORD` |
| `deleteuser` | `{ username }` | `{ deleted: true }` | `USER_NOT_FOUND`, `LAST_ADMIN` |
| `setpassword` | `{ username, password }` | `{ changed: true }` | `USER_NOT_FOUND`, `WEAK_PASSWORD` |
| `setrole` | `{ username, role }` | `{ changed: true }` | `USER_NOT_FOUND`, `INVALID_ROLE`, `LAST_ADMIN` |
| `changepassword` | `{ oldPassword, newPassword }` | `{ changed: true }` | `AUTH_FAILED` (wrong old password), `WEAK_PASSWORD`; identity = the connection's authenticated user |

Validation: username non-empty, ≤ 64 chars; password length ≥ 6; role ∈ {admin, readwrite, readonly}. `changepassword` when unauthenticated (`RequireAuthentication=false`, no identity) → `AUTH_REQUIRED` (it has no meaning without an identity).

### 4. Client library + Admin UI

**Client** (`AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Users.cs`, partial class):
- `Task<IReadOnlyList<UserInfo>> ListUsersAsync()` (`UserInfo(Username, Role, CreatedAt)`)
- `CreateUserAsync(username, password, role)`, `DeleteUserAsync(username)`, `SetUserPasswordAsync(username, password)`, `SetUserRoleAsync(username, role)`, `ChangeMyPasswordAsync(oldPassword, newPassword)` — all return bool / throw `NoSqlClientException` with the server's error message.
- `AuthenticateAsync` additionally captures `role` from the response → exposed as `client.CurrentRole` (null when not authenticated / anonymous).

**TcpAdminService**: thin wrappers for all of the above + `public string? CurrentRole` set at login.

**Admin UI (`AdvGenNoSqlServer.AdminClient`)**:
- New `Pages/Users.razor` (`/users`): MudTable (username, role chip, created date, actions). Actions: change role (inline MudSelect), reset password (TextInputDialog), delete (ConfirmDialog; the UI also disables delete on the last admin). "Create User" button → dialog with username, password, role select. Non-admin visitors see a "requires admin role" MudAlert instead of the table.
- `Shared/NavMenu.razor`: "Users" link (People icon), rendered only when `AdminService.CurrentRole == "admin"`.
- `Shared/MainLayout.razor`: "Change password" icon button beside logout (any role) → dialog (old + new password) → `ChangeMyPasswordAsync`.
- Login page unchanged (role arrives via the auth response).

### 5. Error handling summary

- New error codes: `AUTH_REQUIRED`, `FORBIDDEN`, `USER_EXISTS`, `USER_NOT_FOUND`, `LAST_ADMIN`, `INVALID_ROLE`, `WEAK_PASSWORD`. The UI maps each to a specific snackbar message.
- Corrupt `users.json` → warning + empty store + admin re-seed; the broken file is preserved as `users.json.corrupt-<timestamp>` for inspection.
- Existing behavior preserved: `RequireAuthentication=false` bypasses all gating; all current tests/examples run unchanged.

### 6. Testing

- **Unit — FileUserStore**: save/load round-trip, atomic replace leaves valid file, missing file → empty, corrupt file → empty + `.corrupt` backup.
- **Unit — AuthenticationManager**: role persisted through store reload; last-admin delete/demote guards; SetPassword revokes tokens; admin seeding only when absent.
- **Unit — CommandAuthorizer**: full matrix (3 roles × read/write/admin commands + unknown command pass-through).
- **Dispatcher tests** (Server project, reflection pattern): with `RequireAuthentication=true` — unauthenticated command → `AUTH_REQUIRED`; readonly blocked from `set` (`FORBIDDEN`) but allowed `get`; readwrite blocked from `createuser`; admin full access; auth response contains `role`. With `RequireAuthentication=false` — everything anonymous works (regression guard).
- **User-command tests**: create/list/delete/setrole/setpassword/changepassword round-trips incl. every error code.
- **E2E**: harness against the live Host over TCP+SSL — create `readonly` user, reconnect as it, verify `get` works and `set` is `FORBIDDEN`; restart Host, verify the user survives; Admin UI serves and Users page markup is present.
