// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// Interface for audit logging operations
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Logs an audit event
    /// </summary>
    /// <param name="auditEvent">The audit event to log</param>
    void Log(AuditEvent auditEvent);

    /// <summary>
    /// Logs an audit event asynchronously
    /// </summary>
    /// <param name="auditEvent">The audit event to log</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a successful authentication event
    /// </summary>
    /// <param name="username">The username that authenticated</param>
    /// <param name="ipAddress">The client IP address</param>
    /// <param name="details">Additional details</param>
    void LogAuthentication(string username, string? ipAddress = null, string? details = null);

    /// <summary>
    /// Logs a failed authentication attempt
    /// </summary>
    /// <param name="username">The username that failed to authenticate</param>
    /// <param name="ipAddress">The client IP address</param>
    /// <param name="reason">The reason for failure</param>
    void LogAuthenticationFailure(string username, string? ipAddress = null, string? reason = null);

    /// <summary>
    /// Logs a logout event
    /// </summary>
    /// <param name="username">The username that logged out</param>
    /// <param name="ipAddress">The client IP address</param>
    void LogLogout(string username, string? ipAddress = null);

    /// <summary>
    /// Logs an authorization (permission) check
    /// </summary>
    /// <param name="username">The username attempting the action</param>
    /// <param name="permission">The permission being checked</param>
    /// <param name="granted">Whether the permission was granted</param>
    /// <param name="resource">The resource being accessed</param>
    void LogAuthorization(string username, string permission, bool granted, string? resource = null);

    /// <summary>
    /// Logs a data access event
    /// </summary>
    /// <param name="username">The username accessing data</param>
    /// <param name="operation">The operation performed (GET, SET, DELETE)</param>
    /// <param name="collection">The collection being accessed</param>
    /// <param name="documentId">The document ID (if applicable)</param>
    void LogDataAccess(string username, string operation, string collection, string? documentId = null);

    /// <summary>
    /// Logs an administrative action
    /// </summary>
    /// <param name="username">The username performing the action</param>
    /// <param name="action">The administrative action</param>
    /// <param name="details">Additional details</param>
    void LogAdminAction(string username, string action, string? details = null);

    /// <summary>
    /// Logs a security warning
    /// </summary>
    /// <param name="message">The warning message</param>
    /// <param name="username">The username involved (if applicable)</param>
    /// <param name="ipAddress">The client IP address (if applicable)</param>
    void LogSecurityWarning(string message, string? username = null, string? ipAddress = null);

    /// <summary>
    /// Gets recent audit events
    /// </summary>
    /// <param name="count">Maximum number of events to return</param>
    /// <returns>A list of recent audit events</returns>
    IReadOnlyList<AuditEvent> GetRecentEvents(int count = 100);

    /// <summary>
    /// Gets audit events by username
    /// </summary>
    /// <param name="username">The username to filter by</param>
    /// <param name="count">Maximum number of events to return</param>
    /// <returns>A list of audit events for the user</returns>
    IReadOnlyList<AuditEvent> GetEventsByUser(string username, int count = 100);

    /// <summary>
    /// Gets audit events by event type
    /// </summary>
    /// <param name="eventType">The event type to filter by</param>
    /// <param name="count">Maximum number of events to return</param>
    /// <returns>A list of audit events of the specified type</returns>
    IReadOnlyList<AuditEvent> GetEventsByType(AuditEventType eventType, int count = 100);

    /// <summary>
    /// Flushes any buffered audit events to persistent storage
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Types of audit events
/// </summary>
public enum AuditEventType
{
    /// <summary>Successful user authentication</summary>
    AuthenticationSuccess,

    /// <summary>Failed authentication attempt</summary>
    AuthenticationFailure,

    /// <summary>User logout</summary>
    Logout,

    /// <summary>Token refresh</summary>
    TokenRefresh,

    /// <summary>Token revocation</summary>
    TokenRevoked,

    /// <summary>Authorization check passed</summary>
    AuthorizationGranted,

    /// <summary>Authorization check denied</summary>
    AuthorizationDenied,

    /// <summary>Document read operation</summary>
    DataRead,

    /// <summary>Document write operation</summary>
    DataWrite,

    /// <summary>Document delete operation</summary>
    DataDelete,

    /// <summary>Collection created</summary>
    CollectionCreated,

    /// <summary>Collection dropped</summary>
    CollectionDropped,

    /// <summary>User created</summary>
    UserCreated,

    /// <summary>User deleted</summary>
    UserDeleted,

    /// <summary>User password changed</summary>
    PasswordChanged,

    /// <summary>Role assigned to user</summary>
    RoleAssigned,

    /// <summary>Role removed from user</summary>
    RoleRemoved,

    /// <summary>Configuration changed</summary>
    ConfigurationChanged,

    /// <summary>Server started</summary>
    ServerStarted,

    /// <summary>Server stopped</summary>
    ServerStopped,

    /// <summary>Security warning</summary>
    SecurityWarning,

    /// <summary>Connection established</summary>
    ConnectionEstablished,

    /// <summary>Connection closed</summary>
    ConnectionClosed
}

/// <summary>
/// Represents an audit event
/// </summary>
public class AuditEvent
{
    /// <summary>
    /// Unique identifier for the audit event
    /// </summary>
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The timestamp when the event occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The type of audit event
    /// </summary>
    public AuditEventType EventType { get; set; }

    /// <summary>
    /// The username associated with the event (if applicable)
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// The client IP address (if applicable)
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// The resource being accessed (collection, document, etc.)
    /// </summary>
    public string? Resource { get; set; }

    /// <summary>
    /// The action performed
    /// </summary>
    public string? Action { get; set; }

    /// <summary>
    /// Whether the action was successful
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Additional details about the event
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// The session or connection ID (if applicable)
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// The correlation ID for tracing related events
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Creates a string representation of the audit event
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>
        {
            $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}]",
            $"[{EventType}]"
        };

        if (!string.IsNullOrEmpty(Username))
            parts.Add($"User={Username}");

        if (!string.IsNullOrEmpty(IpAddress))
            parts.Add($"IP={IpAddress}");

        if (!string.IsNullOrEmpty(Resource))
            parts.Add($"Resource={Resource}");

        if (!string.IsNullOrEmpty(Action))
            parts.Add($"Action={Action}");

        parts.Add($"Success={Success}");

        if (!string.IsNullOrEmpty(Details))
            parts.Add($"Details={Details}");

        return string.Join(" ", parts);
    }
}
