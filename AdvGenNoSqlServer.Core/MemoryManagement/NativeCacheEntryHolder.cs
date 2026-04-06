// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
namespace AdvGenNoSqlServer.Core.MemoryManagement;

/// <summary>
/// Reference-type wrapper for NativeCacheEntry.
/// Prevents the struct from being copied (which would duplicate raw pointer ownership).
/// The dictionary owns the holder; callers never retain a holder reference past TryGet.
/// </summary>
internal sealed class NativeCacheEntryHolder : IDisposable
{
    private NativeCacheEntry _entry;
    private int _disposed;

    public NativeCacheEntryHolder(ReadOnlySpan<byte> data) =>
        _entry = new NativeCacheEntry(data);

    public int Size => _entry.Size;

    /// <summary>
    /// Caller must copy the bytes before leaving the call frame.
    /// The span is backed by unmanaged memory owned by this holder.
    /// </summary>
    public ReadOnlySpan<byte> AsSpan()
    {
        if (_disposed != 0)
            throw new ObjectDisposedException(nameof(NativeCacheEntryHolder));
        return _entry.AsSpan();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _entry.Dispose();
    }
}
