## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.

## 2024-05-24 - [Critical] Path Traversal in HybridDocumentStore
**Vulnerability:** The `HybridDocumentStore` was combining user-supplied input (`collectionName` and `documentId`) directly into `Path.Combine()` without validation, allowing path traversal (e.g., `../../`) to read, write, or delete arbitrary files on disk.
**Learning:** Even if a storage engine wraps an in-memory dictionary, underlying read-through caching and async write queues must validate file paths before hitting the filesystem.
**Prevention:** All filesystem interactions utilizing external input must consistently pass through `AdvGenNoSqlServer.Core.Security.PathValidator.GetSafePath()` to ensure they remain within intended base directories.
