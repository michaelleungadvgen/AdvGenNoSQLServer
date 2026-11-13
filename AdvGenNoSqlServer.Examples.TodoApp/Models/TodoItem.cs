// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Examples.TodoApp.Models;

/// <summary>
/// Todo entity stored in the AdvGenNoSQL server's "todos" collection.
/// Mirrors the WPF example's TodoItem (Title / IsCompleted / CreatedAt).
/// </summary>
public class TodoItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}