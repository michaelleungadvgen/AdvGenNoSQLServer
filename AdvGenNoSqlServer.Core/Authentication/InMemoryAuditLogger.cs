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

    public Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Log(auditEvent);
        return Task.CompletedTask;
    }

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

    public void LogLogout(string username, string? ipAddress = null)
        => Log(new AuditEvent
        {
            EventType = AuditEventType.Logout,
            Username = username,
            IpAddress = ipAddress,
            Action = "LOGOUT",
            Success = true
        });

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

    public void LogAdminAction(string username, string action, string? details = null)
        => Log(new AuditEvent
        {
            EventType = AuditEventType.ConfigurationChanged,
            Username = username,
            Action = action,
            Success = true,
            Details = details
        });

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

    public IReadOnlyList<AuditEvent> GetRecentEvents(int count = 100)
    {
        lock (_gate)
        {
            return _events.TakeLast(Math.Max(0, count)).Reverse().ToList();
        }
    }

    public IReadOnlyList<AuditEvent> GetEventsByUser(string username, int count = 100)
    {
        lock (_gate)
        {
            return _events.Where(e => e.Username == username).TakeLast(Math.Max(0, count)).Reverse().ToList();
        }
    }

    public IReadOnlyList<AuditEvent> GetEventsByType(AuditEventType eventType, int count = 100)
    {
        lock (_gate)
        {
            return _events.Where(e => e.EventType == eventType).TakeLast(Math.Max(0, count)).Reverse().ToList();
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}