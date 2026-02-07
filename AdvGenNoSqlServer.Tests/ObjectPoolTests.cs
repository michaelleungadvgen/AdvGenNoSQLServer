// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Pooling;
using System.Text;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for the Object Pooling components.
/// </summary>
public class ObjectPoolTests
{
    #region ObjectPool<T> Tests

    [Fact]
    public void ObjectPool_Constructor_WithDefaultCapacity_CreatesPool()
    {
        using var pool = new ObjectPool<TestObject>();

        Assert.Equal(100, pool.MaxCapacity);
        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public void ObjectPool_Constructor_WithCustomCapacity_CreatesPool()
    {
        using var pool = new ObjectPool<TestObject>(maxCapacity: 50);

        Assert.Equal(50, pool.MaxCapacity);
        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public void ObjectPool_Constructor_WithZeroCapacity_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ObjectPool<TestObject>(maxCapacity: 0));
    }

    [Fact]
    public void ObjectPool_Constructor_WithNegativeCapacity_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ObjectPool<TestObject>(maxCapacity: -1));
    }

    [Fact]
    public void Rent_WhenPoolIsEmpty_CreatesNewObject()
    {
        using var pool = new ObjectPool<TestObject>();

        var obj = pool.Rent();

        Assert.NotNull(obj);
        Assert.Equal(0, pool.Count);
        Assert.Equal(1, pool.Statistics.TotalCreated);
        Assert.Equal(1, pool.Statistics.TotalRented);
    }

    [Fact]
    public void Rent_WhenPoolHasObjects_ReturnsPooledObject()
    {
        using var pool = new ObjectPool<TestObject>();
        var obj1 = pool.Rent();
        pool.Return(obj1);

        var obj2 = pool.Rent();

        Assert.Same(obj1, obj2);
        Assert.Equal(0, pool.Count);
        Assert.Equal(1, pool.Statistics.TotalCreated);
        Assert.Equal(2, pool.Statistics.TotalRented);
    }

    [Fact]
    public void Return_NullObject_ThrowsArgumentNullException()
    {
        using var pool = new ObjectPool<TestObject>();

        Assert.Throws<ArgumentNullException>(() => pool.Return(null!));
    }

    [Fact]
    public void Return_WhenPoolHasSpace_AddsToPool()
    {
        using var pool = new ObjectPool<TestObject>(maxCapacity: 10);
        var obj = pool.Rent();

        pool.Return(obj);

        Assert.Equal(1, pool.Count);
        Assert.Equal(1, pool.Statistics.TotalReturned);
    }

    [Fact]
    public void Return_WhenPoolIsFull_DropsObject()
    {
        using var pool = new ObjectPool<TestObject>(maxCapacity: 2);
        var obj1 = pool.Rent();
        var obj2 = pool.Rent();
        var obj3 = pool.Rent();

        pool.Return(obj1);
        pool.Return(obj2);
        pool.Return(obj3); // Should be dropped

        Assert.Equal(2, pool.Count);
        Assert.Equal(1, pool.Statistics.TotalDropped);
    }

    [Fact]
    public void Return_WithResetAction_ResetsObject()
    {
        using var pool = new ObjectPool<TestObject>(
            maxCapacity: 10,
            resetAction: obj => obj.Value = 0);

        var obj = pool.Rent();
        obj.Value = 42;
        pool.Return(obj);

        var obj2 = pool.Rent();
        Assert.Equal(0, obj2.Value);
    }

    [Fact]
    public void PrePopulate_AddsObjectsToPool()
    {
        using var pool = new ObjectPool<TestObject>(maxCapacity: 10);

        pool.PrePopulate(5);

        Assert.Equal(5, pool.Count);
        Assert.Equal(5, pool.Statistics.TotalCreated);
    }

    [Fact]
    public void PrePopulate_ExceedsMaxCapacity_StopsAtMax()
    {
        using var pool = new ObjectPool<TestObject>(maxCapacity: 5);

        pool.PrePopulate(10);

        Assert.Equal(5, pool.Count);
    }

    [Fact]
    public void Clear_RemovesAllObjects()
    {
        using var pool = new ObjectPool<TestObject>(maxCapacity: 10);
        pool.PrePopulate(5);

        pool.Clear();

        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public void Dispose_ClearsPool()
    {
        var pool = new ObjectPool<TestObject>(maxCapacity: 10);
        pool.PrePopulate(5);

        pool.Dispose();

        Assert.Equal(0, pool.Count);
        Assert.Throws<ObjectDisposedException>(() => pool.Rent());
    }

    [Fact]
    public void Statistics_InUse_CalculatesCorrectly()
    {
        using var pool = new ObjectPool<TestObject>(maxCapacity: 10);
        
        var obj1 = pool.Rent();
        var obj2 = pool.Rent();
        
        Assert.Equal(2, pool.Statistics.InUse);
        
        pool.Return(obj1);
        Assert.Equal(1, pool.Statistics.InUse);
        
        pool.Return(obj2);
        Assert.Equal(0, pool.Statistics.InUse);
    }

    [Fact]
    public void Statistics_Reset_ClearsCounters()
    {
        using var pool = new ObjectPool<TestObject>(maxCapacity: 10);
        
        var obj = pool.Rent();
        pool.Return(obj);
        
        pool.Statistics.Reset();

        Assert.Equal(0, pool.Statistics.TotalRented);
        Assert.Equal(0, pool.Statistics.TotalReturned);
        Assert.Equal(0, pool.Statistics.TotalCreated);
        Assert.Equal(0, pool.Statistics.TotalDropped);
    }

    [Fact]
    public void Rent_WithCustomFactory_UsesFactory()
    {
        int factoryCallCount = 0;
        using var pool = new ObjectPool<TestObject>(
            maxCapacity: 10,
            factory: () =>
            {
                factoryCallCount++;
                return new TestObject { Value = 100 };
            });

        var obj = pool.Rent();

        Assert.Equal(1, factoryCallCount);
        Assert.Equal(100, obj.Value);
    }

    #endregion

    #region BufferPool Tests

    [Fact]
    public void BufferPool_Rent_WithValidSize_ReturnsBuffer()
    {
        using var pool = new BufferPool();

        var buffer = pool.Rent(1024);

        Assert.NotNull(buffer);
        Assert.True(buffer.Length >= 1024);
        pool.Return(buffer);
    }

    [Fact]
    public void BufferPool_Rent_ZeroSize_ThrowsArgumentException()
    {
        using var pool = new BufferPool();

        Assert.Throws<ArgumentException>(() => pool.Rent(0));
    }

    [Fact]
    public void BufferPool_Rent_NegativeSize_ThrowsArgumentException()
    {
        using var pool = new BufferPool();

        Assert.Throws<ArgumentException>(() => pool.Rent(-1));
    }

    [Fact]
    public void BufferPool_Rent_ExceedsMaxSize_ThrowsArgumentException()
    {
        using var pool = new BufferPool();

        Assert.Throws<ArgumentException>(() => pool.Rent(BufferPool.MaxBufferSize + 1));
    }

    [Fact]
    public void BufferPool_Return_NullBuffer_ThrowsArgumentNullException()
    {
        using var pool = new BufferPool();

        Assert.Throws<ArgumentNullException>(() => pool.Return(null!));
    }

    [Fact]
    public void BufferPool_RentMemory_WithValidSize_ReturnsPooledMemory()
    {
        using var pool = new BufferPool();

        using var memory = pool.RentMemory(1024);

        Assert.NotNull(memory.Buffer);
        Assert.True(memory.Length >= 1024);
    }

    [Fact]
    public void BufferPool_RentMemory_Dispose_ReturnsToPool()
    {
        using var pool = new BufferPool();

        using (var memory = pool.RentMemory(1024))
        {
            Assert.NotNull(memory.Buffer);
        }

        // After disposal, buffer is returned
        Assert.Equal(0, pool.InUse);
    }

    [Fact]
    public void BufferPool_Statistics_TrackRentedAndReturned()
    {
        using var pool = new BufferPool();

        var buffer = pool.Rent(1024);
        Assert.Equal(1, pool.TotalRented);
        
        pool.Return(buffer);
        Assert.Equal(1, pool.TotalReturned);
        Assert.Equal(0, pool.InUse);
    }

    [Fact]
    public void BufferPool_ResetStatistics_ClearsCounters()
    {
        using var pool = new BufferPool();

        var buffer = pool.Rent(1024);
        pool.Return(buffer);
        
        pool.ResetStatistics();

        Assert.Equal(0, pool.TotalRented);
        Assert.Equal(0, pool.TotalReturned);
    }

    [Fact]
    public void BufferPool_Default_IsAccessible()
    {
        var pool = BufferPool.Default;
        Assert.NotNull(pool);

        var buffer = pool.Rent(1024);
        Assert.NotNull(buffer);
        pool.Return(buffer);
    }

    [Fact]
    public void BufferPool_Dispose_PreventsFurtherRents()
    {
        var pool = new BufferPool();
        pool.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pool.Rent(1024));
    }

    #endregion

    #region ObjectPoolManager Tests

    [Fact]
    public void ObjectPoolManager_GetOrCreatePool_CreatesNewPool()
    {
        using var manager = new ObjectPoolManager();

        var pool = manager.GetOrCreatePool<TestObject>("test-pool");

        Assert.NotNull(pool);
        Assert.Equal(100, pool.MaxCapacity);
    }

    [Fact]
    public void ObjectPoolManager_GetOrCreatePool_WithExistingName_ReturnsSamePool()
    {
        using var manager = new ObjectPoolManager();

        var pool1 = manager.GetOrCreatePool<TestObject>("test-pool");
        var pool2 = manager.GetOrCreatePool<TestObject>("test-pool");

        Assert.Same(pool1, pool2);
    }

    [Fact]
    public void ObjectPoolManager_GetOrCreatePool_EmptyName_ThrowsArgumentException()
    {
        using var manager = new ObjectPoolManager();

        Assert.Throws<ArgumentException>(() => manager.GetOrCreatePool<TestObject>(""));
    }

    [Fact]
    public void ObjectPoolManager_RegisterPool_WithNewName_ReturnsTrue()
    {
        using var manager = new ObjectPoolManager();
        using var pool = new ObjectPool<TestObject>();

        var result = manager.RegisterPool("custom-pool", pool);

        Assert.True(result);
    }

    [Fact]
    public void ObjectPoolManager_RegisterPool_WithExistingName_ReturnsFalse()
    {
        using var manager = new ObjectPoolManager();
        using var pool1 = new ObjectPool<TestObject>();
        using var pool2 = new ObjectPool<TestObject>();

        manager.RegisterPool("custom-pool", pool1);
        var result = manager.RegisterPool("custom-pool", pool2);

        Assert.False(result);
    }

    [Fact]
    public void ObjectPoolManager_GetPool_WithExistingPool_ReturnsPool()
    {
        using var manager = new ObjectPoolManager();
        using var pool = new ObjectPool<TestObject>();
        manager.RegisterPool("custom-pool", pool);

        var retrieved = manager.GetPool<TestObject>("custom-pool");

        Assert.Same(pool, retrieved);
    }

    [Fact]
    public void ObjectPoolManager_GetPool_WithNonExistingPool_ReturnsNull()
    {
        using var manager = new ObjectPoolManager();

        var retrieved = manager.GetPool<TestObject>("non-existing");

        Assert.Null(retrieved);
    }

    [Fact]
    public void ObjectPoolManager_RemovePool_WithExistingPool_ReturnsTrue()
    {
        using var manager = new ObjectPoolManager();
        using var pool = new ObjectPool<TestObject>();
        manager.RegisterPool("custom-pool", pool);

        var result = manager.RemovePool("custom-pool");

        Assert.True(result);
    }

    [Fact]
    public void ObjectPoolManager_RemovePool_WithNonExistingPool_ReturnsFalse()
    {
        using var manager = new ObjectPoolManager();

        var result = manager.RemovePool("non-existing");

        Assert.False(result);
    }

    [Fact]
    public void ObjectPoolManager_GetPoolNames_ReturnsAllNames()
    {
        using var manager = new ObjectPoolManager();
        manager.GetOrCreatePool<TestObject>("pool1");
        manager.GetOrCreatePool<TestObject>("pool2");
        manager.GetOrCreatePool<TestObject>("pool3");

        var names = manager.GetPoolNames().ToList();

        Assert.Contains("pool1", names);
        Assert.Contains("pool2", names);
        Assert.Contains("pool3", names);
        Assert.Equal(3, names.Count);
    }

    [Fact]
    public void ObjectPoolManager_Dispose_ClearsAllPools()
    {
        var manager = new ObjectPoolManager();
        manager.GetOrCreatePool<TestObject>("pool1");
        manager.GetOrCreatePool<TestObject>("pool2");

        manager.Dispose();

        Assert.Throws<ObjectDisposedException>(() => manager.GetPoolNames());
    }

    [Fact]
    public void ObjectPoolManager_Default_IsAccessible()
    {
        var manager = ObjectPoolManager.Default;
        Assert.NotNull(manager);

        var pool = manager.GetOrCreatePool<TestObject>("default-test");
        Assert.NotNull(pool);
    }

    #endregion

    #region PooledObject Tests

    [Fact]
    public void PooledObject_RentDisposable_ReturnsPooledObject()
    {
        using var pool = new ObjectPool<TestObject>();

        using var pooled = pool.RentDisposable();

        Assert.NotNull(pooled.Value);
    }

    [Fact]
    public void PooledObject_Dispose_ReturnsToPool()
    {
        using var pool = new ObjectPool<TestObject>();
        
        using (var pooled = pool.RentDisposable())
        {
            Assert.NotNull(pooled.Value);
        }

        Assert.Equal(1, pool.Count);
    }

    [Fact]
    public void PooledObject_ImplicitConversion_ReturnsValue()
    {
        using var pool = new ObjectPool<TestObject>();

        using var pooled = pool.RentDisposable();
        TestObject obj = pooled;

        Assert.Same(pooled.Value, obj);
    }

    #endregion

    #region ObjectPoolExtensions Tests

    [Fact]
    public void RentAndExecute_ExecutesAction()
    {
        using var pool = new ObjectPool<TestObject>();
        var executed = false;

        pool.RentAndExecute(obj =>
        {
            executed = true;
            obj.Value = 42;
        });

        Assert.True(executed);
        Assert.Equal(1, pool.Count); // Object returned
    }

    [Fact]
    public void RentAndExecute_NullAction_ThrowsArgumentNullException()
    {
        using var pool = new ObjectPool<TestObject>();

        Assert.Throws<ArgumentNullException>(() => pool.RentAndExecute((Action<TestObject>)null!));
    }

    [Fact]
    public void RentAndExecute_WithResult_ReturnsResult()
    {
        using var pool = new ObjectPool<TestObject>();

        var result = pool.RentAndExecute(obj =>
        {
            obj.Value = 42;
            return obj.Value;
        });

        Assert.Equal(42, result);
        Assert.Equal(1, pool.Count); // Object returned
    }

    [Fact]
    public void RentAndExecute_WithResult_NullFunc_ThrowsArgumentNullException()
    {
        using var pool = new ObjectPool<TestObject>();

        Assert.Throws<ArgumentNullException>(() => pool.RentAndExecute((Func<TestObject, int>)null!));
    }

    [Fact]
    public async Task RentAndExecuteAsync_ExecutesAsyncAction()
    {
        using var pool = new ObjectPool<TestObject>();
        var executed = false;

        await pool.RentAndExecuteAsync(async obj =>
        {
            await Task.Delay(1);
            executed = true;
            obj.Value = 42;
        });

        Assert.True(executed);
        Assert.Equal(1, pool.Count);
    }

    [Fact]
    public async Task RentAndExecuteAsync_WithResult_ReturnsResult()
    {
        using var pool = new ObjectPool<TestObject>();

        var result = await pool.RentAndExecuteAsync(async obj =>
        {
            await Task.Delay(1);
            obj.Value = 42;
            return obj.Value;
        });

        Assert.Equal(42, result);
        Assert.Equal(1, pool.Count);
    }

    #endregion

    #region StringBuilderPool Tests

    [Fact]
    public void StringBuilderPool_Rent_ReturnsStringBuilder()
    {
        using var pool = new StringBuilderPool();

        var sb = pool.Rent();

        Assert.NotNull(sb);
        Assert.Equal(0, sb.Length);
    }

    [Fact]
    public void StringBuilderPool_Return_AddsToPool()
    {
        using var pool = new StringBuilderPool();
        var sb = pool.Rent();

        pool.Return(sb);

        Assert.Equal(1, pool.Statistics.TotalReturned);
    }

    [Fact]
    public void StringBuilderPool_Return_Null_DoesNotThrow()
    {
        using var pool = new StringBuilderPool();

        pool.Return(null!); // Should not throw
    }

    [Fact]
    public void StringBuilderPool_RentAndExecute_ReturnsString()
    {
        using var pool = new StringBuilderPool();

        var result = pool.RentAndExecute(sb =>
        {
            sb.Append("Hello");
            sb.Append(" ");
            sb.Append("World");
        });

        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void StringBuilderPool_RentDisposable_ReturnsPooledStringBuilder()
    {
        using var pool = new StringBuilderPool();

        using var sb = pool.RentDisposable();

        sb.Append("Test");
        Assert.Equal("Test", sb.ToString());
    }

    [Fact]
    public void StringBuilderPool_Reset_ClearsBuilder()
    {
        using var pool = new StringBuilderPool();
        var sb = pool.Rent();
        sb.Append("Previous Content");

        pool.Return(sb);
        var sb2 = pool.Rent();

        Assert.Equal(0, sb2.Length);
    }

    [Fact]
    public void StringBuilderPool_Reset_LargeCapacity_ReducesCapacity()
    {
        using var pool = new StringBuilderPool(maxPoolSize: 10);
        var sb = pool.Rent();
        
        // Append enough to grow capacity beyond max
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("Large content that will expand the capacity significantly ");
        }
        
        var largeCapacity = sb.Capacity;
        pool.Return(sb);
        var sb2 = pool.Rent();

        Assert.True(sb2.Capacity <= StringBuilderPool.MaxCapacity);
    }

    [Fact]
    public void StringBuilderPool_Default_IsAccessible()
    {
        var pool = StringBuilderPool.Default;
        Assert.NotNull(pool);

        var sb = pool.Rent();
        Assert.NotNull(sb);
        pool.Return(sb);
    }

    [Fact]
    public void PooledStringBuilder_Chaining_ReturnsSameInstance()
    {
        using var pool = new StringBuilderPool();

        using var sb = pool.RentDisposable();
        var result = sb.Append("A").Append("B").Append("C");

        // Value types don't have identity, so we compare by value
        Assert.Equal(sb.ToString(), result.ToString());
        Assert.Equal("ABC", sb.ToString());
    }

    [Fact]
    public void PooledStringBuilder_AppendLine_WithNoArgs_AppendsNewLine()
    {
        using var pool = new StringBuilderPool();

        using var sb = pool.RentDisposable();
        sb.AppendLine();

        Assert.Equal(Environment.NewLine, sb.ToString());
    }

    [Fact]
    public void PooledStringBuilder_Clear_ClearsContent()
    {
        using var pool = new StringBuilderPool();

        using var sb = pool.RentDisposable();
        sb.Append("Content");
        sb.Clear();

        Assert.Equal(0, sb.Builder.Length);
    }

    [Fact]
    public void PooledStringBuilder_ImplicitConversion_ToString()
    {
        using var pool = new StringBuilderPool();

        using var sb = pool.RentDisposable();
        sb.Append("Test");
        string result = sb;

        Assert.Equal("Test", result);
    }

    #endregion

    #region Test Helper Classes

    private class TestObject
    {
        public int Value { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    #endregion
}
