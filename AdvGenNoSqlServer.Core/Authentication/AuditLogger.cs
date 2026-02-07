// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Configuration;

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// File-based audit logger implementation with in-memory buffering
/// </summary>
public class AuditLogger : IAuditLogger, IDisposable
{
    private readonly ServerConfiguration _configuration;
    private readonly ConcurrentQueue<AuditEvent> _eventBuffer;
    private readonly LinkedList<AuditEvent> _recentEvents;
    private readonly object _recentEventsLock = new();
    private readonly int _maxRecentEvents;
    private readonly string _logDirectory;
    private readonly string _logFilePrefix;
    private readonly int _maxLogFileSizeBytes;
    private readonly SemaphoreSlim _writeLock;
    private readonly Timer? _flushTimer;
    private readonly bool _enableFileLogging;
    private bool _disposed;

    /// <summary>
    /// Creates a new AuditLogger instance
    /// </summary>
    /// <param name="configuration">Server configuration</param>
    /// <param name="logDirectory">Directory for audit log files (defaults to "logs/audit")</param>
    /// <param name="enableFileLogging">Whether to enable file-based logging</param>
    /// <param name="maxRecentEvents">Maximum number of recent events to keep in memory</param>
    /// <param name="flushIntervalSeconds">Interval for automatic flushing (0 to disable)</param>
    public AuditLogger(
        ServerConfiguration configuration,
        string? logDirectory = null,
        bool enableFileLogging = true,
        int maxRecentEvents = 1000,
        int flushIntervalSeconds = 30)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _eventBuffer = new ConcurrentQueue<AuditEvent>();
        _recentEvents = new LinkedList<AuditEvent>();
        _maxRecentEvents = maxRecentEvents;
        _logDirectory = logDirectory ?? Path.Combine(_configuration.StoragePath, "logs", "audit");
        _logFilePrefix = "audit";
        _maxLogFileSizeBytes = 10 * 1024 * 1024; // 10 MB default
        _writeLock = new SemaphoreSlim(1, 1);
        _enableFileLogging = enableFileLogging;

        if (_enableFileLogging)
        {
            EnsureLogDirectoryExists();
        }

        if (flushIntervalSeconds > 0 && _enableFileLogging)
        {
            _flushTimer = new Timer(
                async _ => await FlushAsync(),
                null,
                TimeSpan.FromSeconds(flushIntervalSeconds),
                TimeSpan.FromSeconds(flushIntervalSeconds));
        }
    }

    /// <inheritdoc />
    public void Log(AuditEvent auditEvent)
    {
        if (auditEvent == null)
            throw new ArgumentNullException(nameof(auditEvent));

        AddToRecentEvents(auditEvent);

        if (_enableFileLogging)
        {
            _eventBuffer.Enqueue(auditEvent);
        }
    }

    /// <inheritdoc />
    public async Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        if (auditEvent == null)
            throw new ArgumentNullException(nameof(auditEvent));

        AddToRecentEvents(auditEvent);

        if (_enableFileLogging)
        {
            _eventBuffer.Enqueue(auditEvent);

            // Flush immediately for critical events
            if (IsCriticalEvent(auditEvent.EventType))
            {
                await FlushAsync(cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    public void LogAuthentication(string username, string? ipAddress = null, string? details = null)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.AuthenticationSuccess,
            Username = username,
            IpAddress = ipAddress,
            Action = "LOGIN",
            Success = true,
            Details = details
        });
    }

    /// <inheritdoc />
    public void LogAuthenticationFailure(string username, string? ipAddress = null, string? reason = null)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.AuthenticationFailure,
            Username = username,
            IpAddress = ipAddress,
            Action = "LOGIN_FAILED",
            Success = false,
            Details = reason ?? "Invalid credentials"
        });
    }

    /// <inheritdoc />
    public void LogLogout(string username, string? ipAddress = null)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.Logout,
            Username = username,
            IpAddress = ipAddress,
            Action = "LOGOUT",
            Success = true
        });
    }

    /// <inheritdoc />
    public void LogAuthorization(string username, string permission, bool granted, string? resource = null)
    {
        Log(new AuditEvent
        {
            EventType = granted ? AuditEventType.AuthorizationGranted : AuditEventType.AuthorizationDenied,
            Username = username,
            Action = permission,
            Resource = resource,
            Success = granted,
            Details = granted ? null : $"Permission '{permission}' denied"
        });
    }

    /// <inheritdoc />
    public void LogDataAccess(string username, string operation, string collection, string? documentId = null)
    {
        var eventType = operation.ToUpperInvariant() switch
        {
            "GET" or "READ" or "FIND" => AuditEventType.DataRead,
            "SET" or "WRITE" or "INSERT" or "UPDATE" => AuditEventType.DataWrite,
            "DELETE" or "REMOVE" => AuditEventType.DataDelete,
            _ => AuditEventType.DataRead
        };

        var resource = string.IsNullOrEmpty(documentId)
            ? collection
            : $"{collection}/{documentId}";

        Log(new AuditEvent
        {
            EventType = eventType,
            Username = username,
            Action = operation.ToUpperInvariant(),
            Resource = resource,
            Success = true
        });
    }

    /// <inheritdoc />
    public void LogAdminAction(string username, string action, string? details = null)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.ConfigurationChanged,
            Username = username,
            Action = action,
            Success = true,
            Details = details
        });
    }

    /// <inheritdoc />
    public void LogSecurityWarning(string message, string? username = null, string? ipAddress = null)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.SecurityWarning,
            Username = username,
            IpAddress = ipAddress,
            Action = "SECURITY_WARNING",
            Success = false,
            Details = message
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<AuditEvent> GetRecentEvents(int count = 100)
    {
        lock (_recentEventsLock)
        {
            return _recentEvents.Take(Math.Min(count, _recentEvents.Count)).ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<AuditEvent> GetEventsByUser(string username, int count = 100)
    {
        if (string.IsNullOrEmpty(username))
            return Array.Empty<AuditEvent>();

        lock (_recentEventsLock)
        {
            return _recentEvents
                .Where(e => string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase))
                .Take(count)
                .ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<AuditEvent> GetEventsByType(AuditEventType eventType, int count = 100)
    {
        lock (_recentEventsLock)
        {
            return _recentEvents
                .Where(e => e.EventType == eventType)
                .Take(count)
                .ToList();
        }
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!_enableFileLogging || _eventBuffer.IsEmpty)
            return;

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var events = new List<AuditEvent>();
            while (_eventBuffer.TryDequeue(out var evt))
            {
                events.Add(evt);
            }

            if (events.Count == 0)
                return;

            var logFile = GetCurrentLogFile();
            var lines = events.Select(FormatEventForFile);

            await File.AppendAllLinesAsync(logFile, lines, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Logs a user creation event
    /// </summary>
    public void LogUserCreated(string adminUsername, string newUsername)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.UserCreated,
            Username = adminUsername,
            Action = "CREATE_USER",
            Resource = newUsername,
            Success = true,
            Details = $"User '{newUsername}' created by '{adminUsername}'"
        });
    }

    /// <summary>
    /// Logs a user deletion event
    /// </summary>
    public void LogUserDeleted(string adminUsername, string deletedUsername)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.UserDeleted,
            Username = adminUsername,
            Action = "DELETE_USER",
            Resource = deletedUsername,
            Success = true,
            Details = $"User '{deletedUsername}' deleted by '{adminUsername}'"
        });
    }

    /// <summary>
    /// Logs a password change event
    /// </summary>
    public void LogPasswordChanged(string username)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.PasswordChanged,
            Username = username,
            Action = "CHANGE_PASSWORD",
            Success = true
        });
    }

    /// <summary>
    /// Logs a role assignment event
    /// </summary>
    public void LogRoleAssigned(string adminUsername, string targetUsername, string roleName)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.RoleAssigned,
            Username = adminUsername,
            Action = "ASSIGN_ROLE",
            Resource = targetUsername,
            Success = true,
            Details = $"Role '{roleName}' assigned to '{targetUsername}'"
        });
    }

    /// <summary>
    /// Logs a role removal event
    /// </summary>
    public void LogRoleRemoved(string adminUsername, string targetUsername, string roleName)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.RoleRemoved,
            Username = adminUsername,
            Action = "REMOVE_ROLE",
            Resource = targetUsername,
            Success = true,
            Details = $"Role '{roleName}' removed from '{targetUsername}'"
        });
    }

    /// <summary>
    /// Logs a collection creation event
    /// </summary>
    public void LogCollectionCreated(string username, string collectionName)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.CollectionCreated,
            Username = username,
            Action = "CREATE_COLLECTION",
            Resource = collectionName,
            Success = true
        });
    }

    /// <summary>
    /// Logs a collection drop event
    /// </summary>
    public void LogCollectionDropped(string username, string collectionName)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.CollectionDropped,
            Username = username,
            Action = "DROP_COLLECTION",
            Resource = collectionName,
            Success = true
        });
    }

    /// <summary>
    /// Logs a server start event
    /// </summary>
    public void LogServerStarted()
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.ServerStarted,
            Action = "SERVER_START",
            Success = true,
            Details = $"Server started on {_configuration.Host}:{_configuration.Port}"
        });
    }

    /// <summary>
    /// Logs a server stop event
    /// </summary>
    public void LogServerStopped()
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.ServerStopped,
            Action = "SERVER_STOP",
            Success = true
        });
    }

    /// <summary>
    /// Logs a connection established event
    /// </summary>
    public void LogConnectionEstablished(string connectionId, string? ipAddress = null)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.ConnectionEstablished,
            SessionId = connectionId,
            IpAddress = ipAddress,
            Action = "CONNECT",
            Success = true
        });
    }

    /// <summary>
    /// Logs a connection closed event
    /// </summary>
    public void LogConnectionClosed(string connectionId, string? ipAddress = null, string? reason = null)
    {
        Log(new AuditEvent
        {
            EventType = AuditEventType.ConnectionClosed,
            SessionId = connectionId,
            IpAddress = ipAddress,
            Action = "DISCONNECT",
            Success = true,
            Details = reason
        });
    }

    /// <summary>
    /// Gets the total number of audit events logged
    /// </summary>
    public int TotalEventsLogged
    {
        get
        {
            lock (_recentEventsLock)
            {
                return _recentEvents.Count;
            }
        }
    }

    /// <summary>
    /// Gets the number of events pending flush to disk
    /// </summary>
    public int PendingFlushCount => _eventBuffer.Count;

    private void AddToRecentEvents(AuditEvent auditEvent)
    {
        lock (_recentEventsLock)
        {
            _recentEvents.AddFirst(auditEvent);

            while (_recentEvents.Count > _maxRecentEvents)
            {
                _recentEvents.RemoveLast();
            }
        }
    }

    private void EnsureLogDirectoryExists()
    {
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }
    }

    private string GetCurrentLogFile()
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var baseFileName = $"{_logFilePrefix}-{date}.log";
        var filePath = Path.Combine(_logDirectory, baseFileName);

        // Check if we need to rotate
        if (File.Exists(filePath))
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length >= _maxLogFileSizeBytes)
            {
                // Find next available rotation number
                var rotationNumber = 1;
                string rotatedPath;
                do
                {
                    rotatedPath = Path.Combine(_logDirectory, $"{_logFilePrefix}-{date}.{rotationNumber}.log");
                    rotationNumber++;
                } while (File.Exists(rotatedPath));

                return rotatedPath;
            }
        }

        return filePath;
    }

    private static string FormatEventForFile(AuditEvent evt)
    {
        var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return json;
    }

    private static bool IsCriticalEvent(AuditEventType eventType)
    {
        return eventType switch
        {
            AuditEventType.AuthenticationFailure => true,
            AuditEventType.AuthorizationDenied => true,
            AuditEventType.SecurityWarning => true,
            AuditEventType.UserDeleted => true,
            AuditEventType.CollectionDropped => true,
            _ => false
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _flushTimer?.Dispose();

        // Final flush
        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore errors during dispose
        }

        _writeLock.Dispose();
    }
}
