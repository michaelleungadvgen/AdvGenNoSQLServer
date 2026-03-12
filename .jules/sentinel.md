## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.
## 2024-03-12 - Timing Attack in Password Verification
**Vulnerability:** Direct string comparison (`!=`) was used for comparing password hashes, allowing for potential timing attacks where an attacker could deduce valid hashes by measuring verification time. Furthermore, the codebase was using a less secure `SHA256.Create().ComputeHash()` instead of a standard key derivation function.
**Learning:** For securely hashing and verifying passwords in .NET, PBKDF2 (`Rfc2898DeriveBytes`) should be used for generation, and `CryptographicOperations.FixedTimeEquals` must be employed for verification to prevent timing attacks. Additionally, shared state like user and session dictionaries must use `ConcurrentDictionary` to prevent thread-safety issues during concurrent access.
**Prevention:** Enforce the use of standard key derivation algorithms (like PBKDF2, Argon2) for password hashing and always mandate `CryptographicOperations.FixedTimeEquals` for any secure token/hash comparisons. Use thread-safe collections for in-memory shared state.
