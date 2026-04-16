## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.

## 2026-03-05 - [ReDoS in FilterEngine Regex Evaluation]
**Vulnerability:** Regular Expression Denial of Service (ReDoS) vulnerability in `AdvGenNoSqlServer.Query/Filtering/FilterEngine.cs` during `$regex` evaluation.
**Learning:** Evaluating user-supplied or highly variable regex patterns using `Regex.IsMatch` without a timeout leaves the server vulnerable to catastrophic backtracking when complex strings are provided. Additionally, using `RegexOptions.Compiled` for one-off patterns forces compilation to IL and severely degraded server performance.
**Prevention:** Always supply a `TimeSpan` timeout (e.g. 100ms) to `Regex.IsMatch` and handle `RegexMatchTimeoutException`. Never use `RegexOptions.Compiled` for dynamic patterns generated from user queries.
## 2026-03-05 - [Exportable Certificates in TlsStreamHelper]
**Vulnerability:** Loading certificates from PFX files using `X509KeyStorageFlags.Exportable`.
**Learning:** In production environments, private keys of server certificates should not be exportable from memory to prevent extraction if the application memory is dumped or compromised.
**Prevention:** Use `X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet` instead of `X509KeyStorageFlags.Exportable` when loading certificates for server use.
