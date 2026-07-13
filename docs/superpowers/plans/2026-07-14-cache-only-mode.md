# Cache-Only Mode + Redis-Style KV Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a whole-server `CacheOnly` (memory-only) storage mode and a Redis-style key-value cache API (GET/SET/DEL/EXPIRE/TTL/INCR/MGET/MSET/KEYS/SCAN/FLUSH/STATS) over a new binary TCP frame, per the approved spec `docs/superpowers/specs/2026-07-14-cache-only-mode-design.md`.

**Architecture:** Reuse the existing `Core/MemoryManagement` engines (Managed/Native/Mixed with TTL + LRU/LFU/TTL eviction) behind a new `CacheStore` facade in the Storage project. Cache ops travel as a dedicated binary `MessageType.CacheOperation` payload (raw bytes, no JSON) decoded by a new `CacheProtocol` in the Network project. `NoSqlServer` picks its document store from a new `StorageMode` config value (`Hybrid` = today's behavior, `CacheOnly` = the existing in-memory `DocumentStore`), and always hosts one `CacheStore`. The client library gains a `client.Cache.*` surface.

**Tech Stack:** .NET 9, xUnit + Moq (existing test project), BenchmarkDotNet (existing benchmarks project), `System.Buffers.Binary.BinaryPrimitives` big-endian encoding (matches existing `MessageProtocol`).

**Read the spec first:** `docs/superpowers/specs/2026-07-14-cache-only-mode-design.md`.

**Conventions used throughout:**
- All multi-byte integers are **big-endian** (matches existing `MessageProtocol`).
- Test command template: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~<TestClass>"` run from repo root `e:\Projects\AdvGenNoSQLServer`.
- Every file starts with the repo's standard header comment (`// Copyright (c) 2026 AdvanGeneration Pty. Ltd. ...`) — copy from a neighboring file.
- Follow @superpowers:test-driven-development: test first, watch it fail, minimal implementation, watch it pass, commit.

---

## File Structure Overview

| File | Action | Responsibility |
|---|---|---|
| `AdvGenNoSqlServer.Network/CacheProtocol.cs` | Create | Op/status enums, request/response structs, binary encode/decode |
| `AdvGenNoSqlServer.Network/MessageProtocol.cs` | Modify | Add `MessageType.CacheOperation = 0x0B`, `CacheResponse = 0x0C` |
| `AdvGenNoSqlServer.Core/MemoryManagement/EvictionManager.cs` | Modify | Expose per-key expiry + key snapshot |
| `AdvGenNoSqlServer.Core/MemoryManagement/IMemoryStorageEngine.cs` | Modify | Add `IIntrospectableMemoryStorageEngine` |
| `AdvGenNoSqlServer.Core/MemoryManagement/ManagedMemoryStorageEngine.cs` | Modify | Implement introspection |
| `AdvGenNoSqlServer.Core/MemoryManagement/NativeMemoryStorageEngine.cs` | Modify | Implement introspection |
| `AdvGenNoSqlServer.Core/MemoryManagement/MixedMemoryStorageEngine.cs` | Modify | Implement introspection (hot + cold tier) |
| `AdvGenNoSqlServer.Core/MemoryManagement/MemoryEngineFactory.cs` | Modify | Add non-DI `Create(...)` method |
| `AdvGenNoSqlServer.Core/MemoryManagement/MemoryManagementConfiguration.cs` | Modify | Add `MaxValueSizeMB` |
| `AdvGenNoSqlServer.Core/Configuration/ServerConfiguration.cs` | Modify | Add `StorageMode`, `MemoryManagement` |
| `AdvGenNoSqlServer.Storage/CacheStore.cs` | Create | Redis-semantics facade over the engine |
| `AdvGenNoSqlServer.Server/NoSqlServer.cs` | Modify | StorageMode selection, CacheStore lifetime, cache op handler |
| `AdvGenNoSqlServer.Server/appsettings.json` | Modify | Add `storageMode` |
| `AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Cache.cs` | Create | `client.Cache.*` API (nested class, binary frames) |
| `AdvGenNoSqlServer.Benchmarks/CacheStoreBenchmarks.cs` | Create | GET/SET/INCR throughput benchmarks |
| Tests: `CacheProtocolTests.cs`, `EngineIntrospectionTests.cs`, `CacheStoreTests.cs`, `CacheOnlyModeTests.cs`, `CacheOperationHandlerTests.cs`, `CacheClientTests.cs` | Create | Per-component coverage |

---

### Task 1: Binary cache protocol (Network project)

**Files:**
- Modify: `AdvGenNoSqlServer.Network/MessageProtocol.cs` (MessageType enum, ~line 60-66)
- Create: `AdvGenNoSqlServer.Network/CacheProtocol.cs`
- Test: `AdvGenNoSqlServer.Tests/CacheProtocolTests.cs`

- [ ] **Step 1.1: Write failing round-trip tests**

```csharp
// AdvGenNoSqlServer.Tests/CacheProtocolTests.cs
using AdvGenNoSqlServer.Network;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class CacheProtocolTests
{
    [Fact]
    public void RequestRoundTrip_SetWithTtlAndValue()
    {
        byte[] value = [1, 2, 3, 4];
        var payload = CacheProtocol.EncodeRequest(CacheOp.Set, "user:42", value, ttlSeconds: 300, flags: CacheRequestFlags.Nx);
        var req = CacheProtocol.DecodeRequest(payload, payload.Length);

        Assert.Equal(CacheOp.Set, req.Op);
        Assert.Equal("user:42", req.Key);
        Assert.Equal(value, req.Value);
        Assert.Equal(300, req.TtlSeconds);
        Assert.Equal(CacheRequestFlags.Nx, req.Flags);
    }

    [Fact]
    public void RequestRoundTrip_GetHasNoValue()
    {
        var payload = CacheProtocol.EncodeRequest(CacheOp.Get, "k", value: null, ttlSeconds: -1, flags: CacheRequestFlags.None);
        var req = CacheProtocol.DecodeRequest(payload, payload.Length);
        Assert.Equal(CacheOp.Get, req.Op);
        Assert.Null(req.Value);
    }

    [Fact]
    public void RequestRoundTrip_EmptyValueIsDistinctFromNull()
    {
        var payload = CacheProtocol.EncodeRequest(CacheOp.Set, "k", Array.Empty<byte>(), -1, CacheRequestFlags.None);
        var req = CacheProtocol.DecodeRequest(payload, payload.Length);
        Assert.NotNull(req.Value);
        Assert.Empty(req.Value!);
    }

    [Fact]
    public void BatchRoundTrip_MSet()
    {
        var pairs = new List<KeyValuePair<string, byte[]>>
        {
            new("a", [1]), new("b", [2, 2]),
        };
        var payload = CacheProtocol.EncodeMSetRequest(pairs);
        var req = CacheProtocol.DecodeRequest(payload, payload.Length);
        Assert.Equal(CacheOp.MSet, req.Op);
        Assert.Equal(2, req.BatchPairs!.Count);
        Assert.Equal("b", req.BatchPairs[1].Key);
        Assert.Equal(new byte[] { 2, 2 }, req.BatchPairs[1].Value);
    }

    [Fact]
    public void BatchRoundTrip_MGet()
    {
        var payload = CacheProtocol.EncodeMGetRequest(["a", "b", "c"]);
        var req = CacheProtocol.DecodeRequest(payload, payload.Length);
        Assert.Equal(CacheOp.MGet, req.Op);
        Assert.Equal(new[] { "a", "b", "c" }, req.BatchKeys);
    }

    [Fact]
    public void ScanRequest_CarriesCursorAndCountInValue()
    {
        var payload = CacheProtocol.EncodeScanRequest("user:*", cursor: 40, count: 100);
        var req = CacheProtocol.DecodeRequest(payload, payload.Length);
        Assert.Equal(CacheOp.Scan, req.Op);
        Assert.Equal("user:*", req.Key);
        Assert.Equal(40, req.ScanCursor);
        Assert.Equal(100, req.ScanCount);
    }

    [Fact]
    public void ResponseRoundTrip_OkWithValue()
    {
        var payload = CacheProtocol.EncodeResponse(CacheStatus.Ok, [9, 8, 7]);
        var resp = CacheProtocol.DecodeResponse(payload, payload.Length);
        Assert.Equal(CacheStatus.Ok, resp.Status);
        Assert.Equal(new byte[] { 9, 8, 7 }, resp.Value);
    }

    [Fact]
    public void ResponseRoundTrip_NotFoundHasNoValue()
    {
        var payload = CacheProtocol.EncodeResponse(CacheStatus.NotFound, null);
        var resp = CacheProtocol.DecodeResponse(payload, payload.Length);
        Assert.Equal(CacheStatus.NotFound, resp.Status);
        Assert.Null(resp.Value);
    }

    [Fact]
    public void ResponseRoundTrip_Int64()
    {
        var payload = CacheProtocol.EncodeInt64Response(CacheStatus.Ok, -1);
        var resp = CacheProtocol.DecodeResponse(payload, payload.Length);
        Assert.Equal(-1L, CacheProtocol.ReadInt64Value(resp));
    }

    [Fact]
    public void ResponseRoundTrip_MGetPreservesOrderAndMisses()
    {
        var values = new byte[]?[] { [1], null, [3, 3] };
        var payload = CacheProtocol.EncodeMGetResponse(values);
        var resp = CacheProtocol.DecodeResponse(payload, payload.Length);
        var decoded = CacheProtocol.ReadMGetValues(resp);
        Assert.Equal(3, decoded.Count);
        Assert.Equal(new byte[] { 1 }, decoded[0]);
        Assert.Null(decoded[1]);
        Assert.Equal(new byte[] { 3, 3 }, decoded[2]);
    }

    [Fact]
    public void ResponseRoundTrip_KeysWithCursor()
    {
        var payload = CacheProtocol.EncodeKeysResponse(nextCursor: 12, ["k1", "k2"]);
        var resp = CacheProtocol.DecodeResponse(payload, payload.Length);
        var (cursor, keys) = CacheProtocol.ReadKeysValue(resp);
        Assert.Equal(12, cursor);
        Assert.Equal(new[] { "k1", "k2" }, keys);
    }

    [Fact]
    public void DecodeRequest_TruncatedPayload_Throws()
    {
        var payload = CacheProtocol.EncodeRequest(CacheOp.Set, "key", [1, 2, 3], -1, CacheRequestFlags.None);
        Assert.Throws<ProtocolException>(() => CacheProtocol.DecodeRequest(payload, payload.Length - 2));
    }

    [Fact]
    public void MessageType_HasCacheValues()
    {
        Assert.Equal((byte)0x0B, (byte)MessageType.CacheOperation);
        Assert.Equal((byte)0x0C, (byte)MessageType.CacheResponse);
    }
}
```

- [ ] **Step 1.2: Run tests, verify they fail to compile**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~CacheProtocolTests"`
Expected: build error — `CacheProtocol` does not exist.

- [ ] **Step 1.3: Add message types**

In `MessageProtocol.cs` append to the `MessageType` enum after `Notification = 0x0A`:

```csharp
        /// <summary>
        /// Binary KV cache operation request
        /// </summary>
        CacheOperation = 0x0B,

        /// <summary>
        /// Binary KV cache operation response
        /// </summary>
        CacheResponse = 0x0C
```

(`ValidateHeader` uses `Enum.IsDefined`, so new values become valid automatically.)

- [ ] **Step 1.4: Implement `CacheProtocol.cs`**

```csharp
// AdvGenNoSqlServer.Network/CacheProtocol.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>KV cache operation codes (spec section 3).</summary>
    public enum CacheOp : byte
    {
        Get = 1, Set = 2, Del = 3, Exists = 4, Expire = 5, Ttl = 6,
        Incr = 7, Decr = 8, IncrBy = 9, MGet = 10, MSet = 11,
        Keys = 12, Scan = 13, Flush = 14, Stats = 15
    }

    /// <summary>Cache response status byte.</summary>
    public enum CacheStatus : byte { Ok = 0, NotFound = 1, WrongType = 2, Error = 3 }

    [Flags]
    public enum CacheRequestFlags : byte { None = 0, Nx = 1, Xx = 2 }

    /// <summary>Decoded cache request.</summary>
    public sealed class CacheRequest
    {
        public CacheOp Op { get; init; }
        public CacheRequestFlags Flags { get; init; }
        public int TtlSeconds { get; init; } = -1;          // -1 = none
        public string Key { get; init; } = string.Empty;
        public byte[]? Value { get; init; }                  // null = absent; empty = empty value
        public IReadOnlyList<string>? BatchKeys { get; init; }                       // MGet
        public IReadOnlyList<KeyValuePair<string, byte[]>>? BatchPairs { get; init; } // MSet
        public long ScanCursor { get; init; }
        public int ScanCount { get; init; }
    }

    /// <summary>Decoded cache response.</summary>
    public sealed class CacheResponse
    {
        public CacheStatus Status { get; init; }
        public byte[]? Value { get; init; }
        public string? ErrorMessage =>
            Status is CacheStatus.Error or CacheStatus.WrongType && Value is { Length: > 0 }
                ? Encoding.UTF8.GetString(Value) : null;
    }

    /// <summary>
    /// Binary encoding for cache operations. Layout (all big-endian):
    /// Request:  [op:1][flags:1][ttlSeconds:4][keyLen:2][key][valueLen:4 (-1=absent)][value]
    ///           batch ops append records after the fixed fields (keyLen=0, valueLen=-1):
    ///           MGet:  [count:2] { [keyLen:2][key] }*
    ///           MSet:  [count:2] { [keyLen:2][key][valueLen:4][value] }*
    ///           Scan carries [cursor:8][count:4] as the value bytes; Keys sends pattern as key.
    /// Response: [status:1][valueLen:4 (-1=absent)][value]
    ///           MGet value:  [count:2] { [valueLen:4 (-1=miss)][value] }*
    ///           Keys/Scan value: [nextCursor:8][count:4] { [keyLen:2][key] }*
    /// </summary>
    public static class CacheProtocol
    {
        public const int MaxKeyBytes = 1024;

        public static byte[] EncodeRequest(CacheOp op, string key, byte[]? value, int ttlSeconds, CacheRequestFlags flags)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            if (keyBytes.Length > MaxKeyBytes)
                throw new ProtocolException($"Cache key exceeds {MaxKeyBytes} bytes");
            int valueLen = value?.Length ?? -1;
            var buf = new byte[1 + 1 + 4 + 2 + keyBytes.Length + 4 + Math.Max(valueLen, 0)];
            int o = 0;
            buf[o++] = (byte)op;
            buf[o++] = (byte)flags;
            BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(o, 4), ttlSeconds); o += 4;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(o, 2), (ushort)keyBytes.Length); o += 2;
            keyBytes.CopyTo(buf, o); o += keyBytes.Length;
            BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(o, 4), valueLen); o += 4;
            value?.CopyTo(buf, o);
            return buf;
        }

        public static byte[] EncodeScanRequest(string pattern, long cursor, int count)
        {
            var value = new byte[12];
            BinaryPrimitives.WriteInt64BigEndian(value.AsSpan(0, 8), cursor);
            BinaryPrimitives.WriteInt32BigEndian(value.AsSpan(8, 4), count);
            return EncodeRequest(CacheOp.Scan, pattern, value, -1, CacheRequestFlags.None);
        }

        public static byte[] EncodeInt64Request(CacheOp op, string key, long number)
        {
            var value = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(value, number);
            return EncodeRequest(op, key, value, -1, CacheRequestFlags.None);
        }

        public static byte[] EncodeMGetRequest(IReadOnlyList<string> keys)
        {
            var header = EncodeRequest(CacheOp.MGet, string.Empty, null, -1, CacheRequestFlags.None);
            using var ms = new System.IO.MemoryStream();
            ms.Write(header);
            WriteUInt16(ms, (ushort)keys.Count);
            foreach (var k in keys) WriteKey(ms, k);
            return ms.ToArray();
        }

        public static byte[] EncodeMSetRequest(IReadOnlyList<KeyValuePair<string, byte[]>> pairs)
        {
            var header = EncodeRequest(CacheOp.MSet, string.Empty, null, -1, CacheRequestFlags.None);
            using var ms = new System.IO.MemoryStream();
            ms.Write(header);
            WriteUInt16(ms, (ushort)pairs.Count);
            foreach (var (k, v) in pairs)
            {
                WriteKey(ms, k);
                WriteInt32(ms, v.Length);
                ms.Write(v);
            }
            return ms.ToArray();
        }

        public static CacheRequest DecodeRequest(byte[] payload, int length)
        {
            try
            {
                var span = payload.AsSpan(0, length);
                int o = 0;
                var op = (CacheOp)span[o++];
                var flags = (CacheRequestFlags)span[o++];
                int ttl = BinaryPrimitives.ReadInt32BigEndian(span.Slice(o, 4)); o += 4;
                int keyLen = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(o, 2)); o += 2;
                string key = Encoding.UTF8.GetString(span.Slice(o, keyLen)); o += keyLen;
                int valueLen = BinaryPrimitives.ReadInt32BigEndian(span.Slice(o, 4)); o += 4;
                byte[]? value = null;
                if (valueLen >= 0) { value = span.Slice(o, valueLen).ToArray(); o += valueLen; }

                List<string>? batchKeys = null;
                List<KeyValuePair<string, byte[]>>? batchPairs = null;
                if (op == CacheOp.MGet)
                {
                    int count = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(o, 2)); o += 2;
                    batchKeys = new List<string>(count);
                    for (int i = 0; i < count; i++)
                    {
                        int kl = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(o, 2)); o += 2;
                        batchKeys.Add(Encoding.UTF8.GetString(span.Slice(o, kl))); o += kl;
                    }
                }
                else if (op == CacheOp.MSet)
                {
                    int count = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(o, 2)); o += 2;
                    batchPairs = new List<KeyValuePair<string, byte[]>>(count);
                    for (int i = 0; i < count; i++)
                    {
                        int kl = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(o, 2)); o += 2;
                        string k = Encoding.UTF8.GetString(span.Slice(o, kl)); o += kl;
                        int vl = BinaryPrimitives.ReadInt32BigEndian(span.Slice(o, 4)); o += 4;
                        batchPairs.Add(new(k, span.Slice(o, vl).ToArray())); o += vl;
                    }
                }

                long scanCursor = 0; int scanCount = 0;
                if (op == CacheOp.Scan && value is { Length: 12 })
                {
                    scanCursor = BinaryPrimitives.ReadInt64BigEndian(value.AsSpan(0, 8));
                    scanCount = BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(8, 4));
                }

                return new CacheRequest
                {
                    Op = op, Flags = flags, TtlSeconds = ttl, Key = key, Value = value,
                    BatchKeys = batchKeys, BatchPairs = batchPairs,
                    ScanCursor = scanCursor, ScanCount = scanCount
                };
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new ProtocolException("Malformed cache request payload", ex);
            }
        }

        public static byte[] EncodeResponse(CacheStatus status, byte[]? value)
        {
            int valueLen = value?.Length ?? -1;
            var buf = new byte[1 + 4 + Math.Max(valueLen, 0)];
            buf[0] = (byte)status;
            BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(1, 4), valueLen);
            value?.CopyTo(buf, 5);
            return buf;
        }

        public static byte[] EncodeErrorResponse(CacheStatus status, string message)
            => EncodeResponse(status, Encoding.UTF8.GetBytes(message));

        public static byte[] EncodeInt64Response(CacheStatus status, long value)
        {
            var v = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(v, value);
            return EncodeResponse(status, v);
        }

        public static byte[] EncodeBoolResponse(bool result)
            => EncodeResponse(CacheStatus.Ok, [result ? (byte)1 : (byte)0]);

        public static byte[] EncodeMGetResponse(IReadOnlyList<byte[]?> values)
        {
            using var ms = new System.IO.MemoryStream();
            ms.WriteByte((byte)CacheStatus.Ok);
            // valueLen placeholder filled below: body = [count:2]{[len:4][bytes]}*
            using var body = new System.IO.MemoryStream();
            WriteUInt16(body, (ushort)values.Count);
            foreach (var v in values)
            {
                WriteInt32(body, v?.Length ?? -1);
                if (v != null) body.Write(v);
            }
            var bodyBytes = body.ToArray();
            WriteInt32(ms, bodyBytes.Length);
            ms.Write(bodyBytes);
            return ms.ToArray();
        }

        public static byte[] EncodeKeysResponse(long nextCursor, IReadOnlyList<string> keys)
        {
            using var body = new System.IO.MemoryStream();
            var cursorBytes = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(cursorBytes, nextCursor);
            body.Write(cursorBytes);
            WriteInt32(body, keys.Count);
            foreach (var k in keys) WriteKey(body, k);
            return EncodeResponse(CacheStatus.Ok, body.ToArray());
        }

        public static CacheResponse DecodeResponse(byte[] payload, int length)
        {
            try
            {
                var span = payload.AsSpan(0, length);
                var status = (CacheStatus)span[0];
                int valueLen = BinaryPrimitives.ReadInt32BigEndian(span.Slice(1, 4));
                byte[]? value = valueLen >= 0 ? span.Slice(5, valueLen).ToArray() : null;
                return new CacheResponse { Status = status, Value = value };
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new ProtocolException("Malformed cache response payload", ex);
            }
        }

        public static long ReadInt64Value(CacheResponse resp)
        {
            if (resp.Value is not { Length: 8 })
                throw new ProtocolException("Expected int64 cache response value");
            return BinaryPrimitives.ReadInt64BigEndian(resp.Value);
        }

        public static IReadOnlyList<byte[]?> ReadMGetValues(CacheResponse resp)
        {
            if (resp.Value == null) throw new ProtocolException("Expected MGET response body");
            var span = resp.Value.AsSpan();
            int o = 0;
            int count = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(o, 2)); o += 2;
            var result = new List<byte[]?>(count);
            for (int i = 0; i < count; i++)
            {
                int len = BinaryPrimitives.ReadInt32BigEndian(span.Slice(o, 4)); o += 4;
                if (len < 0) { result.Add(null); continue; }
                result.Add(span.Slice(o, len).ToArray()); o += len;
            }
            return result;
        }

        public static (long NextCursor, IReadOnlyList<string> Keys) ReadKeysValue(CacheResponse resp)
        {
            if (resp.Value == null) throw new ProtocolException("Expected KEYS/SCAN response body");
            var span = resp.Value.AsSpan();
            long cursor = BinaryPrimitives.ReadInt64BigEndian(span.Slice(0, 8));
            int count = BinaryPrimitives.ReadInt32BigEndian(span.Slice(8, 4));
            int o = 12;
            var keys = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                int kl = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(o, 2)); o += 2;
                keys.Add(Encoding.UTF8.GetString(span.Slice(o, kl))); o += kl;
            }
            return (cursor, keys);
        }

        private static void WriteKey(System.IO.Stream s, string key)
        {
            var kb = Encoding.UTF8.GetBytes(key);
            if (kb.Length > MaxKeyBytes) throw new ProtocolException($"Cache key exceeds {MaxKeyBytes} bytes");
            WriteUInt16(s, (ushort)kb.Length);
            s.Write(kb);
        }

        private static void WriteUInt16(System.IO.Stream s, ushort v)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b, v);
            s.Write(b);
        }

        private static void WriteInt32(System.IO.Stream s, int v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(b, v);
            s.Write(b);
        }
    }
}
```

Note: `ProtocolException` already exists in the Network project (used by `MessageProtocol.Deserialize`). Check its constructors — if it lacks an `(string, Exception)` overload, add one.

- [ ] **Step 1.5: Run tests, verify pass**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~CacheProtocolTests"`
Expected: all PASS.

- [ ] **Step 1.6: Commit**

```bash
git add AdvGenNoSqlServer.Network/CacheProtocol.cs AdvGenNoSqlServer.Network/MessageProtocol.cs AdvGenNoSqlServer.Tests/CacheProtocolTests.cs
git commit -m "feat: add binary cache operation protocol (CacheOp frames)"
```

---

### Task 2: Engine introspection (TTL read + key enumeration)

**Files:**
- Modify: `AdvGenNoSqlServer.Core/MemoryManagement/EvictionManager.cs`
- Modify: `AdvGenNoSqlServer.Core/MemoryManagement/IMemoryStorageEngine.cs`
- Modify: `AdvGenNoSqlServer.Core/MemoryManagement/ManagedMemoryStorageEngine.cs`
- Modify: `AdvGenNoSqlServer.Core/MemoryManagement/NativeMemoryStorageEngine.cs`
- Modify: `AdvGenNoSqlServer.Core/MemoryManagement/MixedMemoryStorageEngine.cs`
- Test: `AdvGenNoSqlServer.Tests/EngineIntrospectionTests.cs`

- [ ] **Step 2.1: Write failing tests**

```csharp
// AdvGenNoSqlServer.Tests/EngineIntrospectionTests.cs
using AdvGenNoSqlServer.Core.MemoryManagement;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class EngineIntrospectionTests
{
    private static MemoryManagementConfiguration Config(string plan) => new()
    {
        Plan = plan, MaxMemoryMB = 64, MaxMemoryPercent = 0, DefaultTtlSeconds = 0
    };

    [Theory]
    [InlineData("Managed")]
    [InlineData("Native")]
    public void TryGetTtl_ReturnsRemainingTtl(string plan)
    {
        using var engine = CreateEngine(plan);
        var introspect = Assert.IsAssignableFrom<IIntrospectableMemoryStorageEngine>(engine);

        engine.Set("with-ttl", new byte[] { 1 }, TimeSpan.FromMinutes(5));
        engine.Set("no-ttl", new byte[] { 1 });

        Assert.True(introspect.TryGetTtl("with-ttl", out var remaining));
        Assert.NotNull(remaining);
        Assert.InRange(remaining!.Value.TotalSeconds, 290, 300);

        Assert.True(introspect.TryGetTtl("no-ttl", out var none));
        Assert.Null(none);

        Assert.False(introspect.TryGetTtl("missing", out _));
    }

    [Theory]
    [InlineData("Managed")]
    [InlineData("Native")]
    public void EnumerateKeys_ReturnsAllLiveKeys(string plan)
    {
        using var engine = CreateEngine(plan);
        var introspect = (IIntrospectableMemoryStorageEngine)engine;
        engine.Set("a", new byte[] { 1 });
        engine.Set("b", new byte[] { 2 });
        var keys = introspect.EnumerateKeys().ToHashSet();
        Assert.Superset(new HashSet<string> { "a", "b" }, keys);
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public void Mixed_EnumerateKeys_IncludesColdTier()
    {
        var coldStore = new DocumentStore();
        var cfg = Config("Mixed");
        cfg.Mixed.HotTierMaxMB = 1;
        using var engine = new MixedMemoryStorageEngine(cfg, 64L * 1_048_576, coldStore);
        var introspect = (IIntrospectableMemoryStorageEngine)engine;

        // Fill hot tier past 1MB to force spills to cold
        var big = new byte[300 * 1024];
        for (int i = 0; i < 8; i++) engine.Set($"k{i}", big);

        var keys = introspect.EnumerateKeys().ToHashSet();
        for (int i = 0; i < 8; i++) Assert.Contains($"k{i}", keys);
    }

    private static IMemoryStorageEngine CreateEngine(string plan) => plan switch
    {
        "Native" => new NativeMemoryStorageEngine(Config("Native"), 64L * 1_048_576),
        _ => new ManagedMemoryStorageEngine(Config("Managed"), 64L * 1_048_576),
    };
}
```

- [ ] **Step 2.2: Run, verify compile failure** (`IIntrospectableMemoryStorageEngine` missing)

- [ ] **Step 2.3: Implement**

`EvictionManager.cs` — add two members (metadata already tracks `ExpireAtMs` per key):

```csharp
    /// <summary>
    /// Reads expiry metadata for a key. Returns false if the key is not tracked.
    /// expireAtMs = 0 means the entry has no expiry.
    /// </summary>
    public bool TryGetExpiry(string key, out long expireAtMs)
    {
        if (_metadata.TryGetValue(key, out var meta))
        {
            expireAtMs = meta.ExpireAtMs;
            return true;
        }
        expireAtMs = 0;
        return false;
    }

    /// <summary>Snapshot of all tracked keys (expired entries may be included).</summary>
    public IReadOnlyList<string> SnapshotKeys() => _metadata.Keys.ToArray();
```

`IMemoryStorageEngine.cs` — append:

```csharp
/// <summary>
/// Engines that can report per-key TTL and enumerate keys.
/// Required by CacheStore for EXPIRE/TTL/KEYS/SCAN. All shipped engines implement it.
/// </summary>
public interface IIntrospectableMemoryStorageEngine : IMemoryStorageEngine
{
    /// <summary>False if the key does not exist. remaining=null means no expiry.</summary>
    bool TryGetTtl(string key, out TimeSpan? remaining);

    /// <summary>Snapshot enumeration; keys may be concurrently mutated.</summary>
    IEnumerable<string> EnumerateKeys();
}
```

`ManagedMemoryStorageEngine.cs` and `NativeMemoryStorageEngine.cs` — change class declaration to add `IIntrospectableMemoryStorageEngine` (Native already declares `IEvictingMemoryStorageEngine`; add the new one alongside) and implement, delegating to `_eviction`:

```csharp
    public bool TryGetTtl(string key, out TimeSpan? remaining)
    {
        remaining = null;
        if (!_eviction.TryGetExpiry(key, out long expireAtMs))
            return false;
        if (expireAtMs <= 0)
            return true; // exists, no expiry
        long msLeft = expireAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (msLeft <= 0)
            return false; // already expired — treat as missing
        remaining = TimeSpan.FromMilliseconds(msLeft);
        return true;
    }

    public IEnumerable<string> EnumerateKeys() => _eviction.SnapshotKeys();
```

`MixedMemoryStorageEngine.cs` — implement `IIntrospectableMemoryStorageEngine`; hot tier first, then cold docs (`_hot` is a `NativeMemoryStorageEngine`, so cast to the new interface):

```csharp
    public bool TryGetTtl(string key, out TimeSpan? remaining)
    {
        if (((IIntrospectableMemoryStorageEngine)_hot).TryGetTtl(key, out remaining))
            return true;

        var coldDoc = _store.GetAsync(_config.Mixed.SpillCollection, key, _cts.Token)
            .GetAwaiter().GetResult();
        remaining = null;
        if (coldDoc == null) return false;

        long expireAtMs = coldDoc.Data.TryGetValue("_expiry", out var expObj) && expObj != null
            ? (expObj is System.Text.Json.JsonElement je ? je.GetInt64() : Convert.ToInt64(expObj))
            : 0;
        if (expireAtMs <= 0) return true;
        long msLeft = expireAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (msLeft <= 0) return false;
        remaining = TimeSpan.FromMilliseconds(msLeft);
        return true;
    }

    public IEnumerable<string> EnumerateKeys()
    {
        var seen = new HashSet<string>(((IIntrospectableMemoryStorageEngine)_hot).EnumerateKeys());
        var coldDocs = _store.GetAllAsync(_config.Mixed.SpillCollection, _cts.Token)
            .GetAwaiter().GetResult();
        foreach (var doc in coldDocs) seen.Add(doc.Id);
        return seen;
    }
```

Note: the test references `DocumentStore` from the Storage project. `AdvGenNoSqlServer.Tests` already references both Core and Storage. If `MixedMemoryStorageEngine`'s constructor logger parameter is required, pass `null`.

- [ ] **Step 2.4: Run introspection tests AND existing engine tests, verify pass**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~EngineIntrospectionTests|FullyQualifiedName~MemoryStorageEngineTests|FullyQualifiedName~EvictionManagerTests"`
Expected: all PASS (no regressions in existing engine tests).

- [ ] **Step 2.5: Commit**

```bash
git add AdvGenNoSqlServer.Core/MemoryManagement/ AdvGenNoSqlServer.Tests/EngineIntrospectionTests.cs
git commit -m "feat: add TTL read and key enumeration to memory storage engines"
```

---

### Task 3: Non-DI engine factory + MaxValueSizeMB config

**Files:**
- Modify: `AdvGenNoSqlServer.Core/MemoryManagement/MemoryEngineFactory.cs`
- Modify: `AdvGenNoSqlServer.Core/MemoryManagement/MemoryManagementConfiguration.cs`
- Test: `AdvGenNoSqlServer.Tests/MemoryEngineFactoryTests.cs` (append to existing file)

- [ ] **Step 3.1: Write failing tests** (append to existing `MemoryEngineFactoryTests.cs`, matching its style)

```csharp
    [Fact]
    public void Create_ManagedPlan_ReturnsManagedEngine()
    {
        using var engine = MemoryEngineFactory.Create(
            new MemoryManagementConfiguration { Plan = "Managed", MaxMemoryMB = 64, MaxMemoryPercent = 0 });
        Assert.IsType<ManagedMemoryStorageEngine>(engine);
    }

    [Fact]
    public void Create_MixedPlanWithoutColdStore_Throws()
    {
        var cfg = new MemoryManagementConfiguration { Plan = "Mixed", MaxMemoryMB = 64, MaxMemoryPercent = 0 };
        cfg.Mixed.HotTierMaxMB = 32;
        Assert.Throws<InvalidOperationException>(() => MemoryEngineFactory.Create(cfg));
    }

    [Fact]
    public void Create_UnknownPlan_FallsBackToManaged()
    {
        using var engine = MemoryEngineFactory.Create(
            new MemoryManagementConfiguration { Plan = "Bogus", MaxMemoryMB = 64, MaxMemoryPercent = 0 });
        Assert.IsType<ManagedMemoryStorageEngine>(engine);
    }
```

- [ ] **Step 3.2: Run, verify failure** (no `Create` method)

- [ ] **Step 3.3: Implement**

Add to `MemoryEngineFactory` (refactor the switch shared with `AddMemoryEngine` so both paths use it — DRY):

```csharp
    /// <summary>
    /// Direct (non-DI) construction. coldStore is required for Plan=Mixed and must be a
    /// dedicated store — the KV cache must never share the server's document store (spec §2).
    /// </summary>
    public static IMemoryStorageEngine Create(
        MemoryManagementConfiguration config,
        AdvGenNoSqlServer.Core.Abstractions.IDocumentStore? coldStore = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateConfig(config);
        long effectiveLimit = ComputeEffectiveLimit(config);

        return config.Plan switch
        {
            "Native" => new NativeMemoryStorageEngine(config, effectiveLimit),
            "Mixed" => new MixedMemoryStorageEngine(
                config, effectiveLimit,
                coldStore ?? throw new InvalidOperationException(
                    "Plan=Mixed requires a dedicated cold-tier IDocumentStore."),
                logger),
            "Managed" => new ManagedMemoryStorageEngine(config, effectiveLimit),
            _ => new ManagedMemoryStorageEngine(config, effectiveLimit) // fallback, log if logger given
        };
    }
```

(Have the `AddMemoryEngine` DI registration delegate to `Create` internally, keeping the existing percent-cap warning behavior.)

Add to `MemoryManagementConfiguration`:

```csharp
    /// <summary>Maximum size of a single cache value in MB (default 16).</summary>
    public int MaxValueSizeMB { get; set; } = 16;
```

- [ ] **Step 3.4: Run factory tests (all of them), verify pass**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~MemoryEngineFactoryTests"`

- [ ] **Step 3.5: Commit**

```bash
git add AdvGenNoSqlServer.Core/MemoryManagement/
git add AdvGenNoSqlServer.Tests/MemoryEngineFactoryTests.cs
git commit -m "feat: add non-DI MemoryEngineFactory.Create and MaxValueSizeMB config"
```

---

### Task 4: CacheStore — core ops, TTL, counters, batch, scan

**Files:**
- Create: `AdvGenNoSqlServer.Storage/CacheStore.cs`
- Test: `AdvGenNoSqlServer.Tests/CacheStoreTests.cs`

This is the largest unit; the steps below split it into four TDD cycles within one file.

- [ ] **Step 4.1: Write failing tests — core ops**

```csharp
// AdvGenNoSqlServer.Tests/CacheStoreTests.cs
using AdvGenNoSqlServer.Core.MemoryManagement;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class CacheStoreTests : IDisposable
{
    private readonly CacheStore _cache = new(new MemoryManagementConfiguration
    {
        Plan = "Managed", MaxMemoryMB = 64, MaxMemoryPercent = 0, DefaultTtlSeconds = 0
    });

    public void Dispose() => _cache.Dispose();

    // --- core ---

    [Fact]
    public void SetThenGet_ReturnsValue()
    {
        Assert.True(_cache.Set("k", new byte[] { 1, 2 }));
        Assert.Equal(new byte[] { 1, 2 }, _cache.Get("k"));
    }

    [Fact]
    public void Get_MissingKey_ReturnsNull() => Assert.Null(_cache.Get("nope"));

    [Fact]
    public void Set_Nx_OnlyWhenAbsent()
    {
        Assert.True(_cache.Set("k", new byte[] { 1 }, condition: CacheSetCondition.NotExists));
        Assert.False(_cache.Set("k", new byte[] { 2 }, condition: CacheSetCondition.NotExists));
        Assert.Equal(new byte[] { 1 }, _cache.Get("k"));
    }

    [Fact]
    public void Set_Xx_OnlyWhenPresent()
    {
        Assert.False(_cache.Set("k", new byte[] { 1 }, condition: CacheSetCondition.Exists));
        _cache.Set("k", new byte[] { 1 });
        Assert.True(_cache.Set("k", new byte[] { 2 }, condition: CacheSetCondition.Exists));
        Assert.Equal(new byte[] { 2 }, _cache.Get("k"));
    }

    [Fact]
    public void DeleteAndExists_Work()
    {
        _cache.Set("k", new byte[] { 1 });
        Assert.True(_cache.Exists("k"));
        Assert.True(_cache.Delete("k"));
        Assert.False(_cache.Exists("k"));
        Assert.False(_cache.Delete("k"));
    }

    [Fact]
    public void Flush_RemovesEverything()
    {
        _cache.Set("a", new byte[] { 1 });
        _cache.Set("b", new byte[] { 1 });
        _cache.Flush();
        Assert.False(_cache.Exists("a"));
        Assert.Equal(0, _cache.GetStats().EntryCount);
    }

    [Fact]
    public void Set_EmptyKey_Throws() =>
        Assert.Throws<CacheValidationException>(() => _cache.Set("", new byte[] { 1 }));

    [Fact]
    public void Set_OversizedValue_Throws()
    {
        using var small = new CacheStore(new MemoryManagementConfiguration
        {
            Plan = "Managed", MaxMemoryMB = 64, MaxMemoryPercent = 0, MaxValueSizeMB = 1
        });
        Assert.Throws<CacheValidationException>(() => small.Set("k", new byte[2 * 1024 * 1024]));
    }

    // --- TTL ---

    [Fact]
    public void Ttl_ReportsRemainingAndNoExpiryAndMissing()
    {
        _cache.Set("t", new byte[] { 1 }, TimeSpan.FromMinutes(5));
        _cache.Set("f", new byte[] { 1 });

        var t = _cache.Ttl("t");
        Assert.True(t.Exists);
        Assert.InRange(t.Remaining!.Value.TotalSeconds, 290, 300);

        var f = _cache.Ttl("f");
        Assert.True(f.Exists);
        Assert.Null(f.Remaining);

        Assert.False(_cache.Ttl("missing").Exists);
    }

    [Fact]
    public void Expire_ResetsTtl_PreservesValue()
    {
        _cache.Set("k", new byte[] { 7 });
        Assert.True(_cache.Expire("k", TimeSpan.FromMinutes(1)));
        Assert.Equal(new byte[] { 7 }, _cache.Get("k"));
        Assert.InRange(_cache.Ttl("k").Remaining!.Value.TotalSeconds, 50, 60);
        Assert.False(_cache.Expire("missing", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Set_WithShortTtl_ExpiresOnRead()
    {
        _cache.Set("k", new byte[] { 1 }, TimeSpan.FromMilliseconds(50));
        await Task.Delay(150);
        Assert.False(_cache.Ttl("k").Exists);
    }

    [Fact]
    public void DefaultTtl_AppliedWhenConfigured()
    {
        using var withDefault = new CacheStore(new MemoryManagementConfiguration
        {
            Plan = "Managed", MaxMemoryMB = 64, MaxMemoryPercent = 0, DefaultTtlSeconds = 600
        });
        withDefault.Set("k", new byte[] { 1 });
        Assert.InRange(withDefault.Ttl("k").Remaining!.Value.TotalSeconds, 590, 600);
    }

    // --- counters ---

    [Fact]
    public void Incr_MissingKey_StartsAtZero()
    {
        Assert.Equal(1, _cache.Increment("c", 1));
        Assert.Equal(2, _cache.Increment("c", 1));
        Assert.Equal(-3, _cache.Increment("c", -5));
    }

    [Fact]
    public void Incr_NonNumericValue_ThrowsWrongType()
    {
        _cache.Set("s", "hello"u8.ToArray());
        Assert.Throws<CacheWrongTypeException>(() => _cache.Increment("s", 1));
    }

    [Fact]
    public void Incr_PreservesTtl()
    {
        _cache.Set("c", "5"u8.ToArray(), TimeSpan.FromMinutes(5));
        _cache.Increment("c", 1);
        Assert.InRange(_cache.Ttl("c").Remaining!.Value.TotalSeconds, 200, 300);
    }

    [Fact]
    public async Task Incr_IsAtomicUnderParallelLoad()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => { for (int i = 0; i < 1000; i++) _cache.Increment("ctr", 1); })));
        Assert.Equal(8000, _cache.Increment("ctr", 0));
    }

    // --- batch + scan ---

    [Fact]
    public void MGetMSet_RoundTrip_PreservesOrderAndMisses()
    {
        _cache.MSet([new("a", [1]), new("b", [2])]);
        var result = _cache.MGet(["a", "missing", "b"]);
        Assert.Equal(new byte[] { 1 }, result[0]);
        Assert.Null(result[1]);
        Assert.Equal(new byte[] { 2 }, result[2]);
    }

    [Fact]
    public void Keys_GlobPatterns()
    {
        _cache.MSet([new("user:1", [1]), new("user:2", [1]), new("order:1", [1])]);
        Assert.Equal(2, _cache.Keys("user:*").Count);
        Assert.Single(_cache.Keys("user:?").Where(k => k == "user:1")); // '?' matches one char
        Assert.Equal(3, _cache.Keys("*").Count);
        Assert.Empty(_cache.Keys("nothing*"));
    }

    [Fact]
    public void Scan_CursorWalksAllKeys()
    {
        for (int i = 0; i < 25; i++) _cache.Set($"k{i:D2}", [1]);
        var seen = new HashSet<string>();
        long cursor = 0;
        do
        {
            var (next, keys) = _cache.Scan("*", cursor, count: 10);
            foreach (var k in keys) seen.Add(k);
            cursor = next;
        } while (cursor != 0);
        Assert.Equal(25, seen.Count);
    }

    [Fact]
    public void Stats_TracksEntriesAndHits()
    {
        _cache.Set("k", [1]);
        _cache.Get("k");
        _cache.Get("missing");
        var stats = _cache.GetStats();
        Assert.Equal(1, stats.EntryCount);
        Assert.True(stats.HitCount >= 1);
        Assert.True(stats.MissCount >= 1);
    }
}
```

- [ ] **Step 4.2: Run, verify compile failure**

- [ ] **Step 4.3: Implement `CacheStore.cs`**

```csharp
// AdvGenNoSqlServer.Storage/CacheStore.cs
using System.Buffers.Text;
using System.Text;
using System.Text.RegularExpressions;
using AdvGenNoSqlServer.Core.MemoryManagement;
using Microsoft.Extensions.Logging;

namespace AdvGenNoSqlServer.Storage;

/// <summary>SET conditions matching Redis NX/XX.</summary>
public enum CacheSetCondition { None = 0, NotExists = 1, Exists = 2 }

/// <summary>Result of a TTL query. Remaining=null with Exists=true means no expiry.</summary>
public readonly record struct CacheTtlResult(bool Exists, TimeSpan? Remaining);

public class CacheValidationException : Exception
{
    public CacheValidationException(string message) : base(message) { }
}

public class CacheWrongTypeException : Exception
{
    public CacheWrongTypeException(string message) : base(message) { }
}

/// <summary>
/// Redis-style KV cache facade over IMemoryStorageEngine (spec §2).
/// Plan=Mixed gets a dedicated private in-memory DocumentStore as its cold tier —
/// never the server's document store, so cache data is never persisted.
/// Counters and conditional sets serialize via 64 striped locks; plain Get/Set/Del
/// go straight to the engine (already thread-safe).
/// </summary>
public sealed class CacheStore : IDisposable
{
    private const int StripeCount = 64;
    public const int MaxKeyBytes = 1024;

    private readonly IMemoryStorageEngine _engine;
    private readonly IIntrospectableMemoryStorageEngine _introspect;
    private readonly DocumentStore? _privateColdStore;
    private readonly object[] _stripes;
    private readonly TimeSpan? _defaultTtl;
    private readonly long _maxValueBytes;
    private int _disposed;

    public CacheStore(MemoryManagementConfiguration config, ILogger? logger = null)
    {
        _privateColdStore = config.Plan == "Mixed" ? new DocumentStore() : null;
        _engine = MemoryEngineFactory.Create(config, _privateColdStore, logger);
        _introspect = (IIntrospectableMemoryStorageEngine)_engine;
        _stripes = new object[StripeCount];
        for (int i = 0; i < StripeCount; i++) _stripes[i] = new object();
        _defaultTtl = config.DefaultTtlSeconds > 0 ? TimeSpan.FromSeconds(config.DefaultTtlSeconds) : null;
        _maxValueBytes = (long)Math.Max(config.MaxValueSizeMB, 1) * 1_048_576;
    }

    private object StripeFor(string key) =>
        _stripes[(key.GetHashCode() & 0x7FFFFFFF) % StripeCount];

    private void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new CacheValidationException("Cache key must not be empty");
        if (Encoding.UTF8.GetByteCount(key) > MaxKeyBytes)
            throw new CacheValidationException($"Cache key exceeds {MaxKeyBytes} bytes");
    }

    private void ValidateValue(ReadOnlySpan<byte> value)
    {
        if (value.Length > _maxValueBytes)
            throw new CacheValidationException(
                $"Cache value of {value.Length} bytes exceeds limit of {_maxValueBytes} bytes");
    }

    public byte[]? Get(string key)
    {
        ValidateKey(key);
        return _engine.TryGet(key, out var span) ? span.ToArray() : null;
    }

    /// <summary>Returns false only when an NX/XX condition is not met.</summary>
    public bool Set(string key, ReadOnlySpan<byte> value, TimeSpan? ttl = null,
        CacheSetCondition condition = CacheSetCondition.None)
    {
        ValidateKey(key);
        ValidateValue(value);
        var effectiveTtl = ttl ?? _defaultTtl;

        if (condition == CacheSetCondition.None)
        {
            _engine.Set(key, value, effectiveTtl);
            return true;
        }

        // Conditional sets must be check-then-set atomic → stripe lock
        byte[] copy = value.ToArray();
        lock (StripeFor(key))
        {
            bool exists = _engine.TryGet(key, out _);
            if (condition == CacheSetCondition.NotExists && exists) return false;
            if (condition == CacheSetCondition.Exists && !exists) return false;
            _engine.Set(key, copy, effectiveTtl);
            return true;
        }
    }

    public bool Delete(string key)
    {
        ValidateKey(key);
        return _engine.Remove(key);
    }

    public bool Exists(string key)
    {
        ValidateKey(key);
        return _engine.TryGet(key, out _);
    }

    public bool Expire(string key, TimeSpan ttl)
    {
        ValidateKey(key);
        lock (StripeFor(key))
        {
            if (!_engine.TryGet(key, out var span)) return false;
            _engine.Set(key, span, ttl);
            return true;
        }
    }

    public CacheTtlResult Ttl(string key)
    {
        ValidateKey(key);
        return _introspect.TryGetTtl(key, out var remaining)
            ? new CacheTtlResult(true, remaining)
            : new CacheTtlResult(false, null);
    }

    /// <summary>Atomic add. Missing key starts at 0. Preserves remaining TTL, Redis-style.</summary>
    public long Increment(string key, long delta)
    {
        ValidateKey(key);
        lock (StripeFor(key))
        {
            long current = 0;
            TimeSpan? remaining = null;
            if (_engine.TryGet(key, out var span))
            {
                if (!Utf8Parser.TryParse(span, out long parsed, out int consumed) || consumed != span.Length)
                    throw new CacheWrongTypeException(
                        $"Value at key '{key}' is not an integer");
                current = parsed;
                _introspect.TryGetTtl(key, out remaining);
            }
            long next = checked(current + delta);
            _engine.Set(key, Encoding.ASCII.GetBytes(next.ToString()), remaining);
            return next;
        }
    }

    public IReadOnlyList<byte[]?> MGet(IReadOnlyList<string> keys)
    {
        var result = new List<byte[]?>(keys.Count);
        foreach (var key in keys) result.Add(Get(key));
        return result;
    }

    /// <summary>Not atomic across keys (spec §2).</summary>
    public void MSet(IReadOnlyList<KeyValuePair<string, byte[]>> pairs)
    {
        foreach (var (key, value) in pairs) Set(key, value);
    }

    public IReadOnlyList<string> Keys(string pattern)
    {
        var regex = GlobToRegex(pattern);
        return _introspect.EnumerateKeys().Where(k => regex.IsMatch(k))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Cursor = ordinal offset into the sorted key snapshot; 0 return = done.
    /// Best-effort under concurrent mutation (like Redis SCAN).
    /// </summary>
    public (long NextCursor, IReadOnlyList<string> Keys) Scan(string pattern, long cursor, int count)
    {
        if (count <= 0) count = 10;
        var all = Keys(pattern);
        if (cursor < 0 || cursor >= all.Count) return (0, Array.Empty<string>());
        var page = all.Skip((int)cursor).Take(count).ToList();
        long next = cursor + page.Count;
        return (next >= all.Count ? 0 : next, page);
    }

    public void Flush() => _engine.Clear();

    public MemoryEngineStats GetStats() => _engine.GetStats();

    internal static Regex GlobToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (char c in pattern)
        {
            switch (c)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                case '[': sb.Append('['); break;
                case ']': sb.Append(']'); break;
                default: sb.Append(Regex.Escape(c.ToString())); break;
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.Compiled);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _engine.Dispose();
    }
}
```

Implementation notes for the executor:
- The `Incr_PreservesTtl` test range (200–300s) is deliberately loose because `Expire`+`Set` round-trips timestamps.
- `checked(...)` makes counter overflow throw `OverflowException`; the server handler maps it to `Error` status (Task 6).
- If `DocumentStore` requires no constructor args (it doesn't — see `DocumentStore.cs:23`), the private cold store needs no initialization call.

- [ ] **Step 4.4: Run all CacheStore tests, verify pass**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~CacheStoreTests"`

- [ ] **Step 4.5: Commit**

```bash
git add AdvGenNoSqlServer.Storage/CacheStore.cs AdvGenNoSqlServer.Tests/CacheStoreTests.cs
git commit -m "feat: add CacheStore with Redis-style semantics over memory engines"
```

---

### Task 5: Server configuration — StorageMode + MemoryManagement

**Files:**
- Modify: `AdvGenNoSqlServer.Core/Configuration/ServerConfiguration.cs`
- Modify: `AdvGenNoSqlServer.Server/appsettings.json` (and `appsettings.Development.json`, `appsettings.Production.json`, `appsettings.Testing.json` — add `"storageMode": "Hybrid"`)
- Test: `AdvGenNoSqlServer.Tests/ConfigurationManagerTests.cs` (append)

- [ ] **Step 5.1: Write failing test** (append to existing `ConfigurationManagerTests.cs`, following its file-loading pattern)

```csharp
    [Fact]
    public void Configuration_DeserializesStorageModeAndMemoryManagement()
    {
        var json = """
        {
          "storageMode": "CacheOnly",
          "MemoryManagement": { "Plan": "Native", "MaxMemoryMB": 256, "EvictionPolicy": "LFU" }
        }
        """;
        var config = System.Text.Json.JsonSerializer.Deserialize<ServerConfiguration>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.Equal("CacheOnly", config!.StorageMode);
        Assert.Equal("Native", config.MemoryManagement.Plan);
        Assert.Equal(256, config.MemoryManagement.MaxMemoryMB);
        Assert.Equal("LFU", config.MemoryManagement.EvictionPolicy);
    }

    [Fact]
    public void Configuration_StorageModeDefaultsToHybrid()
    {
        var config = new ServerConfiguration();
        Assert.Equal("Hybrid", config.StorageMode);
        Assert.NotNull(config.MemoryManagement);
    }
```

- [ ] **Step 5.2: Run, verify failure**

- [ ] **Step 5.3: Implement**

Add to `ServerConfiguration` (near `StoragePath`, ~line 70):

```csharp
    /// <summary>
    /// Storage mode: "Hybrid" (memory cache + disk persistence, default) or
    /// "CacheOnly" (everything in RAM, no disk I/O, data lost on restart).
    /// Unknown values fall back to Hybrid with a warning.
    /// </summary>
    public string StorageMode { get; set; } = "Hybrid";

    /// <summary>
    /// KV cache engine configuration (also used by CacheOnly-mode sizing).
    /// </summary>
    public AdvGenNoSqlServer.Core.MemoryManagement.MemoryManagementConfiguration MemoryManagement { get; set; } = new();
```

Add `"storageMode": "Hybrid"` to each appsettings JSON in the Server project (the `MemoryManagement` section already exists in `appsettings.json`; add it to the other environment files too, copying the same block). Check `AdvGenNoSqlServer.Host` for its own appsettings files and update those the same way.

- [ ] **Step 5.4: Run config tests, verify pass** (`--filter "FullyQualifiedName~ConfigurationManagerTests"`)

- [ ] **Step 5.5: Commit**

```bash
git add AdvGenNoSqlServer.Core/Configuration/ServerConfiguration.cs AdvGenNoSqlServer.Server/appsettings*.json AdvGenNoSqlServer.Tests/ConfigurationManagerTests.cs
git commit -m "feat: add StorageMode and MemoryManagement server configuration"
```

---

### Task 6: Server wiring — mode selection + cache operation handler

**Files:**
- Modify: `AdvGenNoSqlServer.Server/NoSqlServer.cs`
- Test: `AdvGenNoSqlServer.Tests/CacheOperationHandlerTests.cs` (new)
- Test: `AdvGenNoSqlServer.Tests/CacheOnlyModeTests.cs` (new)

- [ ] **Step 6.1: Write failing handler tests**

Follow the `ClusterCommandTests` pattern (Moq `IConfigurationManager`, reflection or `internal` access to invoke the handler). Prefer making `HandleMessageAsync` internal + `[assembly: InternalsVisibleTo("AdvGenNoSqlServer.Tests")]` if not already present (check `AdvGenNoSqlServer.Server.csproj` / an `AssemblyInfo.cs` — `ClusterCommandTests` uses a helper `InvokeHandleCommandAsync`; reuse whatever mechanism it uses).

```csharp
// AdvGenNoSqlServer.Tests/CacheOperationHandlerTests.cs
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ServerNoSql = AdvGenNoSqlServer.Server.NoSqlServer;

namespace AdvGenNoSqlServer.Tests;

public class CacheOperationHandlerTests : IAsyncLifetime
{
    private readonly ServerNoSql _server;
    private readonly ServerConfiguration _config = new()
    {
        Host = "127.0.0.1",
        Port = 19291,                       // unique port per test class
        StorageMode = "CacheOnly",
        RequireAuthentication = false,
        MemoryManagement = new() { Plan = "Managed", MaxMemoryMB = 64, MaxMemoryPercent = 0, DefaultTtlSeconds = 0 }
    };

    public CacheOperationHandlerTests()
    {
        var configManager = new Mock<IConfigurationManager>();
        configManager.Setup(c => c.Configuration).Returns(_config);
        _server = new ServerNoSql(
            new Mock<ILogger<ServerNoSql>>().Object, configManager.Object,
            new AdvGenNoSqlServer.Server.ApiDataService());
    }

    public async Task InitializeAsync() => await _server.StartAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _server.DisposeAsync();

    private async Task<CacheResponse> SendAsync(byte[] requestPayload)
    {
        var msg = new NoSqlMessage
        {
            MessageType = MessageType.CacheOperation,
            Payload = requestPayload,
            PayloadLength = requestPayload.Length
        };
        var response = await _server.HandleMessageForTestsAsync(msg, "test-conn");
        Assert.Equal(MessageType.CacheResponse, response.MessageType);
        return CacheProtocol.DecodeResponse(response.Payload!, response.PayloadLength);
    }

    [Fact]
    public async Task SetThenGet_RoundTrips()
    {
        var set = await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Set, "k", [1, 2], -1, CacheRequestFlags.None));
        Assert.Equal(CacheStatus.Ok, set.Status);

        var get = await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Get, "k", null, -1, CacheRequestFlags.None));
        Assert.Equal(CacheStatus.Ok, get.Status);
        Assert.Equal(new byte[] { 1, 2 }, get.Value);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNotFound()
    {
        var get = await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Get, "missing", null, -1, CacheRequestFlags.None));
        Assert.Equal(CacheStatus.NotFound, get.Status);
        Assert.Null(get.Value);
    }

    [Fact]
    public async Task Incr_OnString_ReturnsWrongType()
    {
        await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Set, "s", "abc"u8.ToArray(), -1, CacheRequestFlags.None));
        var incr = await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Incr, "s", null, -1, CacheRequestFlags.None));
        Assert.Equal(CacheStatus.WrongType, incr.Status);
    }

    [Fact]
    public async Task Incr_ReturnsNewValue()
    {
        var incr = await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Incr, "ctr", null, -1, CacheRequestFlags.None));
        Assert.Equal(1L, CacheProtocol.ReadInt64Value(incr));
    }

    [Fact]
    public async Task MalformedPayload_ReturnsErrorStatus_NotException()
    {
        var resp = await SendAsync([0xFF, 0x01]);
        Assert.Equal(CacheStatus.Error, resp.Status);
    }

    [Fact]
    public async Task Keys_ReturnsMatchingKeys()
    {
        await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Set, "u:1", [1], -1, CacheRequestFlags.None));
        await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Set, "u:2", [1], -1, CacheRequestFlags.None));
        var resp = await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Keys, "u:*", null, -1, CacheRequestFlags.None));
        var (cursor, keys) = CacheProtocol.ReadKeysValue(resp);
        Assert.Equal(0, cursor);
        Assert.Equal(2, keys.Count);
    }
}
```

Note: pick whichever internal-access mechanism `ClusterCommandTests.InvokeHandleCommandAsync` already uses (reflection helper or `InternalsVisibleTo`) and mirror it as `HandleMessageForTestsAsync`. `ClusterCommandTests` uses a reflection helper on the private `HandleMessageAsync` — so write the same reflection helper here and **rewrite the `_server.HandleMessageForTestsAsync(...)` calls shown in the test code above to use it**; don't paste the shown code verbatim.

Auth note: the spec says cache ops "require the same authenticated session state as document commands." Today the server enforces **no per-message auth on document commands** (`HandleCommandAsync` never checks session state; `HandleAuthenticationAsync` only issues a token). Parity therefore means adding **no auth gate** to the cache handler — do not invent one.

- [ ] **Step 6.2: Write failing CacheOnly-mode integration tests**

```csharp
// AdvGenNoSqlServer.Tests/CacheOnlyModeTests.cs
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;
using ServerNoSql = AdvGenNoSqlServer.Server.NoSqlServer;

namespace AdvGenNoSqlServer.Tests;

public class CacheOnlyModeTests
{
    private static (ServerNoSql Server, string StoragePath) CreateServer(string storageMode, int port)
    {
        var storagePath = Path.Combine(Path.GetTempPath(), "advgen-cacheonly-test-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1", Port = port,
            StorageMode = storageMode, StoragePath = storagePath,
            RequireAuthentication = false,
            MemoryManagement = new() { Plan = "Managed", MaxMemoryMB = 64, MaxMemoryPercent = 0 }
        };
        var configManager = new Mock<IConfigurationManager>();
        configManager.Setup(c => c.Configuration).Returns(config);
        var server = new ServerNoSql(
            new Mock<ILogger<ServerNoSql>>().Object, configManager.Object,
            new AdvGenNoSqlServer.Server.ApiDataService());
        return (server, storagePath);
    }

    [Fact]
    public async Task CacheOnly_DocumentCommandsWork_NoDiskWrites()
    {
        var (server, storagePath) = CreateServer("CacheOnly", 19292);
        await server.StartAsync(CancellationToken.None);
        try
        {
            var insert = NoSqlMessage.Create(MessageType.Command,
                JsonSerializer.Serialize(new { command = "insert", collection = "c", document = new { name = "x" } }));
            var response = await server.HandleMessageForTestsAsync(insert, "t");
            Assert.Equal(MessageType.Response, response.MessageType);

            Assert.False(Directory.Exists(storagePath),
                "CacheOnly mode must not create the storage directory");
        }
        finally
        {
            await server.DisposeAsync();
        }
        Assert.False(Directory.Exists(storagePath));
    }

    [Fact]
    public async Task Hybrid_StillCreatesStorageDirectory()
    {
        var (server, storagePath) = CreateServer("Hybrid", 19293);
        await server.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(Directory.Exists(storagePath));
        }
        finally
        {
            await server.DisposeAsync();
            Directory.Delete(storagePath, recursive: true);
        }
    }

    [Fact]
    public async Task UnknownStorageMode_FallsBackToHybrid()
    {
        var (server, storagePath) = CreateServer("Bogus", 19294);
        await server.StartAsync(CancellationToken.None);
        try { Assert.True(Directory.Exists(storagePath)); }
        finally { await server.DisposeAsync(); Directory.Delete(storagePath, true); }
    }

    [Fact]
    public async Task CacheOnly_RestartLosesDocuments()
    {
        var (server, storagePath) = CreateServer("CacheOnly", 19297);
        await server.StartAsync(CancellationToken.None);
        var insert = NoSqlMessage.Create(MessageType.Command,
            JsonSerializer.Serialize(new { command = "insert", collection = "c", document = new { name = "x" } }));
        await server.HandleMessageForTestsAsync(insert, "t");
        await server.DisposeAsync();

        var (server2, _) = CreateServer("CacheOnly", 19297);
        await server2.StartAsync(CancellationToken.None);
        try
        {
            var count = NoSqlMessage.Create(MessageType.Command,
                JsonSerializer.Serialize(new { command = "count", collection = "c" }));
            var response = await server2.HandleMessageForTestsAsync(count, "t");
            var json = JsonDocument.Parse(response.GetPayloadAsString());
            // count must be 0 — data did not survive the restart
            Assert.Contains("0", json.RootElement.GetProperty("data").ToString());
        }
        finally { await server2.DisposeAsync(); }
    }

    [Fact]
    public async Task CacheCommands_WorkInHybridModeToo()
    {
        var (server, storagePath) = CreateServer("Hybrid", 19295);
        await server.StartAsync(CancellationToken.None);
        try
        {
            var payload = CacheProtocol.EncodeRequest(CacheOp.Set, "k", [1], -1, CacheRequestFlags.None);
            var msg = new NoSqlMessage { MessageType = MessageType.CacheOperation, Payload = payload, PayloadLength = payload.Length };
            var response = await server.HandleMessageForTestsAsync(msg, "t");
            Assert.Equal(MessageType.CacheResponse, response.MessageType);
        }
        finally { await server.DisposeAsync(); Directory.Delete(storagePath, true); }
    }
}
```

- [ ] **Step 6.3: Run, verify failure**

- [ ] **Step 6.4: Implement `NoSqlServer` changes**

1. Field changes (line ~27):

```csharp
    private IDocumentStore? _documentStore;
    private CacheStore? _cacheStore;
```

(add `using AdvGenNoSqlServer.Core.MemoryManagement;` if needed; `AdvGenNoSqlServer.Storage` is already imported.)

2. `StartAsync` — replace the hybrid-store block (lines ~62-78) with mode selection:

```csharp
        var storageMode = string.Equals(config.StorageMode, "CacheOnly", StringComparison.OrdinalIgnoreCase)
            ? "CacheOnly" : "Hybrid";
        if (!string.Equals(config.StorageMode, storageMode, StringComparison.OrdinalIgnoreCase))
            _logger.LogWarning("Unknown StorageMode '{Mode}', falling back to Hybrid", config.StorageMode);

        if (storageMode == "CacheOnly")
        {
            _logger.LogWarning(
                "StorageMode=CacheOnly: all data is held in memory only and will be LOST on restart. No disk I/O will occur.");
            _documentStore = new DocumentStore();
        }
        else
        {
            var storagePath = config.StoragePath;
            if (string.IsNullOrEmpty(storagePath)) storagePath = "data";
            if (!Path.IsPathRooted(storagePath))
                storagePath = Path.Combine(AppContext.BaseDirectory, storagePath);

            _logger.LogInformation("Initializing hybrid storage at: {Path}", storagePath);
            var hybrid = new HybridDocumentStore(storagePath);
            await hybrid.InitializeAsync();
            _documentStore = hybrid;
            _logger.LogInformation("Hybrid storage initialized successfully");
        }

        _cacheStore = new CacheStore(config.MemoryManagement);
        _logger.LogInformation("KV cache initialized: plan={Plan}, maxMemoryMB={MaxMB}, eviction={Policy}",
            config.MemoryManagement.Plan, config.MemoryManagement.MaxMemoryMB, config.MemoryManagement.EvictionPolicy);
```

3. `StopAsync`/`DisposeAsync` — replace the typed flush block (lines ~114-121) with capability checks:

```csharp
        if (_documentStore is HybridDocumentStore hybrid)
        {
            _logger.LogInformation("Flushing pending writes to disk...");
            await hybrid.FlushAsync();
        }
        if (_documentStore is IAsyncDisposable disposableStore)
            await disposableStore.DisposeAsync();
        _documentStore = null;

        _cacheStore?.Dispose();
        _cacheStore = null;
```

Check `DisposeAsync` at the bottom of the file for a duplicate of this logic and apply the same change there.

4. `HandleMessageAsync` — add a case:

```csharp
            MessageType.CacheOperation => HandleCacheOperationAsync(message, connectionId),
```

5. New handler (place after `HandleBulkOperationAsync`):

```csharp
    private Task<NoSqlMessage> HandleCacheOperationAsync(NoSqlMessage message, string connectionId)
    {
        if (_cacheStore == null)
            return Task.FromResult(CacheReply(CacheProtocol.EncodeErrorResponse(CacheStatus.Error, "Cache not initialized")));
        if (message.Payload == null || message.PayloadLength == 0)
            return Task.FromResult(CacheReply(CacheProtocol.EncodeErrorResponse(CacheStatus.Error, "Empty cache request")));

        byte[] responsePayload;
        try
        {
            var req = CacheProtocol.DecodeRequest(message.Payload, message.PayloadLength);
            responsePayload = Execute(req);
        }
        catch (ProtocolException ex)
        {
            responsePayload = CacheProtocol.EncodeErrorResponse(CacheStatus.Error, ex.Message);
        }
        catch (CacheValidationException ex)
        {
            responsePayload = CacheProtocol.EncodeErrorResponse(CacheStatus.Error, ex.Message);
        }
        catch (CacheWrongTypeException ex)
        {
            responsePayload = CacheProtocol.EncodeErrorResponse(CacheStatus.WrongType, ex.Message);
        }
        catch (OverflowException)
        {
            responsePayload = CacheProtocol.EncodeErrorResponse(CacheStatus.Error, "Counter overflow");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache operation failed for connection {ConnectionId}", connectionId);
            responsePayload = CacheProtocol.EncodeErrorResponse(CacheStatus.Error, "Internal cache error");
        }
        return Task.FromResult(CacheReply(responsePayload));

        byte[] Execute(CacheRequest req)
        {
            var cache = _cacheStore!;
            switch (req.Op)
            {
                case CacheOp.Get:
                {
                    var value = cache.Get(req.Key);
                    return value == null
                        ? CacheProtocol.EncodeResponse(CacheStatus.NotFound, null)
                        : CacheProtocol.EncodeResponse(CacheStatus.Ok, value);
                }
                case CacheOp.Set:
                {
                    if (req.Value == null)
                        return CacheProtocol.EncodeErrorResponse(CacheStatus.Error, "SET requires a value");
                    var condition = req.Flags.HasFlag(CacheRequestFlags.Nx) ? CacheSetCondition.NotExists
                        : req.Flags.HasFlag(CacheRequestFlags.Xx) ? CacheSetCondition.Exists
                        : CacheSetCondition.None;
                    TimeSpan? ttl = req.TtlSeconds >= 0 ? TimeSpan.FromSeconds(req.TtlSeconds) : null;
                    bool set = cache.Set(req.Key, req.Value, ttl, condition);
                    return set
                        ? CacheProtocol.EncodeResponse(CacheStatus.Ok, null)
                        : CacheProtocol.EncodeResponse(CacheStatus.NotFound, null); // NX/XX unmet (spec §3)
                }
                case CacheOp.Del: return CacheProtocol.EncodeBoolResponse(cache.Delete(req.Key));
                case CacheOp.Exists: return CacheProtocol.EncodeBoolResponse(cache.Exists(req.Key));
                case CacheOp.Expire:
                {
                    if (req.TtlSeconds < 0)
                        return CacheProtocol.EncodeErrorResponse(CacheStatus.Error, "EXPIRE requires ttlSeconds >= 0");
                    return CacheProtocol.EncodeBoolResponse(cache.Expire(req.Key, TimeSpan.FromSeconds(req.TtlSeconds)));
                }
                case CacheOp.Ttl:
                {
                    var result = cache.Ttl(req.Key);
                    if (!result.Exists) return CacheProtocol.EncodeResponse(CacheStatus.NotFound, null);
                    long seconds = result.Remaining.HasValue ? (long)result.Remaining.Value.TotalSeconds : -1;
                    return CacheProtocol.EncodeInt64Response(CacheStatus.Ok, seconds);
                }
                case CacheOp.Incr: return CacheProtocol.EncodeInt64Response(CacheStatus.Ok, cache.Increment(req.Key, 1));
                case CacheOp.Decr: return CacheProtocol.EncodeInt64Response(CacheStatus.Ok, cache.Increment(req.Key, -1));
                case CacheOp.IncrBy:
                {
                    if (req.Value is not { Length: 8 })
                        return CacheProtocol.EncodeErrorResponse(CacheStatus.Error, "INCRBY requires an int64 delta");
                    long delta = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(req.Value);
                    return CacheProtocol.EncodeInt64Response(CacheStatus.Ok, cache.Increment(req.Key, delta));
                }
                case CacheOp.MGet:
                    return CacheProtocol.EncodeMGetResponse(cache.MGet(req.BatchKeys ?? Array.Empty<string>()));
                case CacheOp.MSet:
                {
                    cache.MSet(req.BatchPairs ?? Array.Empty<KeyValuePair<string, byte[]>>());
                    return CacheProtocol.EncodeResponse(CacheStatus.Ok, null);
                }
                case CacheOp.Keys:
                    return CacheProtocol.EncodeKeysResponse(0, cache.Keys(req.Key));
                case CacheOp.Scan:
                {
                    var (next, keys) = cache.Scan(req.Key, req.ScanCursor, req.ScanCount);
                    return CacheProtocol.EncodeKeysResponse(next, keys);
                }
                case CacheOp.Flush:
                {
                    cache.Flush();
                    return CacheProtocol.EncodeResponse(CacheStatus.Ok, null);
                }
                case CacheOp.Stats:
                {
                    var statsJson = JsonSerializer.SerializeToUtf8Bytes(cache.GetStats());
                    return CacheProtocol.EncodeResponse(CacheStatus.Ok, statsJson);
                }
                default:
                    return CacheProtocol.EncodeErrorResponse(CacheStatus.Error, $"Unknown cache op {(byte)req.Op}");
            }
        }

        static NoSqlMessage CacheReply(byte[] payload) => new()
        {
            MessageType = MessageType.CacheResponse,
            Payload = payload,
            PayloadLength = payload.Length
        };
    }
```

Also expose the test hook the tests use, mirroring the existing `ClusterCommandTests` mechanism (if it uses reflection, no production change is needed; if not, add `internal Task<NoSqlMessage> HandleMessageForTestsAsync(NoSqlMessage m, string c) => HandleMessageAsync(m, c);` plus `InternalsVisibleTo` if missing).

- [ ] **Step 6.5: Run new tests + full test suite, verify pass, no regressions**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release`
Expected: all PASS (note any pre-existing failures on master before blaming this change — run `git stash` comparison if unsure).

- [ ] **Step 6.6: Commit**

```bash
git add AdvGenNoSqlServer.Server/NoSqlServer.cs AdvGenNoSqlServer.Tests/CacheOperationHandlerTests.cs AdvGenNoSqlServer.Tests/CacheOnlyModeTests.cs
git commit -m "feat: add CacheOnly storage mode and cache operation handler to server"
```

---

### Task 7: Client API — `client.Cache.*`

**Files:**
- Create: `AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Cache.cs`
- Test: `AdvGenNoSqlServer.Tests/CacheClientTests.cs`

- [ ] **Step 7.1: Write failing end-to-end tests** (real server + real client over TCP; look at existing client-vs-server tests such as `ClientGetFixTests` / `LoadTests` for the startup pattern and copy it — unique port, `RequireAuthentication = false`)

```csharp
// AdvGenNoSqlServer.Tests/CacheClientTests.cs
using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Core.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ServerNoSql = AdvGenNoSqlServer.Server.NoSqlServer;

namespace AdvGenNoSqlServer.Tests;

public class CacheClientTests : IAsyncLifetime
{
    private const int Port = 19296;
    private ServerNoSql _server = null!;
    private AdvGenNoSqlClient _client = null!;

    public async Task InitializeAsync()
    {
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1", Port = Port,
            StorageMode = "CacheOnly", RequireAuthentication = false,
            MemoryManagement = new() { Plan = "Managed", MaxMemoryMB = 64, MaxMemoryPercent = 0, DefaultTtlSeconds = 0 }
        };
        var configManager = new Mock<IConfigurationManager>();
        configManager.Setup(c => c.Configuration).Returns(config);
        _server = new ServerNoSql(new Mock<ILogger<ServerNoSql>>().Object, configManager.Object,
            new AdvGenNoSqlServer.Server.ApiDataService());
        await _server.StartAsync(CancellationToken.None);

        _client = new AdvGenNoSqlClient($"127.0.0.1:{Port}");
        await _client.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task SetGet_Bytes_RoundTrip()
    {
        Assert.True(await _client.Cache.SetAsync("k", [1, 2, 3]));
        Assert.Equal(new byte[] { 1, 2, 3 }, await _client.Cache.GetAsync("k"));
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull() =>
        Assert.Null(await _client.Cache.GetAsync("missing"));

    [Fact]
    public async Task SetGet_String_RoundTrip()
    {
        await _client.Cache.SetStringAsync("greet", "hello");
        Assert.Equal("hello", await _client.Cache.GetStringAsync("greet"));
    }

    [Fact]
    public async Task Set_WithTtl_TtlReadsBack()
    {
        await _client.Cache.SetAsync("t", [1], TimeSpan.FromMinutes(5));
        var ttl = await _client.Cache.TtlAsync("t");
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value.TotalSeconds, 290, 300);
    }

    [Fact]
    public async Task Ttl_NoExpiry_ReturnsNull_ButKeyExists()
    {
        await _client.Cache.SetAsync("f", [1]);
        Assert.Null(await _client.Cache.TtlAsync("f"));
        Assert.True(await _client.Cache.ExistsAsync("f"));
    }

    [Fact]
    public async Task Ttl_MissingKey_Throws()
        => await Assert.ThrowsAsync<CacheKeyNotFoundException>(() => _client.Cache.TtlAsync("missing"));

    [Fact]
    public async Task SetNx_ReturnsFalseWhenPresent()
    {
        Assert.True(await _client.Cache.SetAsync("nx", [1], condition: CacheSetCondition.NotExists));
        Assert.False(await _client.Cache.SetAsync("nx", [2], condition: CacheSetCondition.NotExists));
    }

    [Fact]
    public async Task IncrDecr_Work()
    {
        Assert.Equal(1, await _client.Cache.IncrAsync("c"));
        Assert.Equal(11, await _client.Cache.IncrByAsync("c", 10));
        Assert.Equal(10, await _client.Cache.DecrAsync("c"));
    }

    [Fact]
    public async Task Incr_OnString_ThrowsWrongType()
    {
        await _client.Cache.SetStringAsync("s", "abc");
        await Assert.ThrowsAsync<CacheWrongTypeException>(() => _client.Cache.IncrAsync("s"));
    }

    [Fact]
    public async Task MGetMSet_RoundTrip()
    {
        await _client.Cache.MSetAsync(new Dictionary<string, byte[]> { ["a"] = [1], ["b"] = [2] });
        var result = await _client.Cache.MGetAsync(["a", "missing", "b"]);
        Assert.Equal(new byte[] { 1 }, result["a"]);
        Assert.Null(result["missing"]);
        Assert.Equal(new byte[] { 2 }, result["b"]);
    }

    [Fact]
    public async Task Scan_AutoIteratesToCompletion()
    {
        for (int i = 0; i < 30; i++) await _client.Cache.SetAsync($"scan:{i}", [1]);
        var keys = await _client.Cache.ScanAsync("scan:*", pageSize: 7);
        Assert.Equal(30, keys.Count);
    }

    [Fact]
    public async Task DeleteExpireFlushStats_Work()
    {
        await _client.Cache.SetAsync("d", [1]);
        Assert.True(await _client.Cache.ExpireAsync("d", TimeSpan.FromMinutes(1)));
        Assert.True(await _client.Cache.DeleteAsync("d"));
        Assert.False(await _client.Cache.DeleteAsync("d"));

        await _client.Cache.SetAsync("x", [1]);
        var stats = await _client.Cache.StatsAsync();
        Assert.True(stats.EntryCount >= 1);

        await _client.Cache.FlushAsync();
        Assert.False(await _client.Cache.ExistsAsync("x"));
    }
}
```

- [ ] **Step 7.2: Run, verify compile failure**

- [ ] **Step 7.3: Implement `AdvGenNoSqlClient.Cache.cs`**

```csharp
// AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Cache.cs
using System.Text;
using System.Text.Json;
using AdvGenNoSqlServer.Core.MemoryManagement;
using AdvGenNoSqlServer.Network;

namespace AdvGenNoSqlServer.Client
{
    /// <summary>SET conditions matching Redis NX/XX (client-side mirror).</summary>
    public enum CacheSetCondition { None = 0, NotExists = 1, Exists = 2 }

    /// <summary>Server reported the value has the wrong type for the operation (e.g. INCR on a string).</summary>
    public class CacheWrongTypeException : NoSqlClientException
    {
        public CacheWrongTypeException(string message) : base(message) { }
    }

    /// <summary>Server reported a cache error (validation, overflow, internal).</summary>
    public class CacheException : NoSqlClientException
    {
        public CacheException(string message) : base(message) { }
    }

    /// <summary>Key did not exist for an operation that requires it (TTL).</summary>
    public class CacheKeyNotFoundException : NoSqlClientException
    {
        public CacheKeyNotFoundException(string key) : base($"Cache key not found: {key}") { }
    }

    public partial class AdvGenNoSqlClient
    {
        private CacheOperations? _cacheOperations;

        /// <summary>Redis-style KV cache operations.</summary>
        public CacheOperations Cache => _cacheOperations ??= new CacheOperations(this);

        /// <summary>
        /// KV cache API. Byte arrays are the primitive; string overloads are UTF-8 wrappers.
        /// NotFound maps to null/false returns; WrongType/Error map to exceptions (spec §4).
        /// </summary>
        public sealed class CacheOperations
        {
            private readonly AdvGenNoSqlClient _client;
            internal CacheOperations(AdvGenNoSqlClient client) => _client = client;

            private async Task<Network.CacheResponse> SendAsync(byte[] requestPayload, CancellationToken ct)
            {
                _client.EnsureConnected();
                var message = new NoSqlMessage
                {
                    MessageType = Network.MessageType.CacheOperation,
                    Payload = requestPayload,
                    PayloadLength = requestPayload.Length
                };
                var response = await _client.SendAndReceiveAsync(message, ct);
                if (response.MessageType != Network.MessageType.CacheResponse)
                    throw new NoSqlProtocolException($"Unexpected response type {response.MessageType}");
                var decoded = CacheProtocol.DecodeResponse(response.Payload ?? [], response.PayloadLength);
                if (decoded.Status == CacheStatus.WrongType)
                    throw new CacheWrongTypeException(decoded.ErrorMessage ?? "Wrong value type");
                if (decoded.Status == CacheStatus.Error)
                    throw new CacheException(decoded.ErrorMessage ?? "Cache error");
                return decoded;
            }

            public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
            {
                var resp = await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Get, key, null, -1, CacheRequestFlags.None), ct);
                return resp.Status == CacheStatus.NotFound ? null : resp.Value;
            }

            public async Task<string?> GetStringAsync(string key, CancellationToken ct = default)
            {
                var bytes = await GetAsync(key, ct);
                return bytes == null ? null : Encoding.UTF8.GetString(bytes);
            }

            /// <summary>Returns false only when an NX/XX condition was not met.</summary>
            public async Task<bool> SetAsync(string key, byte[] value, TimeSpan? ttl = null,
                CacheSetCondition condition = CacheSetCondition.None, CancellationToken ct = default)
            {
                var flags = condition switch
                {
                    CacheSetCondition.NotExists => CacheRequestFlags.Nx,
                    CacheSetCondition.Exists => CacheRequestFlags.Xx,
                    _ => CacheRequestFlags.None
                };
                int ttlSeconds = ttl.HasValue ? (int)ttl.Value.TotalSeconds : -1;
                var resp = await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Set, key, value, ttlSeconds, flags), ct);
                return resp.Status == CacheStatus.Ok;
            }

            public Task<bool> SetStringAsync(string key, string value, TimeSpan? ttl = null,
                CacheSetCondition condition = CacheSetCondition.None, CancellationToken ct = default)
                => SetAsync(key, Encoding.UTF8.GetBytes(value), ttl, condition, ct);

            public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
                => ReadBool(await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Del, key, null, -1, CacheRequestFlags.None), ct));

            public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
                => ReadBool(await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Exists, key, null, -1, CacheRequestFlags.None), ct));

            public async Task<bool> ExpireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
                => ReadBool(await SendAsync(CacheProtocol.EncodeRequest(
                    CacheOp.Expire, key, null, (int)ttl.TotalSeconds, CacheRequestFlags.None), ct));

            /// <summary>Null = key exists with no expiry. Throws CacheKeyNotFoundException if missing.</summary>
            public async Task<TimeSpan?> TtlAsync(string key, CancellationToken ct = default)
            {
                var resp = await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Ttl, key, null, -1, CacheRequestFlags.None), ct);
                if (resp.Status == CacheStatus.NotFound) throw new CacheKeyNotFoundException(key);
                long seconds = CacheProtocol.ReadInt64Value(resp);
                return seconds < 0 ? null : TimeSpan.FromSeconds(seconds);
            }

            public async Task<long> IncrAsync(string key, CancellationToken ct = default)
                => CacheProtocol.ReadInt64Value(await SendAsync(
                    CacheProtocol.EncodeRequest(CacheOp.Incr, key, null, -1, CacheRequestFlags.None), ct));

            public async Task<long> DecrAsync(string key, CancellationToken ct = default)
                => CacheProtocol.ReadInt64Value(await SendAsync(
                    CacheProtocol.EncodeRequest(CacheOp.Decr, key, null, -1, CacheRequestFlags.None), ct));

            public async Task<long> IncrByAsync(string key, long delta, CancellationToken ct = default)
                => CacheProtocol.ReadInt64Value(await SendAsync(
                    CacheProtocol.EncodeInt64Request(CacheOp.IncrBy, key, delta), ct));

            public async Task<IReadOnlyDictionary<string, byte[]?>> MGetAsync(
                IReadOnlyList<string> keys, CancellationToken ct = default)
            {
                var resp = await SendAsync(CacheProtocol.EncodeMGetRequest(keys), ct);
                var values = CacheProtocol.ReadMGetValues(resp);
                var result = new Dictionary<string, byte[]?>(keys.Count);
                for (int i = 0; i < keys.Count; i++) result[keys[i]] = values[i];
                return result;
            }

            public Task MSetAsync(IReadOnlyDictionary<string, byte[]> pairs, CancellationToken ct = default)
                => SendAsync(CacheProtocol.EncodeMSetRequest(pairs.ToList()), ct);

            /// <summary>Auto-iterates the server cursor to completion (spec §4).</summary>
            public async Task<IReadOnlyList<string>> ScanAsync(string pattern, int pageSize = 100, CancellationToken ct = default)
            {
                var all = new List<string>();
                long cursor = 0;
                do
                {
                    var resp = await SendAsync(CacheProtocol.EncodeScanRequest(pattern, cursor, pageSize), ct);
                    var (next, keys) = CacheProtocol.ReadKeysValue(resp);
                    all.AddRange(keys);
                    cursor = next;
                } while (cursor != 0);
                return all;
            }

            public async Task<IReadOnlyList<string>> KeysAsync(string pattern, CancellationToken ct = default)
            {
                var resp = await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Keys, pattern, null, -1, CacheRequestFlags.None), ct);
                return CacheProtocol.ReadKeysValue(resp).Keys;
            }

            public Task FlushAsync(CancellationToken ct = default)
                => SendAsync(CacheProtocol.EncodeRequest(CacheOp.Flush, string.Empty, null, -1, CacheRequestFlags.None), ct);

            public async Task<MemoryEngineStats> StatsAsync(CancellationToken ct = default)
            {
                var resp = await SendAsync(CacheProtocol.EncodeRequest(CacheOp.Stats, string.Empty, null, -1, CacheRequestFlags.None), ct);
                return JsonSerializer.Deserialize<MemoryEngineStats>(resp.Value!)
                    ?? throw new NoSqlProtocolException("Invalid stats payload");
            }

            private static bool ReadBool(Network.CacheResponse resp)
                => resp.Value is [1];
        }
    }
}
```

Implementation notes:
- The nested class accesses the parent's private `SendAndReceiveAsync` and `EnsureConnected` — nested classes can use the enclosing class's private members, so no visibility changes are needed.
- `CacheOp.Keys` with an empty pattern key: `Flush`/`Stats` pass `string.Empty` as key — `EncodeRequest` allows empty keys (validation is server-side per-op).
- `MemoryEngineStats` must be JSON-serializable both ways; check it has public setters (it uses init-style properties in `MemoryEngineStats.cs` — if `JsonSerializer.Deserialize` fails, add `[JsonInclude]` or plain setters).
- If a name collision arises between `AdvGenNoSqlServer.Client.CacheSetCondition` and `AdvGenNoSqlServer.Storage.CacheSetCondition` in tests, alias in the test file (`using CacheSetCondition = AdvGenNoSqlServer.Client.CacheSetCondition;`). The client project must NOT reference Storage.

- [ ] **Step 7.4: Run client tests, verify pass**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~CacheClientTests"`

- [ ] **Step 7.5: Commit**

```bash
git add AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Cache.cs AdvGenNoSqlServer.Tests/CacheClientTests.cs
git commit -m "feat: add client.Cache API for Redis-style KV operations"
```

---

### Task 8: Benchmarks

**Files:**
- Create: `AdvGenNoSqlServer.Benchmarks/CacheStoreBenchmarks.cs`

- [ ] **Step 8.1: Look at an existing benchmark class** in `AdvGenNoSqlServer.Benchmarks/` and mirror its structure (`[MemoryDiagnoser]`, `Program.cs` runner registration if it uses a manual switcher).

- [ ] **Step 8.2: Implement**

```csharp
// AdvGenNoSqlServer.Benchmarks/CacheStoreBenchmarks.cs
using AdvGenNoSqlServer.Core.MemoryManagement;
using AdvGenNoSqlServer.Storage;
using BenchmarkDotNet.Attributes;

namespace AdvGenNoSqlServer.Benchmarks;

[MemoryDiagnoser]
public class CacheStoreBenchmarks
{
    [Params("Managed", "Native")]
    public string Plan { get; set; } = "Managed";

    private CacheStore _cache = null!;
    private byte[] _value = null!;
    private int _counter;

    [GlobalSetup]
    public void Setup()
    {
        _cache = new CacheStore(new MemoryManagementConfiguration
        {
            Plan = Plan, MaxMemoryMB = 512, MaxMemoryPercent = 0, DefaultTtlSeconds = 0
        });
        _value = new byte[256];
        Random.Shared.NextBytes(_value);
        for (int i = 0; i < 10_000; i++) _cache.Set($"warm:{i}", _value);
    }

    [GlobalCleanup]
    public void Cleanup() => _cache.Dispose();

    [Benchmark]
    public byte[]? Get() => _cache.Get($"warm:{(_counter++ & 8191)}");

    [Benchmark]
    public bool Set() => _cache.Set($"bench:{(_counter++ & 1023)}", _value);

    [Benchmark]
    public long Incr() => _cache.Increment("bench:counter", 1);
}
```

Register the class in the benchmarks `Program.cs` if it uses explicit registration.

- [ ] **Step 8.3: Verify it builds and runs briefly**

Run: `dotnet run --project AdvGenNoSqlServer.Benchmarks -c Release -- --filter "*CacheStoreBenchmarks*" --job Dry`
Expected: benchmark executes (Dry job = 1 iteration; do not run the full suite in CI).

- [ ] **Step 8.4: Commit**

```bash
git add AdvGenNoSqlServer.Benchmarks/
git commit -m "feat: add CacheStore throughput benchmarks"
```

---

### Task 9: Documentation + final verification

**Files:**
- Modify: `README.md` (Features section + a short "Cache mode" usage section)
- Modify: `AdvGenNoSqlServer.Client/README.md` (add `client.Cache` examples)

- [ ] **Step 9.1: Update READMEs**

Add to root `README.md` features list: `- **Redis-style KV Cache**: In-memory key-value cache with TTL, eviction policies (LRU/LFU/TTL), counters, and batch operations` and `- **Cache-Only Mode**: run the whole server in RAM ("storageMode": "CacheOnly") — no disk I/O, Redis-like deployment`. Add a short config + client usage snippet mirroring the spec's examples. Update the client README with `client.Cache.SetAsync/GetAsync/IncrAsync` examples.

- [ ] **Step 9.2: Full solution build + full test run**

```bash
dotnet build AdvGenNoSqlServer.sln -c Release
dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release
```
Expected: build clean, all tests pass (compare against master baseline for pre-existing failures).

- [ ] **Step 9.3: Use @superpowers:verification-before-completion, then commit**

```bash
git add README.md AdvGenNoSqlServer.Client/README.md
git commit -m "docs: document cache-only mode and KV cache API"
```

- [ ] **Step 9.4: Use @superpowers:requesting-code-review to review the whole feature branch, then @superpowers:finishing-a-development-branch**

---

## Execution notes

- Tasks 1–4 are independent of the server and can be done strictly in order 1 → 2 → 3 → 4 (each builds on the previous).
- Task 5 is independent of 1–4. Tasks 6–7 depend on everything before them.
- Ports 19291–19296 are used by the new test classes to avoid collisions with existing tests; if any existing test already binds one of these, pick a free one nearby.
- If `NativeMemoryStorageEngine` tests are flaky on this machine (native allocation), run them in isolation before assuming a regression.
