// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>A user record as persisted to disk (hash + salt, never plaintext).</summary>
public sealed class PersistedUser
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public string Role { get; set; } = UserRole.ReadWrite;
    public DateTime CreatedAt { get; set; }
}

/// <summary>Persistence contract for user accounts.</summary>
public interface IUserStore
{
    IReadOnlyList<PersistedUser> Load();
    void Save(IEnumerable<PersistedUser> users);
}
