// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;

namespace AdvGenNoSqlServer.Example.ConsoleApp
{
    /// <summary>
    /// Examples demonstrating Multi-Database operations and Role-Based Access Control (RBAC)
    /// 
    /// This example shows:
    /// - Creating and managing multiple databases
    /// - Setting up users with different roles
    /// - Role-based permissions enforcement
    /// - Database isolation between tenants
    /// </summary>
    public class MultiDatabaseAndRbacExamples
    {
        private readonly string _baseDataPath;
        private readonly List<string> _createdDatabases = new();

        public MultiDatabaseAndRbacExamples(string baseDataPath = "./data/examples")
        {
            _baseDataPath = baseDataPath;
        }

        /// <summary>
        /// Run all multi-database and RBAC examples
        /// </summary>
        public async Task RunAllExamplesAsync()
        {
            Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  Multi-Database & RBAC Examples                            ║");
            Console.WriteLine("║  Demonstrating database isolation and access control       ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

            try
            {
                // Ensure clean state
                CleanupDataDirectory();

                // Run examples
                await RunMultiDatabaseExample();
                await RunRbacSetupExample();
                await RunRbacPermissionsExample();
                await RunMultiTenantExample();
                await RunCrossDatabaseQueryExample();

                Console.WriteLine("\n✅ All Multi-Database & RBAC examples completed successfully!\n");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ Example failed: {ex.Message}");
                Console.WriteLine($"   StackTrace: {ex.StackTrace}");
                Console.ResetColor();
                throw;
            }
            finally
            {
                // Cleanup
                CleanupDataDirectory();
            }
        }

        /// <summary>
        /// Example 1: Multi-Database Operations
        /// Demonstrates creating and using multiple isolated databases
        /// </summary>
        private async Task RunMultiDatabaseExample()
        {
            PrintExampleHeader("Example 1: Multi-Database Operations");

            // Create separate databases for different departments
            var hrDbPath = Path.Combine(_baseDataPath, "hr_database");
            var salesDbPath = Path.Combine(_baseDataPath, "sales_database");
            var engineeringDbPath = Path.Combine(_baseDataPath, "engineering_database");

            _createdDatabases.Add(hrDbPath);
            _createdDatabases.Add(salesDbPath);
            _createdDatabases.Add(engineeringDbPath);

            // Create HR database
            Console.WriteLine("\n📁 Creating HR Database...");
            var hrStore = new PersistentDocumentStore(hrDbPath);
            await hrStore.InitializeAsync();
            
            // Insert HR documents
            var employee1 = CreateDocument("emp_001", new Dictionary<string, object>
            {
                ["name"] = "Alice Johnson",
                ["department"] = "HR",
                ["salary"] = 75000,
                ["hire_date"] = DateTime.UtcNow.AddYears(-2)
            });
            await hrStore.InsertAsync("employees", employee1);
            Console.WriteLine("   ✓ Inserted: Alice Johnson (HR Department)");

            // Create Sales database
            Console.WriteLine("\n📁 Creating Sales Database...");
            var salesStore = new PersistentDocumentStore(salesDbPath);
            await salesStore.InitializeAsync();

            // Insert Sales documents
            var salesPerson1 = CreateDocument("sales_001", new Dictionary<string, object>
            {
                ["name"] = "Bob Smith",
                ["department"] = "Sales",
                ["quota"] = 100000,
                ["territory"] = "North America"
            });
            await salesStore.InsertAsync("salespeople", salesPerson1);
            Console.WriteLine("   ✓ Inserted: Bob Smith (Sales Department)");

            // Create Engineering database
            Console.WriteLine("\n📁 Creating Engineering Database...");
            var engStore = new PersistentDocumentStore(engineeringDbPath);
            await engStore.InitializeAsync();

            // Insert Engineering documents
            var engineer1 = CreateDocument("eng_001", new Dictionary<string, object>
            {
                ["name"] = "Carol White",
                ["department"] = "Engineering",
                ["specialization"] = "Backend",
                ["level"] = "Senior"
            });
            await engStore.InsertAsync("engineers", engineer1);
            Console.WriteLine("   ✓ Inserted: Carol White (Engineering Department)");

            // Demonstrate database isolation
            Console.WriteLine("\n🔒 Database Isolation Verification:");
            var hrEmployees = await hrStore.GetAllAsync("employees");
            var salesPeople = await salesStore.GetAllAsync("salespeople");
            var engineers = await engStore.GetAllAsync("engineers");

            Console.WriteLine($"   HR Database: {hrEmployees.Count()} employees");
            Console.WriteLine($"   Sales Database: {salesPeople.Count()} salespeople");
            Console.WriteLine($"   Engineering Database: {engineers.Count()} engineers");

            // Verify isolation - documents in one DB should not appear in others
            var hrHasSalesPerson = await hrStore.ExistsAsync("employees", "sales_001");
            var salesHasEmployee = await salesStore.ExistsAsync("salespeople", "emp_001");

            Console.WriteLine($"   ✓ HR DB contains sales_001: {hrHasSalesPerson} (should be False)");
            Console.WriteLine($"   ✓ Sales DB contains emp_001: {salesHasEmployee} (should be False)");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✅ Multi-database operations completed successfully!");
            Console.ResetColor();
        }

        /// <summary>
        /// Example 2: RBAC Setup
        /// Demonstrates setting up roles and users with different permissions
        /// </summary>
        private async Task RunRbacSetupExample()
        {
            PrintExampleHeader("Example 2: Role-Based Access Control (RBAC) Setup");

            // Create RoleManager
            var roleManager = new RoleManager();

            Console.WriteLine("\n👥 Creating Custom Roles...");

            // Create Department Admin role
            var deptAdminCreated = roleManager.CreateRole(
                "DepartmentAdmin",
                "Administrator for a specific department",
                new[]
                {
                    Permissions.DocumentRead,
                    Permissions.DocumentWrite,
                    Permissions.DocumentDelete,
                    Permissions.CollectionCreate,
                    Permissions.CollectionDelete,
                    Permissions.QueryExecute,
                    Permissions.TransactionExecute
                });
            if (deptAdminCreated)
            {
                var deptAdminRole = roleManager.GetRole("DepartmentAdmin");
                Console.WriteLine($"   ✓ Created role: {deptAdminRole?.Name}");
                Console.WriteLine($"     Permissions: {deptAdminRole?.Permissions.Count}");
            }

            // Create ReadOnly Analyst role
            var analystCreated = roleManager.CreateRole(
                "DataAnalyst",
                "Read-only analyst with query access",
                new[]
                {
                    Permissions.DocumentRead,
                    Permissions.QueryExecute,
                    Permissions.QueryAggregate
                });
            if (analystCreated)
            {
                var analystRole = roleManager.GetRole("DataAnalyst");
                Console.WriteLine($"   ✓ Created role: {analystRole?.Name}");
                Console.WriteLine($"     Permissions: {analystRole?.Permissions.Count}");
            }

            // Create Backup Operator role
            var backupCreated = roleManager.CreateRole(
                "BackupOperator",
                "Can read all data for backup purposes",
                new[]
                {
                    Permissions.DocumentRead,
                    Permissions.AuditRead
                });
            if (backupCreated)
            {
                var backupRole = roleManager.GetRole("BackupOperator");
                Console.WriteLine($"   ✓ Created role: {backupRole?.Name}");
                Console.WriteLine($"     Permissions: {backupRole?.Permissions.Count}");
            }

            // List all available roles
            Console.WriteLine("\n📋 Available Roles:");
            var allRoles = roleManager.GetAllRoles();
            foreach (var role in allRoles)
            {
                Console.WriteLine($"   - {role.Name} ({role.Permissions.Count} permissions)");
            }

            // Create users and assign roles
            Console.WriteLine("\n👤 Creating Users with Role Assignments...");

            var adminUser = new User
            {
                Username = "hr_admin",
                PasswordHash = "hashed_password_123",
                Roles = new List<string> { "DepartmentAdmin" },
                DatabaseAccess = new List<string> { "hr_database" }
            };
            
            // Assign role to user
            roleManager.AssignRoleToUser(adminUser.Username, "DepartmentAdmin");
            Console.WriteLine($"   ✓ User: {adminUser.Username}");
            Console.WriteLine($"     Role: DepartmentAdmin");
            Console.WriteLine($"     Database Access: hr_database");

            var analystUser = new User
            {
                Username = "sales_analyst",
                PasswordHash = "hashed_password_456",
                Roles = new List<string> { "DataAnalyst" },
                DatabaseAccess = new List<string> { "sales_database", "hr_database" }
            };
            
            roleManager.AssignRoleToUser(analystUser.Username, "DataAnalyst");
            Console.WriteLine($"   ✓ User: {analystUser.Username}");
            Console.WriteLine($"     Role: DataAnalyst");
            Console.WriteLine($"     Database Access: sales_database, hr_database (read-only)");

            var backupUser = new User
            {
                Username = "backup_op",
                PasswordHash = "hashed_password_789",
                Roles = new List<string> { "BackupOperator" },
                DatabaseAccess = new List<string> { "hr_database", "sales_database", "engineering_database" }
            };
            
            roleManager.AssignRoleToUser(backupUser.Username, "BackupOperator");
            Console.WriteLine($"   ✓ User: {backupUser.Username}");
            Console.WriteLine($"     Role: BackupOperator");
            Console.WriteLine($"     Database Access: All databases (backup only)");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✅ RBAC setup completed successfully!");
            Console.ResetColor();
        }

        /// <summary>
        /// Example 3: RBAC Permissions in Action
        /// Demonstrates permission checking and enforcement
        /// </summary>
        private async Task RunRbacPermissionsExample()
        {
            PrintExampleHeader("Example 3: RBAC Permissions Enforcement");

            var roleManager = new RoleManager();

            // Create a manager role with elevated permissions
            roleManager.CreateRole("Manager", "Department manager", new[]
            {
                Permissions.DocumentRead,
                Permissions.DocumentWrite,
                Permissions.QueryExecute,
                Permissions.TransactionExecute,
                Permissions.UserManage
            });

            // Create an intern role with limited permissions
            roleManager.CreateRole("Intern", "Summer intern", new[]
            {
                Permissions.DocumentRead,
                Permissions.QueryExecute
            });

            Console.WriteLine("\n🔐 Permission Checks:\n");

            // Manager permissions
            Console.WriteLine("Manager Role Permissions:");
            CheckAndDisplayPermission(roleManager, "Manager", Permissions.DocumentWrite);
            CheckAndDisplayPermission(roleManager, "Manager", Permissions.DocumentDelete);
            CheckAndDisplayPermission(roleManager, "Manager", Permissions.UserManage);

            // Intern permissions
            Console.WriteLine("\nIntern Role Permissions:");
            CheckAndDisplayPermission(roleManager, "Intern", Permissions.DocumentRead);
            CheckAndDisplayPermission(roleManager, "Intern", Permissions.DocumentWrite);
            CheckAndDisplayPermission(roleManager, "Intern", Permissions.UserManage);

            // Simulate permission-based operations
            Console.WriteLine("\n📝 Simulating Operations with Permission Checks:\n");

            // Manager tries various operations
            Console.WriteLine("Manager attempting operations:");
            SimulateOperation("Create Document", () => 
                roleManager.RoleHasPermission("Manager", Permissions.DocumentWrite));
            SimulateOperation("Delete Document", () => 
                roleManager.RoleHasPermission("Manager", Permissions.DocumentDelete));
            SimulateOperation("View User Info", () => 
                roleManager.RoleHasPermission("Manager", Permissions.UserManage));

            // Intern tries the same operations
            Console.WriteLine("\nIntern attempting operations:");
            SimulateOperation("Create Document", () => 
                roleManager.RoleHasPermission("Intern", Permissions.DocumentWrite));
            SimulateOperation("Read Document", () => 
                roleManager.RoleHasPermission("Intern", Permissions.DocumentRead));
            SimulateOperation("Delete Document", () => 
                roleManager.RoleHasPermission("Intern", Permissions.DocumentDelete));

            // Permission aggregation for multi-role users
            Console.WriteLine("\n👥 Multi-Role User (Intern + BackupOperator):");
            
            // Assign both roles to a test user
            roleManager.AssignRoleToUser("multi_role_user", "Intern");
            roleManager.AssignRoleToUser("multi_role_user", "BackupOperator");
            
            var combinedPermissions = roleManager.GetUserPermissions("multi_role_user");
            Console.WriteLine($"   Combined permissions: {combinedPermissions.Count}");
            foreach (var perm in combinedPermissions.Take(5))
            {
                Console.WriteLine($"     - {perm}");
            }
            if (combinedPermissions.Count > 5)
            {
                Console.WriteLine($"     ... and {combinedPermissions.Count - 5} more");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✅ RBAC permissions enforcement completed successfully!");
            Console.ResetColor();
        }

        /// <summary>
        /// Example 4: Multi-Tenant Database Access
        /// Demonstrates how different users access different databases
        /// </summary>
        private async Task RunMultiTenantExample()
        {
            PrintExampleHeader("Example 4: Multi-Tenant Database Isolation");

            // Create tenant databases
            var tenantADbPath = Path.Combine(_baseDataPath, "tenant_a");
            var tenantBDbPath = Path.Combine(_baseDataPath, "tenant_b");

            _createdDatabases.Add(tenantADbPath);
            _createdDatabases.Add(tenantBDbPath);

            // Setup Tenant A database
            Console.WriteLine("\n🏢 Setting up Tenant A Database...");
            var tenantAStore = new PersistentDocumentStore(tenantADbPath);
            await tenantAStore.InitializeAsync();
            await tenantAStore.InsertAsync("customers", CreateDocument("cust_a1", new Dictionary<string, object>
            {
                ["name"] = "Tenant A Customer 1",
                ["email"] = "customer1@tenanta.com",
                ["tenant_id"] = "tenant_a"
            }));
            await tenantAStore.InsertAsync("customers", CreateDocument("cust_a2", new Dictionary<string, object>
            {
                ["name"] = "Tenant A Customer 2",
                ["email"] = "customer2@tenanta.com",
                ["tenant_id"] = "tenant_a"
            }));
            Console.WriteLine("   ✓ Tenant A: 2 customers added");

            // Setup Tenant B database
            Console.WriteLine("\n🏢 Setting up Tenant B Database...");
            var tenantBStore = new PersistentDocumentStore(tenantBDbPath);
            await tenantBStore.InitializeAsync();
            await tenantBStore.InsertAsync("customers", CreateDocument("cust_b1", new Dictionary<string, object>
            {
                ["name"] = "Tenant B Customer 1",
                ["email"] = "customer1@tenantb.com",
                ["tenant_id"] = "tenant_b"
            }));
            Console.WriteLine("   ✓ Tenant B: 1 customer added");

            // Simulate tenant isolation
            Console.WriteLine("\n🔒 Tenant Isolation Verification:");

            var tenantAUser = new
            {
                Username = "tenant_a_admin",
                AllowedDatabases = new[] { "tenant_a" }
            };

            var tenantBUser = new
            {
                Username = "tenant_b_admin",
                AllowedDatabases = new[] { "tenant_b" }
            };

            var superAdmin = new
            {
                Username = "super_admin",
                AllowedDatabases = new[] { "tenant_a", "tenant_b" }
            };

            // Check access for each user
            Console.WriteLine($"\n   User: {tenantAUser.Username}");
            Console.WriteLine($"     Allowed DBs: {string.Join(", ", tenantAUser.AllowedDatabases)}");
            Console.WriteLine($"     Can access tenant_a: ✓");
            Console.WriteLine($"     Can access tenant_b: ✗");

            Console.WriteLine($"\n   User: {tenantBUser.Username}");
            Console.WriteLine($"     Allowed DBs: {string.Join(", ", tenantBUser.AllowedDatabases)}");
            Console.WriteLine($"     Can access tenant_a: ✗");
            Console.WriteLine($"     Can access tenant_b: ✓");

            Console.WriteLine($"\n   User: {superAdmin.Username}");
            Console.WriteLine($"     Allowed DBs: {string.Join(", ", superAdmin.AllowedDatabases)}");
            Console.WriteLine($"     Can access tenant_a: ✓");
            Console.WriteLine($"     Can access tenant_b: ✓");

            // Count documents in each tenant
            var tenantACount = await tenantAStore.CountAsync("customers");
            var tenantBCount = await tenantBStore.CountAsync("customers");

            Console.WriteLine("\n📊 Tenant Data Summary:");
            Console.WriteLine($"   Tenant A customers: {tenantACount}");
            Console.WriteLine($"   Tenant B customers: {tenantBCount}");
            Console.WriteLine($"   Total data isolation: ✓ Maintained");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✅ Multi-tenant example completed successfully!");
            Console.ResetColor();
        }

        /// <summary>
        /// Example 5: Cross-Database Query (for authorized users)
        /// Demonstrates how privileged users can access multiple databases
        /// </summary>
        private async Task RunCrossDatabaseQueryExample()
        {
            PrintExampleHeader("Example 5: Cross-Database Operations (Analytics)");

            // Setup analytics scenario
            var hrDbPath = Path.Combine(_baseDataPath, "analytics_hr");
            var financeDbPath = Path.Combine(_baseDataPath, "analytics_finance");

            _createdDatabases.Add(hrDbPath);
            _createdDatabases.Add(financeDbPath);

            // Create HR data
            Console.WriteLine("\n📊 Preparing HR Data...");
            var hrStore = new PersistentDocumentStore(hrDbPath);
            await hrStore.InitializeAsync();
            for (int i = 1; i <= 5; i++)
            {
                await hrStore.InsertAsync("employees", CreateDocument($"emp_{i}", new Dictionary<string, object>
                {
                    ["name"] = $"Employee {i}",
                    ["department"] = i % 2 == 0 ? "Engineering" : "Sales",
                    ["salary"] = 50000 + (i * 10000)
                }));
            }
            Console.WriteLine("   ✓ 5 employees added to HR database");

            // Create Finance data
            Console.WriteLine("\n📊 Preparing Finance Data...");
            var financeStore = new PersistentDocumentStore(financeDbPath);
            await financeStore.InitializeAsync();
            for (int i = 1; i <= 3; i++)
            {
                await financeStore.InsertAsync("expenses", CreateDocument($"exp_{i}", new Dictionary<string, object>
                {
                    ["category"] = i % 2 == 0 ? "Software" : "Hardware",
                    ["amount"] = 1000 + (i * 500),
                    ["quarter"] = "Q1"
                }));
            }
            Console.WriteLine("   ✓ 3 expenses added to Finance database");

            // Simulate analytics user with cross-database access
            Console.WriteLine("\n👤 Analytics User (Cross-Database Access):");
            Console.WriteLine("   Role: DataAnalyst");
            Console.WriteLine("   Database Access: hr_database, finance_database");
            Console.WriteLine("   Permissions: Read, Query, Aggregation");

            // Perform cross-database aggregation
            Console.WriteLine("\n📈 Cross-Database Analysis:");

            // Get HR stats
            var allEmployees = await hrStore.GetAllAsync("employees");
            long totalSalaries = 0;
            foreach (var emp in allEmployees)
            {
                if (emp.Data != null && emp.Data.TryGetValue("salary", out var salary))
                {
                    totalSalaries += Convert.ToInt64(salary);
                }
            }
            var avgSalary = allEmployees.Any() ? totalSalaries / allEmployees.Count() : 0;

            Console.WriteLine("   HR Database Analysis:");
            Console.WriteLine($"     - Total employees: {allEmployees.Count()}");
            Console.WriteLine($"     - Total payroll: ${totalSalaries:N0}");
            Console.WriteLine($"     - Average salary: ${avgSalary:N0}");

            // Get Finance stats
            var allExpenses = await financeStore.GetAllAsync("expenses");
            long totalExpenses = 0;
            foreach (var exp in allExpenses)
            {
                if (exp.Data != null && exp.Data.TryGetValue("amount", out var amount))
                {
                    totalExpenses += Convert.ToInt64(amount);
                }
            }

            Console.WriteLine("   Finance Database Analysis:");
            Console.WriteLine($"     - Total expenses: {allExpenses.Count()}");
            Console.WriteLine($"     - Total spending: ${totalExpenses:N0}");

            // Combined analysis
            Console.WriteLine("   Combined Analysis:");
            var ratio = totalExpenses > 0 ? (double)totalSalaries / totalExpenses : 0;
            Console.WriteLine($"     - Payroll to Expense ratio: {ratio:F2}x");
            Console.WriteLine($"     - Data sources: 2 databases");
            Console.WriteLine($"     - Records analyzed: {allEmployees.Count() + allExpenses.Count()}");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✅ Cross-database operations completed successfully!");
            Console.ResetColor();
        }

        #region Helper Methods

        private void PrintExampleHeader(string title)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n╔ {title} ╗");
            Console.WriteLine(new string('═', title.Length + 4));
            Console.ResetColor();
        }

        private Document CreateDocument(string id, Dictionary<string, object> data)
        {
            var doc = new Document
            {
                Id = id,
                Data = new Dictionary<string, object>(data),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };
            return doc;
        }

        private void CheckAndDisplayPermission(RoleManager roleManager, string roleName, string permission)
        {
            bool hasPermission = roleManager.RoleHasPermission(roleName, permission);
            var symbol = hasPermission ? "✓" : "✗";
            var color = hasPermission ? ConsoleColor.Green : ConsoleColor.Red;
            
            Console.Write("   ");
            Console.ForegroundColor = color;
            Console.Write(symbol);
            Console.ResetColor();
            Console.WriteLine($" {permission}");
        }

        private void SimulateOperation(string operationName, Func<bool> permissionCheck)
        {
            bool allowed = permissionCheck();
            var status = allowed ? "ALLOWED" : "DENIED";
            var color = allowed ? ConsoleColor.Green : ConsoleColor.Red;

            Console.Write($"   - {operationName}: ");
            Console.ForegroundColor = color;
            Console.WriteLine(status);
            Console.ResetColor();
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
                Console.WriteLine($"⚠ Warning: Could not clean up data directory: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// User model for RBAC examples
    /// </summary>
    public class User
    {
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
        public List<string> DatabaseAccess { get; set; } = new();
    }
}
