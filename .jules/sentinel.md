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

## 2026-03-05 - [Missing JWT Signature Validation]
**Vulnerability:** JWT Signature Verification Bypass in `JwtTokenProvider.cs`. `ExtractUsername` and `GetExpirationTime` decoded the token and returned data without validating the HMAC signature.
**Learning:** Returning parsed data from a JWT without explicitly calling the signature validation logic (or a dedicated internal token validator) permits attackers to forge claims by tampering with the payload and submitting it. Any logic relying on these methods for authentication or authorization is vulnerable.
**Prevention:** Before extracting and returning claims from a JSON Web Token, ensure that the token's signature is verified using fixed-time cryptographic comparison (`CryptographicOperations.FixedTimeEquals`).
## 2026-03-05 - [ReDoS in DocumentValidator.cs]
**Vulnerability:** Regular Expression Denial of Service (ReDoS) vulnerability in `AdvGenNoSqlServer.Core/Validation/DocumentValidator.cs` when evaluating the string "pattern" property and the email, ipv4, and hostname formats using `Regex.IsMatch`.
**Learning:** Hardcoding regular expression checks on user-supplied strings using `Regex.IsMatch` without providing a `TimeSpan` timeout makes the application vulnerable to excessive CPU consumption, especially for inherently complex regex patterns.
**Prevention:** For `Regex.IsMatch` calls evaluating external inputs against patterns (even static/precompiled ones for formats), always inject a static readonly timeout configuration (e.g. `RegexTimeout = TimeSpan.FromMilliseconds(100)`) and safely handle the resulting `RegexMatchTimeoutException`.

## 2026-10-24 - [Path Traversal in DatabaseManager]
**Vulnerability:** Path Traversal via unvalidated `name` and `_defaultDatabaseName` in `DatabaseManager.cs` allowing directory creation and access outside the intended base directory.
**Learning:** `DatabaseManager.cs` was using simple string filtering (`.Contains('/')` and `.Contains('\\')`) on database names instead of structural path validation. This approach is prone to errors, as edge cases in path construction via `Path.Combine` can still allow directory escape. Moreover, it was completely absent for the default database path.
**Prevention:** Always rely on `PathValidator.GetSafePath` around `Path.Combine` to validate the final resolved path instead of attempting manual string filtering. This prevents path traversal vulnerabilities comprehensively.
