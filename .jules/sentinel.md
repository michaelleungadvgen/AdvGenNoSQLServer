## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.

## 2026-02-16 - Prevent Brute-force and Timing Attacks
**Vulnerability:** Used fast SHA256 for password hashing and non-constant-time string comparison for hashes.
**Learning:** Found a critical cryptographic failure pattern where password hashes were stored unsafely without using a key derivation function with enough iterations, leaving credentials vulnerable to brute-forcing. Furthermore, comparisons used simple string equality (`!=`), exposing the authentication system to timing side-channel attacks.
**Prevention:** Always use PBKDF2 (`Rfc2898DeriveBytes`), bcrypt, or Argon2 with high iteration counts (e.g., 100,000 for PBKDF2) for hashing passwords. Always use constant-time operations like `CryptographicOperations.FixedTimeEquals` for comparing security tokens or password hashes.
