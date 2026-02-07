// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Pooling;

/// <summary>
/// Represents a pooled object that automatically returns to the pool when disposed.
/// This is the recommended way to use object pools with 'using' statements.
/// </summary>
/// <typeparam name="T">The type of the pooled object.</typeparam>
public readonly struct PooledObject<T> : IDisposable where T : class
{
    private readonly IObjectPool<T>? _pool;

    /// <summary>
    /// Gets the pooled object.
    /// </summary>
    public T Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledObject{T}"/> struct.
    /// </summary>
    /// <param name="pool">The pool to return to.</param>
    public PooledObject(IObjectPool<T> pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        Value = pool.Rent();
    }

    /// <summary>
    /// Returns the object to the pool.
    /// </summary>
    public void Dispose()
    {
        if (Value != null && _pool != null)
        {
            _pool.Return(Value);
        }
    }

    /// <summary>
    /// Implicitly converts to the underlying object type.
    /// </summary>
    /// <param name="pooled">The pooled object wrapper.</param>
    public static implicit operator T(PooledObject<T> pooled) => pooled.Value;
}

/// <summary>
/// Extension methods for object pools to enable easier usage with PooledObject.
/// </summary>
public static class ObjectPoolExtensions
{
    /// <summary>
    /// Rents an object from the pool wrapped in a PooledObject for automatic return.
    /// </summary>
    /// <typeparam name="T">The type of the pooled object.</typeparam>
    /// <param name="pool">The object pool.</param>
    /// <returns>A pooled object wrapper that returns to pool when disposed.</returns>
    /// <example>
    /// using var obj = pool.RentDisposable();
    /// // Use obj.Value...
    /// // Automatically returned to pool at end of scope
    /// </example>
    public static PooledObject<T> RentDisposable<T>(this IObjectPool<T> pool) where T : class
    {
        return new PooledObject<T>(pool);
    }

    /// <summary>
    /// Rents an object from the pool and executes an action, automatically returning it.
    /// </summary>
    /// <typeparam name="T">The type of the pooled object.</typeparam>
    /// <param name="pool">The object pool.</param>
    /// <param name="action">The action to execute with the rented object.</param>
    public static void RentAndExecute<T>(this IObjectPool<T> pool, Action<T> action) where T : class
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var obj = pool.Rent();
        try
        {
            action(obj);
        }
        finally
        {
            pool.Return(obj);
        }
    }

    /// <summary>
    /// Rents an object from the pool and executes a function, automatically returning it.
    /// </summary>
    /// <typeparam name="T">The type of the pooled object.</typeparam>
    /// <typeparam name="TResult">The return type of the function.</typeparam>
    /// <param name="pool">The object pool.</param>
    /// <param name="func">The function to execute with the rented object.</param>
    /// <returns>The result of the function.</returns>
    public static TResult RentAndExecute<T, TResult>(this IObjectPool<T> pool, Func<T, TResult> func) where T : class
    {
        if (func == null)
            throw new ArgumentNullException(nameof(func));

        var obj = pool.Rent();
        try
        {
            return func(obj);
        }
        finally
        {
            pool.Return(obj);
        }
    }

    /// <summary>
    /// Asynchronously rents an object from the pool and executes a function, automatically returning it.
    /// </summary>
    /// <typeparam name="T">The type of the pooled object.</typeparam>
    /// <typeparam name="TResult">The return type of the function.</typeparam>
    /// <param name="pool">The object pool.</param>
    /// <param name="func">The async function to execute with the rented object.</param>
    /// <returns>A task containing the result of the function.</returns>
    public static async Task<TResult> RentAndExecuteAsync<T, TResult>(
        this IObjectPool<T> pool, 
        Func<T, Task<TResult>> func) where T : class
    {
        if (func == null)
            throw new ArgumentNullException(nameof(func));

        var obj = pool.Rent();
        try
        {
            return await func(obj).ConfigureAwait(false);
        }
        finally
        {
            pool.Return(obj);
        }
    }

    /// <summary>
    /// Asynchronously rents an object from the pool and executes an action, automatically returning it.
    /// </summary>
    /// <typeparam name="T">The type of the pooled object.</typeparam>
    /// <param name="pool">The object pool.</param>
    /// <param name="func">The async action to execute with the rented object.</param>
    /// <returns>A task representing the operation.</returns>
    public static async Task RentAndExecuteAsync<T>(
        this IObjectPool<T> pool, 
        Func<T, Task> func) where T : class
    {
        if (func == null)
            throw new ArgumentNullException(nameof(func));

        var obj = pool.Rent();
        try
        {
            await func(obj).ConfigureAwait(false);
        }
        finally
        {
            pool.Return(obj);
        }
    }
}
