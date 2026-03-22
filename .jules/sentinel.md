## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.

## 2024-05-27 - [High] Implement Thread-Safe RoleManager Collections
**Vulnerability:** SEC-011: The `RoleManager` and `PermissionRegistry` used standard, non-thread-safe collections (`Dictionary`, `HashSet`) for managing users, roles, and permissions, which could lead to race conditions, application crashes (DoS), or privilege escalation under concurrent authorization loads.
**Learning:** Naively converting collections to `ConcurrentDictionary` breaks public API definitions (like changing `HashSet` to `ConcurrentDictionary` in the `Role` model) and breaks serialization structures. Also, consecutive concurrent dictionary methods (e.g., check then update) are vulnerable to Time-of-Check to Time-of-Use (TOCTOU) race conditions in multi-step workflows.
**Prevention:** For multi-step state operations across related collections, utilize a coarse-grained exclusive `lock (_syncLock)` over standard dictionaries instead of fine-grained concurrent collections to ensure strict atomicity without altering public signatures.
