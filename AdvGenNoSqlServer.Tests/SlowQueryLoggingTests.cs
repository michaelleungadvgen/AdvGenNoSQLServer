// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Query.Profiling;
using System.Text.Json;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for Slow Query Logging and Query Profiling
/// </summary>
public class SlowQueryLoggingTests
{
    #region ProfilingOptions Tests

    [Fact]
    public void ProfilingOptions_DefaultValues_AreCorrect()
    {
        var options = new ProfilingOptions();

        Assert.False(options.Enabled);
        Assert.Equal(100, options.SlowQueryThresholdMs);
        Assert.True(options.LogQueryPlan);
        Assert.Equal(1.0, options.SampleRate);
        Assert.Equal(10000, options.MaxLoggedQueries);
        Assert.False(options.LogOnlySlowQueries);
    }

    [Fact]
    public void ProfilingOptions_Validate_WithValidOptions_DoesNotThrow()
    {
        var options = new ProfilingOptions
        {
            Enabled = true,
            SlowQueryThresholdMs = 50,
            SampleRate = 0.5,
            MaxLoggedQueries = 100
        };

        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void ProfilingOptions_Validate_WithNegativeThreshold_ThrowsArgumentException()
    {
        var options = new ProfilingOptions
        {
            SlowQueryThresholdMs = -1
        };

        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("SlowQueryThresholdMs", exception.Message);
    }

    [Fact]
    public void ProfilingOptions_Validate_WithSampleRateOverOne_ThrowsArgumentException()
    {
        var options = new ProfilingOptions
        {
            SampleRate = 1.5
        };

        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("SampleRate", exception.Message);
    }

    [Fact]
    public void ProfilingOptions_Validate_WithSampleRateUnderZero_ThrowsArgumentException()
    {
        var options = new ProfilingOptions
        {
            SampleRate = -0.1
        };

        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("SampleRate", exception.Message);
    }

    [Fact]
    public void ProfilingOptions_Validate_WithZeroMaxLoggedQueries_ThrowsArgumentException()
    {
        var options = new ProfilingOptions
        {
            MaxLoggedQueries = 0
        };

        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("MaxLoggedQueries", exception.Message);
    }

    #endregion

    #region QueryProfile Tests

    [Fact]
    public void QueryProfile_Create_WithRequiredProperties_Succeeds()
    {
        var profile = new QueryProfile
        {
            Collection = "users",
            DurationMs = 150,
            DocumentsExamined = 100,
            DocumentsReturned = 10,
            UsedIndex = true
        };

        Assert.NotNull(profile.QueryId);
        Assert.NotEmpty(profile.QueryId);
        Assert.Equal("users", profile.Collection);
        Assert.Equal(150, profile.DurationMs);
        Assert.Equal(100, profile.DocumentsExamined);
        Assert.Equal(10, profile.DocumentsReturned);
        Assert.True(profile.UsedIndex);
        Assert.True(profile.Timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void QueryProfile_Create_WithOptionalProperties_Succeeds()
    {
        var queryJson = JsonSerializer.Deserialize<JsonElement>("{\"name\":\"John\"}");
        var metadata = new Dictionary<string, object> { ["user"] = "admin" };

        var profile = new QueryProfile
        {
            Collection = "users",
            DurationMs = 150,
            DocumentsExamined = 100,
            DocumentsReturned = 10,
            UsedIndex = true,
            IndexUsed = "users_name_idx",
            Query = queryJson,
            User = "testuser",
            ClientIp = "127.0.0.1",
            IsSlowQuery = true,
            Metadata = metadata
        };

        Assert.Equal("users_name_idx", profile.IndexUsed);
        Assert.Equal("testuser", profile.User);
        Assert.Equal("127.0.0.1", profile.ClientIp);
        Assert.True(profile.IsSlowQuery);
        Assert.Equal("admin", profile.Metadata!["user"]);
    }

    [Fact]
    public void QueryProfile_QueryId_IsUnique()
    {
        var profile1 = new QueryProfile { Collection = "test", DurationMs = 100 };
        var profile2 = new QueryProfile { Collection = "test", DurationMs = 100 };

        Assert.NotEqual(profile1.QueryId, profile2.QueryId);
    }

    #endregion

    #region QueryProfiler Tests - Basic Functionality

    [Fact]
    public void QueryProfiler_Create_WithDefaultOptions_SetsProperties()
    {
        var profiler = new QueryProfiler();

        Assert.False(profiler.IsEnabled);
        Assert.NotNull(profiler.Options);
        Assert.Equal(100, profiler.Options.SlowQueryThresholdMs);
    }

    [Fact]
    public void QueryProfiler_Create_WithCustomOptions_SetsProperties()
    {
        var options = new ProfilingOptions
        {
            Enabled = true,
            SlowQueryThresholdMs = 50,
            LogQueryPlan = false
        };

        var profiler = new QueryProfiler(options);

        Assert.True(profiler.IsEnabled);
        Assert.Equal(50, profiler.Options.SlowQueryThresholdMs);
        Assert.False(profiler.Options.LogQueryPlan);
    }

    [Fact]
    public void QueryProfiler_Create_ValidatesOptions()
    {
        var options = new ProfilingOptions { SlowQueryThresholdMs = -1 };

        Assert.Throws<ArgumentException>(() => new QueryProfiler(options));
    }

    [Fact]
    public void QueryProfiler_RecordQuery_WhenDisabled_DoesNotStore()
    {
        var options = new ProfilingOptions { Enabled = false };
        var profiler = new QueryProfiler(options);

        var profile = new QueryProfile
        {
            Collection = "test",
            DurationMs = 200,
            IsSlowQuery = true
        };

        profiler.RecordQuery(profile);

        var stats = profiler.GetStatistics();
        Assert.Equal(0, stats.TotalQueriesProfiled);
    }

    [Fact]
    public void QueryProfiler_RecordQuery_WhenEnabled_StoresQuery()
    {
        var options = new ProfilingOptions { Enabled = true };
        var profiler = new QueryProfiler(options);

        var profile = new QueryProfile
        {
            Collection = "test",
            DurationMs = 200,
            IsSlowQuery = true
        };

        profiler.RecordQuery(profile);

        var stats = profiler.GetStatistics();
        Assert.Equal(1, stats.TotalQueriesProfiled);
    }

    [Fact]
    public void QueryProfiler_RecordQuery_NullProfile_ThrowsArgumentNullException()
    {
        var options = new ProfilingOptions { Enabled = true };
        var profiler = new QueryProfiler(options);

        Assert.Throws<ArgumentNullException>(() => profiler.RecordQuery(null!));
    }

    #endregion

    #region QueryProfiler Tests - Slow Query Detection

    [Fact]
    public void QueryProfiler_RecordQuery_SlowQuery_RaisesEvent()
    {
        var options = new ProfilingOptions 
        { 
            Enabled = true, 
            SlowQueryThresholdMs = 100 
        };
        var profiler = new QueryProfiler(options);
        
        SlowQueryDetectedEventArgs? capturedArgs = null;
        profiler.SlowQueryDetected += (sender, args) => capturedArgs = args;

        var profile = new QueryProfile
        {
            Collection = "test",
            DurationMs = 150,
            IsSlowQuery = true
        };

        profiler.RecordQuery(profile);

        Assert.NotNull(capturedArgs);
        Assert.Equal(150, capturedArgs!.Profile.DurationMs);
        Assert.Equal(100, capturedArgs.ThresholdMs);
        Assert.Equal(50, capturedArgs.ExceededByMs);
    }

    [Fact]
    public void QueryProfiler_RecordQuery_NotSlowQuery_DoesNotRaiseEvent()
    {
        var options = new ProfilingOptions 
        { 
            Enabled = true, 
            SlowQueryThresholdMs = 100 
        };
        var profiler = new QueryProfiler(options);
        
        bool eventRaised = false;
        profiler.SlowQueryDetected += (sender, args) => eventRaised = true;

        var profile = new QueryProfile
        {
            Collection = "test",
            DurationMs = 50,
            IsSlowQuery = false
        };

        profiler.RecordQuery(profile);

        Assert.False(eventRaised);
    }

    [Fact]
    public async Task QueryProfiler_GetSlowQueries_ReturnsOnlySlowQueries()
    {
        var options = new ProfilingOptions { Enabled = true };
        var profiler = new QueryProfiler(options);

        profiler.RecordQuery(new QueryProfile { Collection = "test", DurationMs = 50, IsSlowQuery = false });
        profiler.RecordQuery(new QueryProfile { Collection = "test", DurationMs = 150, IsSlowQuery = true });
        profiler.RecordQuery(new QueryProfile { Collection = "test", DurationMs = 200, IsSlowQuery = true });

        var slowQueries = await profiler.GetSlowQueriesAsync();

        Assert.Equal(2, slowQueries.Count);
        Assert.All(slowQueries, q => Assert.True(q.IsSlowQuery));
    }

    [Fact]
    public async Task QueryProfiler_GetSlowQueries_WithLimit_RespectsLimit()
    {
        var options = new ProfilingOptions { Enabled = true };
        var profiler = new QueryProfiler(options);

        for (int i = 0; i < 10; i++)
        {
            profiler.RecordQuery(new QueryProfile 
            { 
                Collection = "test", 
                DurationMs = 150 + i, 
                IsSlowQuery = true 
            });
        }

        var slowQueries = await profiler.GetSlowQueriesAsync(5);

        Assert.Equal(5, slowQueries.Count);
    }

    #endregion

    #region QueryProfiler Tests - Statistics

    [Fact]
    public void QueryProfiler_GetStatistics_InitialState_IsCorrect()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });

        var stats = profiler.GetStatistics();

        Assert.Equal(0, stats.TotalQueriesProfiled);
        Assert.Equal(0, stats.SlowQueriesCount);
        Assert.Equal(0, stats.AverageQueryTimeMs);
        Assert.Equal(0, stats.MaxQueryTimeMs);
        Assert.Equal(0, stats.MinQueryTimeMs);
        Assert.Equal(0, stats.IndexUsagePercentage);
        Assert.Equal(0, stats.CurrentQueryCount);
        Assert.True(stats.ProfilingStartedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void QueryProfiler_GetStatistics_AfterQueries_UpdatesCorrectly()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });

        profiler.RecordQuery(new QueryProfile { Collection = "test", DurationMs = 100, IsSlowQuery = false, UsedIndex = true });
        profiler.RecordQuery(new QueryProfile { Collection = "test", DurationMs = 200, IsSlowQuery = true, UsedIndex = false });
        profiler.RecordQuery(new QueryProfile { Collection = "test", DurationMs = 50, IsSlowQuery = false, UsedIndex = true });

        var stats = profiler.GetStatistics();

        Assert.Equal(3, stats.TotalQueriesProfiled);
        Assert.Equal(1, stats.SlowQueriesCount);
        Assert.Equal(116.67, stats.AverageQueryTimeMs, 2); // (100+200+50)/3
        Assert.Equal(200, stats.MaxQueryTimeMs);
        Assert.Equal(50, stats.MinQueryTimeMs);
        Assert.Equal(66.67, stats.IndexUsagePercentage, 2); // 2 out of 3 used index
    }

    #endregion

    #region QueryProfiler Tests - Query Retrieval

    [Fact]
    public async Task QueryProfiler_GetAllQueries_ReturnsQueriesInOrder()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });

        // Record queries with different timestamps
        profiler.RecordQuery(new QueryProfile 
        { 
            Collection = "first", 
            DurationMs = 100, 
            Timestamp = DateTime.UtcNow.AddMinutes(-5) 
        });
        
        profiler.RecordQuery(new QueryProfile 
        { 
            Collection = "second", 
            DurationMs = 100, 
            Timestamp = DateTime.UtcNow 
        });
        
        profiler.RecordQuery(new QueryProfile 
        { 
            Collection = "third", 
            DurationMs = 100, 
            Timestamp = DateTime.UtcNow.AddMinutes(-2) 
        });

        var queries = await profiler.GetAllQueriesAsync();

        Assert.Equal(3, queries.Count);
        Assert.Equal("second", queries[0].Collection); // Most recent first
    }

    [Fact]
    public async Task QueryProfiler_GetAllQueries_WithLimit_RespectsLimit()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });

        for (int i = 0; i < 20; i++)
        {
            profiler.RecordQuery(new QueryProfile { Collection = $"test{i}", DurationMs = 100 });
        }

        var queries = await profiler.GetAllQueriesAsync(5);

        Assert.Equal(5, queries.Count);
    }

    [Fact]
    public void QueryProfiler_GetQueryById_ExistingQuery_ReturnsQuery()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });
        var profile = new QueryProfile { Collection = "test", DurationMs = 100 };
        
        profiler.RecordQuery(profile);

        var retrieved = profiler.GetQueryById(profile.QueryId);

        Assert.NotNull(retrieved);
        Assert.Equal(profile.QueryId, retrieved!.QueryId);
        Assert.Equal("test", retrieved.Collection);
    }

    [Fact]
    public void QueryProfiler_GetQueryById_NonExistingQuery_ReturnsNull()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });

        var retrieved = profiler.GetQueryById("nonexistent");

        Assert.Null(retrieved);
    }

    [Fact]
    public void QueryProfiler_GetQueriesByCollection_ReturnsMatchingQueries()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });

        profiler.RecordQuery(new QueryProfile { Collection = "users", DurationMs = 100 });
        profiler.RecordQuery(new QueryProfile { Collection = "orders", DurationMs = 100 });
        profiler.RecordQuery(new QueryProfile { Collection = "users", DurationMs = 100 });

        var userQueries = profiler.GetQueriesByCollection("users");

        Assert.Equal(2, userQueries.Count);
        Assert.All(userQueries, q => Assert.Equal("users", q.Collection));
    }

    [Fact]
    public void QueryProfiler_GetQueriesByTimeRange_ReturnsQueriesInRange()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });
        var baseTime = new DateTime(2026, 3, 19, 12, 0, 0, DateTimeKind.Utc);

        profiler.RecordQuery(new QueryProfile 
        { 
            Collection = "old", 
            DurationMs = 100, 
            Timestamp = baseTime.AddHours(-2)  // 10:00 AM
        });
        
        profiler.RecordQuery(new QueryProfile 
        { 
            Collection = "inrange", 
            DurationMs = 100, 
            Timestamp = baseTime  // 12:00 PM - exactly at start boundary
        });
        
        profiler.RecordQuery(new QueryProfile 
        { 
            Collection = "future", 
            DurationMs = 100, 
            Timestamp = baseTime.AddHours(2)  // 2:00 PM
        });

        var rangeQueries = profiler.GetQueriesByTimeRange(baseTime, baseTime.AddHours(1.5));

        Assert.Single(rangeQueries);
        Assert.Equal("inrange", rangeQueries[0].Collection);
    }

    #endregion

    #region QueryProfiler Tests - Clear Data

    [Fact]
    public async Task QueryProfiler_ClearProfileDataAsync_RemovesAllQueries()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });

        for (int i = 0; i < 10; i++)
        {
            profiler.RecordQuery(new QueryProfile { Collection = "test", DurationMs = 100 });
        }

        await profiler.ClearProfileDataAsync();

        var stats = profiler.GetStatistics();
        Assert.Equal(0, stats.TotalQueriesProfiled);
        Assert.Equal(0, stats.CurrentQueryCount);
        Assert.Equal(0, stats.SlowQueriesCount);
    }

    #endregion

    #region QueryProfiler Tests - Sampling

    [Fact]
    public void QueryProfiler_RecordQuery_WithSampling_MaySkipQueries()
    {
        var options = new ProfilingOptions 
        { 
            Enabled = true, 
            SampleRate = 0.0 // Sample nothing
        };
        var profiler = new QueryProfiler(options);

        for (int i = 0; i < 100; i++)
        {
            profiler.RecordQuery(new QueryProfile { Collection = "test", DurationMs = 100 });
        }

        var stats = profiler.GetStatistics();
        Assert.Equal(0, stats.TotalQueriesProfiled);
    }

    [Fact]
    public void QueryProfiler_RecordQuery_WithFullSampling_RecordsAllQueries()
    {
        var options = new ProfilingOptions 
        { 
            Enabled = true, 
            SampleRate = 1.0 // Sample everything
        };
        var profiler = new QueryProfiler(options);

        for (int i = 0; i < 100; i++)
        {
            profiler.RecordQuery(new QueryProfile { Collection = "test", DurationMs = 100 });
        }

        var stats = profiler.GetStatistics();
        Assert.Equal(100, stats.TotalQueriesProfiled);
    }

    #endregion

    #region QueryProfiler Tests - Log Only Slow Queries

    [Fact]
    public void QueryProfiler_RecordQuery_LogOnlySlowQueries_SkipsFastQueries()
    {
        var options = new ProfilingOptions 
        { 
            Enabled = true, 
            SlowQueryThresholdMs = 100,
            LogOnlySlowQueries = true
        };
        var profiler = new QueryProfiler(options);

        profiler.RecordQuery(new QueryProfile { Collection = "test", DurationMs = 50, IsSlowQuery = false });
        profiler.RecordQuery(new QueryProfile { Collection = "test", DurationMs = 150, IsSlowQuery = true });

        var stats = profiler.GetStatistics();
        Assert.Equal(1, stats.TotalQueriesProfiled);
    }

    #endregion

    #region QueryProfiler Tests - Max Queries Limit

    [Fact]
    public void QueryProfiler_RecordQuery_MaxQueries_TrimsOldQueries()
    {
        var options = new ProfilingOptions 
        { 
            Enabled = true, 
            MaxLoggedQueries = 5 
        };
        var profiler = new QueryProfiler(options);

        for (int i = 0; i < 10; i++)
        {
            profiler.RecordQuery(new QueryProfile { Collection = $"test{i}", DurationMs = 100 });
        }

        var stats = profiler.GetStatistics();
        Assert.Equal(10, stats.TotalQueriesProfiled); // Total tracked
        Assert.Equal(5, stats.CurrentQueryCount); // But only 5 in memory
    }

    #endregion

    #region QueryProfiler Tests - Disposal

    [Fact]
    public void QueryProfiler_Dispose_CanBeCalledMultipleTimes()
    {
        var profiler = new QueryProfiler();
        
        profiler.Dispose();
        profiler.Dispose(); // Should not throw
    }

    [Fact]
    public void QueryProfiler_Dispose_AfterDisposal_StatisticsStillWork()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });
        profiler.RecordQuery(new QueryProfile { Collection = "test", DurationMs = 100 });
        
        profiler.Dispose();

        var stats = profiler.GetStatistics();
        Assert.Equal(1, stats.TotalQueriesProfiled);
    }

    #endregion

    #region QueryProfiler Tests - Edge Cases

    [Fact]
    public void QueryProfiler_GetQueriesByCollection_EmptyCollectionName_ThrowsArgumentException()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });

        Assert.Throws<ArgumentException>(() => profiler.GetQueriesByCollection(""));
    }

    [Fact]
    public void QueryProfiler_GetQueryById_EmptyId_ThrowsArgumentException()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });

        Assert.Throws<ArgumentException>(() => profiler.GetQueryById(""));
    }

    [Fact]
    public async Task QueryProfiler_GetSlowQueries_ZeroLimit_ThrowsArgumentException()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });

        await Assert.ThrowsAsync<ArgumentException>(() => profiler.GetSlowQueriesAsync(0));
    }

    [Fact]
    public async Task QueryProfiler_GetAllQueries_NegativeLimit_ThrowsArgumentException()
    {
        var profiler = new QueryProfiler(new ProfilingOptions { Enabled = true });

        await Assert.ThrowsAsync<ArgumentException>(() => profiler.GetAllQueriesAsync(-1));
    }

    #endregion

    #region SlowQueryDetectedEventArgs Tests

    [Fact]
    public void SlowQueryDetectedEventArgs_ExceededByMs_CalculatesCorrectly()
    {
        var args = new SlowQueryDetectedEventArgs
        {
            Profile = new QueryProfile { Collection = "test", DurationMs = 250 },
            ThresholdMs = 100
        };

        Assert.Equal(150, args.ExceededByMs);
    }

    [Fact]
    public void SlowQueryDetectedEventArgs_ProfileBelowThreshold_NegativeExceededByMs()
    {
        var args = new SlowQueryDetectedEventArgs
        {
            Profile = new QueryProfile { Collection = "test", DurationMs = 50 },
            ThresholdMs = 100
        };

        Assert.Equal(-50, args.ExceededByMs);
    }

    #endregion
}
