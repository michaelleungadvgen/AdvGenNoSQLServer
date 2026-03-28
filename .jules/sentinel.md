## 2024-03-28 - [Title]
 **Vulnerability:** Unsafe manual JSON construction via string interpolation (`$"{{\"code\":\"{code}\"}}"`) leading to potential JSON injection.
 **Learning:** Replacing manual JSON construction with `System.Text.Json.JsonSerializer.Serialize` is the secure way to format JSON payloads. However, when doing so, verify that existing tests do not rely on exact string matches of the old, manually constructed output.
 **Prevention:** Always construct JSON using a robust serializer rather than string builders or interpolation, and proactively search for tests that might break due to subtle output changes (like whitespace or property ordering) when migrating to a serializer.
