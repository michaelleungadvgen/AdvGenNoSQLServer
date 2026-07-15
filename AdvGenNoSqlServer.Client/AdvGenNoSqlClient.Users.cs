// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Network;

namespace AdvGenNoSqlServer.Client
{
    /// <summary>A user account as reported by the server (no password material).</summary>
    public record UserInfo(string Username, string Role, DateTime CreatedAt);

    public partial class AdvGenNoSqlClient
    {
        /// <summary>Lists all user accounts (admin only).</summary>
        public async Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken ct = default)
        {
            var resp = await SendUserCommandAsync(new { command = "listusers" }, ct);
            var list = new List<UserInfo>();
            if (resp.Data is System.Text.Json.JsonElement data && data.TryGetProperty("users", out var arr))
            {
                foreach (var u in arr.EnumerateArray())
                {
                    list.Add(new UserInfo(
                        u.GetProperty("username").GetString() ?? "",
                        u.GetProperty("role").GetString() ?? "",
                        u.TryGetProperty("createdAt", out var c) ? c.GetDateTime() : default));
                }
            }
            return list;
        }

        /// <summary>Creates a user with the given role (admin only).</summary>
        public Task<bool> CreateUserAsync(string username, string password, string role, CancellationToken ct = default)
            => BoolUserCommandAsync(new { command = "createuser", username, password, role }, "created", ct);

        /// <summary>Deletes a user (admin only).</summary>
        public Task<bool> DeleteUserAsync(string username, CancellationToken ct = default)
            => BoolUserCommandAsync(new { command = "deleteuser", username }, "deleted", ct);

        /// <summary>Resets a user's password without the old password (admin only).</summary>
        public Task<bool> SetUserPasswordAsync(string username, string password, CancellationToken ct = default)
            => BoolUserCommandAsync(new { command = "setpassword", username, password }, "changed", ct);

        /// <summary>Changes a user's role (admin only).</summary>
        public Task<bool> SetUserRoleAsync(string username, string role, CancellationToken ct = default)
            => BoolUserCommandAsync(new { command = "setrole", username, role }, "changed", ct);

        /// <summary>Changes the authenticated user's own password.</summary>
        public Task<bool> ChangeMyPasswordAsync(string oldPassword, string newPassword, CancellationToken ct = default)
            => BoolUserCommandAsync(new { command = "changepassword", oldPassword, newPassword }, "changed", ct);

        private async Task<NoSqlResponse> SendUserCommandAsync(object payload, CancellationToken ct)
        {
            EnsureConnected();
            var message = NoSqlMessage.Create(MessageType.Command,
                System.Text.Json.JsonSerializer.Serialize(payload));
            var response = await SendAndReceiveAsync(message, ct);
            var result = ParseResponse(response);
            if (!result.Success)
                throw new NoSqlClientException($"{result.Error?.Code}: {result.Error?.Message}");
            return result;
        }

        private async Task<bool> BoolUserCommandAsync(object payload, string flag, CancellationToken ct)
        {
            var resp = await SendUserCommandAsync(payload, ct);
            return resp.Data is System.Text.Json.JsonElement d && d.TryGetProperty(flag, out var f) && f.GetBoolean();
        }
    }
}
