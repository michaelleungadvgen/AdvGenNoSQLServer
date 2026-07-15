// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Network;

namespace AdvGenNoSqlServer.Client
{
    /// <summary>Metadata for a document attachment (no binary content).</summary>
    public record AttachmentMetadata(string Name, string ContentType, long Size, string Hash, DateTime CreatedAt, DateTime UpdatedAt);

    public partial class AdvGenNoSqlClient
    {
        private AttachmentOperations? _attachments;

        /// <summary>Document attachment operations.</summary>
        public AttachmentOperations Attachments => _attachments ??= new AttachmentOperations(this);

        /// <summary>
        /// Attachment API. Bytes are base64-encoded on the wire; callers pass/receive byte[].
        /// Blocked content types and oversize uploads throw NoSqlClientException; a missing
        /// attachment maps to null/false rather than an exception.
        /// </summary>
        public sealed class AttachmentOperations
        {
            private readonly AdvGenNoSqlClient _client;
            internal AttachmentOperations(AdvGenNoSqlClient client) => _client = client;

            private async Task<NoSqlResponse> SendAsync(object payload, CancellationToken ct)
            {
                _client.EnsureConnected();
                var msg = NoSqlMessage.Create(MessageType.Command, System.Text.Json.JsonSerializer.Serialize(payload));
                var response = await _client.SendAndReceiveAsync(msg, ct);
                var result = _client.ParseResponse(response);
                if (!result.Success)
                    throw new NoSqlClientException($"{result.Error?.Code}: {result.Error?.Message}");
                return result;
            }

            private static AttachmentMetadata ReadMeta(System.Text.Json.JsonElement e)
                => new(
                    e.GetProperty("name").GetString() ?? "",
                    e.GetProperty("contentType").GetString() ?? "",
                    e.GetProperty("size").GetInt64(),
                    e.TryGetProperty("hash", out var h) ? h.GetString() ?? "" : "",
                    e.TryGetProperty("createdAt", out var c) ? c.GetDateTime() : default,
                    e.TryGetProperty("updatedAt", out var u) ? u.GetDateTime() : default);

            /// <summary>Lists attachments for a document.</summary>
            public async Task<IReadOnlyList<AttachmentMetadata>> ListAsync(string collection, string id, CancellationToken ct = default)
            {
                var resp = await SendAsync(new { command = "listattachments", collection, id }, ct);
                var list = new List<AttachmentMetadata>();
                if (resp.Data is System.Text.Json.JsonElement d && d.TryGetProperty("attachments", out var arr))
                    foreach (var a in arr.EnumerateArray()) list.Add(ReadMeta(a));
                return list;
            }

            /// <summary>Gets an attachment's metadata, or null if not found.</summary>
            public async Task<AttachmentMetadata?> InfoAsync(string collection, string id, string name, CancellationToken ct = default)
            {
                var resp = await SendAsync(new { command = "attachmentinfo", collection, id, name }, ct);
                if (resp.Data is System.Text.Json.JsonElement d && d.TryGetProperty("found", out var f) && f.GetBoolean())
                    return ReadMeta(d.GetProperty("info"));
                return null;
            }

            /// <summary>Uploads an attachment (throws on blocked type or oversize).</summary>
            public async Task<AttachmentMetadata> UploadAsync(string collection, string id, string name, string contentType, byte[] content, CancellationToken ct = default)
            {
                var resp = await SendAsync(new
                {
                    command = "uploadattachment", collection, id, name,
                    contentType, contentBase64 = Convert.ToBase64String(content)
                }, ct);
                var d = (System.Text.Json.JsonElement)resp.Data!;
                return new AttachmentMetadata(
                    d.GetProperty("name").GetString() ?? name, contentType,
                    d.GetProperty("size").GetInt64(),
                    d.TryGetProperty("hash", out var h) ? h.GetString() ?? "" : "",
                    default, default);
            }

            /// <summary>Downloads an attachment's bytes, or null if not found.</summary>
            public async Task<byte[]?> DownloadAsync(string collection, string id, string name, CancellationToken ct = default)
            {
                var resp = await SendAsync(new { command = "downloadattachment", collection, id, name }, ct);
                if (resp.Data is System.Text.Json.JsonElement d && d.TryGetProperty("found", out var f) && f.GetBoolean())
                    return Convert.FromBase64String(d.GetProperty("contentBase64").GetString() ?? "");
                return null;
            }

            /// <summary>Deletes an attachment. Returns true if it existed.</summary>
            public async Task<bool> DeleteAsync(string collection, string id, string name, CancellationToken ct = default)
            {
                var resp = await SendAsync(new { command = "deleteattachment", collection, id, name }, ct);
                return resp.Data is System.Text.Json.JsonElement d && d.TryGetProperty("deleted", out var del) && del.GetBoolean();
            }

            /// <summary>Total bytes used by all attachments on the server.</summary>
            public async Task<long> TotalStorageBytesAsync(CancellationToken ct = default)
            {
                var resp = await SendAsync(new { command = "totalstorage" }, ct);
                return resp.Data is System.Text.Json.JsonElement d && d.TryGetProperty("bytes", out var b) ? b.GetInt64() : 0;
            }
        }
    }
}
