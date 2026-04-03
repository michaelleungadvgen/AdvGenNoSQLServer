// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AdvGenNoSqlServer.Core.MemoryManagement;

/// <summary>
/// Low-level allocation primitive. Stores bytes in unmanaged heap memory.
/// Always held inside NativeCacheEntryHolder — never stored directly in collections.
/// </summary>
public unsafe struct NativeCacheEntry : IDisposable
{
    private byte* _dataPtr;
    private readonly int _size;
    public long Timestamp { get; }

    public NativeCacheEntry(ReadOnlySpan<byte> data)
    {
        _size = data.Length;
        _dataPtr = (byte*)NativeMemory.Alloc((nuint)_size);
        fixed (byte* src = data)
            Unsafe.CopyBlock(_dataPtr, src, (uint)_size);
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public int Size => _size;

    public ReadOnlySpan<byte> AsSpan() => new(_dataPtr, _size);

    public void Dispose()
    {
        if (_dataPtr != null)
        {
            NativeMemory.Free(_dataPtr);
            _dataPtr = null;
        }
    }
}
