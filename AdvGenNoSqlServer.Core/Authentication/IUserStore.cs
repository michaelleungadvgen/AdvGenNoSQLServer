// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>A user record as persisted to disk (hash + salt, never plaintext).</summary>
public sealed class PersistedUser
{
    /// <summary>Gets or sets the username.</summary>
    public string Username { get; set; } = "";

    /// <summary>Gets or sets the base64-encoded PBKDF2 password hash.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>Gets or sets the base64-encoded salt used to derive the password hash.</summary>
    public string Salt { get; set; } = "";

    /// <summary>Gets or sets the user's role name; defaults to <see cref="UserRole.ReadWrite"/>.</summary>
    public string Role { get; set; } = UserRole.ReadWrite;

    /// <summary>Gets or sets the UTC time the user was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>PBKDF2 iteration count used for this hash; 0/missing = legacy 100k.</summary>
    public int Iterations { get; set; }
}

/// <summary>Persistence contract for user accounts.</summary>
public interface IUserStore
{
    /// <summary>
    /// Loads all persisted user records
    /// </summary>
    /// <returns>The persisted users, or an empty list when none exist</returns>
    IReadOnlyList<PersistedUser> Load();

    /// <summary>
    /// Persists the full set of user records, replacing any previously stored users
    /// </summary>
    /// <param name="users">The user records to persist</param>
    void Save(IEnumerable<PersistedUser> users);
}
