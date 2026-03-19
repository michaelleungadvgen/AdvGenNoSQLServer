// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Validation;
using System.Text.Json;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for document validation functionality.
    /// </summary>
    public class DocumentValidationTests
    {
        private readonly DocumentValidator _validator;

        public DocumentValidationTests()
        {
            _validator = new DocumentValidator();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_Default_CreatesInstance()
        {
            var validator = new DocumentValidator();
            Assert.NotNull(validator);
        }

        #endregion

        #region ValidationConfig Management Tests

        [Fact]
        public async Task SetValidationConfigAsync_ValidConfig_SetsConfig()
        {
            var config = new CollectionValidationConfig
            {
                CollectionName = "users",
                Level = ValidationLevel.Strict
            };

            await _validator.SetValidationConfigAsync(config);

            var retrieved = await _validator.GetValidationConfigAsync("users");
            Assert.NotNull(retrieved);
            Assert.Equal("users", retrieved.CollectionName);
            Assert.Equal(ValidationLevel.Strict, retrieved.Level);
        }

        [Fact]
        public async Task SetValidationConfigAsync_NullConfig_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _validator.SetValidationConfigAsync(null!));
        }

        [Fact]
        public async Task SetValidationConfigAsync_EmptyCollectionName_ThrowsArgumentException()
        {
            var config = new CollectionValidationConfig
            {
                CollectionName = "",
                Level = ValidationLevel.Strict
            };

            await Assert.ThrowsAsync<ArgumentException>(() => _validator.SetValidationConfigAsync(config));
        }

        [Fact]
        public async Task GetValidationConfigAsync_NonExistentCollection_ReturnsNull()
        {
            var config = await _validator.GetValidationConfigAsync("nonexistent");
            Assert.Null(config);
        }

        [Fact]
        public async Task RemoveValidationConfigAsync_ExistingConfig_RemovesAndReturnsTrue()
        {
            var config = new CollectionValidationConfig
            {
                CollectionName = "users",
                Level = ValidationLevel.Strict
            };
            await _validator.SetValidationConfigAsync(config);

            var removed = await _validator.RemoveValidationConfigAsync("users");

            Assert.True(removed);
            var retrieved = await _validator.GetValidationConfigAsync("users");
            Assert.Null(retrieved);
        }

        [Fact]
        public async Task RemoveValidationConfigAsync_NonExistentConfig_ReturnsFalse()
        {
            var removed = await _validator.RemoveValidationConfigAsync("nonexistent");
            Assert.False(removed);
        }

        [Fact]
        public async Task GetCollectionsWithValidationAsync_WithConfigs_ReturnsCollectionNames()
        {
            await _validator.SetValidationConfigAsync(new CollectionValidationConfig { CollectionName = "users" });
            await _validator.SetValidationConfigAsync(new CollectionValidationConfig { CollectionName = "products" });

            var collections = await _validator.GetCollectionsWithValidationAsync();

            Assert.Contains("users", collections);
            Assert.Contains("products", collections);
            Assert.Equal(2, collections.Count);
        }

        [Fact]
        public async Task GetCollectionsWithValidationAsync_NoConfigs_ReturnsEmptyList()
        {
            var collections = await _validator.GetCollectionsWithValidationAsync();
            Assert.Empty(collections);
        }

        #endregion

        #region Required Field Tests

        [Fact]
        public async Task ValidateAsync_RequiredFieldPresent_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""required"": [""name""]
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["name"] = "John" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_RequiredFieldMissing_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""required"": [""name""]
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["age"] = 30 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
            Assert.Equal("REQUIRED_FIELD", result.Errors[0].ErrorCode);
            Assert.Contains("name", result.Errors[0].Path);
        }

        [Fact]
        public async Task ValidateAsync_RequiredFieldIsNull_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""required"": [""name""]
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["name"] = null });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("REQUIRED_FIELD", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_MultipleRequiredFields_OneMissing_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""required"": [""name"", ""email"", ""age""]
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["name"] = "John", ["age"] = 30 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
            Assert.Contains("email", result.Errors[0].Path);
        }

        #endregion

        #region String Validation Tests

        [Fact]
        public async Task ValidateAsync_StringMinLength_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""minLength"": 3 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["name"] = "hello" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_StringMinLength_TooShort_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""minLength"": 5 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["name"] = "hi" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("MIN_LENGTH", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_StringMaxLength_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""maxLength"": 10 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["name"] = "hello" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_StringMaxLength_TooLong_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""maxLength"": 5 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["name"] = "hello world" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("MAX_LENGTH", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_StringPattern_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""code"": { ""type"": ""string"", ""pattern"": ""^[a-z]+$"" }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["code"] = "hello" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_StringPattern_Invalid_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""code"": { ""type"": ""string"", ""pattern"": ""^[a-z]+$"" }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["code"] = "Hello123" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("PATTERN", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_StringFormatEmail_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""email"": { ""type"": ""string"", ""format"": ""email"" }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["email"] = "test@example.com" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_StringFormatEmail_Invalid_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""email"": { ""type"": ""string"", ""format"": ""email"" }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["email"] = "not-an-email" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("FORMAT", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_StringFormatUuid_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""id"": { ""type"": ""string"", ""format"": ""uuid"" }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["id"] = "550e8400-e29b-41d4-a716-446655440000" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_StringFormatIpv4_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""ip"": { ""type"": ""string"", ""format"": ""ipv4"" }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["ip"] = "192.168.1.1" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        #endregion

        #region Number Validation Tests

        [Fact]
        public async Task ValidateAsync_NumberMinimum_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""count"": { ""type"": ""number"", ""minimum"": 0 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["count"] = 10 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_NumberMinimum_Below_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""count"": { ""type"": ""number"", ""minimum"": 0 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["count"] = -5 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("MINIMUM", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_NumberMaximum_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""score"": { ""type"": ""number"", ""maximum"": 100 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["score"] = 50 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_NumberMaximum_Above_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""score"": { ""type"": ""number"", ""maximum"": 100 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["score"] = 150 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("MAXIMUM", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_NumberExclusiveMinimum_Equal_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""count"": { ""type"": ""number"", ""exclusiveMinimum"": 0 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["count"] = 0 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("EXCLUSIVE_MINIMUM", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_IntegerType_WithDouble_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""count"": { ""type"": ""integer"" }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["count"] = 3.14 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("TYPE_MISMATCH", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_IntegerType_WithInteger_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""count"": { ""type"": ""integer"" }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["count"] = 42 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        #endregion

        #region Array Validation Tests

        [Fact]
        public async Task ValidateAsync_ArrayMinItems_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""items"": { ""type"": ""array"", ""minItems"": 2 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["items"] = new[] { 1, 2, 3 } });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_ArrayMinItems_TooFew_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""items"": { ""type"": ""array"", ""minItems"": 3 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["items"] = new[] { 1, 2 } });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("MIN_ITEMS", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_ArrayMaxItems_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""items"": { ""type"": ""array"", ""maxItems"": 5 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["items"] = new[] { 1, 2, 3 } });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_ArrayMaxItems_TooMany_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""items"": { ""type"": ""array"", ""maxItems"": 2 }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["items"] = new[] { 1, 2, 3 } });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("MAX_ITEMS", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_ArrayUniqueItems_Duplicates_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""items"": { ""type"": ""array"", ""uniqueItems"": true }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["items"] = new[] { 1, 2, 1 } });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("UNIQUE_ITEMS", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_ArrayUniqueItems_Unique_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""items"": { ""type"": ""array"", ""uniqueItems"": true }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["items"] = new[] { 1, 2, 3 } });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_ArrayItemsSchema_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""tags"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" }
                    }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["tags"] = new[] { "a", "b", "c" } });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_ArrayItemsSchema_Invalid_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""tags"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" }
                    }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["tags"] = new object[] { "a", 123, "c" } });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Path.Contains("tags[1]"));
        }

        #endregion

        #region Object Validation Tests

        [Fact]
        public async Task ValidateAsync_ObjectMinProperties_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""minProperties"": 2
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2, ["c"] = 3 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_ObjectMinProperties_TooFew_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""minProperties"": 3
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("MIN_PROPERTIES", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_ObjectMaxProperties_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""maxProperties"": 5
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_ObjectMaxProperties_TooMany_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""maxProperties"": 2
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2, ["c"] = 3 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("MAX_PROPERTIES", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_ObjectAdditionalPropertiesFalse_Extra_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"" }
                },
                ""additionalProperties"": false
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["name"] = "John", ["extra"] = 123 });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("ADDITIONAL_PROPERTY", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_ObjectAdditionalPropertiesFalse_NoExtra_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"" }
                },
                ""additionalProperties"": false
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["name"] = "John" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        #endregion

        #region Enum and Const Tests

        [Fact]
        public async Task ValidateAsync_Enum_ValidValue_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""color"": { ""enum"": [""red"", ""green"", ""blue""] }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["color"] = "green" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_Enum_InvalidValue_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""color"": { ""enum"": [""red"", ""green"", ""blue""] }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["color"] = "yellow" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("ENUM", result.Errors[0].ErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_Const_Matching_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""type"": { ""const"": ""user"" }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["type"] = "user" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_Const_NotMatching_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""type"": { ""const"": ""user"" }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> { ["type"] = "admin" });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Equal("CONST", result.Errors[0].ErrorCode);
        }

        #endregion

        #region Nested Object Tests

        [Fact]
        public async Task ValidateAsync_NestedObject_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""user"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""name"": { ""type"": ""string"" },
                            ""age"": { ""type"": ""integer"" }
                        },
                        ""required"": [""name""]
                    }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> 
            { 
                ["user"] = new Dictionary<string, object?> { ["name"] = "John", ["age"] = 30 } 
            });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_NestedObject_InvalidNestedField_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""user"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""name"": { ""type"": ""string"" }
                        },
                        ""required"": [""name""]
                    }
                }
            }").RootElement;
            var document = CreateDocument(new Dictionary<string, object?> 
            { 
                ["user"] = new Dictionary<string, object?> { ["age"] = 30 } 
            });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Path.Contains("user.name"));
        }

        #endregion

        #region Collection Validation Tests

        [Fact]
        public async Task ValidateAsync_WithCollectionConfig_ValidationLevelNone_Passes()
        {
            var config = new CollectionValidationConfig
            {
                CollectionName = "users",
                Level = ValidationLevel.None,
                Schema = JsonDocument.Parse(@"{ ""type"": ""string"" }").RootElement
            };
            await _validator.SetValidationConfigAsync(config);

            var document = CreateDocument(new Dictionary<string, object?> { ["any"] = "data" });
            var result = await _validator.ValidateAsync(document, "users");

            Assert.True(result.IsValid); // Validation skipped due to Level=None
        }

        [Fact]
        public async Task ValidateAsync_WithCollectionConfig_NoSchema_Passes()
        {
            var config = new CollectionValidationConfig
            {
                CollectionName = "users",
                Level = ValidationLevel.Strict
                // No schema set
            };
            await _validator.SetValidationConfigAsync(config);

            var document = CreateDocument(new Dictionary<string, object?> { ["any"] = "data" });
            var result = await _validator.ValidateAsync(document, "users");

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_WithCollectionConfig_WithSchema_Validates()
        {
            var config = new CollectionValidationConfig
            {
                CollectionName = "users",
                Level = ValidationLevel.Strict,
                Schema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""required"": [""email""],
                    ""properties"": {
                        ""email"": { ""type"": ""string"", ""format"": ""email"" }
                    }
                }").RootElement
            };
            await _validator.SetValidationConfigAsync(config);

            var document = CreateDocument(new Dictionary<string, object?> { ["email"] = "invalid-email" });
            var result = await _validator.ValidateAsync(document, "users");

            Assert.False(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_NoCollectionConfig_Passes()
        {
            var document = CreateDocument(new Dictionary<string, object?> { ["any"] = "data" });
            var result = await _validator.ValidateAsync(document, "uncategorized");

            Assert.True(result.IsValid);
        }

        #endregion

        #region ValidationError Factory Tests

        [Fact]
        public void ValidationError_RequiredField_CreatesCorrectError()
        {
            var error = ValidationError.RequiredField("name");

            Assert.Equal("name", error.Path);
            Assert.Equal("REQUIRED_FIELD", error.ErrorCode);
            Assert.Contains("name", error.Message);
        }

        [Fact]
        public void ValidationError_TypeMismatch_CreatesCorrectError()
        {
            var error = ValidationError.TypeMismatch("age", "integer", "string");

            Assert.Equal("age", error.Path);
            Assert.Equal("TYPE_MISMATCH", error.ErrorCode);
            Assert.NotNull(error.Context);
            Assert.Equal("integer", error.Context!["expectedType"]);
            Assert.Equal("string", error.Context["actualType"]);
        }

        [Fact]
        public void ValidationError_MinLength_CreatesCorrectError()
        {
            var error = ValidationError.MinLength("password", 8, 5);

            Assert.Equal("password", error.Path);
            Assert.Equal("MIN_LENGTH", error.ErrorCode);
            Assert.NotNull(error.Context);
            Assert.Equal(8, error.Context!["minLength"]);
            Assert.Equal(5, error.Context["actualLength"]);
        }

        [Fact]
        public void ValidationError_MaxLength_CreatesCorrectError()
        {
            var error = ValidationError.MaxLength("username", 20, 25);

            Assert.Equal("username", error.Path);
            Assert.Equal("MAX_LENGTH", error.ErrorCode);
            Assert.NotNull(error.Context);
            Assert.Equal(20, error.Context!["maxLength"]);
            Assert.Equal(25, error.Context["actualLength"]);
        }

        [Fact]
        public void ValidationError_Minimum_CreatesCorrectError()
        {
            var error = ValidationError.Minimum("age", 18, 15);

            Assert.Equal("age", error.Path);
            Assert.Equal("MINIMUM", error.ErrorCode);
            Assert.NotNull(error.Context);
            Assert.Equal(18.0, error.Context!["minimum"]);
            Assert.Equal(15.0, error.Context["actual"]);
        }

        [Fact]
        public void ValidationError_Maximum_CreatesCorrectError()
        {
            var error = ValidationError.Maximum("score", 100, 150);

            Assert.Equal("score", error.Path);
            Assert.Equal("MAXIMUM", error.ErrorCode);
            Assert.NotNull(error.Context);
            Assert.Equal(100.0, error.Context!["maximum"]);
            Assert.Equal(150.0, error.Context["actual"]);
        }

        [Fact]
        public void ValidationError_Pattern_CreatesCorrectError()
        {
            var error = ValidationError.Pattern("phone", @"^\d{10}$", "123");

            Assert.Equal("phone", error.Path);
            Assert.Equal("PATTERN", error.ErrorCode);
            Assert.NotNull(error.Context);
            Assert.Equal(@"^\d{10}$", error.Context!["pattern"]);
        }

        [Fact]
        public void ValidationError_Enum_CreatesCorrectError()
        {
            var error = ValidationError.Enum("status", new[] { "active", "inactive" }, "pending");

            Assert.Equal("status", error.Path);
            Assert.Equal("ENUM", error.ErrorCode);
            Assert.NotNull(error.Context);
        }

        [Fact]
        public void ValidationError_Custom_CreatesCorrectError()
        {
            var context = new Dictionary<string, object> { ["custom"] = "value" };
            var error = ValidationError.Custom("field", "CUSTOM_ERROR", "Custom message", context);

            Assert.Equal("field", error.Path);
            Assert.Equal("CUSTOM_ERROR", error.ErrorCode);
            Assert.Equal("Custom message", error.Message);
            Assert.Equal(context, error.Context);
        }

        #endregion

        #region ValidationResult Tests

        [Fact]
        public void ValidationResult_Success_IsValidTrue()
        {
            var result = ValidationResult.Success();

            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidationResult_Success_WithMetadata()
        {
            var metadata = new Dictionary<string, object> { ["key"] = "value" };
            var result = ValidationResult.Success(metadata);

            Assert.Equal(metadata, result.Metadata);
        }

        [Fact]
        public void ValidationResult_Failure_WithErrors_IsValidFalse()
        {
            var errors = new[] { ValidationError.RequiredField("name") };
            var result = ValidationResult.Failure(errors);

            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
        }

        [Fact]
        public void ValidationResult_Failure_WithSingleError()
        {
            var error = ValidationError.RequiredField("name");
            var result = ValidationResult.Failure(error);

            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
            Assert.Equal("name", result.Errors[0].Path);
        }

        #endregion

        #region DocumentValidationException Tests

        [Fact]
        public void DocumentValidationException_Constructor_SetsProperties()
        {
            var errors = new[] { ValidationError.RequiredField("name") };
            var ex = new DocumentValidationException("users", "doc123", errors);

            Assert.Equal("users", ex.CollectionName);
            Assert.Equal("doc123", ex.DocumentId);
            Assert.Equal(errors, ex.ValidationErrors);
            Assert.Contains("users", ex.Message);
            Assert.Contains("doc123", ex.Message);
        }

        #endregion

        #region Complex Schema Tests

        [Fact]
        public async Task ValidateAsync_ComplexUserSchema_Valid_Passes()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""required"": [""name"", ""email""],
                ""properties"": {
                    ""name"": {
                        ""type"": ""string"",
                        ""minLength"": 1,
                        ""maxLength"": 100
                    },
                    ""email"": {
                        ""type"": ""string"",
                        ""format"": ""email""
                    },
                    ""age"": {
                        ""type"": ""integer"",
                        ""minimum"": 0,
                        ""maximum"": 150
                    },
                    ""roles"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" }
                    }
                }
            }").RootElement;

            var document = CreateDocument(new Dictionary<string, object?>
            {
                ["name"] = "John Doe",
                ["email"] = "john@example.com",
                ["age"] = 30,
                ["roles"] = new[] { "user", "admin" }
            });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_ComplexUserSchema_MultipleErrors_Fails()
        {
            var schema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""required"": [""name"", ""email""],
                ""properties"": {
                    ""name"": {
                        ""type"": ""string"",
                        ""minLength"": 1,
                        ""maxLength"": 100
                    },
                    ""email"": {
                        ""type"": ""string"",
                        ""format"": ""email""
                    },
                    ""age"": {
                        ""type"": ""integer"",
                        ""minimum"": 0,
                        ""maximum"": 150
                    }
                }
            }").RootElement;

            var document = CreateDocument(new Dictionary<string, object?>
            {
                ["email"] = "invalid-email",
                ["age"] = 200
            });

            var result = await _validator.ValidateAsync(document, schema);

            Assert.False(result.IsValid);
            Assert.True(result.Errors.Count >= 2);
        }

        #endregion

        #region Helper Methods

        private static Document CreateDocument(Dictionary<string, object?> data)
        {
            return new Document
            {
                Id = Guid.NewGuid().ToString(),
                Data = data,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };
        }

        #endregion
    }
}
