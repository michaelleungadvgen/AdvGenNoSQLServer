// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>Built-in user roles enforced on TCP commands.</summary>
public static class UserRole
{
    public const string Admin = "admin";
    public const string ReadWrite = "readwrite";
    public const string ReadOnly = "readonly";

    public static bool IsValid(string? role) =>
        role is Admin or ReadWrite or ReadOnly;
}
