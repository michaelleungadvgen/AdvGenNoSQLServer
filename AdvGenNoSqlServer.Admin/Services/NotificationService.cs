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
    public Guid Id { get; set; }
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Notification type enum.
/// </summary>
public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}
