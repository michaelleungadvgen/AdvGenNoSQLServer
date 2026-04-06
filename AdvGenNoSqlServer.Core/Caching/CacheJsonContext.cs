// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using System.Text.Json.Serialization;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Caching;

[JsonSerializable(typeof(Document))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
internal partial class CacheJsonContext : JsonSerializerContext
{
}
