## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.

## 2026-03-05 - [Regex Denial of Service in Filtering & Validation]
**Vulnerability:** Regular Expression Denial of Service (ReDoS) due to unbounded `Regex.IsMatch` execution times in `FilterEngine.cs` and `DocumentValidator.cs`.
**Learning:** `Regex.IsMatch` was used to evaluate dynamic/wildcard user input against regular expressions without a timeout. This allowed attackers to craft malicious patterns and long strings that caused catastrophic backtracking, hogging CPU and resulting in a denial of service.
**Prevention:** Always use `TimeSpan` timeouts (e.g., 100ms) with `Regex.IsMatch`, especially when evaluating dynamic or complex patterns, and catch `RegexMatchTimeoutException` to fail securely.
