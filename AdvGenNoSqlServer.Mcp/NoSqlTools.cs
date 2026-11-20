// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.ComponentModel;
using System.Text.Json;
using AdvGenNoSqlServer.Client;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AdvGenNoSqlServer.Mcp;

/// <summary>
/// MCP tools exposing AdvGenNoSQL Server data, user, and attachment operations.
/// All tools share one authenticated TCP session via <see cref="NoSqlClientService"/>.
/// </summary>
[McpServerToolType]
public sealed class NoSqlTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly NoSqlClientService _service;

    public NoSqlTools(NoSqlClientService service)
    {
        _service = service;
    }

    // ---------- collections ----------

    [McpServerTool(Name = "list_collections"), Description("List all collections in the current database.")]
    public async Task<string> ListCollectionsAsync(CancellationToken ct)
        => await CommandAsync("listcollections", "", null, ct);

    [McpServerTool(Name = "create_collection"), Description("Create a new collection. Fails if it already exists.")]
    public async Task<string> CreateCollectionAsync(
        [Description("Collection name")] string name, CancellationToken ct)
        => await CommandAsync("createcollection", name, null, ct);

    [McpServerTool(Name = "drop_collection"), Description("Permanently delete a collection and all its documents. Requires the admin role.")]
    public async Task<string> DropCollectionAsync(
        [Description("Collection name")] string name, CancellationToken ct)
        => await CommandAsync("dropcollection", name, null, ct);

    // ---------- documents ----------

    [McpServerTool(Name = "get_document"), Description("Get a document by ID. Returns { found, document } where document is the flat {_id, ...fields} shape, or null when missing.")]
    public async Task<string> GetDocumentAsync(
        [Description("Collection name")] string collection,
        [Description("Document ID")] string id, CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var document = await client.GetAsync(collection, id, ct);
        return JsonSerializer.Serialize(new { found = document != null, document }, JsonOptions);
    }

    [McpServerTool(Name = "set_document"), Description("Insert or update (upsert) a document. documentJson is a JSON object of fields; include _id or pass id to target an existing document. Returns the stored document ID.")]
    public async Task<string> SetDocumentAsync(
        [Description("Collection name")] string collection,
        [Description("Document fields as a JSON object, e.g. {\"name\":\"Milk\",\"price\":3.5}")] string documentJson,
        [Description("Optional document ID; generated when omitted")] string? id = null, CancellationToken ct = default)
    {
        var fields = ParseDocument(documentJson);
        if (!string.IsNullOrEmpty(id))
            fields["_id"] = id;

        var client = await _service.GetClientAsync(ct);
        var storedId = await client.SetAsync(collection, fields, ct);
        return JsonSerializer.Serialize(new { id = storedId }, JsonOptions);
    }

    [McpServerTool(Name = "delete_document"), Description("Delete a document by ID. Returns { deleted: true|false }.")]
    public async Task<string> DeleteDocumentAsync(
        [Description("Collection name")] string collection,
        [Description("Document ID")] string id, CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var deleted = await client.DeleteAsync(collection, id, ct);
        return JsonSerializer.Serialize(new { deleted }, JsonOptions);
    }

    [McpServerTool(Name = "document_exists"), Description("Check whether a document exists. Returns { exists: true|false }.")]
    public async Task<string> DocumentExistsAsync(
        [Description("Collection name")] string collection,
        [Description("Document ID")] string id, CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var exists = await client.ExistsAsync(collection, id, ct);
        return JsonSerializer.Serialize(new { exists }, JsonOptions);
    }

    [McpServerTool(Name = "count_documents"), Description("Count documents in a collection.")]
    public async Task<string> CountDocumentsAsync(
        [Description("Collection name")] string collection, CancellationToken ct)
        => await CommandAsync("count", collection, null, ct);

    [McpServerTool(Name = "list_documents"), Description("Browse documents in a collection ordered by ID, with paging (take is capped at 200 by the server).")]
    public async Task<string> ListDocumentsAsync(
        [Description("Collection name")] string collection,
        [Description("Number of documents to skip")] int skip = 0,
        [Description("Maximum documents to return (1-200)")] int take = 50, CancellationToken ct = default)
        => await CommandAsync("listdocuments", collection, new { skip, take }, ct);

    // ---------- server ----------

    [McpServerTool(Name = "server_stats"), Description("Server statistics: uptime, connections, memory, storage, and connection-pool stats.")]
    public async Task<string> ServerStatsAsync(CancellationToken ct)
        => await CommandAsync("stats", "", null, ct);

    // ---------- users (admin role required) ----------

    [McpServerTool(Name = "list_users"), Description("List user accounts and their roles. Requires the admin role.")]
    public async Task<string> ListUsersAsync(CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var users = await client.ListUsersAsync(ct);
        return JsonSerializer.Serialize(users, JsonOptions);
    }

    [McpServerTool(Name = "create_user"), Description("Create a user account. Role is one of: admin, readwrite, readonly. Requires the admin role.")]
    public async Task<string> CreateUserAsync(
        [Description("Username")] string username,
        [Description("Password")] string password,
        [Description("Role: admin, readwrite, or readonly")] string role, CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var created = await client.CreateUserAsync(username, password, role, ct);
        return JsonSerializer.Serialize(new { created }, JsonOptions);
    }

    [McpServerTool(Name = "delete_user"), Description("Delete a user account. Requires the admin role.")]
    public async Task<string> DeleteUserAsync(
        [Description("Username")] string username, CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var deleted = await client.DeleteUserAsync(username, ct);
        return JsonSerializer.Serialize(new { deleted }, JsonOptions);
    }

    [McpServerTool(Name = "set_user_role"), Description("Change a user's role (admin, readwrite, readonly). Requires the admin role.")]
    public async Task<string> SetUserRoleAsync(
        [Description("Username")] string username,
        [Description("New role")] string role, CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var changed = await client.SetUserRoleAsync(username, role, ct);
        return JsonSerializer.Serialize(new { changed }, JsonOptions);
    }

    [McpServerTool(Name = "set_user_password"), Description("Reset a user's password. Requires the admin role.")]
    public async Task<string> SetUserPasswordAsync(
        [Description("Username")] string username,
        [Description("New password")] string password, CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var changed = await client.SetUserPasswordAsync(username, password, ct);
        return JsonSerializer.Serialize(new { changed }, JsonOptions);
    }

    // ---------- attachments ----------

    [McpServerTool(Name = "list_attachments"), Description("List binary attachments on a document.")]
    public async Task<string> ListAttachmentsAsync(
        [Description("Collection name")] string collection,
        [Description("Document ID")] string id, CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var attachments = await client.Attachments.ListAsync(collection, id, ct);
        return JsonSerializer.Serialize(attachments, JsonOptions);
    }

    [McpServerTool(Name = "attachment_info"), Description("Get metadata for one attachment (size, content type, SHA-256).")]
    public async Task<string> AttachmentInfoAsync(
        [Description("Collection name")] string collection,
        [Description("Document ID")] string id,
        [Description("Attachment name")] string name, CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var info = await client.Attachments.InfoAsync(collection, id, name, ct);
        return JsonSerializer.Serialize(info, JsonOptions);
    }

    [McpServerTool(Name = "upload_attachment"), Description("Attach a binary file to a document. Content is base64-encoded; size is capped by the server's MaxAttachmentSizeMB. Requires write permission.")]
    public async Task<string> UploadAttachmentAsync(
        [Description("Collection name")] string collection,
        [Description("Document ID")] string id,
        [Description("Attachment name")] string name,
        [Description("MIME content type, e.g. application/pdf")] string contentType,
        [Description("File content, base64-encoded")] string base64Content, CancellationToken ct)
    {
        var bytes = Convert.FromBase64String(base64Content);
        var client = await _service.GetClientAsync(ct);
        var metadata = await client.Attachments.UploadAsync(collection, id, name, contentType, bytes, ct);
        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    [McpServerTool(Name = "download_attachment"), Description("Download an attachment. Returns { name, base64Content } or { found: false }.")]
    public async Task<string> DownloadAttachmentAsync(
        [Description("Collection name")] string collection,
        [Description("Document ID")] string id,
        [Description("Attachment name")] string name, CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var bytes = await client.Attachments.DownloadAsync(collection, id, name, ct);
        return bytes == null
            ? JsonSerializer.Serialize(new { found = false }, JsonOptions)
            : JsonSerializer.Serialize(new { found = true, name, base64Content = Convert.ToBase64String(bytes) }, JsonOptions);
    }

    [McpServerTool(Name = "delete_attachment"), Description("Delete an attachment from a document. Requires write permission.")]
    public async Task<string> DeleteAttachmentAsync(
        [Description("Collection name")] string collection,
        [Description("Document ID")] string id,
        [Description("Attachment name")] string name, CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var deleted = await client.Attachments.DeleteAsync(collection, id, name, ct);
        return JsonSerializer.Serialize(new { deleted }, JsonOptions);
    }

    [McpServerTool(Name = "total_storage"), Description("Total attachment storage used across the server, in bytes.")]
    public async Task<string> TotalStorageAsync(CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var bytes = await client.Attachments.TotalStorageBytesAsync(ct);
        return JsonSerializer.Serialize(new { totalStorageBytes = bytes }, JsonOptions);
    }

    // ---------- helpers ----------

    private async Task<string> CommandAsync(string command, string collection, object? document, CancellationToken ct)
    {
        var client = await _service.GetClientAsync(ct);
        var response = await client.ExecuteCommandAsync(command, collection, document, ct);
        if (!response.Success)
            throw new McpException(response.Error?.Message ?? $"Command '{command}' failed");
        return JsonSerializer.Serialize(response.Data, JsonOptions);
    }

    private static Dictionary<string, object> ParseDocument(string documentJson)
    {
        try
        {
            var fields = JsonSerializer.Deserialize<Dictionary<string, object>>(documentJson);
            if (fields == null)
                throw new McpException("documentJson must be a JSON object");
            return fields;
        }
        catch (JsonException ex)
        {
            throw new McpException($"documentJson is not valid JSON: {ex.Message}");
        }
    }
}