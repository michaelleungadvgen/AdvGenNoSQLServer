// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>Built-in user roles enforced on TCP commands.</summary>
public static class UserRole
{
    /// <summary>Administrator role with unrestricted command access.</summary>
    public const string Admin = "admin";

    /// <summary>Role allowing read and write commands but not administration.</summary>
    public const string ReadWrite = "readwrite";

    /// <summary>Role restricted to read-only commands.</summary>
    public const string ReadOnly = "readonly";

    /// <summary>
    /// Checks whether the given value is one of the built-in role names
    /// </summary>
    /// <param name="role">The role name to validate</param>
    /// <returns>True if the role is a built-in role; otherwise false</returns>
    public static bool IsValid(string? role) =>
        role is Admin or ReadWrite or ReadOnly;
}
