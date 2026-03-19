// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using System.Text.Json;

namespace AdvGenNoSqlServer.Core.Validation
{
    /// <summary>
    /// Represents a validation error for a specific document field.
    /// </summary>
    public record ValidationError
    {
        /// <summary>
        /// Gets the path to the field that failed validation (e.g., "user.email" or "items[0].name").
        /// </summary>
        public required string Path { get; init; }

        /// <summary>
        /// Gets the error code for the validation failure.
        /// </summary>
        public required string ErrorCode { get; init; }

        /// <summary>
        /// Gets the human-readable error message.
        /// </summary>
        public required string Message { get; init; }

        /// <summary>
        /// Gets additional context about the validation error.
        /// </summary>
        public Dictionary<string, object>? Context { get; init; }

        /// <summary>
        /// Creates a validation error for a required field that is missing.
        /// </summary>
        public static ValidationError RequiredField(string path)
        {
            return new ValidationError
            {
                Path = path,
                ErrorCode = "REQUIRED_FIELD",
                Message = $"Required field '{path}' is missing or null."
            };
        }

        /// <summary>
        /// Creates a validation error for a type mismatch.
        /// </summary>
        public static ValidationError TypeMismatch(string path, string expectedType, string actualType)
        {
            return new ValidationError
            {
                Path = path,
                ErrorCode = "TYPE_MISMATCH",
                Message = $"Field '{path}' expected type '{expectedType}' but got '{actualType}'.",
                Context = new Dictionary<string, object>
                {
                    ["expectedType"] = expectedType,
                    ["actualType"] = actualType
                }
            };
        }

        /// <summary>
        /// Creates a validation error for a value that is too short.
        /// </summary>
        public static ValidationError MinLength(string path, int minLength, int actualLength)
        {
            return new ValidationError
            {
                Path = path,
                ErrorCode = "MIN_LENGTH",
                Message = $"Field '{path}' must be at least {minLength} characters but was {actualLength}.",
                Context = new Dictionary<string, object>
                {
                    ["minLength"] = minLength,
                    ["actualLength"] = actualLength
                }
            };
        }

        /// <summary>
        /// Creates a validation error for a value that is too long.
        /// </summary>
        public static ValidationError MaxLength(string path, int maxLength, int actualLength)
        {
            return new ValidationError
            {
                Path = path,
                ErrorCode = "MAX_LENGTH",
                Message = $"Field '{path}' must be at most {maxLength} characters but was {actualLength}.",
                Context = new Dictionary<string, object>
                {
                    ["maxLength"] = maxLength,
                    ["actualLength"] = actualLength
                }
            };
        }

        /// <summary>
        /// Creates a validation error for a value that is below minimum.
        /// </summary>
        public static ValidationError Minimum(string path, double minimum, double actual)
        {
            return new ValidationError
            {
                Path = path,
                ErrorCode = "MINIMUM",
                Message = $"Field '{path}' must be at least {minimum} but was {actual}.",
                Context = new Dictionary<string, object>
                {
                    ["minimum"] = minimum,
                    ["actual"] = actual
                }
            };
        }

        /// <summary>
        /// Creates a validation error for a value that is above maximum.
        /// </summary>
        public static ValidationError Maximum(string path, double maximum, double actual)
        {
            return new ValidationError
            {
                Path = path,
                ErrorCode = "MAXIMUM",
                Message = $"Field '{path}' must be at most {maximum} but was {actual}.",
                Context = new Dictionary<string, object>
                {
                    ["maximum"] = maximum,
                    ["actual"] = actual
                }
            };
        }

        /// <summary>
        /// Creates a validation error for a pattern mismatch.
        /// </summary>
        public static ValidationError Pattern(string path, string pattern, string actual)
        {
            return new ValidationError
            {
                Path = path,
                ErrorCode = "PATTERN",
                Message = $"Field '{path}' does not match pattern '{pattern}'.",
                Context = new Dictionary<string, object>
                {
                    ["pattern"] = pattern,
                    ["actual"] = actual
                }
            };
        }

        /// <summary>
        /// Creates a validation error for an invalid enum value.
        /// </summary>
        public static ValidationError Enum(string path, string[] allowedValues, string actual)
        {
            return new ValidationError
            {
                Path = path,
                ErrorCode = "ENUM",
                Message = $"Field '{path}' must be one of [{string.Join(", ", allowedValues)}] but was '{actual}'.",
                Context = new Dictionary<string, object>
                {
                    ["allowedValues"] = allowedValues,
                    ["actual"] = actual
                }
            };
        }

        /// <summary>
        /// Creates a custom validation error.
        /// </summary>
        public static ValidationError Custom(string path, string errorCode, string message, Dictionary<string, object>? context = null)
        {
            return new ValidationError
            {
                Path = path,
                ErrorCode = errorCode,
                Message = message,
                Context = context
            };
        }
    }

    /// <summary>
    /// Represents the result of a document validation operation.
    /// </summary>
    public record ValidationResult
    {
        /// <summary>
        /// Gets whether the document is valid.
        /// </summary>
        public bool IsValid => Errors.Count == 0;

        /// <summary>
        /// Gets the collection of validation errors.
        /// </summary>
        public IReadOnlyList<ValidationError> Errors { get; init; } = Array.Empty<ValidationError>();

        /// <summary>
        /// Gets additional validation metadata.
        /// </summary>
        public Dictionary<string, object>? Metadata { get; init; }

        /// <summary>
        /// Creates a successful validation result.
        /// </summary>
        public static ValidationResult Success(Dictionary<string, object>? metadata = null)
        {
            return new ValidationResult
            {
                Errors = Array.Empty<ValidationError>(),
                Metadata = metadata
            };
        }

        /// <summary>
        /// Creates a failed validation result with the specified errors.
        /// </summary>
        public static ValidationResult Failure(IEnumerable<ValidationError> errors, Dictionary<string, object>? metadata = null)
        {
            return new ValidationResult
            {
                Errors = errors.ToArray(),
                Metadata = metadata
            };
        }

        /// <summary>
        /// Creates a failed validation result with a single error.
        /// </summary>
        public static ValidationResult Failure(ValidationError error, Dictionary<string, object>? metadata = null)
        {
            return new ValidationResult
            {
                Errors = new[] { error },
                Metadata = metadata
            };
        }
    }

    /// <summary>
    /// Defines the validation level for a collection.
    /// </summary>
    public enum ValidationLevel
    {
        /// <summary>
        /// No validation is performed.
        /// </summary>
        None,

        /// <summary>
        /// Validation errors are logged but documents are still accepted.
        /// </summary>
        Moderate,

        /// <summary>
        /// Validation errors cause the operation to fail.
        /// </summary>
        Strict
    }

    /// <summary>
    /// Defines the action to take when validation fails.
    /// </summary>
    public enum ValidationAction
    {
        /// <summary>
        /// Log a warning but accept the document.
        /// </summary>
        Warn,

        /// <summary>
        /// Reject the document with an error.
        /// </summary>
        Error
    }

    /// <summary>
    /// Configuration for document validation on a collection.
    /// </summary>
    public class CollectionValidationConfig
    {
        /// <summary>
        /// Gets or sets the collection name.
        /// </summary>
        public required string CollectionName { get; set; }

        /// <summary>
        /// Gets or sets the JSON schema for validation.
        /// </summary>
        public JsonElement? Schema { get; set; }

        /// <summary>
        /// Gets or sets the validation level.
        /// </summary>
        public ValidationLevel Level { get; set; } = ValidationLevel.Strict;

        /// <summary>
        /// Gets or sets the validation action.
        /// </summary>
        public ValidationAction Action { get; set; } = ValidationAction.Error;

        /// <summary>
        /// Gets or sets whether to allow additional properties not defined in the schema.
        /// </summary>
        public bool AllowAdditionalProperties { get; set; } = true;

        /// <summary>
        /// Gets or sets when the configuration was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets when the configuration was last modified.
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Interface for document validation services.
    /// </summary>
    public interface IDocumentValidator
    {
        /// <summary>
        /// Validates a document against the schema for its collection.
        /// </summary>
        /// <param name="document">The document to validate.</param>
        /// <param name="collectionName">The name of the collection.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A validation result indicating success or failure with error details.</returns>
        Task<ValidationResult> ValidateAsync(Document document, string collectionName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates a document against a specific JSON schema.
        /// </summary>
        /// <param name="document">The document to validate.</param>
        /// <param name="schema">The JSON schema to validate against.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A validation result indicating success or failure with error details.</returns>
        Task<ValidationResult> ValidateAsync(Document document, JsonElement schema, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets or updates the validation configuration for a collection.
        /// </summary>
        /// <param name="config">The validation configuration.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SetValidationConfigAsync(CollectionValidationConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the validation configuration for a collection.
        /// </summary>
        /// <param name="collectionName">The name of the collection.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The validation configuration, or null if not set.</returns>
        Task<CollectionValidationConfig?> GetValidationConfigAsync(string collectionName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes validation configuration for a collection.
        /// </summary>
        /// <param name="collectionName">The name of the collection.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if configuration was removed, false if it didn't exist.</returns>
        Task<bool> RemoveValidationConfigAsync(string collectionName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists all collections that have validation configured.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A list of collection names with validation.</returns>
        Task<IReadOnlyList<string>> GetCollectionsWithValidationAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Exception thrown when document validation fails.
    /// </summary>
    public class DocumentValidationException : Exception
    {
        /// <summary>
        /// Gets the collection name.
        /// </summary>
        public string CollectionName { get; }

        /// <summary>
        /// Gets the document ID.
        /// </summary>
        public string DocumentId { get; }

        /// <summary>
        /// Gets the validation errors.
        /// </summary>
        public IReadOnlyList<ValidationError> ValidationErrors { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentValidationException"/> class.
        /// </summary>
        public DocumentValidationException(string collectionName, string documentId, IReadOnlyList<ValidationError> errors)
            : base($"Document '{documentId}' in collection '{collectionName}' failed validation with {errors.Count} error(s).")
        {
            CollectionName = collectionName;
            DocumentId = documentId;
            ValidationErrors = errors;
        }
    }
}
