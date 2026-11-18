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
    /// <summary>Gets the creation timestamp in Unix milliseconds.</summary>
    public long Timestamp { get; }

    /// <summary>
    /// Allocates unmanaged memory and copies the given bytes into it
    /// </summary>
    /// <param name="data">The bytes to store</param>
    public NativeCacheEntry(ReadOnlySpan<byte> data)
    {
        _size = data.Length;
        _dataPtr = (byte*)NativeMemory.Alloc((nuint)_size);
        fixed (byte* src = data)
            Unsafe.CopyBlock(_dataPtr, src, (uint)_size);
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>Gets the size of the stored data in bytes.</summary>
    public int Size => _size;

    /// <summary>
    /// Gets a span over the unmanaged data; valid only until the entry is disposed
    /// </summary>
    /// <returns>A read-only span of the stored bytes</returns>
    public ReadOnlySpan<byte> AsSpan() => new(_dataPtr, _size);

    /// <summary>
    /// Frees the unmanaged memory; safe to call multiple times
    /// </summary>
    public void Dispose()
    {
        if (_dataPtr != null)
        {
            NativeMemory.Free(_dataPtr);
            _dataPtr = null;
        }
    }
}
