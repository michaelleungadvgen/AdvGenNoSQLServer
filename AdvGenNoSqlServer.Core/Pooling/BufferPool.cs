// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Buffers;

namespace AdvGenNoSqlServer.Core.Pooling;

/// <summary>
/// Provides pooled byte arrays for efficient buffer management.
/// Uses ArrayPool&lt;byte&gt; for optimal performance with minimal GC pressure.
/// </summary>
public sealed class BufferPool : IDisposable
{
    private static readonly Lazy<BufferPool> _default = new(() => new BufferPool());
    private readonly ArrayPool<byte> _arrayPool;
    private long _rented;
    private long _returned;
    private long _bytesAllocated;
    private bool _disposed;

    /// <summary>
    /// Gets the default global buffer pool instance.
    /// </summary>
    public static BufferPool Default => _default.Value;

    /// <summary>
    /// Gets the default buffer size (64 KB).
    /// </summary>
    public const int DefaultBufferSize = 64 * 1024;

    /// <summary>
    /// Gets the maximum buffer size (1 MB).
    /// </summary>
    public const int MaxBufferSize = 1024 * 1024;

    /// <summary>
    /// Gets the total number of buffers rented.
    /// </summary>
    public long TotalRented => Interlocked.Read(ref _rented);

    /// <summary>
    /// Gets the total number of buffers returned.
    /// </summary>
    public long TotalReturned => Interlocked.Read(ref _returned);

    /// <summary>
    /// Gets the total number of buffers currently in use.
    /// </summary>
    public long InUse => TotalRented - TotalReturned;

    /// <summary>
    /// Gets the total bytes allocated by the pool.
    /// </summary>
    public long TotalBytesAllocated => Interlocked.Read(ref _bytesAllocated);

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferPool"/> class with default settings.
    /// </summary>
    public BufferPool() : this(MaxBufferSize, 100)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferPool"/> class.
    /// </summary>
    /// <param name="maxArrayLength">The maximum length of an array to pool.</param>
    /// <param name="maxArraysPerBucket">The maximum number of arrays to retain per bucket.</param>
    public BufferPool(int maxArrayLength, int maxArraysPerBucket)
    {
        _arrayPool = ArrayPool<byte>.Create(maxArrayLength, maxArraysPerBucket);
    }

    /// <summary>
    /// Rents a buffer of at least the specified minimum length.
    /// </summary>
    /// <param name="minimumLength">The minimum length of the buffer.</param>
    /// <returns>A byte array with at least the specified length.</returns>
    public byte[] Rent(int minimumLength)
    {
        ThrowIfDisposed();

        if (minimumLength <= 0)
            throw new ArgumentException("Minimum length must be greater than 0.", nameof(minimumLength));

        if (minimumLength > MaxBufferSize)
            throw new ArgumentException($"Buffer size exceeds maximum ({MaxBufferSize}).", nameof(minimumLength));

        Interlocked.Increment(ref _rented);
        var buffer = _arrayPool.Rent(minimumLength);
        Interlocked.Add(ref _bytesAllocated, buffer.Length);
        return buffer;
    }

    /// <summary>
    /// Rents a buffer with the default buffer size.
    /// </summary>
    /// <returns>A byte array with at least the default buffer size.</returns>
    public byte[] Rent()
    {
        return Rent(DefaultBufferSize);
    }

    /// <summary>
    /// Returns a buffer to the pool.
    /// </summary>
    /// <param name="buffer">The buffer to return.</param>
    /// <param name="clearArray">Whether to clear the array before returning it to the pool.</param>
    public void Return(byte[] buffer, bool clearArray = false)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        ThrowIfDisposed();

        Interlocked.Increment(ref _returned);
        Interlocked.Add(ref _bytesAllocated, -buffer.Length);
        _arrayPool.Return(buffer, clearArray);
    }

    /// <summary>
    /// Creates a pooled memory wrapper that automatically returns the buffer when disposed.
    /// </summary>
    /// <param name="minimumLength">The minimum length of the buffer.</param>
    /// <returns>A pooled memory instance.</returns>
    public PooledMemory RentMemory(int minimumLength)
    {
        var buffer = Rent(minimumLength);
        return new PooledMemory(buffer, this);
    }

    /// <summary>
    /// Creates a pooled memory wrapper with default buffer size.
    /// </summary>
    /// <returns>A pooled memory instance.</returns>
    public PooledMemory RentMemory()
    {
        return RentMemory(DefaultBufferSize);
    }

    /// <summary>
    /// Resets all statistics counters.
    /// </summary>
    public void ResetStatistics()
    {
        Interlocked.Exchange(ref _rented, 0);
        Interlocked.Exchange(ref _returned, 0);
        Interlocked.Exchange(ref _bytesAllocated, 0);
    }

    /// <summary>
    /// Disposes the buffer pool.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BufferPool));
    }
}

/// <summary>
/// Represents a pooled memory buffer that automatically returns to the pool when disposed.
/// </summary>
public readonly struct PooledMemory : IDisposable
{
    private readonly BufferPool? _pool;

    /// <summary>
    /// Gets the underlying byte array.
    /// </summary>
    public byte[] Buffer { get; }

    /// <summary>
    /// Gets the actual length of the rented buffer (may be larger than requested).
    /// </summary>
    public int Length => Buffer?.Length ?? 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledMemory"/> struct.
    /// </summary>
    /// <param name="buffer">The byte array.</param>
    /// <param name="pool">The pool to return to.</param>
    public PooledMemory(byte[] buffer, BufferPool pool)
    {
        Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _pool = pool;
    }

    /// <summary>
    /// Returns the buffer to the pool.
    /// </summary>
    public void Dispose()
    {
        _pool?.Return(Buffer);
    }

    /// <summary>
    /// Creates a span over the buffer.
    /// </summary>
    /// <returns>A span over the entire buffer.</returns>
    public Span<byte> AsSpan() => Buffer;

    /// <summary>
    /// Creates a span over a portion of the buffer.
    /// </summary>
    /// <param name="start">The start index.</param>
    /// <param name="length">The length of the span.</param>
    /// <returns>A span over the specified portion.</returns>
    public Span<byte> AsSpan(int start, int length) => Buffer.AsSpan(start, length);

    /// <summary>
    /// Creates a memory over the buffer.
    /// </summary>
    /// <returns>A memory over the entire buffer.</returns>
    public Memory<byte> AsMemory() => Buffer;

    /// <summary>
    /// Creates a memory over a portion of the buffer.
    /// </summary>
    /// <param name="start">The start index.</param>
    /// <param name="length">The length of the memory.</param>
    /// <returns>A memory over the specified portion.</returns>
    public Memory<byte> AsMemory(int start, int length) => Buffer.AsMemory(start, length);
}
