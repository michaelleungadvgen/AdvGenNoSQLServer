// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Admin.Services;

using System;
using System.Collections.Generic;

/// <summary>
/// Service for managing notifications in the admin app.
/// </summary>
public class NotificationService
{
    private readonly List<Notification> _notifications = new();

    /// <summary>
    /// Event raised when notifications change.
    /// </summary>
    public event EventHandler? NotificationsChanged;

    /// <summary>
    /// Gets all notifications.
    /// </summary>
    public IReadOnlyList<Notification> Notifications => _notifications.AsReadOnly();

    /// <summary>
    /// Adds a new notification.
    /// </summary>
    public void AddNotification(string message, NotificationType type = NotificationType.Info)
    {
        _notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            Message = message,
            Type = type,
            Timestamp = DateTime.Now
        });
        
        // Keep only last 100 notifications
        if (_notifications.Count > 100)
        {
            _notifications.RemoveAt(0);
        }
        
        NotificationsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes a notification.
    /// </summary>
    public void RemoveNotification(Guid id)
    {
        _notifications.RemoveAll(n => n.Id == id);
        NotificationsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears all notifications.
    /// </summary>
    public void ClearAll()
    {
        _notifications.Clear();
        NotificationsChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Notification model.
/// </summary>
public class Notification
{
    /// <summary>
    /// Gets or sets the unique identifier of the notification.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the notification message text.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the severity type of the notification.
    /// </summary>
    public NotificationType Type { get; set; }

    /// <summary>
    /// Gets or sets the local time when the notification was created.
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Notification type enum.
/// </summary>
public enum NotificationType
{
    /// <summary>
    /// Informational notification.
    /// </summary>
    Info,

    /// <summary>
    /// Success notification for a completed operation.
    /// </summary>
    Success,

    /// <summary>
    /// Warning notification about a potential problem.
    /// </summary>
    Warning,

    /// <summary>
    /// Error notification for a failed operation.
    /// </summary>
    Error
}
