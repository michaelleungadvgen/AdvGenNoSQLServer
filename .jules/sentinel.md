## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.
## 2025-02-13 - [SEC-017] Disable Development Mode Certificate Bypass in Production
**Vulnerability:** The `ServerCertificateValidationCallback` in `TlsStreamHelper.cs` explicitly allowed localhost certificates with a `RemoteCertificateNameMismatch` error to bypass validation unconditionally. This meant any production environment would silently accept an invalid certificate if its subject simply contained "localhost".
**Learning:** Development conveniences (like bypassing cert name checks for localhost) must always be conditionally compiled or placed behind explicit configuration flags that default to false. Unconditional bypasses left in the codebase are a major security risk when moving to production.
**Prevention:** Implement `AllowDevelopmentCertificates` flag in `TlsStreamHelper` (defaulting to `false`) to ensure that certificate name mismatch bypasses are strictly opt-in and cannot happen accidentally in a production environment. Always scrutinize `SslPolicyErrors.None` bypasses during code reviews.
