// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Network;

namespace AdvGenNoSqlServer.Client
{
    /// <summary>
    /// Command methods for AdvGenNoSqlClient
    /// </summary>
    public partial class AdvGenNoSqlClient
    {
        /// <summary>
        /// Gets a document by ID from the specified collection
        /// </summary>
        /// <param name="collection">The collection name</param>
        /// <param name="id">The document ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The document if found, null otherwise</returns>
        public async Task<Dictionary<string, object>?> GetAsync(
            string collection, 
            string id, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection))
                throw new ArgumentNullException(nameof(collection));
            if (string.IsNullOrEmpty(id))
                throw new ArgumentNullException(nameof(id));

            EnsureConnected();

            // Create GET command with id at the top level (as expected by server)
            var payload = new
            {
                command = "get",
                collection,
                id
            };

            var message = NoSqlMessage.Create(
                Network.MessageType.Command, 
                System.Text.Json.JsonSerializer.Serialize(payload));
            
            var response = await SendAndReceiveAsync(message, cancellationToken);
            var result = ParseResponse(response);

            if (!result.Success || result.Data == null)
                return null;

            // Extract the value from the response
            if (result.Data is System.Text.Json.JsonElement dataElement)
            {
                if (dataElement.TryGetProperty("value", out var valueElement) &&
                    valueElement.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    // Convert JsonElement to Dictionary<string, object> to avoid disposal issues
                    return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(valueElement.GetRawText());
                }
            }

            return null;
        }

        /// <summary>
        /// Stores a document in the specified collection
        /// </summary>
        /// <param name="collection">The collection name</param>
        /// <param name="document">The document to store (must have an _id property or one will be generated)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The ID of the stored document</returns>
        public async Task<string> SetAsync(
            string collection, 
            object document, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection))
                throw new ArgumentNullException(nameof(collection));
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            EnsureConnected();

            // Create SET command with document nested (as expected by server)
            var message = NoSqlMessage.CreateCommand("set", collection, document);
            var response = await SendAndReceiveAsync(message, cancellationToken);
            var result = ParseResponse(response);

            if (!result.Success)
            {
                var errorMessage = result.Error?.Message ?? "Unknown error";
                throw new NoSqlClientException($"Set operation failed: {errorMessage}");
            }

            // Extract the id from the response
            if (result.Data != null && result.Data is System.Text.Json.JsonElement dataElement)
            {
                if (dataElement.TryGetProperty("id", out var idElement))
                {
                    return idElement.GetString() ?? throw new NoSqlClientException("Server returned null ID");
                }
            }

            throw new NoSqlClientException("Server response did not contain document ID");
        }

        /// <summary>
        /// Deletes a document by ID from the specified collection
        /// </summary>
        /// <param name="collection">The collection name</param>
        /// <param name="id">The document ID to delete</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if the document was deleted, false if not found</returns>
        public async Task<bool> DeleteAsync(
            string collection, 
            string id, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection))
                throw new ArgumentNullException(nameof(collection));
            if (string.IsNullOrEmpty(id))
                throw new ArgumentNullException(nameof(id));

            EnsureConnected();

            // Create DELETE command with id at the top level (as expected by server)
            var payload = new
            {
                command = "delete",
                collection,
                id
            };

            var message = NoSqlMessage.Create(
                Network.MessageType.Command, 
                System.Text.Json.JsonSerializer.Serialize(payload));
            
            var response = await SendAndReceiveAsync(message, cancellationToken);
            var result = ParseResponse(response);

            if (!result.Success)
                return false;

            // Extract the deleted status from the response
            if (result.Data != null && result.Data is System.Text.Json.JsonElement dataElement)
            {
                if (dataElement.TryGetProperty("deleted", out var deletedElement))
                {
                    return deletedElement.GetBoolean();
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if a document exists in the specified collection
        /// </summary>
        /// <param name="collection">The collection name</param>
        /// <param name="id">The document ID to check</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if the document exists, false otherwise</returns>
        public async Task<bool> ExistsAsync(
            string collection, 
            string id, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection))
                throw new ArgumentNullException(nameof(collection));
            if (string.IsNullOrEmpty(id))
                throw new ArgumentNullException(nameof(id));

            EnsureConnected();

            // Create EXISTS command with id at the top level
            var payload = new
            {
                command = "exists",
                collection,
                id
            };

            var message = NoSqlMessage.Create(
                Network.MessageType.Command, 
                System.Text.Json.JsonSerializer.Serialize(payload));
            
            var response = await SendAndReceiveAsync(message, cancellationToken);
            var result = ParseResponse(response);

            if (!result.Success)
                return false;

            // Extract the exists status from the response
            if (result.Data != null && result.Data is System.Text.Json.JsonElement dataElement)
            {
                if (dataElement.TryGetProperty("exists", out var existsElement))
                {
                    return existsElement.GetBoolean();
                }
            }

            return false;
        }
    }
}
