# System API Design - AdvGenNoSQL Server

**Version**: 1.0.0
**Framework**: ASP.NET Core 9.0 Web API
**Author**: Systems Architect & Team Lead

---

## 1. Architectural Overview

The Web API layer acts as a secure, high-performance HTTP frontend for the AdvGenNoSQL Server. It is designed following Clean Architecture and SOLID principles, ensuring loose coupling between the HTTP presentation layer and the core NoSQL engine (`AdvGenNoSqlServer.Core`, `AdvGenNoSqlServer.Query`, `AdvGenNoSqlServer.Storage`).

### 1.1 Components & Flow
- **Controllers**: Handle HTTP requests, input validation, and route mapping.
- **Services/Use Cases**: Mediate between controllers and the core NoSQL logic. They encapsulate business rules and transaction boundaries.
- **Core Engine Integration**: Directly interfaces with `IDocumentStore`, `IQueryExecutor`, and `ITransactionCoordinator`.

### 1.2 Technology Stack
- **Framework**: ASP.NET Core 9.0 Minimal APIs / Controllers
- **Serialization**: `System.Text.Json` with source generators for maximum performance.
- **Dependency Injection**: Native Microsoft DI.
- **Documentation**: Swagger/OpenAPI (`Swashbuckle.AspNetCore`).

---

## 2. Security & Authentication (OWASP Mitigation)

Security is paramount. The API must be protected against common vulnerabilities and ensure strict access control.

### 2.1 Authentication & Authorization
- **JWT (JSON Web Tokens)**: All endpoints (except `/api/auth/login` and `/health`) require a valid JWT. The token contains the user's identity and roles.
- **RBAC (Role-Based Access Control)**: Enforced via ASP.NET Core policies (e.g., `[Authorize(Roles = "Admin")]`). Roles align with the existing `RoleManager`.
- **Token Lifecycle**: Short-lived access tokens (e.g., 15 minutes) with secure, HttpOnly refresh tokens.

### 2.2 OWASP Mitigations
- **Injection Prevention**: All queries and document IDs are parameterized and validated before reaching the `QueryEngine` or `StorageEngine`. The custom query syntax must be strictly parsed to prevent NoSQL injection.
- **Rate Limiting**: Implementation of ASP.NET Core Rate Limiting middleware to prevent DoS attacks and brute-forcing (especially on the `/auth` endpoints).
- **CORS (Cross-Origin Resource Sharing)**: Strictly configured to allow only trusted origins. Wildcard `*` origins are explicitly forbidden in production.
- **Path Traversal**: Utilize the existing `PathValidator` in `AdvGenNoSqlServer.Core.Security` to ensure any file-based operations stay within intended boundaries.
- **Audit Logging**: All security-relevant events (logins, authorization failures, critical data access) must be logged using both standard `ILogger` and the custom `IAuditLogger.LogSecurityWarning` to maintain a robust audit trail.
- **HTTPS Only**: Enforcement of HTTPS redirection and HSTS.

---

## 3. API Endpoints

All endpoints are prefixed with `/api/v1`.

### 3.1 Authentication
- **`POST /api/v1/auth/login`**
  - **Description**: Authenticate and receive a JWT.
  - **Body**: `{ "username": "...", "password": "..." }`
  - **Response**: `200 OK` with JWT token and refresh token.

### 3.2 System & Health
- **`GET /api/v1/health`**
  - **Description**: Lightweight health check for load balancers.
  - **Response**: `200 OK`
- **`GET /api/v1/system/status`**
  - **Description**: Detailed system status (requires `Admin` role).
  - **Response**: `200 OK` with metrics (memory, connections, storage).

### 3.3 Collections
- **`GET /api/v1/collections`**
  - **Description**: List all collections.
  - **Response**: `200 OK` with `[ "users", "products", ... ]`
- **`POST /api/v1/collections/{collectionName}`**
  - **Description**: Create a new collection.
  - **Response**: `201 Created`
- **`DELETE /api/v1/collections/{collectionName}`**
  - **Description**: Drop a collection. Requires high privileges.
  - **Response**: `204 No Content`

### 3.4 Documents (CRUD)
- **`GET /api/v1/{collectionName}/{id}`**
  - **Description**: Retrieve a document by ID.
  - **Response**: `200 OK` with document payload or `404 Not Found`.
- **`POST /api/v1/{collectionName}`**
  - **Description**: Insert a new document.
  - **Body**: JSON document payload.
  - **Response**: `201 Created` with the assigned `id`.
- **`PUT /api/v1/{collectionName}/{id}`**
  - **Description**: Fully replace a document.
  - **Body**: JSON document payload.
  - **Response**: `200 OK` or `204 No Content`.
- **`DELETE /api/v1/{collectionName}/{id}`**
  - **Description**: Delete a document.
  - **Response**: `204 No Content`.

### 3.5 Querying
- **`POST /api/v1/{collectionName}/query`**
  - **Description**: Execute a complex query against a collection. Uses `POST` to allow complex JSON filter payloads without URL length limitations.
  - **Body**:
    ```json
    {
      "filter": { "age": { "$gt": 18 } },
      "sort": [{ "field": "name", "direction": "asc" }],
      "skip": 0,
      "limit": 50
    }
    ```
  - **Response**: `200 OK` with `QueryResult` containing documents and total count.

### 3.6 Batch Operations
- **`POST /api/v1/{collectionName}/batch`**
  - **Description**: Execute multiple insert/update/delete operations in a single request.
  - **Body**: `BatchOperationRequest` payload.
  - **Response**: `200 OK` with `BatchOperationResponse`.

---

## 4. Performance Optimization

- **Asynchronous Processing**: All endpoints are fully asynchronous (`async Task<IActionResult>`) to ensure thread-pool scalability under heavy load.
- **JSON Serialization**: Use `System.Text.Json` with source generators where possible to eliminate reflection overhead during serialization/deserialization.
- **Pagination & Limits**: Mandatory pagination for queries. Hard limits on `limit` parameters (e.g., max 1000 documents per request) to prevent memory exhaustion.
- **Caching Headers**: Implement ETag and `Cache-Control` headers for `GET` requests where appropriate to allow client-side caching.
- **Batch Resolution**: Utilize `GetManyAsync` on implementations of `IDocumentStore` to resolve N+1 query patterns efficiently when retrieving multiple documents.
