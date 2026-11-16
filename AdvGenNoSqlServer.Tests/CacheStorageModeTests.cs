// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Attachments;
using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Attachments;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Tests for the memory-only "Cache" storage mode: configuration, the in-memory
    /// database manager, and the in-memory user / attachment stores. Cache mode must
    /// never touch the disk.
    /// </summary>
    public class CacheStorageModeTests
    {
        // ---------- Configuration ----------

        [Fact]
        public void StorageMode_DefaultsToPersistent()
        {
            var config = new ServerConfiguration();
            Assert.Equal(ServerConfiguration.StorageModePersistent, config.StorageMode);
            Assert.False(config.IsCacheMode);
        }

        [Theory]
        [InlineData("Cache")]
        [InlineData("cache")]
        [InlineData("CACHE")]
        public void IsCacheMode_TrueForCache_CaseInsensitive(string mode)
        {
            var config = new ServerConfiguration { StorageMode = mode };
            Assert.True(config.IsCacheMode);
        }

        [Fact]
        public void Validate_InvalidStorageMode_ReturnsError()
        {
            var config = new ServerConfiguration { StorageMode = "Bogus" };
            var errors = config.Validate(isProduction: false);
            Assert.Contains(errors, e => e.Contains("StorageMode"));
        }

        [Fact]
        public void Validate_CacheMode_AllowsEmptyStoragePath()
        {
            var config = new ServerConfiguration { StorageMode = "Cache", StoragePath = "" };
            var errors = config.Validate(isProduction: false);
            Assert.DoesNotContain(errors, e => e.Contains("StoragePath"));
            Assert.DoesNotContain(errors, e => e.Contains("StorageMode"));
        }

        [Fact]
        public void EnvVar_NOSQL_STORAGE_MODE_SetsCacheMode()
        {
            var prev = Environment.GetEnvironmentVariable("NOSQL_STORAGE_MODE");
            var configPath = Path.Combine(Path.GetTempPath(), $"cfg_{Guid.NewGuid():N}.json");
            File.WriteAllText(configPath, "{}");
            try
            {
                Environment.SetEnvironmentVariable("NOSQL_STORAGE_MODE", "Cache");
                var cm = new ConfigurationManager(configPath);
                Assert.True(cm.Configuration.IsCacheMode);
            }
            finally
            {
                Environment.SetEnvironmentVariable("NOSQL_STORAGE_MODE", prev);
                try { File.Delete(configPath); } catch { }
            }
        }

        // ---------- DatabaseManager (cache mode) ----------

        [Fact]
        public void CacheMode_DoesNotCreateStorageDirectory()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"cache_{Guid.NewGuid():N}");
            var manager = new DatabaseManager(basePath, cacheMode: true);
            try
            {
                Assert.False(Directory.Exists(basePath));
                Assert.Contains(manager.DefaultDatabaseName, manager.GetDatabaseNames());
            }
            finally
            {
                manager.DisposeDatabasesAsync().GetAwaiter().GetResult();
            }
        }

        [Fact]
        public void CacheMode_DefaultDatabase_IsInMemoryStore()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"cache_{Guid.NewGuid():N}");
            var manager = new DatabaseManager(basePath, cacheMode: true);
            try
            {
                var store = manager.GetDatabase(manager.DefaultDatabaseName);
                Assert.IsType<DocumentStore>(store);
            }
            finally
            {
                manager.DisposeDatabasesAsync().GetAwaiter().GetResult();
            }
        }

        [Fact]
        public async Task CacheMode_CrudWorks()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"cache_{Guid.NewGuid():N}");
            var manager = new DatabaseManager(basePath, cacheMode: true);
            try
            {
                var store = manager.GetDatabase(manager.DefaultDatabaseName);
                await store.CreateCollectionAsync("items");
                await store.InsertAsync("items", new Document { Id = "1", Data = new Dictionary<string, object> { ["name"] = "alpha" } });

                var fetched = await store.GetAsync("items", "1");
                Assert.NotNull(fetched);
                Assert.Equal(1, await store.CountAsync("items"));

                Assert.True(await store.DeleteAsync("items", "1"));
                Assert.Equal(0, await store.CountAsync("items"));
                Assert.False(Directory.Exists(basePath));
            }
            finally
            {
                await manager.DisposeDatabasesAsync();
            }
        }

        [Fact]
        public async Task CacheMode_DataNotPersistedAcrossInstances()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"cache_{Guid.NewGuid():N}");
            var first = new DatabaseManager(basePath, cacheMode: true);
            var store = first.GetDatabase(first.DefaultDatabaseName);
            await store.InsertAsync("items", new Document { Id = "1", Data = new Dictionary<string, object>() });
            await first.DisposeDatabasesAsync();

            var second = new DatabaseManager(basePath, cacheMode: true);
            try
            {
                var fresh = second.GetDatabase(second.DefaultDatabaseName);
                Assert.Equal(0, await fresh.CountAsync("items"));
            }
            finally
            {
                await second.DisposeDatabasesAsync();
            }
        }

        [Fact]
        public async Task CacheMode_CreateAndDeleteDatabase_NoDirectories()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"cache_{Guid.NewGuid():N}");
            var manager = new DatabaseManager(basePath, cacheMode: true);
            try
            {
                Assert.True(await manager.CreateDatabaseAsync("extra"));
                Assert.True(manager.DatabaseExists("extra"));
                Assert.IsType<DocumentStore>(manager.GetDatabase("extra"));

                Assert.True(await manager.DeleteDatabaseAsync("extra"));
                Assert.False(manager.DatabaseExists("extra"));
                Assert.False(Directory.Exists(basePath));
            }
            finally
            {
                await manager.DisposeDatabasesAsync();
            }
        }

        // ---------- InMemoryUserStore ----------

        [Fact]
        public void InMemoryUserStore_SaveLoad_RoundTrip()
        {
            var store = new InMemoryUserStore();
            Assert.Empty(store.Load());

            var users = new[]
            {
                new PersistedUser { Username = "admin", PasswordHash = "h", Salt = "s", Role = UserRole.Admin }
            };
            store.Save(users);

            var loaded = store.Load();
            Assert.Single(loaded);
            Assert.Equal("admin", loaded[0].Username);
            Assert.Equal(UserRole.Admin, loaded[0].Role);
        }

        // ---------- InMemoryAttachmentStore ----------

        private static AttachmentStoreOptions Options(string basePath, long maxSize = 1024 * 1024)
            => new() { BasePath = basePath, MaxAttachmentSize = maxSize };

        [Fact]
        public async Task InMemoryAttachmentStore_StoreGet_RoundTrip_NoFiles()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"att_{Guid.NewGuid():N}");
            using var store = new InMemoryAttachmentStore(Options(basePath));
            var content = new byte[] { 1, 2, 3, 4 };

            var result = await store.StoreAsync("col", "doc1", "file.bin", "application/octet-stream", content);
            Assert.True(result.Success);

            var fetched = await store.GetAsync("col", "doc1", "file.bin");
            Assert.NotNull(fetched);
            Assert.Equal(content, fetched!.Content);
            Assert.True(await store.ExistsAsync("col", "doc1", "file.bin"));
            Assert.Equal(4, await store.GetTotalStorageSizeAsync());
            Assert.False(Directory.Exists(basePath));
        }

        [Fact]
        public async Task InMemoryAttachmentStore_ListAndDelete()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"att_{Guid.NewGuid():N}");
            using var store = new InMemoryAttachmentStore(Options(basePath));
            await store.StoreAsync("col", "doc1", "a.txt", "text/plain", new byte[] { 1 });
            await store.StoreAsync("col", "doc1", "b.txt", "text/plain", new byte[] { 2 });

            var list = await store.ListAsync("col", "doc1");
            Assert.Equal(2, list.Count);

            Assert.True(await store.DeleteAsync("col", "doc1", "a.txt"));
            Assert.Single(await store.ListAsync("col", "doc1"));

            Assert.Equal(1, await store.DeleteAllAsync("col", "doc1"));
            Assert.Empty(await store.ListAsync("col", "doc1"));
        }

        [Fact]
        public async Task InMemoryAttachmentStore_SizeLimit_RejectsOversized()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"att_{Guid.NewGuid():N}");
            using var store = new InMemoryAttachmentStore(Options(basePath, maxSize: 4));

            var result = await store.StoreAsync("col", "doc1", "big.bin", "application/octet-stream", new byte[] { 1, 2, 3, 4, 5 });
            Assert.False(result.Success);
            Assert.False(await store.ExistsAsync("col", "doc1", "big.bin"));
        }

        // ---------- InMemoryAuditLogger ----------

        [Fact]
        public void InMemoryAuditLogger_LogAndQuery()
        {
            var logger = new InMemoryAuditLogger();
            logger.LogAuthentication("admin");
            logger.LogDataAccess("admin", "SET", "items", "1");
            logger.LogAuthenticationFailure("bob");

            Assert.Equal(3, logger.GetRecentEvents().Count);
            Assert.Single(logger.GetEventsByUser("bob"));
            Assert.Single(logger.GetEventsByType(AuditEventType.AuthenticationFailure));
        }

        [Fact]
        public async Task InMemoryAuditLogger_FlushIsNoOp()
        {
            var logger = new InMemoryAuditLogger();
            logger.LogSecurityWarning("test");
            await logger.FlushAsync();
            Assert.Single(logger.GetRecentEvents());
        }
    }
}