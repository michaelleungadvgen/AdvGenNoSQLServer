## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.
## 2025-02-15 - Unauthenticated Database Command Execution Bypass
**Vulnerability:** The stateless TCP server (`AdvGenNoSqlServer.Host`) failed to enforce per-connection authentication state before processing `HandleCommandAsync` and `HandleBulkOperationAsync`. Even if `RequireAuthentication` was true, any client could bypass the authentication handshake and send raw database commands directly.
**Learning:** In stateless connection protocols (like raw TCP wrappers over NoSQL messages), authentication must be explicitly tracked on the server-side per connection and gated at the entry point of sensitive operations, not just assumed by the sequence of incoming messages.
**Prevention:** Always use thread-safe dictionaries (`ConcurrentDictionary`) mapped by `ConnectionId` to store authentication state upon successful login, and explicitly check this state at the top of every sensitive command handler.
