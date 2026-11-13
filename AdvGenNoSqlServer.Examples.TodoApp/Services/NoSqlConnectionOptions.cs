// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Examples.TodoApp.Services;

/// <summary>Connection settings for the AdvGenNoSQL server (bound from the "NoSql" section).</summary>
public class NoSqlConnectionOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 9191;
    public bool UseSsl { get; set; }
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = string.Empty;
    public string Collection { get; set; } = "todos";
}