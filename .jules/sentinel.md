## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.

## 2025-02-13 - [Fix User Registration Atomicity]
**Vulnerability:** User registration and initial role assignment were performed non-atomically. If a failure occurred during role assignment, the user would remain registered in the system without an assigned role.
**Learning:** This could lead to a situation where authenticated users are orphaned without proper permissions, making them either completely useless or posing a security risk if the system defaults to permissive behavior for users without roles.
**Prevention:** Use transactional boundaries for multi-step data modification processes, or implement explicit cleanup/rollback logic (e.g. `_authManager.RemoveUser(username)`) when a dependent secondary operation (like role assignment) fails to complete.
