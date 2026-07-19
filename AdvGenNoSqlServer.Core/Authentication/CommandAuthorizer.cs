// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>Access level a command requires.</summary>
public enum CommandAccess { Read, Write, Admin }

/// <summary>
/// Maps TCP commands to the minimum access they need and decides whether a role
/// may run them. The command name is lowercased before lookup. Unknown commands
/// are <b>fail-closed</b>: they are denied for non-Admin roles, so a command added
/// to dispatch without an explicit mapping never becomes silently available to
/// everyone (Admins still reach the dispatcher and receive its UNKNOWN_COMMAND).
/// changepassword needs only an authenticated identity (mapped as Read here;
/// dispatchers additionally require a non-anonymous identity before dispatch).
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
        ["bulk"] = CommandAccess.Write,
        // Admin
        ["dropcollection"] = CommandAccess.Admin,
        ["listusers"] = CommandAccess.Admin,
        ["createuser"] = CommandAccess.Admin,
        ["deleteuser"] = CommandAccess.Admin,
        ["setpassword"] = CommandAccess.Admin,
        ["setrole"] = CommandAccess.Admin,
        ["cluster"] = CommandAccess.Admin,
        // Attachments — Read
        ["listattachments"] = CommandAccess.Read,
        ["attachmentinfo"] = CommandAccess.Read,
        ["downloadattachment"] = CommandAccess.Read,
        ["totalstorage"] = CommandAccess.Read,
        // Attachments — Write
        ["uploadattachment"] = CommandAccess.Write,
        ["deleteattachment"] = CommandAccess.Write,
    };

    public static bool IsAllowed(string command, string role)
    {
        var key = command.ToLowerInvariant();
        if (!Map.TryGetValue(key, out var access))
            return role == UserRole.Admin; // fail closed — Admin only

        return role switch
        {
            UserRole.Admin => true,
            UserRole.ReadWrite => access != CommandAccess.Admin,
            UserRole.ReadOnly => access == CommandAccess.Read,
            _ => false
        };
    }
}
