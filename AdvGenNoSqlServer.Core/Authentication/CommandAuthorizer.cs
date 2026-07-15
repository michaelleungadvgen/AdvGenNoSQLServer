// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>Access level a command requires.</summary>
public enum CommandAccess { Read, Write, Admin }

/// <summary>
/// Maps TCP commands to the minimum access they need and decides whether a role
/// may run them. The command name is lowercased before lookup. Unknown commands
/// pass through (allowed) so the dispatcher can still return its own
/// UNKNOWN_COMMAND rather than a misleading FORBIDDEN. changepassword needs only
/// an authenticated identity (mapped as Read here; dispatchers additionally
/// require a non-anonymous identity before dispatch).
/// </summary>
public static class CommandAuthorizer
{
    private static readonly Dictionary<string, CommandAccess> Map = new(StringComparer.Ordinal)
    {
        // Read
        ["get"] = CommandAccess.Read,
        ["exists"] = CommandAccess.Read,
        ["count"] = CommandAccess.Read,
        ["find_one"] = CommandAccess.Read,
        ["listcollections"] = CommandAccess.Read,
        ["listdocuments"] = CommandAccess.Read,
        ["stats"] = CommandAccess.Read,
        ["changepassword"] = CommandAccess.Read,
        // Write
        ["set"] = CommandAccess.Write,
        ["delete"] = CommandAccess.Write,
        ["insert"] = CommandAccess.Write,
        ["replace"] = CommandAccess.Write,
        ["upsert"] = CommandAccess.Write,
        ["touch"] = CommandAccess.Write,
        ["createcollection"] = CommandAccess.Write,
        ["dropcollection"] = CommandAccess.Write,
        // Admin
        ["listusers"] = CommandAccess.Admin,
        ["createuser"] = CommandAccess.Admin,
        ["deleteuser"] = CommandAccess.Admin,
        ["setpassword"] = CommandAccess.Admin,
        ["setrole"] = CommandAccess.Admin,
        ["cluster"] = CommandAccess.Admin,
    };

    public static bool IsAllowed(string command, string role)
    {
        var key = command.ToLowerInvariant();
        if (!Map.TryGetValue(key, out var access))
            return true; // unknown → let the dispatcher handle it

        return role switch
        {
            UserRole.Admin => true,
            UserRole.ReadWrite => access != CommandAccess.Admin,
            UserRole.ReadOnly => access == CommandAccess.Read,
            _ => false
        };
    }
}
