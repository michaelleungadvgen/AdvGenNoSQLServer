// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Attachments;
using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Clustering;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Network;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Attachments;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using IConfigurationManager = AdvGenNoSqlServer.Core.Configuration.IConfigurationManager;

namespace AdvGenNoSqlServer.Server;

/// <summary>
/// Main NoSQL server implementation that integrates the TCP server with message handling
/// </summary>
public class NoSqlServer : IHostedService, IAsyncDisposable
{
    private readonly ILogger<NoSqlServer> _logger;
    private readonly IConfigurationManager _configurationManager;
    private readonly IClusterManager? _clusterManager;
    private readonly ApiDataService _apiData;
    private HybridDocumentStore? _documentStore;
    private TcpServer? _tcpServer;
    private AuthenticationManager? _authManager;
    private AttachmentStore? _attachmentStore;
    private readonly ConcurrentDictionary<string, (string Username, string Role)> _authConnections = new();
    private bool _disposed;
    private readonly DateTime _startTime = DateTime.UtcNow;

    /// <summary>
    /// Server version for handshake responses
    /// </summary>
    public const string ServerVersion = "1.0.0";

    public NoSqlServer(ILogger<NoSqlServer> logger, IConfigurationManager configurationManager, ApiDataService apiData, IClusterManager? clusterManager = null)
    {
        _logger = logger;
        _configurationManager = configurationManager;
        _apiData = apiData;
        _clusterManager = clusterManager;
    }

    // Backward-compatible constructor for tests
    public NoSqlServer(ILogger<NoSqlServer> logger, IConfigurationManager configurationManager, IClusterManager clusterManager)
    {
        _logger = logger;
        _configurationManager = configurationManager;
        _apiData = new ApiDataService();
        _clusterManager = clusterManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = _configurationManager.Configuration;

        _logger.LogInformation("Starting NoSQL Server on {Host}:{Port}...", config.Host, config.Port);
        _logger.LogInformation("Max connections: {MaxConnections}", config.MaxConcurrentConnections);
        _logger.LogInformation("Storage path: {StoragePath}", config.StoragePath);

        // Initialize hybrid document store (cache + disk)
        var storagePath = config.StoragePath;
        if (string.IsNullOrEmpty(storagePath))
        {
            storagePath = "data";
        }

        // Ensure storage path is absolute
        if (!Path.IsPathRooted(storagePath))
        {
            storagePath = Path.Combine(AppContext.BaseDirectory, storagePath);
        }

        _logger.LogInformation("Initializing hybrid storage at: {Path}", storagePath);
        _documentStore = new HybridDocumentStore(storagePath);
        await _documentStore.InitializeAsync();
        _logger.LogInformation("Hybrid storage initialized successfully");

        // Initialize authentication (persistent user store)
        var userPath = string.IsNullOrEmpty(config.UserStorePath)
            ? Path.Combine(storagePath, "users.json")
            : config.UserStorePath;
        _authManager = new AuthenticationManager(config, new FileUserStore(userPath));

        // Initialize attachment storage
        _attachmentStore = new AttachmentStore(new AttachmentStoreOptions
        {
            BasePath = Path.Combine(storagePath, "attachments"),
            MaxAttachmentSize = (long)Math.Max(config.MaxAttachmentSizeMB, 1) * 1024 * 1024
        });

        // Create and configure the TCP server
        _tcpServer = new TcpServer(config);
        _tcpServer.ConnectionEstablished += OnConnectionEstablished;
        _tcpServer.ConnectionClosed += OnConnectionClosed;
        _tcpServer.MessageReceived += OnMessageReceivedAsync;

        // Expose live references to the HTTP API
        _apiData.DocumentStore = _documentStore;
        _apiData.TcpServer = _tcpServer;

        // Start the TCP server (awaited: bind failures must surface, not fault a
        // fire-and-forget task while we report a healthy start)
        await _tcpServer.StartAsync(cancellationToken);

        _logger.LogInformation("NoSQL Server started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping NoSQL Server...");

        _apiData.DocumentStore = null;
        _apiData.TcpServer = null;

        if (_tcpServer != null)
        {
            _tcpServer.ConnectionEstablished -= OnConnectionEstablished;
            _tcpServer.ConnectionClosed -= OnConnectionClosed;
            _tcpServer.MessageReceived -= OnMessageReceivedAsync;

            await _tcpServer.StopAsync(TimeSpan.FromSeconds(30));
            _tcpServer.Dispose();
            _tcpServer = null;
        }

        // Flush and dispose document store
        if (_documentStore != null)
        {
            _logger.LogInformation("Flushing pending writes to disk...");
            await _documentStore.FlushAsync();
            await _documentStore.DisposeAsync();
            _documentStore = null;
            _logger.LogInformation("Storage shutdown complete");
        }

        _attachmentStore?.Dispose();
        _attachmentStore = null;

        _logger.LogInformation("NoSQL Server stopped successfully");
    }

    private void OnConnectionEstablished(object? sender, ConnectionEventArgs e)
    {
        _logger.LogDebug("Connection established: {ConnectionId} from {RemoteAddress}",
            e.ConnectionId, e.Client.Client?.RemoteEndPoint?.ToString() ?? "unknown");
    }

    private void OnConnectionClosed(object? sender, ConnectionEventArgs e)
    {
        _logger.LogDebug("Connection closed: {ConnectionId}", e.ConnectionId);
        _authConnections.TryRemove(e.ConnectionId, out _);
    }

    private async void OnMessageReceivedAsync(object? sender, MessageReceivedEventArgs e)
    {
        try
        {
            _logger.LogDebug("Received message type {MessageType} from {ConnectionId}",
                e.Message.MessageType, e.ConnectionId);

            var response = await HandleMessageAsync(e.Message, e.ConnectionId);
            await e.SendResponseAsync(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message from {ConnectionId}", e.ConnectionId);

            // Send error response — the send itself can fail on a dead socket, and this
            // is an async-void method, so any escape here would terminate the process.
            try
            {
                var errorResponse = NoSqlMessage.CreateError("INTERNAL_ERROR", "An error occurred processing the message");
                await e.SendResponseAsync(errorResponse);
            }
            catch (Exception sendEx)
            {
                _logger.LogDebug(sendEx, "Failed to send error response to {ConnectionId}", e.ConnectionId);
            }
        }
    }

    private Task<NoSqlMessage> HandleMessageAsync(NoSqlMessage message, string connectionId)
    {
        return message.MessageType switch
        {
            MessageType.Handshake => HandleHandshakeAsync(message, connectionId),
            MessageType.Ping => HandlePingAsync(message, connectionId),
            MessageType.Authentication => HandleAuthenticationAsync(message, connectionId),
            MessageType.Command => HandleCommandAsync(message, connectionId),
            MessageType.BulkOperation => HandleBulkOperationAsync(message, connectionId),
            _ => Task.FromResult(NoSqlMessage.CreateError("UNSUPPORTED_MESSAGE", $"Message type {message.MessageType} is not supported"))
        };
    }

    private Task<NoSqlMessage> HandleHandshakeAsync(NoSqlMessage message, string connectionId)
    {
        _logger.LogDebug("Processing handshake for connection {ConnectionId}", connectionId);

        // Parse client handshake info if provided
        string? clientVersion = null;
        if (message.Payload != null && message.PayloadLength > 0)
        {
            try
            {
                var payload = message.GetPayloadAsString();
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("version", out var versionProp))
                {
                    clientVersion = versionProp.GetString();
                }
            }
            catch { /* Ignore parsing errors */ }
        }

        // Create handshake response - use Response type for success
        var responsePayload = new
        {
            success = true,
            serverVersion = ServerVersion,
            protocolVersion = 1,
            timestamp = DateTime.UtcNow,
            clientVersion = clientVersion ?? "unknown"
        };

        var response = NoSqlMessage.Create(MessageType.Response, JsonSerializer.Serialize(responsePayload));
        return Task.FromResult(response);
    }

    private Task<NoSqlMessage> HandlePingAsync(NoSqlMessage message, string connectionId)
    {
        _logger.LogDebug("Processing ping for connection {ConnectionId}", connectionId);

        // Return Pong message type - client PingAsync() expects MessageType.Pong
        var response = new NoSqlMessage
        {
            MessageType = MessageType.Pong,
            Payload = Array.Empty<byte>(),
            PayloadLength = 0
        };
        return Task.FromResult(response);
    }

    private Task<NoSqlMessage> HandleAuthenticationAsync(NoSqlMessage message, string connectionId)
    {
        _logger.LogDebug("Processing authentication for connection {ConnectionId}", connectionId);

        if (message.Payload == null || message.PayloadLength == 0)
        {
            return Task.FromResult(NoSqlMessage.CreateError("AUTH_FAILED", "Missing credentials"));
        }

        try
        {
            var payload = message.GetPayloadAsString();
            using var doc = JsonDocument.Parse(payload);

            string? username = null;
            string? password = null;

            if (doc.RootElement.TryGetProperty("username", out var usernameProp))
                username = usernameProp.GetString();
            if (doc.RootElement.TryGetProperty("password", out var passwordProp))
                password = passwordProp.GetString();

            var config = _configurationManager.Configuration;

            // In dev mode (no auth required) grant an anonymous identity with the
            // configured least-privilege anonymous role (never Admin by default).
            if (!config.RequireAuthentication)
            {
                var anonRole = UserRole.IsValid(config.AnonymousRole) ? config.AnonymousRole : UserRole.ReadOnly;
                _authConnections[connectionId] = ("anonymous", anonRole);
                _tcpServer?.RaisePayloadLimit(connectionId);
                return Task.FromResult(NoSqlMessage.CreateSuccess(
                    new { authenticated = true, token = "anonymous", username = "anonymous", role = anonRole }));
            }

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                return Task.FromResult(NoSqlMessage.CreateError("AUTH_FAILED", "Missing username or password"));
            }

            var authToken = _authManager?.Authenticate(username, password);
            if (authToken != null)
            {
                _authConnections[connectionId] = (authToken.Username, authToken.Role);
                _tcpServer?.RaisePayloadLimit(connectionId);
                return Task.FromResult(NoSqlMessage.CreateSuccess(
                    new { authenticated = true, token = authToken.TokenId, username = authToken.Username, role = authToken.Role }));
            }

            return Task.FromResult(NoSqlMessage.CreateError("AUTH_FAILED", "Invalid credentials"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Authentication parsing error for connection {ConnectionId}", connectionId);
            return Task.FromResult(NoSqlMessage.CreateError("AUTH_FAILED", "Invalid authentication format"));
        }
    }

    private Task<NoSqlMessage> HandleCommandAsync(NoSqlMessage message, string connectionId)
    {
        _logger.LogDebug("Processing command for connection {ConnectionId}", connectionId);

        if (message.Payload == null || message.PayloadLength == 0)
        {
            return Task.FromResult(NoSqlMessage.CreateError("INVALID_COMMAND", "Empty command"));
        }

        try
        {
            var payload = message.GetPayloadAsString();
            using var doc = JsonDocument.Parse(payload);

            if (!doc.RootElement.TryGetProperty("command", out var commandProp))
            {
                return Task.FromResult(NoSqlMessage.CreateError("INVALID_COMMAND", "Missing command property"));
            }

            var command = commandProp.GetString()?.ToLowerInvariant();

            // Role-based authorization (only enforced when authentication is required)
            if (_configurationManager.Configuration.RequireAuthentication && command != null)
            {
                if (!_authConnections.TryGetValue(connectionId, out var identity))
                {
                    return Task.FromResult(NoSqlMessage.CreateError("AUTH_REQUIRED", "Authenticate before sending commands"));
                }
                if (command == "changepassword" && identity.Username == "anonymous")
                {
                    return Task.FromResult(NoSqlMessage.CreateError("AUTH_REQUIRED", "changepassword requires an authenticated user"));
                }
                if (!CommandAuthorizer.IsAllowed(command, identity.Role))
                {
                    return Task.FromResult(NoSqlMessage.CreateError("FORBIDDEN", $"Role '{identity.Role}' may not run '{command}'"));
                }
            }

            return command switch
            {
                "get" => HandleGetCommand(doc.RootElement),
                "set" => HandleSetCommand(doc.RootElement),
                "delete" => HandleDeleteCommand(doc.RootElement),
                "exists" => HandleExistsCommand(doc.RootElement),
                "insert" => HandleInsertCommand(doc.RootElement),
                "replace" => HandleReplaceCommand(doc.RootElement),
                "upsert" => HandleUpsertCommand(doc.RootElement),
                "find_one" => HandleFindOneCommand(doc.RootElement),
                "touch" => HandleTouchCommand(doc.RootElement),
                "listcollections" => HandleListCollectionsCommand(doc.RootElement),
                "createcollection" => HandleCreateCollectionCommand(doc.RootElement),
                "dropcollection" => HandleDropCollectionCommand(doc.RootElement),
                "listdocuments" => HandleListDocumentsCommand(doc.RootElement),
                "count" => HandleCountCommand(doc.RootElement),
                "stats" => HandleStatsCommand(),
                "cluster" => HandleClusterCommand(doc.RootElement),
                "listusers" => Task.FromResult(HandleListUsersCommand()),
                "createuser" => Task.FromResult(HandleCreateUserCommand(doc.RootElement)),
                "deleteuser" => Task.FromResult(HandleDeleteUserCommand(doc.RootElement)),
                "setpassword" => Task.FromResult(HandleSetPasswordCommand(doc.RootElement)),
                "setrole" => Task.FromResult(HandleSetRoleCommand(doc.RootElement)),
                "changepassword" => Task.FromResult(HandleChangePasswordCommand(doc.RootElement, connectionId)),
                "listattachments" => HandleListAttachmentsCommand(doc.RootElement),
                "attachmentinfo" => HandleAttachmentInfoCommand(doc.RootElement),
                "uploadattachment" => HandleUploadAttachmentCommand(doc.RootElement),
                "downloadattachment" => HandleDownloadAttachmentCommand(doc.RootElement),
                "deleteattachment" => HandleDeleteAttachmentCommand(doc.RootElement),
                "totalstorage" => HandleTotalStorageCommand(),
                _ => Task.FromResult(NoSqlMessage.CreateError("UNKNOWN_COMMAND", $"Unknown command: {command}"))
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Command parsing error for connection {ConnectionId}", connectionId);
            return Task.FromResult(NoSqlMessage.CreateError("INVALID_COMMAND", "Invalid command format"));
        }
    }

    private NoSqlMessage HandleListUsersCommand()
    {
        if (_authManager == null)
            return NoSqlMessage.CreateError("NOT_INITIALIZED", "Authentication not initialized");
        return NoSqlMessage.CreateSuccess(new
        {
            users = _authManager.ListUsers()
                .Select(u => new { username = u.Username, role = u.Role, createdAt = u.CreatedAt })
        });
    }

    private NoSqlMessage HandleCreateUserCommand(JsonElement e)
    {
        if (_authManager == null)
            return NoSqlMessage.CreateError("NOT_INITIALIZED", "Authentication not initialized");
        var username = e.TryGetProperty("username", out var u) ? u.GetString() : null;
        var password = e.TryGetProperty("password", out var p) ? p.GetString() : null;
        var role = e.TryGetProperty("role", out var r) ? r.GetString() : UserRole.ReadWrite;
        if (string.IsNullOrWhiteSpace(username) || username.Length > 64)
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Username required (<= 64 chars)");
        if (string.IsNullOrEmpty(password) || password.Length < 6)
            return NoSqlMessage.CreateError("WEAK_PASSWORD", "Password must be at least 6 characters");
        if (!UserRole.IsValid(role))
            return NoSqlMessage.CreateError("INVALID_ROLE", $"Invalid role '{role}'");
        if (!_authManager.RegisterUser(username, password, role!))
            return NoSqlMessage.CreateError("USER_EXISTS", $"User '{username}' already exists");
        return NoSqlMessage.CreateSuccess(new { created = true, username });
    }

    private NoSqlMessage HandleDeleteUserCommand(JsonElement e)
    {
        if (_authManager == null)
            return NoSqlMessage.CreateError("NOT_INITIALIZED", "Authentication not initialized");
        var username = e.TryGetProperty("username", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(username))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Username required");
        return _authManager.RemoveUserGuarded(username) switch
        {
            UserOperationResult.Ok => NoSqlMessage.CreateSuccess(new { deleted = true }),
            UserOperationResult.NotFound => NoSqlMessage.CreateError("USER_NOT_FOUND", $"User '{username}' not found"),
            UserOperationResult.LastAdmin => NoSqlMessage.CreateError("LAST_ADMIN", "Cannot delete the last admin"),
            _ => NoSqlMessage.CreateError("COMMAND_ERROR", "Delete failed")
        };
    }

    private NoSqlMessage HandleSetPasswordCommand(JsonElement e)
    {
        if (_authManager == null)
            return NoSqlMessage.CreateError("NOT_INITIALIZED", "Authentication not initialized");
        var username = e.TryGetProperty("username", out var u) ? u.GetString() : null;
        var password = e.TryGetProperty("password", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(username))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Username required");
        if (string.IsNullOrEmpty(password) || password.Length < 6)
            return NoSqlMessage.CreateError("WEAK_PASSWORD", "Password must be at least 6 characters");
        return _authManager.SetPassword(username, password)
            ? NoSqlMessage.CreateSuccess(new { changed = true })
            : NoSqlMessage.CreateError("USER_NOT_FOUND", $"User '{username}' not found");
    }

    private NoSqlMessage HandleSetRoleCommand(JsonElement e)
    {
        if (_authManager == null)
            return NoSqlMessage.CreateError("NOT_INITIALIZED", "Authentication not initialized");
        var username = e.TryGetProperty("username", out var u) ? u.GetString() : null;
        var role = e.TryGetProperty("role", out var r) ? r.GetString() : null;
        if (string.IsNullOrWhiteSpace(username))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Username required");
        return _authManager.SetRoleGuarded(username, role ?? "") switch
        {
            UserOperationResult.Ok => NoSqlMessage.CreateSuccess(new { changed = true }),
            UserOperationResult.NotFound => NoSqlMessage.CreateError("USER_NOT_FOUND", $"User '{username}' not found"),
            UserOperationResult.InvalidRole => NoSqlMessage.CreateError("INVALID_ROLE", $"Invalid role '{role}'"),
            UserOperationResult.LastAdmin => NoSqlMessage.CreateError("LAST_ADMIN", "Cannot demote the last admin"),
            _ => NoSqlMessage.CreateError("COMMAND_ERROR", "Set role failed")
        };
    }

    private NoSqlMessage HandleChangePasswordCommand(JsonElement e, string connectionId)
    {
        if (_authManager == null)
            return NoSqlMessage.CreateError("NOT_INITIALIZED", "Authentication not initialized");
        if (!_authConnections.TryGetValue(connectionId, out var identity) || identity.Username == "anonymous")
            return NoSqlMessage.CreateError("AUTH_REQUIRED", "changepassword requires an authenticated user");
        var oldPw = e.TryGetProperty("oldPassword", out var o) ? o.GetString() : null;
        var newPw = e.TryGetProperty("newPassword", out var n) ? n.GetString() : null;
        if (string.IsNullOrEmpty(newPw) || newPw.Length < 6)
            return NoSqlMessage.CreateError("WEAK_PASSWORD", "Password must be at least 6 characters");
        return _authManager.ChangePassword(identity.Username, oldPw ?? "", newPw)
            ? NoSqlMessage.CreateSuccess(new { changed = true })
            : NoSqlMessage.CreateError("AUTH_FAILED", "Old password is incorrect");
    }

    private async Task<NoSqlMessage> HandleListAttachmentsCommand(JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        if (!e.TryGetProperty("collection", out var col) || !e.TryGetProperty("id", out var id) ||
            string.IsNullOrEmpty(col.GetString()) || string.IsNullOrEmpty(id.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id");
        var list = await _attachmentStore.ListAsync(col.GetString()!, id.GetString()!);
        return NoSqlMessage.CreateSuccess(new
        {
            attachments = list.Select(a => new { name = a.Name, contentType = a.ContentType, size = a.Size, hash = a.Hash, createdAt = a.CreatedAt, updatedAt = a.UpdatedAt })
        });
    }

    private async Task<NoSqlMessage> HandleAttachmentInfoCommand(JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        if (!e.TryGetProperty("collection", out var col) || !e.TryGetProperty("id", out var id) || !e.TryGetProperty("name", out var nm) ||
            string.IsNullOrEmpty(col.GetString()) || string.IsNullOrEmpty(id.GetString()) || string.IsNullOrEmpty(nm.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection, id or name");
        var info = await _attachmentStore.GetInfoAsync(col.GetString()!, id.GetString()!, nm.GetString()!);
        if (info == null) return NoSqlMessage.CreateSuccess(new { found = false, info = (object?)null });
        return NoSqlMessage.CreateSuccess(new { found = true, info = new { name = info.Name, contentType = info.ContentType, size = info.Size, hash = info.Hash, createdAt = info.CreatedAt, updatedAt = info.UpdatedAt } });
    }

    private async Task<NoSqlMessage> HandleUploadAttachmentCommand(JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        var collection = e.TryGetProperty("collection", out var col) ? col.GetString() : null;
        var id = e.TryGetProperty("id", out var idp) ? idp.GetString() : null;
        var name = e.TryGetProperty("name", out var nm) ? nm.GetString() : null;
        var contentType = e.TryGetProperty("contentType", out var ct) ? ct.GetString() : null;
        var b64 = e.TryGetProperty("contentBase64", out var cb) ? cb.GetString() : null;
        if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || b64 == null)
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection, id, name or contentBase64");
        if (name.Length > 255)
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Attachment name too long (max 255)");
        if (string.IsNullOrEmpty(contentType)) contentType = "application/octet-stream";

        byte[] content;
        try { content = Convert.FromBase64String(b64); }
        catch (FormatException) { return NoSqlMessage.CreateError("INVALID_CONTENT", "contentBase64 is not valid base64"); }

        int maxMb = _configurationManager.Configuration.MaxAttachmentSizeMB;
        long maxBytes = (long)Math.Max(maxMb, 1) * 1024 * 1024;
        if (content.Length > maxBytes)
            return NoSqlMessage.CreateError("ATTACHMENT_TOO_LARGE", $"Attachment exceeds {maxMb} MB limit");

        var result = await _attachmentStore.StoreAsync(collection, id, name, contentType, content);
        if (!result.Success)
        {
            var msg = result.ErrorMessage ?? "Upload failed";
            if (msg.Contains("not allowed")) return NoSqlMessage.CreateError("CONTENT_TYPE_BLOCKED", msg);
            return NoSqlMessage.CreateError("COMMAND_ERROR", msg);
        }
        return NoSqlMessage.CreateSuccess(new { stored = true, name = result.Info!.Name, hash = result.Info.Hash, size = result.Info.Size });
    }

    private async Task<NoSqlMessage> HandleDownloadAttachmentCommand(JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        if (!e.TryGetProperty("collection", out var col) || !e.TryGetProperty("id", out var id) || !e.TryGetProperty("name", out var nm) ||
            string.IsNullOrEmpty(col.GetString()) || string.IsNullOrEmpty(id.GetString()) || string.IsNullOrEmpty(nm.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection, id or name");
        var att = await _attachmentStore.GetAsync(col.GetString()!, id.GetString()!, nm.GetString()!);
        if (att == null) return NoSqlMessage.CreateSuccess(new { found = false });
        return NoSqlMessage.CreateSuccess(new { found = true, name = att.Name, contentType = att.ContentType, size = att.Size, contentBase64 = Convert.ToBase64String(att.Content) });
    }

    private async Task<NoSqlMessage> HandleDeleteAttachmentCommand(JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        if (!e.TryGetProperty("collection", out var col) || !e.TryGetProperty("id", out var id) || !e.TryGetProperty("name", out var nm) ||
            string.IsNullOrEmpty(col.GetString()) || string.IsNullOrEmpty(id.GetString()) || string.IsNullOrEmpty(nm.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection, id or name");
        var deleted = await _attachmentStore.DeleteAsync(col.GetString()!, id.GetString()!, nm.GetString()!);
        return NoSqlMessage.CreateSuccess(new { deleted });
    }

    private async Task<NoSqlMessage> HandleTotalStorageCommand()
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        var bytes = await _attachmentStore.GetTotalStorageSizeAsync();
        return NoSqlMessage.CreateSuccess(new { bytes });
    }

    private async Task<NoSqlMessage> HandleGetCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("id", out var idProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id property");
        }

        var collection = collectionProp.GetString() ?? "default";
        var id = idProp.GetString();

        if (string.IsNullOrEmpty(id))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Document id cannot be empty");
        }

        var document = await _documentStore.GetAsync(collection, id);
        if (document == null)
        {
            return NoSqlMessage.CreateSuccess(new { found = false, document = (object?)null, value = (object?)null });
        }

        var data = document.Data ?? new Dictionary<string, object>();
        var flat = new Dictionary<string, object?>(data.Count + 1) { ["_id"] = document.Id };
        foreach (var kv in data)
        {
            flat[kv.Key] = kv.Value;
        }

        // "document" (flat) is the contract clients read; "value" retained for backward compatibility
        return NoSqlMessage.CreateSuccess(new { found = true, document = flat, value = document });
    }

    private async Task<NoSqlMessage> HandleSetCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("document", out var documentProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or document property");
        }

        var collection = collectionProp.GetString() ?? "default";

        // Extract document data
        string? id = null;
        if (documentProp.TryGetProperty("_id", out var idProp))
        {
            id = idProp.GetString();
        }

        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString();
        }

        // Convert JsonElement to Dictionary
        var data = new Dictionary<string, object>();
        foreach (var prop in documentProp.EnumerateObject())
        {
            if (prop.Name != "_id")
            {
                data[prop.Name] = JsonElementToObject(prop.Value);
            }
        }

        var document = new Document
        {
            Id = id,
            Data = data
        };

        try
        {
            // Check if document exists to determine insert vs update
            var exists = await _documentStore.ExistsAsync(collection, id);
            if (exists)
            {
                await _documentStore.UpdateAsync(collection, document);
            }
            else
            {
                await _documentStore.InsertAsync(collection, document);
            }

            return NoSqlMessage.CreateSuccess(new { stored = true, id = id });
        }
        catch (Exception ex)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", ex.Message);
        }
    }

    private async Task<NoSqlMessage> HandleDeleteCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("id", out var idProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id property");
        }

        var collection = collectionProp.GetString() ?? "default";
        var id = idProp.GetString();

        if (string.IsNullOrEmpty(id))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Document id cannot be empty");
        }

        var deleted = await _documentStore.DeleteAsync(collection, id);
        return NoSqlMessage.CreateSuccess(new { deleted = deleted });
    }

    private async Task<NoSqlMessage> HandleExistsCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("id", out var idProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id property");
        }

        var collection = collectionProp.GetString() ?? "default";
        var id = idProp.GetString();

        if (string.IsNullOrEmpty(id))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Document id cannot be empty");
        }

        var exists = await _documentStore.ExistsAsync(collection, id);
        return NoSqlMessage.CreateSuccess(new { exists = exists });
    }

    private async Task<NoSqlMessage> HandleInsertCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("document", out var documentProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or document property");
        }

        var collection = collectionProp.GetString() ?? "default";

        // Extract document ID
        string? id = null;
        if (documentProp.TryGetProperty("_id", out var idProp))
        {
            id = idProp.GetString();
        }

        if (string.IsNullOrEmpty(id))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Document _id is required for INSERT command");
        }

        // Check if document already exists
        var exists = await _documentStore.ExistsAsync(collection, id);
        if (exists)
        {
            return NoSqlMessage.CreateError("DOCUMENT_ALREADY_EXISTS", $"Document with id '{id}' already exists in collection '{collection}'");
        }

        // Convert JsonElement to Dictionary
        var data = new Dictionary<string, object>();
        foreach (var prop in documentProp.EnumerateObject())
        {
            if (prop.Name != "_id")
            {
                data[prop.Name] = JsonElementToObject(prop.Value);
            }
        }

        var document = new Document
        {
            Id = id,
            Data = data
        };

        try
        {
            await _documentStore.InsertAsync(collection, document);
            _logger.LogDebug("Document inserted: {Collection}/{Id}", collection, id);
            return NoSqlMessage.CreateSuccess(new { inserted = true, id = id });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Insert failed for {Collection}/{Id}", collection, id);
            return NoSqlMessage.CreateError("STORAGE_ERROR", ex.Message);
        }
    }

    private async Task<NoSqlMessage> HandleReplaceCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("document", out var documentProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or document property");
        }

        var collection = collectionProp.GetString() ?? "default";

        // Extract document ID
        string? id = null;
        if (documentProp.TryGetProperty("_id", out var idProp))
        {
            id = idProp.GetString();
        }

        if (string.IsNullOrEmpty(id))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Document _id is required for REPLACE command");
        }

        // Check if document exists
        var exists = await _documentStore.ExistsAsync(collection, id);
        if (!exists)
        {
            return NoSqlMessage.CreateError("DOCUMENT_NOT_FOUND", $"Document with id '{id}' not found in collection '{collection}'");
        }

        // Convert JsonElement to Dictionary
        var data = new Dictionary<string, object>();
        foreach (var prop in documentProp.EnumerateObject())
        {
            if (prop.Name != "_id")
            {
                data[prop.Name] = JsonElementToObject(prop.Value);
            }
        }

        var document = new Document
        {
            Id = id,
            Data = data
        };

        try
        {
            await _documentStore.UpdateAsync(collection, document);
            _logger.LogDebug("Document replaced: {Collection}/{Id}", collection, id);
            return NoSqlMessage.CreateSuccess(new { replaced = true, id = id });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Replace failed for {Collection}/{Id}", collection, id);
            return NoSqlMessage.CreateError("STORAGE_ERROR", ex.Message);
        }
    }

    private async Task<NoSqlMessage> HandleUpsertCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("document", out var documentProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or document property");
        }

        var collection = collectionProp.GetString() ?? "default";

        // Extract document ID
        string? id = null;
        if (documentProp.TryGetProperty("_id", out var idProp))
        {
            id = idProp.GetString();
        }

        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString();
        }

        // Convert JsonElement to Dictionary
        var data = new Dictionary<string, object>();
        foreach (var prop in documentProp.EnumerateObject())
        {
            if (prop.Name != "_id")
            {
                data[prop.Name] = JsonElementToObject(prop.Value);
            }
        }

        var document = new Document
        {
            Id = id,
            Data = data
        };

        try
        {
            // Check if document exists to determine insert vs update
            var exists = await _documentStore.ExistsAsync(collection, id);
            if (exists)
            {
                await _documentStore.UpdateAsync(collection, document);
                _logger.LogDebug("Document updated (upsert): {Collection}/{Id}", collection, id);
                return NoSqlMessage.CreateSuccess(new { upserted = true, id = id, wasInserted = false });
            }
            else
            {
                await _documentStore.InsertAsync(collection, document);
                _logger.LogDebug("Document inserted (upsert): {Collection}/{Id}", collection, id);
                return NoSqlMessage.CreateSuccess(new { upserted = true, id = id, wasInserted = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Upsert failed for {Collection}/{Id}", collection, id);
            return NoSqlMessage.CreateError("STORAGE_ERROR", ex.Message);
        }
    }

    private async Task<NoSqlMessage> HandleFindOneCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection property");
        }

        var collection = collectionProp.GetString() ?? "default";

        try
        {
            // If 'id' is provided, do a direct lookup
            if (commandElement.TryGetProperty("id", out var idProp))
            {
                var id = idProp.GetString();
                if (!string.IsNullOrEmpty(id))
                {
                    var document = await _documentStore.GetAsync(collection, id);
                    if (document == null)
                    {
                        return NoSqlMessage.CreateSuccess(new { found = false, document = (object?)null });
                    }
                    return NoSqlMessage.CreateSuccess(new { found = true, document = document });
                }
            }

            // If 'filter' is provided, use query to find matching document
            if (commandElement.TryGetProperty("filter", out var filterProp))
            {
                // Get all documents and filter manually
                var allDocs = await _documentStore.GetAllAsync(collection);
                Document? matchedDoc = null;

                foreach (var doc in allDocs)
                {
                    if (MatchesFilter(doc, filterProp))
                    {
                        matchedDoc = doc;
                        break;
                    }
                }

                if (matchedDoc == null)
                {
                    return NoSqlMessage.CreateSuccess(new { found = false, document = (object?)null });
                }
                return NoSqlMessage.CreateSuccess(new { found = true, document = matchedDoc });
            }

            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing 'id' or 'filter' property for FIND_ONE command");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding document in collection {Collection}", collection);
            return NoSqlMessage.CreateError("STORAGE_ERROR", $"Failed to find document: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleTouchCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            !commandElement.TryGetProperty("id", out var idProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id property");
        }

        var collection = collectionProp.GetString() ?? "default";
        var id = idProp.GetString();

        if (string.IsNullOrEmpty(id))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Document id cannot be empty");
        }

        try
        {
            // Get the document
            var document = await _documentStore.GetAsync(collection, id);
            if (document == null)
            {
                return NoSqlMessage.CreateError("DOCUMENT_NOT_FOUND", $"Document with id '{id}' not found in collection '{collection}'");
            }

            // Update the UpdatedAt timestamp
            document.UpdatedAt = DateTime.UtcNow;
            document.Version++;

            await _documentStore.UpdateAsync(collection, document);
            _logger.LogDebug("Document touched: {Collection}/{Id}", collection, id);
            return NoSqlMessage.CreateSuccess(new { touched = true, id = id, updatedAt = document.UpdatedAt });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error touching document {Collection}/{Id}", collection, id);
            return NoSqlMessage.CreateError("STORAGE_ERROR", $"Failed to touch document: {ex.Message}");
        }
    }

    private bool MatchesFilter(Document document, JsonElement filter)
    {
        // Simple filter matching - supports exact equality checks
        foreach (var prop in filter.EnumerateObject())
        {
            if (document.Data == null)
                return false;

            if (!document.Data.TryGetValue(prop.Name, out var docValue))
            {
                return false;
            }

            var filterValue = JsonElementToObject(prop.Value);
            if (!Equals(docValue, filterValue))
            {
                return false;
            }
        }
        return true;
    }

    private async Task<NoSqlMessage> HandleStatsCommand()
    {
        try
        {
            var uptime = DateTime.UtcNow - _startTime;
            var memoryBytes = GC.GetTotalMemory(forceFullCollection: false);
            var memoryMB = (int)(memoryBytes / 1_048_576);

            long totalDocuments = 0;
            int totalCollections = 0;

            if (_documentStore != null)
            {
                var collections = (await _documentStore.GetCollectionsAsync()).ToList();
                totalCollections = collections.Count;
                foreach (var coll in collections)
                    totalDocuments += await _documentStore.CountAsync(coll);
            }

            int activeConnections = _tcpServer?.ActiveConnectionCount ?? 0;

            return NoSqlMessage.CreateSuccess(new
            {
                version = ServerVersion,
                uptimeSeconds = (long)uptime.TotalSeconds,
                memoryUsageMB = memoryMB,
                totalDocuments,
                totalCollections,
                activeConnections
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving server stats");
            return NoSqlMessage.CreateError("STATS_ERROR", $"Failed to retrieve stats: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleListCollectionsCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        try
        {
            var collections = await _documentStore.GetCollectionsAsync();
            var collectionList = collections.ToList();

            return NoSqlMessage.CreateSuccess(new
            {
                count = collectionList.Count,
                collections = collectionList
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing collections");
            return NoSqlMessage.CreateError("STORAGE_ERROR", $"Failed to list collections: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleCreateCollectionCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            string.IsNullOrEmpty(collectionProp.GetString()))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection property");
        }

        var collection = collectionProp.GetString()!;
        try
        {
            var existing = await _documentStore.GetCollectionsAsync();
            bool created = !existing.Contains(collection);
            if (created)
            {
                await _documentStore.CreateCollectionAsync(collection);
            }
            return NoSqlMessage.CreateSuccess(new { created, collection });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating collection {Collection}", collection);
            return NoSqlMessage.CreateError("STORAGE_ERROR", $"Failed to create collection: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleDropCollectionCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            string.IsNullOrEmpty(collectionProp.GetString()))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection property");
        }

        var collection = collectionProp.GetString()!;
        try
        {
            var dropped = await _documentStore.DropCollectionAsync(collection);
            return NoSqlMessage.CreateSuccess(new { dropped, collection });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dropping collection {Collection}", collection);
            return NoSqlMessage.CreateError("STORAGE_ERROR", $"Failed to drop collection: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleListDocumentsCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            string.IsNullOrEmpty(collectionProp.GetString()))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection property");
        }

        var collection = collectionProp.GetString()!;
        int skip = 0, take = 50;
        if (commandElement.TryGetProperty("document", out var optionsProp) &&
            optionsProp.ValueKind == JsonValueKind.Object)
        {
            if (optionsProp.TryGetProperty("skip", out var skipProp)) skip = Math.Max(skipProp.GetInt32(), 0);
            if (optionsProp.TryGetProperty("take", out var takeProp)) take = Math.Clamp(takeProp.GetInt32(), 1, 500);
        }

        try
        {
            var total = await _documentStore.CountAsync(collection);
            var all = await _documentStore.GetAllAsync(collection);
            var page = all
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .Skip(skip).Take(take)
                .Select(d =>
                {
                    var data = d.Data ?? new Dictionary<string, object>();
                    var flat = new Dictionary<string, object?>(data.Count + 1) { ["_id"] = d.Id };
                    foreach (var kv in data) flat[kv.Key] = kv.Value;
                    return flat;
                })
                .ToList();

            return NoSqlMessage.CreateSuccess(new { documents = page, total });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing documents in {Collection}", collection);
            return NoSqlMessage.CreateError("STORAGE_ERROR", $"Failed to list documents: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleCountCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
        {
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");
        }

        // Collection is optional - if not provided, count across all collections
        string? collection = null;
        if (commandElement.TryGetProperty("collection", out var collectionProp))
        {
            collection = collectionProp.GetString();
        }

        try
        {
            long count;
            if (string.IsNullOrEmpty(collection))
            {
                // Count across all collections
                var collections = await _documentStore.GetCollectionsAsync();
                count = 0;
                foreach (var coll in collections)
                {
                    count += await _documentStore.CountAsync(coll);
                }
                return NoSqlMessage.CreateSuccess(new { count = count, collection = "*", totalCollections = collections.Count() });
            }
            else
            {
                count = await _documentStore.CountAsync(collection);
                return NoSqlMessage.CreateSuccess(new { count = count, collection = collection });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting documents in collection {Collection}", collection ?? "*");
            return NoSqlMessage.CreateError("STORAGE_ERROR", $"Failed to count documents: {ex.Message}");
        }
    }

    private Task<NoSqlMessage> HandleClusterCommand(JsonElement commandElement)
    {
        if (_clusterManager == null)
        {
            return Task.FromResult(NoSqlMessage.CreateError("CLUSTER_NOT_AVAILABLE", "Clustering is not enabled on this server"));
        }

        if (!commandElement.TryGetProperty("subcommand", out var subcommandProp))
        {
            return Task.FromResult(NoSqlMessage.CreateError("INVALID_COMMAND", "Missing subcommand property for CLUSTER command"));
        }

        var subcommand = subcommandProp.GetString()?.ToLowerInvariant();

        return subcommand switch
        {
            "info" => HandleClusterInfoCommand(),
            "nodes" => HandleClusterNodesCommand(),
            "join" => HandleClusterJoinCommand(commandElement),
            "leave" => HandleClusterLeaveCommand(commandElement),
            "failover" => HandleClusterFailoverCommand(),
            "replicate" => HandleClusterReplicateCommand(commandElement),
            "forget" => HandleClusterForgetCommand(commandElement),
            _ => Task.FromResult(NoSqlMessage.CreateError("UNKNOWN_SUBCOMMAND", $"Unknown CLUSTER subcommand: {subcommand}"))
        };
    }

    private async Task<NoSqlMessage> HandleClusterInfoCommand()
    {
        try
        {
            var clusterInfo = await _clusterManager!.GetClusterInfoAsync();
            var leader = await _clusterManager.GetLeaderAsync();

            var response = new
            {
                clusterId = clusterInfo.ClusterId,
                clusterName = clusterInfo.ClusterName,
                health = clusterInfo.Health.ToString(),
                totalNodes = clusterInfo.TotalNodeCount,
                activeNodes = clusterInfo.ActiveNodeCount,
                quorumSize = clusterInfo.QuorumSize,
                isWritable = clusterInfo.IsWritable,
                hasLeader = clusterInfo.HasLeader,
                leaderNodeId = leader?.NodeId,
                leaderHost = leader?.Host,
                localNodeId = _clusterManager.LocalNode?.NodeId,
                isLocalLeader = _clusterManager.IsLeader,
                isClusterMember = _clusterManager.IsClusterMember
            };

            return NoSqlMessage.CreateSuccess(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cluster info");
            return NoSqlMessage.CreateError("CLUSTER_ERROR", $"Failed to get cluster info: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleClusterNodesCommand()
    {
        try
        {
            var nodes = await _clusterManager!.GetNodesAsync();
            var leader = await _clusterManager.GetLeaderAsync();
            var leaderId = leader?.NodeId;

            var nodeList = nodes.Select(n => new
            {
                nodeId = n.NodeId,
                host = n.Host,
                p2pPort = n.P2PPort,
                state = n.State.ToString(),
                isLeader = n.NodeId == leaderId,
                term = n.Term,
                tags = n.Tags,
                lastSeenAt = n.LastSeenAt
            }).ToList();

            return NoSqlMessage.CreateSuccess(new
            {
                count = nodeList.Count,
                nodes = nodeList
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cluster nodes");
            return NoSqlMessage.CreateError("CLUSTER_ERROR", $"Failed to get cluster nodes: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleClusterJoinCommand(JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("seed", out var seedProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing 'seed' property for CLUSTER JOIN command");
        }

        var seed = seedProp.GetString();
        if (string.IsNullOrEmpty(seed))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Seed node address cannot be empty");
        }

        try
        {
            // Parse optional properties
            TimeSpan timeout = TimeSpan.FromSeconds(30);
            if (commandElement.TryGetProperty("timeout", out var timeoutProp) && timeoutProp.TryGetInt32(out var timeoutMs))
            {
                timeout = TimeSpan.FromMilliseconds(timeoutMs);
            }

            var options = new JoinOptions
            {
                SeedNode = seed,
                Timeout = timeout
            };

            var result = await _clusterManager!.JoinClusterAsync(seed, options);

            if (result.Success)
            {
                return NoSqlMessage.CreateSuccess(new
                {
                    joined = true,
                    clusterId = result.ClusterInfo?.ClusterId,
                    clusterName = result.ClusterInfo?.ClusterName,
                    nodeCount = result.ClusterInfo?.TotalNodeCount
                });
            }
            else
            {
                return NoSqlMessage.CreateError("JOIN_FAILED", result.ErrorMessage ?? "Failed to join cluster");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining cluster with seed {Seed}", seed);
            return NoSqlMessage.CreateError("CLUSTER_ERROR", $"Failed to join cluster: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleClusterLeaveCommand(JsonElement commandElement)
    {
        try
        {
            var options = new LeaveOptions();

            // Parse optional properties - LeaveOptions uses ReplicateData, not Graceful
            if (commandElement.TryGetProperty("replicateData", out var replicateProp))
            {
                if (replicateProp.ValueKind == System.Text.Json.JsonValueKind.True)
                    options.ReplicateData = true;
                else if (replicateProp.ValueKind == System.Text.Json.JsonValueKind.False)
                    options.ReplicateData = false;
            }

            if (commandElement.TryGetProperty("timeout", out var timeoutProp) && timeoutProp.TryGetInt32(out var timeoutMs))
            {
                options.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
            }

            var result = await _clusterManager!.LeaveClusterAsync(options);

            if (result.Success)
            {
                return NoSqlMessage.CreateSuccess(new
                {
                    left = true,
                    message = "Successfully left the cluster"
                });
            }
            else
            {
                return NoSqlMessage.CreateError("LEAVE_FAILED", result.ErrorMessage ?? "Failed to leave cluster");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving cluster");
            return NoSqlMessage.CreateError("CLUSTER_ERROR", $"Failed to leave cluster: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleClusterFailoverCommand()
    {
        try
        {
            var success = await _clusterManager!.RequestLeaderElectionAsync();

            if (success)
            {
                // Wait a moment for election to complete
                await Task.Delay(500);
                var newLeader = await _clusterManager.GetLeaderAsync();

                return NoSqlMessage.CreateSuccess(new
                {
                    failoverInitiated = true,
                    newLeaderNodeId = newLeader?.NodeId,
                    newLeaderHost = newLeader?.Host
                });
            }
            else
            {
                return NoSqlMessage.CreateError("FAILOVER_FAILED", "Failed to initiate leader election");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating failover");
            return NoSqlMessage.CreateError("CLUSTER_ERROR", $"Failed to initiate failover: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleClusterReplicateCommand(JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("nodeId", out var nodeIdProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing 'nodeId' property for CLUSTER REPLICATE command");
        }

        var nodeId = nodeIdProp.GetString();
        if (string.IsNullOrEmpty(nodeId))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Node ID cannot be empty");
        }

        try
        {
            // Note: Direct replication sync is handled by the replication manager
            // This command acknowledges the request and triggers a sync check
            var node = await _clusterManager!.GetNodeAsync(nodeId);

            if (node == null)
            {
                return NoSqlMessage.CreateError("NODE_NOT_FOUND", $"Node '{nodeId}' not found in cluster");
            }

            return NoSqlMessage.CreateSuccess(new
            {
                replicationRequested = true,
                targetNodeId = nodeId,
                targetNodeHost = node.Host,
                message = "Replication sync request acknowledged. The replication manager will handle synchronization."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting replication to node {NodeId}", nodeId);
            return NoSqlMessage.CreateError("CLUSTER_ERROR", $"Failed to request replication: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleClusterForgetCommand(JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("nodeId", out var nodeIdProp))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing 'nodeId' property for CLUSTER FORGET command");
        }

        var nodeId = nodeIdProp.GetString();
        if (string.IsNullOrEmpty(nodeId))
        {
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Node ID cannot be empty");
        }

        try
        {
            var node = await _clusterManager!.GetNodeAsync(nodeId);
            if (node == null)
            {
                return NoSqlMessage.CreateError("NODE_NOT_FOUND", $"Node '{nodeId}' not found in cluster");
            }

            // Prevent forgetting the local node
            if (nodeId == _clusterManager.LocalNode?.NodeId)
            {
                return NoSqlMessage.CreateError("INVALID_OPERATION", "Cannot forget the local node. Use CLUSTER LEAVE instead.");
            }

            // Prevent forgetting the leader without failover
            var leader = await _clusterManager.GetLeaderAsync();
            if (nodeId == leader?.NodeId)
            {
                return NoSqlMessage.CreateError("INVALID_OPERATION", "Cannot forget the current leader. Initiate failover first.");
            }

            var success = await _clusterManager.RemoveNodeAsync(nodeId);

            if (success)
            {
                return NoSqlMessage.CreateSuccess(new
                {
                    forgotten = true,
                    nodeId = nodeId,
                    message = $"Node '{nodeId}' has been removed from the cluster"
                });
            }
            else
            {
                return NoSqlMessage.CreateError("FORGET_FAILED", $"Failed to remove node '{nodeId}' from cluster");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forgetting node {NodeId}", nodeId);
            return NoSqlMessage.CreateError("CLUSTER_ERROR", $"Failed to forget node: {ex.Message}");
        }
    }

    private static object JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            _ => element.GetRawText()
        };
    }

    private async Task<NoSqlMessage> HandleBulkOperationAsync(NoSqlMessage message, string connectionId)
    {
        _logger.LogDebug("Processing bulk operation for connection {ConnectionId}", connectionId);

        // Bulk operations mutate data: enforce the same authentication + authorization
        // gate as regular commands.
        if (_configurationManager.Configuration.RequireAuthentication)
        {
            if (!_authConnections.TryGetValue(connectionId, out var identity))
            {
                return NoSqlMessage.CreateError("AUTH_REQUIRED", "Authenticate before sending bulk operations");
            }
            if (!CommandAuthorizer.IsAllowed("bulk", identity.Role))
            {
                return NoSqlMessage.CreateError("FORBIDDEN", $"Role '{identity.Role}' may not run bulk operations");
            }
        }

        if (message.Payload == null || message.PayloadLength == 0)
        {
            return NoSqlMessage.CreateError("INVALID_BATCH", "Empty batch request");
        }

        try
        {
            var payload = message.GetPayloadAsString();
            var request = JsonSerializer.Deserialize<BatchOperationRequest>(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (request == null)
            {
                return NoSqlMessage.CreateError("INVALID_BATCH", "Failed to deserialize batch request");
            }

            if (string.IsNullOrEmpty(request.Collection))
            {
                return NoSqlMessage.CreateError("INVALID_BATCH", "Collection name is required");
            }

            if (request.Operations.Count == 0)
            {
                return NoSqlMessage.CreateSuccess(new BatchOperationResponse
                {
                    Success = true,
                    TotalProcessed = 0,
                    Results = new List<BatchOperationItemResult>()
                });
            }

            var response = await ProcessBatchRequestAsync(request);
            return NoSqlMessage.CreateSuccess(response);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Batch operation parsing error for connection {ConnectionId}", connectionId);
            return NoSqlMessage.CreateError("INVALID_BATCH", "Invalid batch request format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batch operation for connection {ConnectionId}", connectionId);
            return NoSqlMessage.CreateError("BATCH_ERROR", "Internal error processing batch operation");
        }
    }

    private async Task<BatchOperationResponse> ProcessBatchRequestAsync(BatchOperationRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = new BatchOperationResponse
        {
            Success = true,
            Results = new List<BatchOperationItemResult>()
        };

        for (int i = 0; i < request.Operations.Count; i++)
        {
            var operation = request.Operations[i];
            var result = await ProcessBatchOperationItemAsync(request.Collection, operation, i);

            response.Results.Add(result);

            if (result.Success)
            {
                switch (operation.OperationType)
                {
                    case BatchOperationType.Insert:
                        response.InsertedCount++;
                        break;
                    case BatchOperationType.Update:
                        response.UpdatedCount++;
                        break;
                    case BatchOperationType.Delete:
                        response.DeletedCount++;
                        break;
                }
            }
            else if (request.StopOnError)
            {
                response.Success = false;
                response.ErrorMessage = $"Batch stopped due to error at index {i}: {result.ErrorMessage}";
                break;
            }
        }

        response.TotalProcessed = response.Results.Count;
        stopwatch.Stop();
        response.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;

        _logger.LogDebug("Batch operation completed: {Inserted} inserted, {Updated} updated, {Deleted} deleted in {Ms}ms",
            response.InsertedCount, response.UpdatedCount, response.DeletedCount, response.ProcessingTimeMs);

        return response;
    }

    private async Task<BatchOperationItemResult> ProcessBatchOperationItemAsync(string collection, BatchOperationItem operation, int index)
    {
        var result = new BatchOperationItemResult
        {
            Index = index,
            Success = false
        };

        try
        {
            switch (operation.OperationType)
            {
                case BatchOperationType.Insert:
                    result.Success = await ProcessBatchInsertAsync(collection, operation, result);
                    break;

                case BatchOperationType.Update:
                    result.Success = await ProcessBatchUpdateAsync(collection, operation, result);
                    break;

                case BatchOperationType.Delete:
                    result.Success = await ProcessBatchDeleteAsync(collection, operation, result);
                    break;

                default:
                    result.ErrorCode = "UNSUPPORTED_OPERATION";
                    result.ErrorMessage = $"Operation type {operation.OperationType} is not supported";
                    break;
            }
        }
        catch (Exception ex)
        {
            result.ErrorCode = "INTERNAL_ERROR";
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<bool> ProcessBatchInsertAsync(string collection, BatchOperationItem operation, BatchOperationItemResult result)
    {
        if (_documentStore == null)
        {
            result.ErrorCode = "STORAGE_ERROR";
            result.ErrorMessage = "Storage not initialized";
            return false;
        }

        if (operation.Document == null || operation.Document.Count == 0)
        {
            result.ErrorCode = "MISSING_DOCUMENT";
            result.ErrorMessage = "Document data is required for insert operation";
            return false;
        }

        // Extract document ID if present, or generate one
        if (operation.Document.TryGetValue("_id", out var idValue))
        {
            result.DocumentId = idValue?.ToString();
        }

        if (string.IsNullOrEmpty(result.DocumentId))
        {
            result.DocumentId = Guid.NewGuid().ToString("N");
        }

        // Convert to Document and insert
        var data = new Dictionary<string, object>();
        foreach (var kvp in operation.Document)
        {
            if (kvp.Key != "_id")
            {
                data[kvp.Key] = kvp.Value;
            }
        }

        var document = new Document
        {
            Id = result.DocumentId,
            Data = data
        };

        try
        {
            await _documentStore.InsertAsync(collection, document);
            _logger.LogTrace("Batch insert into {Collection}: {DocumentId}", collection, result.DocumentId);
            return true;
        }
        catch (DocumentAlreadyExistsException)
        {
            result.ErrorCode = "DUPLICATE_KEY";
            result.ErrorMessage = $"Document '{result.DocumentId}' already exists";
            return false;
        }
    }

    private async Task<bool> ProcessBatchUpdateAsync(string collection, BatchOperationItem operation, BatchOperationItemResult result)
    {
        if (_documentStore == null)
        {
            result.ErrorCode = "STORAGE_ERROR";
            result.ErrorMessage = "Storage not initialized";
            return false;
        }

        if (string.IsNullOrEmpty(operation.DocumentId) && (operation.Filter == null || operation.Filter.Count == 0))
        {
            result.ErrorCode = "MISSING_CRITERIA";
            result.ErrorMessage = "DocumentId or Filter is required for update operation";
            return false;
        }

        if ((operation.Document == null || operation.Document.Count == 0) &&
            (operation.UpdateFields == null || operation.UpdateFields.Count == 0))
        {
            result.ErrorCode = "MISSING_UPDATE_DATA";
            result.ErrorMessage = "Document or UpdateFields is required for update operation";
            return false;
        }

        result.DocumentId = operation.DocumentId;

        try
        {
            // Get existing document
            var existing = await _documentStore.GetAsync(collection, operation.DocumentId!);
            if (existing == null)
            {
                result.ErrorCode = "NOT_FOUND";
                result.ErrorMessage = $"Document '{operation.DocumentId}' not found";
                return false;
            }

            // Apply updates
            var data = existing.Data ?? new Dictionary<string, object>();
            var updateSource = operation.UpdateFields ?? operation.Document;
            if (updateSource != null)
            {
                foreach (var kvp in updateSource)
                {
                    if (kvp.Key != "_id")
                    {
                        data[kvp.Key] = kvp.Value;
                    }
                }
            }

            existing.Data = data;
            await _documentStore.UpdateAsync(collection, existing);
            _logger.LogTrace("Batch update in {Collection}: {DocumentId}", collection, result.DocumentId);
            return true;
        }
        catch (DocumentNotFoundException)
        {
            result.ErrorCode = "NOT_FOUND";
            result.ErrorMessage = $"Document '{operation.DocumentId}' not found";
            return false;
        }
    }

    private async Task<bool> ProcessBatchDeleteAsync(string collection, BatchOperationItem operation, BatchOperationItemResult result)
    {
        if (_documentStore == null)
        {
            result.ErrorCode = "STORAGE_ERROR";
            result.ErrorMessage = "Storage not initialized";
            return false;
        }

        if (string.IsNullOrEmpty(operation.DocumentId) && (operation.Filter == null || operation.Filter.Count == 0))
        {
            result.ErrorCode = "MISSING_CRITERIA";
            result.ErrorMessage = "DocumentId or Filter is required for delete operation";
            return false;
        }

        result.DocumentId = operation.DocumentId;

        var deleted = await _documentStore.DeleteAsync(collection, operation.DocumentId!);
        if (!deleted)
        {
            result.ErrorCode = "NOT_FOUND";
            result.ErrorMessage = $"Document '{operation.DocumentId}' not found";
            return false;
        }

        _logger.LogTrace("Batch delete from {Collection}: {DocumentId}", collection, result.DocumentId);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _tcpServer?.Dispose();

        if (_documentStore != null)
        {
            await _documentStore.DisposeAsync();
        }
    }
}
