## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.
## 2026-10-27 - [Fix Path Traversal in HybridDocumentStore]
**Vulnerability:** A critical path traversal vulnerability was found in `AdvGenNoSqlServer.Storage/HybridDocumentStore.cs` where user-controlled inputs `collectionName` and `documentId` were passed directly to `Path.Combine` to construct `collectionPath` and `filePath`.
**Learning:** Although `collectionName` had some basic sanitization via `ValidateCollectionName(collectionName)`, `documentId` was entirely unsanitized. Combining unsanitized user inputs directly via `Path.Combine` allows an attacker to bypass intended directory restrictions (e.g. using `../../`) and read, write, or delete arbitrary files on the host system.
**Prevention:** All filesystem interactions must compute paths using `PathValidator.GetSafePath(basePath, Path.Combine(basePath, userInput))` to ensure that constructed paths cannot break out of their intended sandbox directories.
