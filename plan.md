1. **Fix Critical Hardcoded Secrets in Config:**
   - Modify `AdvGenNoSqlServer.Host/appsettings.json` to replace the hardcoded plaintext secrets.
   - Set `"MasterPassword": ""` (to force environment variable usage or disable master login).
   - Set `"JwtSecretKey": ""` (so the server securely generates a random one at runtime, which is supported by `JwtTokenProvider.cs`).

2. **Add a Journal Entry:**
   - Append the learning to `.jules/sentinel.md` using the exact format.
   - The entry will explain that hardcoding passwords and JWT secrets in `appsettings.json` exposes them to source control, and setting them to empty forces secure runtime generation or environment injection.

3. **Pre-commit Steps:**
   - Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.
