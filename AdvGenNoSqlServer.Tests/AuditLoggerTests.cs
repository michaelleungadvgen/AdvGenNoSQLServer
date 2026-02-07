// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Configuration;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for the AuditLogger class
/// </summary>
public class AuditLoggerTests : IDisposable
{
    private readonly ServerConfiguration _configuration;
    private readonly string _testLogDirectory;
    private readonly AuditLogger _logger;

    public AuditLoggerTests()
    {
        _testLogDirectory = Path.Combine(Path.GetTempPath(), $"audit_test_{Guid.NewGuid()}");
        _configuration = new ServerConfiguration
        {
            StoragePath = _testLogDirectory,
            Host = "127.0.0.1",
            Port = 9090
        };
        _logger = new AuditLogger(_configuration, enableFileLogging: false);
    }

    public void Dispose()
    {
        _logger.Dispose();
        if (Directory.Exists(_testLogDirectory))
        {
            try
            {
                Directory.Delete(_testLogDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Basic Logging Tests

    [Fact]
    public void Log_ValidEvent_AddsToRecentEvents()
    {
        // Arrange
        var evt = new AuditEvent
        {
            EventType = AuditEventType.AuthenticationSuccess,
            Username = "testuser"
        };

        // Act
        _logger.Log(evt);

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal("testuser", recent[0].Username);
    }

    [Fact]
    public void Log_NullEvent_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _logger.Log(null!));
    }

    [Fact]
    public async Task LogAsync_ValidEvent_AddsToRecentEvents()
    {
        // Arrange
        var evt = new AuditEvent
        {
            EventType = AuditEventType.DataRead,
            Username = "asyncuser"
        };

        // Act
        await _logger.LogAsync(evt);

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal("asyncuser", recent[0].Username);
    }

    [Fact]
    public async Task LogAsync_NullEvent_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _logger.LogAsync(null!));
    }

    #endregion

    #region Authentication Event Tests

    [Fact]
    public void LogAuthentication_ValidCredentials_LogsSuccessEvent()
    {
        // Act
        _logger.LogAuthentication("testuser", "192.168.1.100", "Login from web");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.AuthenticationSuccess, recent[0].EventType);
        Assert.Equal("testuser", recent[0].Username);
        Assert.Equal("192.168.1.100", recent[0].IpAddress);
        Assert.Equal("LOGIN", recent[0].Action);
        Assert.True(recent[0].Success);
    }

    [Fact]
    public void LogAuthenticationFailure_InvalidCredentials_LogsFailureEvent()
    {
        // Act
        _logger.LogAuthenticationFailure("baduser", "10.0.0.1", "Invalid password");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.AuthenticationFailure, recent[0].EventType);
        Assert.Equal("baduser", recent[0].Username);
        Assert.Equal("LOGIN_FAILED", recent[0].Action);
        Assert.False(recent[0].Success);
        Assert.Contains("Invalid password", recent[0].Details);
    }

    [Fact]
    public void LogLogout_ValidUser_LogsLogoutEvent()
    {
        // Act
        _logger.LogLogout("testuser", "192.168.1.100");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.Logout, recent[0].EventType);
        Assert.Equal("testuser", recent[0].Username);
        Assert.Equal("LOGOUT", recent[0].Action);
        Assert.True(recent[0].Success);
    }

    #endregion

    #region Authorization Event Tests

    [Fact]
    public void LogAuthorization_PermissionGranted_LogsGrantedEvent()
    {
        // Act
        _logger.LogAuthorization("admin", "document.write", true, "users/123");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.AuthorizationGranted, recent[0].EventType);
        Assert.Equal("admin", recent[0].Username);
        Assert.Equal("document.write", recent[0].Action);
        Assert.Equal("users/123", recent[0].Resource);
        Assert.True(recent[0].Success);
    }

    [Fact]
    public void LogAuthorization_PermissionDenied_LogsDeniedEvent()
    {
        // Act
        _logger.LogAuthorization("guest", "admin.settings", false, "config");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.AuthorizationDenied, recent[0].EventType);
        Assert.Equal("guest", recent[0].Username);
        Assert.False(recent[0].Success);
        Assert.Contains("denied", recent[0].Details);
    }

    #endregion

    #region Data Access Event Tests

    [Theory]
    [InlineData("GET", AuditEventType.DataRead)]
    [InlineData("READ", AuditEventType.DataRead)]
    [InlineData("FIND", AuditEventType.DataRead)]
    [InlineData("SET", AuditEventType.DataWrite)]
    [InlineData("WRITE", AuditEventType.DataWrite)]
    [InlineData("INSERT", AuditEventType.DataWrite)]
    [InlineData("UPDATE", AuditEventType.DataWrite)]
    [InlineData("DELETE", AuditEventType.DataDelete)]
    [InlineData("REMOVE", AuditEventType.DataDelete)]
    public void LogDataAccess_DifferentOperations_MapsToCorrectEventType(string operation, AuditEventType expectedType)
    {
        // Act
        _logger.LogDataAccess("user1", operation, "products", "prod123");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(expectedType, recent[0].EventType);
        Assert.Equal("user1", recent[0].Username);
        Assert.Equal("products/prod123", recent[0].Resource);
    }

    [Fact]
    public void LogDataAccess_WithoutDocumentId_SetsCollectionAsResource()
    {
        // Act
        _logger.LogDataAccess("user1", "GET", "orders");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Equal("orders", recent[0].Resource);
    }

    #endregion

    #region Admin Action Tests

    [Fact]
    public void LogAdminAction_ConfigChange_LogsEvent()
    {
        // Act
        _logger.LogAdminAction("admin", "UPDATE_CONFIG", "Changed max connections to 5000");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.ConfigurationChanged, recent[0].EventType);
        Assert.Equal("admin", recent[0].Username);
        Assert.Contains("5000", recent[0].Details);
    }

    [Fact]
    public void LogSecurityWarning_SuspiciousActivity_LogsWarning()
    {
        // Act
        _logger.LogSecurityWarning("Multiple failed login attempts detected", "attacker", "10.0.0.5");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.SecurityWarning, recent[0].EventType);
        Assert.Equal("attacker", recent[0].Username);
        Assert.Equal("10.0.0.5", recent[0].IpAddress);
        Assert.False(recent[0].Success);
    }

    #endregion

    #region User Management Event Tests

    [Fact]
    public void LogUserCreated_NewUser_LogsEvent()
    {
        // Act
        _logger.LogUserCreated("admin", "newuser");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.UserCreated, recent[0].EventType);
        Assert.Equal("admin", recent[0].Username);
        Assert.Equal("newuser", recent[0].Resource);
    }

    [Fact]
    public void LogUserDeleted_RemovedUser_LogsEvent()
    {
        // Act
        _logger.LogUserDeleted("admin", "olduser");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.UserDeleted, recent[0].EventType);
        Assert.Equal("olduser", recent[0].Resource);
    }

    [Fact]
    public void LogPasswordChanged_UserChangesPassword_LogsEvent()
    {
        // Act
        _logger.LogPasswordChanged("user1");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.PasswordChanged, recent[0].EventType);
        Assert.Equal("user1", recent[0].Username);
    }

    [Fact]
    public void LogRoleAssigned_AdminAssignsRole_LogsEvent()
    {
        // Act
        _logger.LogRoleAssigned("admin", "user1", "PowerUser");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.RoleAssigned, recent[0].EventType);
        Assert.Contains("PowerUser", recent[0].Details);
    }

    [Fact]
    public void LogRoleRemoved_AdminRemovesRole_LogsEvent()
    {
        // Act
        _logger.LogRoleRemoved("admin", "user1", "Admin");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.RoleRemoved, recent[0].EventType);
        Assert.Contains("Admin", recent[0].Details);
    }

    #endregion

    #region Collection Event Tests

    [Fact]
    public void LogCollectionCreated_NewCollection_LogsEvent()
    {
        // Act
        _logger.LogCollectionCreated("user1", "products");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.CollectionCreated, recent[0].EventType);
        Assert.Equal("products", recent[0].Resource);
    }

    [Fact]
    public void LogCollectionDropped_DroppedCollection_LogsEvent()
    {
        // Act
        _logger.LogCollectionDropped("admin", "old_data");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.CollectionDropped, recent[0].EventType);
        Assert.Equal("old_data", recent[0].Resource);
    }

    #endregion

    #region Server Event Tests

    [Fact]
    public void LogServerStarted_ServerStart_LogsEvent()
    {
        // Act
        _logger.LogServerStarted();

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.ServerStarted, recent[0].EventType);
        Assert.Contains("127.0.0.1:9090", recent[0].Details);
    }

    [Fact]
    public void LogServerStopped_ServerStop_LogsEvent()
    {
        // Act
        _logger.LogServerStopped();

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.ServerStopped, recent[0].EventType);
    }

    [Fact]
    public void LogConnectionEstablished_NewConnection_LogsEvent()
    {
        // Act
        _logger.LogConnectionEstablished("conn-123", "192.168.1.50");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.ConnectionEstablished, recent[0].EventType);
        Assert.Equal("conn-123", recent[0].SessionId);
        Assert.Equal("192.168.1.50", recent[0].IpAddress);
    }

    [Fact]
    public void LogConnectionClosed_ClosedConnection_LogsEvent()
    {
        // Act
        _logger.LogConnectionClosed("conn-123", "192.168.1.50", "Client disconnected");

        // Assert
        var recent = _logger.GetRecentEvents(1);
        Assert.Single(recent);
        Assert.Equal(AuditEventType.ConnectionClosed, recent[0].EventType);
        Assert.Contains("disconnected", recent[0].Details);
    }

    #endregion

    #region Query Tests

    [Fact]
    public void GetRecentEvents_MultipleEvents_ReturnsInReverseChronologicalOrder()
    {
        // Arrange
        _logger.LogAuthentication("user1");
        _logger.LogAuthentication("user2");
        _logger.LogAuthentication("user3");

        // Act
        var events = _logger.GetRecentEvents(3);

        // Assert
        Assert.Equal(3, events.Count);
        Assert.Equal("user3", events[0].Username);
        Assert.Equal("user2", events[1].Username);
        Assert.Equal("user1", events[2].Username);
    }

    [Fact]
    public void GetRecentEvents_RequestMoreThanAvailable_ReturnsAllAvailable()
    {
        // Arrange
        _logger.LogAuthentication("user1");
        _logger.LogAuthentication("user2");

        // Act
        var events = _logger.GetRecentEvents(100);

        // Assert
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void GetEventsByUser_FilterByUsername_ReturnsOnlyMatchingEvents()
    {
        // Arrange
        _logger.LogAuthentication("alice");
        _logger.LogAuthentication("bob");
        _logger.LogAuthentication("alice");
        _logger.LogLogout("bob");

        // Act
        var aliceEvents = _logger.GetEventsByUser("alice");
        var bobEvents = _logger.GetEventsByUser("bob");

        // Assert
        Assert.Equal(2, aliceEvents.Count);
        Assert.All(aliceEvents, e => Assert.Equal("alice", e.Username));
        Assert.Equal(2, bobEvents.Count);
        Assert.All(bobEvents, e => Assert.Equal("bob", e.Username));
    }

    [Fact]
    public void GetEventsByUser_EmptyUsername_ReturnsEmptyList()
    {
        // Arrange
        _logger.LogAuthentication("user1");

        // Act
        var events = _logger.GetEventsByUser("");

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public void GetEventsByType_FilterByEventType_ReturnsOnlyMatchingEvents()
    {
        // Arrange
        _logger.LogAuthentication("user1");
        _logger.LogAuthenticationFailure("attacker");
        _logger.LogAuthentication("user2");

        // Act
        var successEvents = _logger.GetEventsByType(AuditEventType.AuthenticationSuccess);
        var failureEvents = _logger.GetEventsByType(AuditEventType.AuthenticationFailure);

        // Assert
        Assert.Equal(2, successEvents.Count);
        Assert.Single(failureEvents);
    }

    #endregion

    #region Event Limiting Tests

    [Fact]
    public void Log_ExceedsMaxEvents_EvictsOldestEvents()
    {
        // Arrange - Create logger with small max
        using var logger = new AuditLogger(_configuration, enableFileLogging: false, maxRecentEvents: 3);

        // Act
        logger.LogAuthentication("user1");
        logger.LogAuthentication("user2");
        logger.LogAuthentication("user3");
        logger.LogAuthentication("user4");
        logger.LogAuthentication("user5");

        // Assert
        var events = logger.GetRecentEvents(10);
        Assert.Equal(3, events.Count);
        Assert.Equal("user5", events[0].Username);
        Assert.Equal("user4", events[1].Username);
        Assert.Equal("user3", events[2].Username);
    }

    #endregion

    #region AuditEvent Model Tests

    [Fact]
    public void AuditEvent_DefaultValues_AreCorrect()
    {
        // Act
        var evt = new AuditEvent();

        // Assert
        Assert.NotNull(evt.EventId);
        Assert.NotEqual(Guid.Empty.ToString(), evt.EventId);
        Assert.True(evt.Timestamp <= DateTime.UtcNow);
        Assert.True(evt.Timestamp > DateTime.UtcNow.AddSeconds(-1));
        Assert.True(evt.Success);
    }

    [Fact]
    public void AuditEvent_ToString_ContainsAllRelevantInfo()
    {
        // Arrange
        var evt = new AuditEvent
        {
            EventType = AuditEventType.AuthenticationSuccess,
            Username = "testuser",
            IpAddress = "192.168.1.1",
            Resource = "orders",
            Action = "LOGIN",
            Success = true,
            Details = "Web login"
        };

        // Act
        var str = evt.ToString();

        // Assert
        Assert.Contains("AuthenticationSuccess", str);
        Assert.Contains("testuser", str);
        Assert.Contains("192.168.1.1", str);
        Assert.Contains("orders", str);
        Assert.Contains("LOGIN", str);
        Assert.Contains("Success=True", str);
        Assert.Contains("Web login", str);
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public void TotalEventsLogged_AfterLogging_ReturnsCorrectCount()
    {
        // Act
        _logger.LogAuthentication("user1");
        _logger.LogAuthentication("user2");
        _logger.LogLogout("user1");

        // Assert
        Assert.Equal(3, _logger.TotalEventsLogged);
    }

    [Fact]
    public void PendingFlushCount_NoFileLogging_ReturnsZero()
    {
        // Act
        _logger.LogAuthentication("user1");

        // Assert (file logging is disabled, so no pending flush)
        Assert.Equal(0, _logger.PendingFlushCount);
    }

    #endregion

    #region File Logging Tests

    [Fact]
    public async Task FlushAsync_WithFileLogging_CreatesLogFile()
    {
        // Arrange
        var logDir = Path.Combine(_testLogDirectory, "file_test");
        using var fileLogger = new AuditLogger(
            _configuration,
            logDirectory: logDir,
            enableFileLogging: true,
            flushIntervalSeconds: 0);

        fileLogger.LogAuthentication("user1");
        fileLogger.LogAuthentication("user2");

        // Act
        await fileLogger.FlushAsync();

        // Assert
        var logFiles = Directory.GetFiles(logDir, "audit-*.log");
        Assert.NotEmpty(logFiles);

        var content = await File.ReadAllTextAsync(logFiles[0]);
        Assert.Contains("user1", content);
        Assert.Contains("user2", content);
    }

    [Fact]
    public async Task FlushAsync_EmptyBuffer_DoesNotThrow()
    {
        // Arrange
        var logDir = Path.Combine(_testLogDirectory, "empty_test");
        using var fileLogger = new AuditLogger(
            _configuration,
            logDirectory: logDir,
            enableFileLogging: true,
            flushIntervalSeconds: 0);

        // Act & Assert (should not throw)
        await fileLogger.FlushAsync();
    }

    #endregion
}
