## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.
## 2026-02-16 - [Fix Weak Password Hashing & Timing Attacks]
**Vulnerability:** Passwords were automatically hashed using SHA256 (a fast, non-salted hashing algorithm) making the application vulnerable to brute-force and rainbow table attacks. Also, password hashes were compared using the `!=` operator, leaving it vulnerable to timing attacks. Finally, session dictionaries were not thread-safe.
**Learning:** For secure password storage, rely on proper key-derivation functions like PBKDF2 (Rfc2898DeriveBytes) with at least 100k iterations and a large salt. Always use `CryptographicOperations.FixedTimeEquals` for comparing sensitive data (like password hashes or API tokens). When creating shared in-memory user sessions, always use `ConcurrentDictionary`.
**Prevention:** Establish a project-wide security baseline: enforce PBKDF2 (or bcrypt/Argon2) for all password hashing, `FixedTimeEquals` for string/hash comparisons related to authentication, and standard thread-safe collections (`ConcurrentDictionary`) for global state.
