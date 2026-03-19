## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.

## 2026-03-05 - [Timing Attack Vulnerability in Password Comparison]
**Vulnerability:** Timing attack vulnerability in `AuthenticationManager.cs` where password hashes were compared using the `!=` operator, allowing an attacker to theoretically determine valid password hashes by measuring the time taken for the comparison to fail.
**Learning:** Standard equality operators in C# for strings or byte arrays (unless explicitly designed otherwise) return early on the first non-matching byte or character, leading to timing differences. Security-critical comparisons like password hashes, HMACs, or signatures must use constant-time operations to mitigate timing side-channels.
**Prevention:** Always use `CryptographicOperations.FixedTimeEquals` for comparing security-sensitive byte arrays (such as decoded password hashes) to ensure the comparison time is independent of the input values.
