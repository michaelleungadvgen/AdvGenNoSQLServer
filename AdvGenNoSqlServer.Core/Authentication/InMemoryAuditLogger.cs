// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// In-memory audit logger used by cache storage mode. Events are kept in a bounded
/// buffer for the lifetime of the process and are never written to disk.
/// </summary>
public sealed class InMemoryAuditLogger : IAuditLogger
{
    private const int MaxEvents = 1000;
    private readonly object _gate = new();
    private readonly List<AuditEvent> _events = new();

    /// <summary>
    /// Adds an audit event to the bounded in-memory buffer, discarding the oldest events beyond the limit
    /// </summary>
    /// <param name="auditEvent">The event to record; null events are ignored</param>
    public void Log(AuditEvent auditEvent)
    {
        if (auditEvent == null) return;
        lock (_gate)
        {
            _events.Add(auditEvent);
            if (_events.Count > MaxEvents)
                _events.RemoveRange(0, _events.Count - MaxEvents);
        }
    }

    /// <summary>
    /// Records an audit event asynchronously; completes synchronously since the buffer is in memory
    /// </summary>
    /// <param name="auditEvent">The event to record</param>
    /// <param name="cancellationToken">Cancellation token (unused)</param>
    /// <returns>A completed task</returns>
    public Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Log(auditEvent);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records a successful authentication (login) event
    /// </summary>
    /// <param name="username">The authenticated username</param>
    /// <param name="ipAddress">Optional client IP address</param>
    /// <param name="details">Optional additional details</param>
    public void LogAuthentication(string username, string? ipAddress = null, string? details = null)
        => Log(new AuditEvent
        {
            EventType = AuditEventType.AuthenticationSuccess,
            Username = username,
            IpAddress = ipAddress,
            Action = "LOGIN",
            Success = true,
            Details = details
        });

    /// <summary>
    /// Records a failed authentication attempt
    /// </summary>
    /// <param name="username">The username that failed to authenticate</param>
    /// <param name="ipAddress">Optional client IP address</param>
    /// <param name="reason">Optional failure reason; defaults to "Invalid credentials"</param>
    public void LogAuthenticationFailure(string username, string? ipAddress = null, string? reason = null)
        => Log(new AuditEvent
        {
            EventType = AuditEventType.AuthenticationFailure,
            Username = username,
            IpAddress = ipAddress,
            Action = "LOGIN_FAILED",
            Success = false,
            Details = reason ?? "Invalid credentials"
        });

    /// <summary>
    /// Records a user logout event
    /// </summary>
    /// <param name="username">The username logging out</param>
    /// <param name="ipAddress">Optional client IP address</param>
    public void LogLogout(string username, string? ipAddress = null)
        => Log(new AuditEvent
        {
            EventType = AuditEventType.Logout,
            Username = username,
            IpAddress = ipAddress,
            Action = "LOGOUT",
            Success = true
        });

    /// <summary>
    /// Records an authorization decision (permission granted or denied)
    /// </summary>
    /// <param name="username">The username being authorized</param>
    /// <param name="permission">The permission that was checked</param>
    /// <param name="granted">Whether the permission was granted</param>
    /// <param name="resource">Optional resource the permission applies to</param>
    public void LogAuthorization(string username, string permission, bool granted, string? resource = null)
        => Log(new AuditEvent
        {
            EventType = granted ? AuditEventType.AuthorizationGranted : AuditEventType.AuthorizationDenied,
            Username = username,
            Action = permission,
            Resource = resource,
            Success = granted,
            Details = granted ? null : $"Permission '{permission}' denied"
        });

    /// <summary>
    /// Records a data access event, mapping the operation name to a read, write or delete event type
    /// </summary>
    /// <param name="username">The username performing the operation</param>
    /// <param name="operation">The operation name (e.g. GET, SET, DELETE)</param>
    /// <param name="collection">The collection being accessed</param>
    /// <param name="documentId">Optional document identifier within the collection</param>
    public void LogDataAccess(string username, string operation, string collection, string? documentId = null)
    {
        var eventType = operation.ToUpperInvariant() switch
        {
            "GET" or "READ" or "FIND" => AuditEventType.DataRead,
            "SET" or "WRITE" or "INSERT" or "UPDATE" => AuditEventType.DataWrite,
            "DELETE" or "REMOVE" => AuditEventType.DataDelete,
            _ => AuditEventType.DataRead
        };

        var resource = string.IsNullOrEmpty(documentId) ? collection : $"{collection}/{documentId}";

        Log(new AuditEvent
        {
            EventType = eventType,
            Username = username,
            Action = operation.ToUpperInvariant(),
            Resource = resource,
            Success = true
        });
    }

    /// <summary>
    /// Records an administrative action as a configuration-changed event
    /// </summary>
    /// <param name="username">The administrator performing the action</param>
    /// <param name="action">The action name</param>
    /// <param name="details">Optional additional details</param>
    public void LogAdminAction(string username, string action, string? details = null)
        => Log(new AuditEvent
        {
            EventType = AuditEventType.ConfigurationChanged,
            Username = username,
            Action = action,
            Success = true,
            Details = details
        });

    /// <summary>
    /// Records a security warning event
    /// </summary>
    /// <param name="message">The warning message</param>
    /// <param name="username">Optional associated username</param>
    /// <param name="ipAddress">Optional client IP address</param>
    public void LogSecurityWarning(string message, string? username = null, string? ipAddress = null)
        => Log(new AuditEvent
        {
            EventType = AuditEventType.SecurityWarning,
            Username = username,
            IpAddress = ipAddress,
            Action = "SECURITY_WARNING",
            Success = false,
            Details = message
        });

    /// <summary>
    /// Gets the most recent audit events, newest first
    /// </summary>
    /// <param name="count">Maximum number of events to return</param>
    /// <returns>The recent events in reverse chronological order</returns>
    public IReadOnlyList<AuditEvent> GetRecentEvents(int count = 100)
    {
        lock (_gate)
        {
            return _events.TakeLast(Math.Max(0, count)).Reverse().ToList();
        }
    }

    /// <summary>
    /// Gets the most recent audit events for a specific user, newest first
    /// </summary>
    /// <param name="username">The username to filter by</param>
    /// <param name="count">Maximum number of events to return</param>
    /// <returns>The matching events in reverse chronological order</returns>
    public IReadOnlyList<AuditEvent> GetEventsByUser(string username, int count = 100)
    {
        lock (_gate)
        {
            return _events.Where(e => e.Username == username).TakeLast(Math.Max(0, count)).Reverse().ToList();
        }
    }

    /// <summary>
    /// Gets the most recent audit events of a specific type, newest first
    /// </summary>
    /// <param name="eventType">The event type to filter by</param>
    /// <param name="count">Maximum number of events to return</param>
    /// <returns>The matching events in reverse chronological order</returns>
    public IReadOnlyList<AuditEvent> GetEventsByType(AuditEventType eventType, int count = 100)
    {
        lock (_gate)
        {
            return _events.Where(e => e.EventType == eventType).TakeLast(Math.Max(0, count)).Reverse().ToList();
        }
    }

    /// <summary>
    /// Flushes buffered events; a no-op because events are never written to disk
    /// </summary>
    /// <param name="cancellationToken">Cancellation token (unused)</param>
    /// <returns>A completed task</returns>
    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}