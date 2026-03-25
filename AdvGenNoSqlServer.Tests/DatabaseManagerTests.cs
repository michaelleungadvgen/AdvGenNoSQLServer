// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Database;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    public class DatabaseManagerTests : IDisposable
    {
        private readonly string _testBasePath;
        private readonly DatabaseManager _manager;

        public DatabaseManagerTests()
        {
            _testBasePath = Path.Combine(Path.GetTempPath(), $"AdvGenNoSql_Tests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testBasePath);
            _manager = new DatabaseManager(_testBasePath);
        }

        public void Dispose()
        {
            _manager.Dispose();
            try
            {
                if (Directory.Exists(_testBasePath))
                    Directory.Delete(_testBasePath, recursive: true);
            }
            catch { }
        }

        [Fact]
        public void Constructor_WithValidPath_CreatesInstance()
        {
            var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
            try
            {
                var manager = new DatabaseManager(path);
                Assert.NotNull(manager);
                Assert.Equal("default", manager.DefaultDatabaseName);
                Assert.Equal("_system", manager.SystemDatabaseName);
                manager.Dispose();
            }
            finally
            {
                try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
            }
        }

        [Fact]
        public void Constructor_WithNullPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new DatabaseManager(null!));
        }

        [Fact]
        public void Constructor_WithEmptyPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new DatabaseManager(""));
        }

        [Fact]
        public void Constructor_CreatesSystemDatabases()
        {
            var systemDb = _manager.GetDatabaseAsync("_system").Result;
            var defaultDb = _manager.GetDatabaseAsync("default").Result;

            Assert.NotNull(systemDb);
            Assert.NotNull(defaultDb);
            Assert.Equal("_system", systemDb.Name);
            Assert.Equal("default", defaultDb.Name);
        }

        [Fact]
        public async Task CreateDatabaseAsync_WithValidName_CreatesDatabase()
        {
            var db = await _manager.CreateDatabaseAsync("testdb");

            Assert.NotNull(db);
            Assert.Equal("testdb", db.Name);
            Assert.True(Directory.Exists(db.Path));
        }

        [Fact]
        public async Task CreateDatabaseAsync_WithOptions_UsesOptions()
        {
            var options = new DatabaseOptions
            {
                MaxCollections = 50,
                MaxSizeBytes = 1024 * 1024 * 100, // 100MB
                AllowAnonymousRead = true
            };

            var db = await _manager.CreateDatabaseAsync("testdb", options);

            Assert.Equal(50, db.Options.MaxCollections);
            Assert.Equal(1024 * 1024 * 100, db.Options.MaxSizeBytes);
            Assert.True(db.Options.AllowAnonymousRead);
        }

        [Fact]
        public async Task CreateDatabaseAsync_WithSecurity_UsesSecurity()
        {
            var security = new DatabaseSecurity();
            security.GrantAccess("admin", DatabaseRole.Admin);
            security.GrantAccess("user1", DatabaseRole.Member);

            var db = await _manager.CreateDatabaseAsync("testdb", security: security);

            Assert.Equal(DatabaseRole.Admin, db.Security.GetUserRole("admin"));
            Assert.Equal(DatabaseRole.Member, db.Security.GetUserRole("user1"));
        }

        [Fact]
        public async Task CreateDatabaseAsync_WithInvalidName_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateDatabaseAsync(""));
            await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateDatabaseAsync("   "));
            await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateDatabaseAsync("123invalid")); // starts with number
            await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateDatabaseAsync("invalid name")); // space
            await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateDatabaseAsync("invalid@name")); // special char
        }

        [Fact]
        public async Task CreateDatabaseAsync_WithExistingName_ThrowsInvalidOperationException()
        {
            await _manager.CreateDatabaseAsync("testdb");
            await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.CreateDatabaseAsync("testdb"));
        }

        [Fact]
        public async Task CreateDatabaseAsync_WithInvalidOptions_ThrowsArgumentException()
        {
            var options = new DatabaseOptions { MaxCollections = 0 };
            await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateDatabaseAsync("testdb", options));
        }

        [Fact]
        public async Task CreateDatabaseAsync_RaisesDatabaseCreatedEvent()
        {
            string? createdDbName = null;
            _manager.DatabaseCreated += (s, e) => createdDbName = e.DatabaseName;

            await _manager.CreateDatabaseAsync("testdb");

            Assert.Equal("testdb", createdDbName);
        }

        [Fact]
        public async Task DropDatabaseAsync_WithExistingDatabase_RemovesDatabase()
        {
            await _manager.CreateDatabaseAsync("testdb");
            var result = await _manager.DropDatabaseAsync("testdb");

            Assert.True(result);
            Assert.False(await _manager.DatabaseExistsAsync("testdb"));
        }

        [Fact]
        public async Task DropDatabaseAsync_WithNonExistingDatabase_ReturnsFalse()
        {
            var result = await _manager.DropDatabaseAsync("nonexistent");
            Assert.False(result);
        }

        [Fact]
        public async Task DropDatabaseAsync_WithSystemDatabase_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.DropDatabaseAsync("_system"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.DropDatabaseAsync("default"));
        }

        [Fact]
        public async Task DropDatabaseAsync_RaisesDatabaseDroppedEvent()
        {
            await _manager.CreateDatabaseAsync("testdb");
            string? droppedDbName = null;
            _manager.DatabaseDropped += (s, e) => droppedDbName = e.DatabaseName;

            await _manager.DropDatabaseAsync("testdb");

            Assert.Equal("testdb", droppedDbName);
        }

        [Fact]
        public async Task GetDatabaseAsync_WithExistingDatabase_ReturnsDatabase()
        {
            await _manager.CreateDatabaseAsync("testdb");
            var db = await _manager.GetDatabaseAsync("testdb");

            Assert.NotNull(db);
            Assert.Equal("testdb", db.Name);
        }

        [Fact]
        public async Task GetDatabaseAsync_WithNonExistingDatabase_ReturnsNull()
        {
            var db = await _manager.GetDatabaseAsync("nonexistent");
            Assert.Null(db);
        }

        [Fact]
        public async Task GetDatabaseAsync_UpdatesLastAccessed()
        {
            await _manager.CreateDatabaseAsync("testdb");
            var before = DateTime.UtcNow;

            var db = await _manager.GetDatabaseAsync("testdb");

            Assert.NotNull(db);
            Assert.True(db.LastAccessedAt >= before);
        }

        [Fact]
        public async Task DatabaseExistsAsync_WithExistingDatabase_ReturnsTrue()
        {
            await _manager.CreateDatabaseAsync("testdb");
            Assert.True(await _manager.DatabaseExistsAsync("testdb"));
        }

        [Fact]
        public async Task DatabaseExistsAsync_WithNonExistingDatabase_ReturnsFalse()
        {
            Assert.False(await _manager.DatabaseExistsAsync("nonexistent"));
        }

        [Fact]
        public async Task ListDatabasesAsync_ReturnsAllDatabases()
        {
            await _manager.CreateDatabaseAsync("db1");
            await _manager.CreateDatabaseAsync("db2");
            await _manager.CreateDatabaseAsync("db3");

            var databases = await _manager.ListDatabasesAsync();

            Assert.True(databases.Count >= 5); // 3 created + 2 system
            Assert.Contains(databases, d => d.Name == "db1");
            Assert.Contains(databases, d => d.Name == "db2");
            Assert.Contains(databases, d => d.Name == "db3");
            Assert.Contains(databases, d => d.Name == "_system");
            Assert.Contains(databases, d => d.Name == "default");
        }

        [Fact]
        public async Task GetDatabaseStatisticsAsync_ReturnsStatistics()
        {
            await _manager.CreateDatabaseAsync("db1");
            await _manager.CreateDatabaseAsync("db2");

            var stats = await _manager.GetDatabaseStatisticsAsync();

            Assert.True(stats.Count >= 2);
            Assert.Contains(stats, s => s.Name == "db1");
            Assert.Contains(stats, s => s.Name == "db2");
        }

        [Fact]
        public void GetDatabasePath_ReturnsCorrectPath()
        {
            var path = _manager.GetDatabasePath("mydb");
            Assert.Equal(Path.Combine(_testBasePath, "mydb"), path);
        }

        [Fact]
        public void GetDatabasePath_WithNullName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _manager.GetDatabasePath(null!));
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            _manager.Dispose();
            _manager.Dispose(); // Should not throw
        }
    }

    public class DatabaseTests
    {
        [Fact]
        public void Constructor_WithValidParameters_CreatesInstance()
        {
            var db = new Database("mydb", "/path/to/db");

            Assert.Equal("mydb", db.Name);
            Assert.Equal("/path/to/db", db.Path);
            Assert.NotNull(db.Options);
            Assert.NotNull(db.Security);
            Assert.True(db.CreatedAt <= DateTime.UtcNow);
        }

        [Fact]
        public void Constructor_WithNullName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new Database(null!, "/path"));
        }

        [Fact]
        public void Constructor_WithNullPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new Database("mydb", null!));
        }

        [Fact]
        public void Constructor_WithInvalidName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new Database("123invalid", "/path"));
            Assert.Throws<ArgumentException>(() => new Database("", "/path"));
            Assert.Throws<ArgumentException>(() => new Database("   ", "/path"));
            Assert.Throws<ArgumentException>(() => new Database("invalid name", "/path"));
        }

        [Theory]
        [InlineData("mydb", true)]
        [InlineData("my_db", true)]
        [InlineData("my-db", true)]
        [InlineData("_system", true)]
        [InlineData("MyDB123", true)]
        [InlineData("123invalid", false)]
        [InlineData("", false)]
        [InlineData("invalid name", false)]
        [InlineData("invalid@name", false)]
        public void IsValidDatabaseName_ValidatesCorrectly(string name, bool expected)
        {
            Assert.Equal(expected, Database.IsValidDatabaseName(name));
        }

        [Fact]
        public void IsSystemDatabase_ReturnsCorrectValue()
        {
            var systemDb = new Database("_system", "/path");
            var defaultDb = new Database("default", "/path");
            var userDb = new Database("mydb", "/path");

            Assert.True(systemDb.IsSystemDatabase);
            Assert.False(defaultDb.IsSystemDatabase);
            Assert.False(userDb.IsSystemDatabase);
        }

        [Fact]
        public void Touch_UpdatesLastAccessed()
        {
            var db = new Database("mydb", "/path");
            Assert.Null(db.LastAccessedAt);

            var before = DateTime.UtcNow;
            db.Touch();
            var after = DateTime.UtcNow;

            Assert.NotNull(db.LastAccessedAt);
            Assert.True(db.LastAccessedAt >= before);
            Assert.True(db.LastAccessedAt <= after);
        }

        [Fact]
        public void GetStatistics_ReturnsCorrectData()
        {
            var db = new Database("mydb", "/path", new DatabaseOptions { MaxCollections = 50, MaxSizeBytes = 1000 });
            db.CollectionCount = 10;
            db.SizeBytes = 500;

            var stats = db.GetStatistics();

            Assert.Equal("mydb", stats.Name);
            Assert.Equal(10, stats.CollectionCount);
            Assert.Equal(500, stats.SizeBytes);
            Assert.Equal(50, stats.MaxCollections);
            Assert.Equal(1000, stats.MaxSizeBytes);
            Assert.Equal(50, stats.SizeUtilizationPercent);
            Assert.Equal(20, stats.CollectionUtilizationPercent);
        }
    }

    public class DatabaseOptionsTests
    {
        [Fact]
        public void DefaultValues_AreCorrect()
        {
            var options = new DatabaseOptions();

            Assert.Equal(100, options.MaxCollections);
            Assert.Equal(10_737_418_240, options.MaxSizeBytes); // 10GB
            Assert.False(options.AllowAnonymousRead);
            Assert.True(options.EnableWAL);
            Assert.Null(options.DefaultDocumentTTL);
            Assert.False(options.CompressionEnabled);
        }

        [Fact]
        public void Clone_CreatesIndependentCopy()
        {
            var original = new DatabaseOptions
            {
                MaxCollections = 50,
                MaxSizeBytes = 1000,
                AllowAnonymousRead = true
            };

            var clone = original.Clone();

            Assert.Equal(original.MaxCollections, clone.MaxCollections);
            Assert.Equal(original.MaxSizeBytes, clone.MaxSizeBytes);
            Assert.Equal(original.AllowAnonymousRead, clone.AllowAnonymousRead);

            // Verify independence
            clone.MaxCollections = 999;
            Assert.Equal(50, original.MaxCollections);
        }

        [Fact]
        public void Validate_WithValidOptions_ReturnsEmpty()
        {
            var options = new DatabaseOptions();
            var errors = options.Validate();
            Assert.Empty(errors);
            Assert.True(options.IsValid);
        }

        [Theory]
        [InlineData(0, "MaxCollections must be at least 1")]
        [InlineData(-1, "MaxCollections must be at least 1")]
        [InlineData(10001, "MaxCollections cannot exceed 10000")]
        public void Validate_MaxCollections(int maxCollections, string expectedError)
        {
            var options = new DatabaseOptions { MaxCollections = maxCollections };
            var errors = options.Validate();
            Assert.Contains(expectedError, errors);
            Assert.False(options.IsValid);
        }

        [Fact]
        public void Validate_MaxSizeBytes_TooSmall()
        {
            var options = new DatabaseOptions { MaxSizeBytes = 1024 }; // 1KB
            var errors = options.Validate();
            Assert.Contains("MaxSizeBytes must be at least 1MB", errors);
        }
    }

    public class DatabaseSecurityTests
    {
        [Fact]
        public void GrantAccess_AddsUser()
        {
            var security = new DatabaseSecurity();
            security.GrantAccess("user1", DatabaseRole.Member);

            Assert.Equal(DatabaseRole.Member, security.GetUserRole("user1"));
        }

        [Fact]
        public void GrantAccess_UpdatesExistingUser()
        {
            var security = new DatabaseSecurity();
            security.GrantAccess("user1", DatabaseRole.Member);
            security.GrantAccess("user1", DatabaseRole.Admin);

            Assert.Equal(DatabaseRole.Admin, security.GetUserRole("user1"));
        }

        [Fact]
        public void GrantAccess_WithNullUsername_ThrowsArgumentException()
        {
            var security = new DatabaseSecurity();
            Assert.Throws<ArgumentException>(() => security.GrantAccess(null!, DatabaseRole.Member));
        }

        [Fact]
        public void RevokeAccess_RemovesUser()
        {
            var security = new DatabaseSecurity();
            security.GrantAccess("user1", DatabaseRole.Member);
            var result = security.RevokeAccess("user1");

            Assert.True(result);
            Assert.Equal(DatabaseRole.None, security.GetUserRole("user1"));
        }

        [Fact]
        public void RevokeAccess_WithNonExistingUser_ReturnsFalse()
        {
            var security = new DatabaseSecurity();
            var result = security.RevokeAccess("nonexistent");
            Assert.False(result);
        }

        [Theory]
        [InlineData("admin", DatabaseRole.Admin, true)]
        [InlineData("member", DatabaseRole.Member, true)]
        [InlineData("reader", DatabaseRole.Reader, true)]
        [InlineData("none", DatabaseRole.None, false)]
        [InlineData("nonexistent", DatabaseRole.Admin, false)]
        public void HasRole_ChecksCorrectly(string username, DatabaseRole requiredRole, bool expected)
        {
            var security = new DatabaseSecurity();
            security.GrantAccess("admin", DatabaseRole.Admin);
            security.GrantAccess("member", DatabaseRole.Member);
            security.GrantAccess("reader", DatabaseRole.Reader);

            Assert.Equal(expected, security.HasRole(username, requiredRole));
        }

        [Fact]
        public void CanRead_ChecksAllowedReadRoles()
        {
            var security = new DatabaseSecurity();
            security.GrantAccess("admin", DatabaseRole.Admin);
            security.GrantAccess("member", DatabaseRole.Member);
            security.GrantAccess("reader", DatabaseRole.Reader);
            security.GrantAccess("none", DatabaseRole.None);

            Assert.True(security.CanRead("admin"));
            Assert.True(security.CanRead("member"));
            Assert.True(security.CanRead("reader"));
            Assert.False(security.CanRead("none"));
        }

        [Fact]
        public void CanWrite_ChecksAllowedWriteRoles()
        {
            var security = new DatabaseSecurity();
            security.GrantAccess("admin", DatabaseRole.Admin);
            security.GrantAccess("member", DatabaseRole.Member);
            security.GrantAccess("reader", DatabaseRole.Reader);

            Assert.True(security.CanWrite("admin"));
            Assert.True(security.CanWrite("member"));
            Assert.False(security.CanWrite("reader"));
        }

        [Fact]
        public void CanAdminister_ChecksAllowedAdminRoles()
        {
            var security = new DatabaseSecurity();
            security.GrantAccess("admin", DatabaseRole.Admin);
            security.GrantAccess("member", DatabaseRole.Member);

            Assert.True(security.CanAdminister("admin"));
            Assert.False(security.CanAdminister("member"));
        }

        [Fact]
        public void Clone_CreatesIndependentCopy()
        {
            var original = new DatabaseSecurity();
            original.GrantAccess("user1", DatabaseRole.Admin);
            original.Owner = "admin";

            var clone = original.Clone();

            Assert.Equal(original.Owner, clone.Owner);
            Assert.Equal(original.GetUserRole("user1"), clone.GetUserRole("user1"));

            // Verify independence
            clone.GrantAccess("user1", DatabaseRole.Member);
            Assert.Equal(DatabaseRole.Admin, original.GetUserRole("user1"));
        }
    }

    public class DatabaseEventArgsTests
    {
        [Fact]
        public void Constructor_SetsProperties()
        {
            var args = new DatabaseEventArgs("mydb");

            Assert.Equal("mydb", args.DatabaseName);
            Assert.True(args.Timestamp <= DateTime.UtcNow);
        }

        [Fact]
        public void Constructor_WithNullName_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new DatabaseEventArgs(null!));
        }
    }
}
