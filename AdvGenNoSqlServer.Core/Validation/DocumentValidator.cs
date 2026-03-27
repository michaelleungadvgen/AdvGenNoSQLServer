// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AdvGenNoSqlServer.Core.Validation
{
    /// <summary>
    /// Implementation of document validation using JSON Schema-like validation.
    /// </summary>
    public class DocumentValidator : IDocumentValidator
    {
        private readonly ConcurrentDictionary<string, CollectionValidationConfig> _validationConfigs = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentValidator"/> class.
        /// </summary>
        public DocumentValidator()
        {
        }

        /// <inheritdoc />
        public Task<ValidationResult> ValidateAsync(Document document, string collectionName, CancellationToken cancellationToken = default)
        {
            if (_validationConfigs.TryGetValue(collectionName, out var config))
            {
                if (config.Level == ValidationLevel.None)
                {
                    return Task.FromResult(ValidationResult.Success());
                }

                if (config.Schema.HasValue)
                {
                    return ValidateAsync(document, config.Schema.Value, cancellationToken);
                }
            }

            return Task.FromResult(ValidationResult.Success());
        }

        /// <inheritdoc />
        public Task<ValidationResult> ValidateAsync(Document document, JsonElement schema, CancellationToken cancellationToken = default)
        {
            var errors = new List<ValidationError>();
            var data = document.Data;

            if (data != null)
            {
                ValidateObject(data, schema, "", errors);
            }

            var result = errors.Count == 0
                ? ValidationResult.Success()
                : ValidationResult.Failure(errors);

            return Task.FromResult(result);
        }

        /// <summary>
        /// Validates an object (dictionary) against a schema.
        /// </summary>
        private void ValidateObject(Dictionary<string, object?> data, JsonElement schema, string path, List<ValidationError> errors)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // Check type if specified
            if (schema.TryGetProperty("type", out var typeProperty))
            {
                var expectedType = typeProperty.GetString();
                if (expectedType != null && expectedType != "object")
                {
                    errors.Add(ValidationError.TypeMismatch(path, expectedType, "object"));
                    return;
                }
            }

            // Validate required properties
            if (schema.TryGetProperty("required", out var requiredProperty) && 
                requiredProperty.ValueKind == JsonValueKind.Array)
            {
                foreach (var req in requiredProperty.EnumerateArray())
                {
                    var reqName = req.GetString();
                    if (!string.IsNullOrEmpty(reqName))
                    {
                        var propertyPath = string.IsNullOrEmpty(path) ? reqName : $"{path}.{reqName}";
                        if (!data.ContainsKey(reqName) || data[reqName] == null)
                        {
                            errors.Add(ValidationError.RequiredField(propertyPath));
                        }
                    }
                }
            }

            // Validate properties
            if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in properties.EnumerateObject())
                {
                    var propertyName = property.Name;
                    var propertyPath = string.IsNullOrEmpty(path) ? propertyName : $"{path}.{propertyName}";

                    if (data.TryGetValue(propertyName, out var propertyValue) && propertyValue != null)
                    {
                        ValidateValue(propertyValue, property.Value, propertyPath, errors);
                    }
                }
            }

            // Validate additionalProperties
            if (schema.TryGetProperty("additionalProperties", out var additionalProps))
            {
                if (additionalProps.ValueKind == JsonValueKind.False)
                {
                    // Check for properties not in schema
                    if (schema.TryGetProperty("properties", out var definedProps))
                    {
                        var allowedProps = new HashSet<string>();
                        foreach (var prop in definedProps.EnumerateObject())
                        {
                            allowedProps.Add(prop.Name);
                        }

                        foreach (var actualProp in data.Keys)
                        {
                            if (!allowedProps.Contains(actualProp))
                            {
                                var propPath = string.IsNullOrEmpty(path) ? actualProp : $"{path}.{actualProp}";
                                errors.Add(ValidationError.Custom(propPath, "ADDITIONAL_PROPERTY", 
                                    $"Additional property '{actualProp}' is not allowed."));
                            }
                        }
                    }
                }
            }

            // Validate minProperties
            if (schema.TryGetProperty("minProperties", out var minProps) && 
                minProps.TryGetInt32(out var minProperties))
            {
                var actualCount = data.Count;
                if (actualCount < minProperties)
                {
                    errors.Add(ValidationError.Custom(path, "MIN_PROPERTIES",
                        $"Object must have at least {minProperties} properties but has {actualCount}.",
                        new Dictionary<string, object> { ["minProperties"] = minProperties, ["actual"] = actualCount }));
                }
            }

            // Validate maxProperties
            if (schema.TryGetProperty("maxProperties", out var maxProps) && 
                maxProps.TryGetInt32(out var maxProperties))
            {
                var actualCount = data.Count;
                if (actualCount > maxProperties)
                {
                    errors.Add(ValidationError.Custom(path, "MAX_PROPERTIES",
                        $"Object must have at most {maxProperties} properties but has {actualCount}.",
                        new Dictionary<string, object> { ["maxProperties"] = maxProperties, ["actual"] = actualCount }));
                }
            }
        }

        /// <summary>
        /// Validates a value against a schema.
        /// </summary>
        private void ValidateValue(object? value, JsonElement schema, string path, List<ValidationError> errors)
        {
            if (value == null)
            {
                // Check if null is allowed (type: "null")
                if (schema.TryGetProperty("type", out var typeProp))
                {
                    var expectedType = typeProp.GetString();
                    if (expectedType != "null")
                    {
                        errors.Add(ValidationError.TypeMismatch(path, expectedType ?? "unknown", "null"));
                    }
                }
                return;
            }

            // Validate based on value type
            switch (value)
            {
                case string str:
                    ValidateString(str, schema, path, errors);
                    break;
                case int i:
                    ValidateNumber((double)i, schema, path, errors, true);
                    break;
                case long l:
                    ValidateNumber((double)l, schema, path, errors, true);
                    break;
                case float f:
                    ValidateNumber((double)f, schema, path, errors, false);
                    break;
                case double d:
                    ValidateNumber(d, schema, path, errors, false);
                    break;
                case decimal dec:
                    ValidateNumber((double)dec, schema, path, errors, false);
                    break;
                case bool b:
                    ValidateBoolean(b, schema, path, errors);
                    break;
                case Dictionary<string, object?> dict:
                    ValidateObject(dict, schema, path, errors);
                    break;
                case System.Collections.IEnumerable enumerable when value is not string:
                    ValidateArray(enumerable, schema, path, errors);
                    break;
                case JsonElement jsonElem:
                    ValidateJsonElement(jsonElem, schema, path, errors);
                    break;
                default:
                    // For other types, check if they match the expected type
                    if (schema.TryGetProperty("type", out var typeProp2))
                    {
                        var expected = typeProp2.GetString();
                        var actual = value.GetType().Name.ToLowerInvariant();
                        if (expected != null && expected != actual && !(expected == "integer" && IsIntegerType(value)))
                        {
                            errors.Add(ValidationError.TypeMismatch(path, expected, actual));
                        }
                    }
                    break;
            }

            // Validate enum
            if (schema.TryGetProperty("enum", out var enumProperty) && enumProperty.ValueKind == JsonValueKind.Array)
            {
                ValidateEnum(value, enumProperty, path, errors);
            }

            // Validate const
            if (schema.TryGetProperty("const", out var constProperty))
            {
                ValidateConst(value, constProperty, path, errors);
            }
        }

        /// <summary>
        /// Validates a string value against schema constraints.
        /// </summary>
        private void ValidateString(string value, JsonElement schema, string path, List<ValidationError> errors)
        {
            // Check type
            if (schema.TryGetProperty("type", out var typeProp))
            {
                var expectedType = typeProp.GetString();
                if (expectedType != null && expectedType != "string")
                {
                    if (expectedType == "integer" && int.TryParse(value, out _))
                    {
                        // String represents an integer - valid for integer type
                        return;
                    }
                    errors.Add(ValidationError.TypeMismatch(path, expectedType, "string"));
                    return;
                }
            }

            // Validate minLength
            if (schema.TryGetProperty("minLength", out var minLengthProp) && 
                minLengthProp.TryGetInt32(out var minLength))
            {
                if (value.Length < minLength)
                {
                    errors.Add(ValidationError.MinLength(path, minLength, value.Length));
                }
            }

            // Validate maxLength
            if (schema.TryGetProperty("maxLength", out var maxLengthProp) && 
                maxLengthProp.TryGetInt32(out var maxLength))
            {
                if (value.Length > maxLength)
                {
                    errors.Add(ValidationError.MaxLength(path, maxLength, value.Length));
                }
            }

            // Validate pattern
            if (schema.TryGetProperty("pattern", out var patternProp))
            {
                var pattern = patternProp.GetString();
                if (!string.IsNullOrEmpty(pattern))
                {
                    try
                    {
                        if (!Regex.IsMatch(value, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                        {
                            errors.Add(ValidationError.Pattern(path, pattern, value));
                        }
                    }
                    catch (RegexParseException)
                    {
                        // Invalid regex pattern - skip validation
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        // Pattern matching timed out - treat as validation failure for safety
                        errors.Add(ValidationError.Custom(path, "PATTERN_TIMEOUT",
                            $"Pattern evaluation timed out for pattern '{pattern}'.",
                            new Dictionary<string, object> { ["pattern"] = pattern, ["value"] = value }));
                    }
                }
            }

            // Validate format
            if (schema.TryGetProperty("format", out var formatProp))
            {
                var format = formatProp.GetString();
                if (!string.IsNullOrEmpty(format))
                {
                    ValidateFormat(value, format, path, errors);
                }
            }
        }

        /// <summary>
        /// Validates a numeric value against schema constraints.
        /// </summary>
        private void ValidateNumber(double value, JsonElement schema, string path, List<ValidationError> errors, bool isInteger)
        {
            // Check type
            if (schema.TryGetProperty("type", out var typeProp))
            {
                var expectedType = typeProp.GetString();
                if (expectedType == "integer" && !isInteger)
                {
                    errors.Add(ValidationError.TypeMismatch(path, "integer", "number"));
                    return;
                }
                if (expectedType != null && expectedType != "number" && expectedType != "integer")
                {
                    errors.Add(ValidationError.TypeMismatch(path, expectedType, isInteger ? "integer" : "number"));
                    return;
                }
            }

            // Validate minimum
            if (schema.TryGetProperty("minimum", out var minProp) && minProp.TryGetDouble(out var minimum))
            {
                if (value < minimum)
                {
                    errors.Add(ValidationError.Minimum(path, minimum, value));
                }
            }

            // Validate maximum
            if (schema.TryGetProperty("maximum", out var maxProp) && maxProp.TryGetDouble(out var maximum))
            {
                if (value > maximum)
                {
                    errors.Add(ValidationError.Maximum(path, maximum, value));
                }
            }

            // Validate exclusiveMinimum
            if (schema.TryGetProperty("exclusiveMinimum", out var exMinProp) && exMinProp.TryGetDouble(out var exclusiveMin))
            {
                if (value <= exclusiveMin)
                {
                    errors.Add(ValidationError.Custom(path, "EXCLUSIVE_MINIMUM",
                        $"Value must be greater than {exclusiveMin} but was {value}.",
                        new Dictionary<string, object> { ["exclusiveMinimum"] = exclusiveMin, ["actual"] = value }));
                }
            }

            // Validate exclusiveMaximum
            if (schema.TryGetProperty("exclusiveMaximum", out var exMaxProp) && exMaxProp.TryGetDouble(out var exclusiveMax))
            {
                if (value >= exclusiveMax)
                {
                    errors.Add(ValidationError.Custom(path, "EXCLUSIVE_MAXIMUM",
                        $"Value must be less than {exclusiveMax} but was {value}.",
                        new Dictionary<string, object> { ["exclusiveMaximum"] = exclusiveMax, ["actual"] = value }));
                }
            }

            // Validate multipleOf
            if (schema.TryGetProperty("multipleOf", out var multOfProp) && multOfProp.TryGetDouble(out var multipleOf))
            {
                if (multipleOf != 0)
                {
                    var remainder = value % multipleOf;
                    if (Math.Abs(remainder) > 0.0001) // Allow small floating point errors
                    {
                        errors.Add(ValidationError.Custom(path, "MULTIPLE_OF",
                            $"Value must be a multiple of {multipleOf} but was {value}.",
                            new Dictionary<string, object> { ["multipleOf"] = multipleOf, ["actual"] = value }));
                    }
                }
            }
        }

        /// <summary>
        /// Validates a boolean value against schema.
        /// </summary>
        private void ValidateBoolean(bool value, JsonElement schema, string path, List<ValidationError> errors)
        {
            if (schema.TryGetProperty("type", out var typeProp))
            {
                var expectedType = typeProp.GetString();
                if (expectedType != null && expectedType != "boolean")
                {
                    errors.Add(ValidationError.TypeMismatch(path, expectedType, "boolean"));
                }
            }
        }

        /// <summary>
        /// Validates an array value against schema constraints.
        /// </summary>
        private void ValidateArray(System.Collections.IEnumerable items, JsonElement schema, string path, List<ValidationError> errors)
        {
            // Check type
            if (schema.TryGetProperty("type", out var typeProp))
            {
                var expectedType = typeProp.GetString();
                if (expectedType != null && expectedType != "array")
                {
                    errors.Add(ValidationError.TypeMismatch(path, expectedType, "array"));
                    return;
                }
            }

            var itemList = items.Cast<object?>().ToList();

            // Validate items schema
            if (schema.TryGetProperty("items", out var itemsSchema))
            {
                for (int i = 0; i < itemList.Count; i++)
                {
                    var itemPath = $"{path}[{i}]";
                    ValidateValue(itemList[i], itemsSchema, itemPath, errors);
                }
            }

            // Validate minItems
            if (schema.TryGetProperty("minItems", out var minItemsProp) && 
                minItemsProp.TryGetInt32(out var minItems))
            {
                if (itemList.Count < minItems)
                {
                    errors.Add(ValidationError.Custom(path, "MIN_ITEMS",
                        $"Array must have at least {minItems} items but has {itemList.Count}.",
                        new Dictionary<string, object> { ["minItems"] = minItems, ["actual"] = itemList.Count }));
                }
            }

            // Validate maxItems
            if (schema.TryGetProperty("maxItems", out var maxItemsProp) && 
                maxItemsProp.TryGetInt32(out var maxItems))
            {
                if (itemList.Count > maxItems)
                {
                    errors.Add(ValidationError.Custom(path, "MAX_ITEMS",
                        $"Array must have at most {maxItems} items but has {itemList.Count}.",
                        new Dictionary<string, object> { ["maxItems"] = maxItems, ["actual"] = itemList.Count }));
                }
            }

            // Validate uniqueItems
            if (schema.TryGetProperty("uniqueItems", out var uniqueItemsProp) && 
                uniqueItemsProp.ValueKind == JsonValueKind.True)
            {
                var seen = new HashSet<string>();
                for (int i = 0; i < itemList.Count; i++)
                {
                    var itemJson = JsonSerializer.Serialize(itemList[i]);
                    if (!seen.Add(itemJson))
                    {
                        errors.Add(ValidationError.Custom($"{path}[{i}]", "UNIQUE_ITEMS",
                            $"Array items must be unique. Duplicate found at index {i}."));
                    }
                }
            }
        }

        /// <summary>
        /// Validates a JsonElement value against schema.
        /// </summary>
        private void ValidateJsonElement(JsonElement element, JsonElement schema, string path, List<ValidationError> errors)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText());
                    if (dict != null)
                    {
                        ValidateObject(dict, schema, path, errors);
                    }
                    break;
                case JsonValueKind.Array:
                    var list = JsonSerializer.Deserialize<List<object?>>(element.GetRawText());
                    if (list != null)
                    {
                        ValidateArray(list, schema, path, errors);
                    }
                    break;
                case JsonValueKind.String:
                    ValidateString(element.GetString() ?? "", schema, path, errors);
                    break;
                case JsonValueKind.Number:
                    if (element.TryGetDouble(out var num))
                    {
                        ValidateNumber(num, schema, path, errors, false);
                    }
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    ValidateBoolean(element.GetBoolean(), schema, path, errors);
                    break;
                case JsonValueKind.Null:
                    ValidateValue(null, schema, path, errors);
                    break;
            }
        }

        /// <summary>
        /// Validates that a value matches an enum value.
        /// </summary>
        private void ValidateEnum(object? value, JsonElement enumValues, string path, List<ValidationError> errors)
        {
            var valueJson = JsonSerializer.Serialize(value);
            var allowedValues = enumValues.EnumerateArray().Select(e => e.GetRawText()).ToArray();

            if (!allowedValues.Contains(valueJson))
            {
                var stringValues = enumValues.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? e.GetRawText() : e.GetRawText())
                    .ToArray();

                errors.Add(ValidationError.Enum(path, stringValues!, valueJson.Trim('"')));
            }
        }

        /// <summary>
        /// Validates that a value matches a constant value.
        /// </summary>
        private void ValidateConst(object? value, JsonElement constValue, string path, List<ValidationError> errors)
        {
            var valueJson = JsonSerializer.Serialize(value);
            var constJson = constValue.GetRawText();

            if (valueJson != constJson)
            {
                errors.Add(ValidationError.Custom(path, "CONST",
                    $"Value must be {constJson} but was {valueJson}.",
                    new Dictionary<string, object> { ["expected"] = constJson, ["actual"] = valueJson }));
            }
        }

        /// <summary>
        /// Validates a string format.
        /// </summary>
        private void ValidateFormat(string value, string format, string path, List<ValidationError> errors)
        {
            var isValid = false;
            try
            {
                var timeout = TimeSpan.FromMilliseconds(100);
                isValid = format switch
                {
                    "email" => Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None, timeout),
                    "date" => DateTime.TryParseExact(value, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _),
                    "date-time" => DateTime.TryParse(value, out _),
                    "uri" => Uri.TryCreate(value, UriKind.Absolute, out _),
                    "uuid" => Guid.TryParseExact(value, "D", out _) || Guid.TryParse(value, out _),
                    "hostname" => Regex.IsMatch(value, @"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*$", RegexOptions.None, timeout),
                    "ipv4" => Regex.IsMatch(value, @"^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$", RegexOptions.None, timeout),
                    _ => true // Unknown formats are allowed
                };
            }
            catch (RegexMatchTimeoutException)
            {
                // Format evaluation timed out - treat as validation failure for safety
                errors.Add(ValidationError.Custom(path, "FORMAT_TIMEOUT",
                    $"Format evaluation timed out for format '{format}'.",
                    new Dictionary<string, object> { ["format"] = format, ["value"] = value }));
                return;
            }

            if (!isValid)
            {
                errors.Add(ValidationError.Custom(path, "FORMAT",
                    $"Value does not match format '{format}'.",
                    new Dictionary<string, object> { ["format"] = format, ["value"] = value }));
            }
        }

        /// <summary>
        /// Checks if a value is an integer type.
        /// </summary>
        private bool IsIntegerType(object value)
        {
            return value is int or long or short or byte or uint or ulong or ushort or sbyte;
        }

        /// <inheritdoc />
        public Task SetValidationConfigAsync(CollectionValidationConfig config, CancellationToken cancellationToken = default)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (string.IsNullOrEmpty(config.CollectionName))
            {
                throw new ArgumentException("Collection name cannot be null or empty.", nameof(config));
            }

            config.UpdatedAt = DateTime.UtcNow;
            _validationConfigs[config.CollectionName] = config;

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<CollectionValidationConfig?> GetValidationConfigAsync(string collectionName, CancellationToken cancellationToken = default)
        {
            _validationConfigs.TryGetValue(collectionName, out var config);
            return Task.FromResult(config);
        }

        /// <inheritdoc />
        public Task<bool> RemoveValidationConfigAsync(string collectionName, CancellationToken cancellationToken = default)
        {
            var removed = _validationConfigs.TryRemove(collectionName, out _);
            return Task.FromResult(removed);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> GetCollectionsWithValidationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(_validationConfigs.Keys.ToArray());
        }
    }
}
