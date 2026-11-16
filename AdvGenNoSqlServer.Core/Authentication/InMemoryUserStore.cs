// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// In-memory user store used by cache storage mode. Accounts live only for the
/// lifetime of the process and are never written to disk.
/// </summary>
public sealed class InMemoryUserStore : IUserStore
{
    private readonly object _gate = new();
    private List<PersistedUser> _users = new();

    public IReadOnlyList<PersistedUser> Load()
    {
        lock (_gate)
        {
            return _users.ToList();
        }
    }

    public void Save(IEnumerable<PersistedUser> users)
    {
        lock (_gate)
        {
            _users = users.ToList();
        }
    }
}