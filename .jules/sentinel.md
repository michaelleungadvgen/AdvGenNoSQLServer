## 2026-03-05 - [Path Traversal in HybridDocumentStore]
**Vulnerability:** Path Traversal via unvalidated `collectionName` and `documentId` in `HybridDocumentStore.cs` allowing reads/writes outside the intended base directory.
**Learning:** Even though `collectionName` was partially validated, `documentId` was concatenated directly using `Path.Combine` without checking if the resulting path escaped the base directory. This allowed directory traversal attacks via malicious IDs like `../../sensitive_file`.
**Prevention:** Always use `PathValidator.GetSafePath` when constructing file paths from user inputs to ensure the resulting path remains within the allowed base directory.

## 2026-03-05 - [Authorization Bypass in AuthenticationService]
**Vulnerability:** Authorization Bypass in `AuthenticationService.Authorize`. The method returned `AuthorizationResult.Success()` without actually checking user permissions.
**Learning:** The method was marked as a "simplified version" and missed crucial logic to retrieve the user's username from the token and validate their permissions against the required ones. This left protected actions exposed to any authenticated user.
**Prevention:** Ensure all authorization methods perform concrete permission validation instead of relying on placeholder or simplified logic, mapping the token to the user and verifying their specific roles/permissions.

## 2026-03-05 - [JSON Injection Vulnerability in Client and Protocol]
**Vulnerability:** Constructing JSON payloads manually using string interpolation in `AdvGenNoSqlServer.Client/Client.cs` (`AuthenticateAsync`) and `AdvGenNoSqlServer.Network/MessageProtocol.cs` (`CreateCommand`, `CreateError`) introduced a severe injection risk. If user inputs (like credentials, command parameters, or error messages) contained unescaped quotes or JSON syntax characters, attackers could alter the payload structure or inject arbitrary fields.
**Learning:** Manual JSON construction via string concatenation is extremely fragile and insecure. It implicitly trusts all inputs as safe strings, ignoring serialization standards.
**Prevention:** Always use established serialization libraries like `System.Text.Json.JsonSerializer.Serialize()` to construct JSON. This ensures that all string values are properly escaped and the resulting JSON adheres to specification, fully mitigating injection vectors.
