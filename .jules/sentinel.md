## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.

## 2026-03-18 - [Thread Safety in AuthenticationManager Shared State]
**Vulnerability:** Use of non-thread-safe `Dictionary<string, ...>` for `_users` and `_activeSessions` in `AuthenticationManager.cs` (SEC-003). Concurrent access from multiple connections could cause race conditions, data corruption, or denial of service due to infinite loops during dictionary resizing.
**Learning:** Core authentication systems managing shared state across multiple concurrent network requests must use thread-safe collections. Replacing `Dictionary` with `ConcurrentDictionary` and `Remove` with `TryRemove` provides a robust fix without requiring extensive locking logic.
**Prevention:** Always use `ConcurrentDictionary` or explicitly synchronized collections for shared state in singleton or long-lived service classes that handle concurrent requests.
