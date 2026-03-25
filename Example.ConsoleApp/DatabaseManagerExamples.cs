// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Database;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;

namespace AdvGenNoSqlServer.Example.ConsoleApp
{
    /// <summary>
    /// Examples demonstrating the DatabaseManager functionality for multi-database operations
    /// </summary>
    public class DatabaseManagerExamples
    {
        private readonly string _baseDataPath;

        public DatabaseManagerExamples(string baseDataPath = "./data/databasemanager_examples")
        {
            _baseDataPath = baseDataPath;
        }

        /// <summary>
        /// Run all DatabaseManager examples
        /// </summary>
        public async Task RunAllExamplesAsync()
        {
            Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  DatabaseManager Examples                                  ║");
            Console.WriteLine("║  Demonstrating multi-database support and management       ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

            try
            {
                CleanupDataDirectory();
                await RunDatabaseCreationExample();
                await RunDatabaseSecurityExample();
                await RunDatabaseConfigurationExample();
                await RunMultiTenantDatabaseExample();
                await RunDatabaseStatisticsExample();
                Console.WriteLine("\n✅ All DatabaseManager examples completed successfully!\n");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ Example failed: {ex.Message}");
                Console.ResetColor();
                throw;
            }
            finally
            {
                CleanupDataDirectory();
            }
        }

        private async Task RunDatabaseCreationExample()
        {
            PrintExampleHeader("Example 1: Database Creation and Management");

            Console.WriteLine("\n📁 Creating DatabaseManager...");
            var dbManager = new DatabaseManager(_baseDataPath);
            await dbManager.InitializeAsync();
            Console.WriteLine("   ✓ DatabaseManager initialized");

            Console.WriteLine("\n📋 Listing initial databases:");
            var initialDatabases = await dbManager.ListDatabasesAsync();
            foreach (var db in initialDatabases)
            {
                Console.WriteLine($"   • {db.Name}");
            }

            Console.WriteLine("\n🆕 Creating application databases:");

            var ecommerceDb = await dbManager.CreateDatabaseAsync("ecommerce", new DatabaseOptions
            {
                MaxCollections = 50,
                MaxSizeBytes = 1024L * 1024 * 1024,
                RequireAuthentication = true,
                EnableCompression = true
            });
            Console.WriteLine("   ✓ Created 'ecommerce' database");

            var analyticsDb = await dbManager.CreateDatabaseAsync("analytics", new DatabaseOptions
            {
                MaxCollections = 20,
                MaxSizeBytes = 512L * 1024 * 1024,
                RequireAuthentication = false,
                TtlEnabled = true,
                DefaultTtlSeconds = 86400
            });
            Console.WriteLine("   ✓ Created 'analytics' database");

            var blogDb = await dbManager.CreateDatabaseAsync("blog", new DatabaseOptions
            {
                MaxCollections = 10,
                MaxSizeBytes = 100L * 1024 * 1024,
                RequireAuthentication = true
            });
            Console.WriteLine("   ✓ Created 'blog' database");

            Console.WriteLine("\n📋 Listing all databases:");
            var allDatabases = await dbManager.ListDatabasesAsync();
            Console.WriteLine($"   Total databases: {allDatabases.Count}");

            Console.WriteLine("\n🗑️ Dropping 'blog' database:");
            await dbManager.DropDatabaseAsync("blog");
            Console.WriteLine("   ✓ Database 'blog' dropped successfully");
        }

        private async Task RunDatabaseSecurityExample()
        {
            PrintExampleHeader("Example 2: Database Security and Access Control");

            var dbManager = new DatabaseManager(_baseDataPath);
            await dbManager.InitializeAsync();

            Console.WriteLine("\n🔐 Creating secure database...");
            var secureDb = await dbManager.CreateDatabaseAsync("secure_app", new DatabaseOptions
            {
                RequireAuthentication = true,
                MaxCollections = 25
            });

            Console.WriteLine("\n👤 Granting database access:");
            secureDb.Security.GrantAccess("admin_user", DatabaseRole.Admin);
            secureDb.Security.GrantAccess("john_dev", DatabaseRole.Member);
            secureDb.Security.GrantAccess("sarah_reader", DatabaseRole.Reader);
            Console.WriteLine("   ✓ Granted access to 3 users");

            Console.WriteLine("\n🔍 Checking user roles:");
            Console.WriteLine($"   admin_user: {secureDb.Security.GetUserRole("admin_user")}");
            Console.WriteLine($"   john_dev: {secureDb.Security.GetUserRole("john_dev")}");
            Console.WriteLine($"   sarah_reader: {secureDb.Security.GetUserRole("sarah_reader")}");

            Console.WriteLine("\n🔐 Checking access permissions:");
            Console.WriteLine($"   admin_user is admin: {secureDb.Security.HasAccess("admin_user", DatabaseRole.Admin)}");
            Console.WriteLine($"   john_dev can write: {secureDb.Security.HasAccess("john_dev", DatabaseRole.Member)}");
            Console.WriteLine($"   sarah_reader can read: {secureDb.Security.HasAccess("sarah_reader", DatabaseRole.Reader)}");
        }

        private async Task RunDatabaseConfigurationExample()
        {
            PrintExampleHeader("Example 3: Database Configuration Options");

            var dbManager = new DatabaseManager(_baseDataPath);
            await dbManager.InitializeAsync();

            Console.WriteLine("\n⚙️ Creating databases with different configurations:");

            var perfDb = await dbManager.CreateDatabaseAsync("high_performance", new DatabaseOptions
            {
                MaxCollections = 100,
                MaxSizeBytes = 10L * 1024 * 1024 * 1024,
                EnableCompression = false
            });
            Console.WriteLine("   ✓ Created 'high_performance' database (compression disabled)");

            var archiveDb = await dbManager.CreateDatabaseAsync("archives", new DatabaseOptions
            {
                MaxCollections = 10,
                MaxSizeBytes = 5L * 1024 * 1024 * 1024,
                EnableCompression = true,
                TtlEnabled = true,
                DefaultTtlSeconds = 2592000
            });
            Console.WriteLine("   ✓ Created 'archives' database (compression enabled, 30-day TTL)");

            var referenceDb = await dbManager.CreateDatabaseAsync("reference_data", new DatabaseOptions
            {
                MaxCollections = 5,
                MaxSizeBytes = 100L * 1024 * 1024,
                ReadOnly = true,
                AllowDeletes = false
            });
            Console.WriteLine("   ✓ Created 'reference_data' database (read-only)");
        }

        private async Task RunMultiTenantDatabaseExample()
        {
            PrintExampleHeader("Example 4: Multi-Tenant Database Isolation");

            var dbManager = new DatabaseManager(_baseDataPath);
            await dbManager.InitializeAsync();

            Console.WriteLine("\n🏢 Creating tenant databases:");

            var tenantADb = await dbManager.CreateDatabaseAsync("tenant_acme_corp", new DatabaseOptions
            {
                MaxCollections = 30,
                MaxSizeBytes = 2L * 1024 * 1024 * 1024,
                RequireAuthentication = true
            });
            Console.WriteLine("   ✓ Created database for 'ACME Corp'");

            var tenantBDb = await dbManager.CreateDatabaseAsync("tenant_globex_inc", new DatabaseOptions
            {
                MaxCollections = 50,
                MaxSizeBytes = 5L * 1024 * 1024 * 1024,
                RequireAuthentication = true
            });
            Console.WriteLine("   ✓ Created database for 'Globex Inc'");

            tenantADb.Security.GrantAccess("acme_admin", DatabaseRole.Admin);
            tenantADb.Security.GrantAccess("acme_user1", DatabaseRole.Member);
            tenantBDb.Security.GrantAccess("globex_admin", DatabaseRole.Admin);
            tenantBDb.Security.GrantAccess("globex_user1", DatabaseRole.Member);
            Console.WriteLine("   ✓ Set up tenant-specific security");

            Console.WriteLine("\n🔍 Verifying tenant isolation:");
            Console.WriteLine($"   ACME admin can access ACME DB: {tenantADb.Security.HasAccess("acme_admin", DatabaseRole.Admin)}");
            Console.WriteLine($"   Globex user CANNOT access ACME DB: {!tenantADb.Security.HasAccess("globex_user1", DatabaseRole.Reader)}");
        }

        private async Task RunDatabaseStatisticsExample()
        {
            PrintExampleHeader("Example 5: Database Statistics and Monitoring");

            var dbManager = new DatabaseManager(_baseDataPath);
            await dbManager.InitializeAsync();

            Console.WriteLine("\n📊 Creating test database...");
            var statsDb = await dbManager.CreateDatabaseAsync("stats_demo", new DatabaseOptions
            {
                MaxCollections = 10,
                MaxSizeBytes = 100L * 1024 * 1024
            });

            var dbPath = Path.Combine(_baseDataPath, "stats_demo");
            var documentStore = new PersistentDocumentStore(dbPath);
            await documentStore.InitializeAsync();

            Console.WriteLine("   Adding sample documents...");
            var random = new Random();
            for (int i = 1; i <= 100; i++)
            {
                var doc = new Document
                {
                    Id = $"doc_{i:D4}",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = $"Item {i}",
                        ["value"] = random.Next(1, 1000)
                    }
                };
                await documentStore.InsertAsync("items", doc);
            }
            Console.WriteLine("   ✓ Added 100 documents to 'items' collection");

            Console.WriteLine("\n📈 Database Statistics:");
            var dbStats = await dbManager.GetDatabaseStatisticsAsync();
            Console.WriteLine($"   Total Databases: {dbStats.TotalDatabases}");
            Console.WriteLine($"   Total Size: {FormatBytes(dbStats.TotalSizeBytes)}");
            Console.WriteLine($"   Average Database Size: {FormatBytes(dbStats.AverageDatabaseSizeBytes)}");

            Console.WriteLine("\n💾 Database Utilization:");
            var statsDbInfo = await dbManager.GetDatabaseAsync("stats_demo");
            if (statsDbInfo != null)
            {
                var size = await dbManager.GetDatabaseSizeAsync("stats_demo");
                var utilization = (double)size / statsDbInfo.Options.MaxSizeBytes * 100;
                Console.WriteLine($"   Current Size: {FormatBytes(size)}");
                Console.WriteLine($"   Max Size: {FormatBytes(statsDbInfo.Options.MaxSizeBytes)}");
                Console.WriteLine($"   Utilization: {utilization:F2}%");
            }
        }

        private void PrintExampleHeader(string title)
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("  " + title);
            Console.WriteLine(new string('═', 60));
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return $"{number:n2} {suffixes[counter]}";
        }

        private void CleanupDataDirectory()
        {
            try
            {
                if (Directory.Exists(_baseDataPath))
                {
                    Directory.Delete(_baseDataPath, true);
                    Console.WriteLine($"🧹 Cleaned up data directory: {_baseDataPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Warning: Could not clean up: {ex.Message}");
            }
        }
    }
}
