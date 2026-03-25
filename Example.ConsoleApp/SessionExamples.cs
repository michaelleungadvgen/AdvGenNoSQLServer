// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Sessions;
using AdvGenNoSqlServer.Core.Transactions;
using AdvGenNoSqlServer.Storage;

namespace AdvGenNoSqlServer.Example.ConsoleApp;

/// <summary>
/// Examples demonstrating Session/Unit of Work pattern for database operations
/// </summary>
public static class SessionExamples
{
    /// <summary>
    /// Run all Session examples
    /// </summary>
    public static async Task RunAllExamplesAsync()
    {
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           Session/Unit of Work Pattern Examples              ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝\n");

        await Example1_BasicSessionWithTransaction();
        await Example2_ChangeTracking();
        await Example3_UnitOfWorkPattern();
    }

    /// <summary>
    /// Example 1: Basic Session with Transaction
    /// Demonstrates basic CRUD operations using the Session pattern
    /// </summary>
    public static async Task Example1_BasicSessionWithTransaction()
    {
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Example 1: Basic Session with Transaction                   │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────┘\n");

        // Create a document store and transaction coordinator
        var documentStore = new DocumentStore();
        var writeAheadLog = new WriteAheadLog(new WalOptions { LogDirectory = "./data/examples/wal" });
        var lockManager = new LockManager();
        var transactionCoordinator = new TransactionCoordinator(writeAheadLog, lockManager);

        // Create session with default options
        var options = SessionOptions.Default;
        using var session = new Session(documentStore, transactionCoordinator, options);

        Console.WriteLine($"Session created with ID: {session.SessionId}");
        Console.WriteLine($"Initial session state: {session.State}");
        Console.WriteLine($"Current transaction ID: {session.CurrentTransactionId}");

        // Insert documents
        Console.WriteLine("\n  Inserting documents...");
        var user1 = new Document
        {
            Id = "user_001",
            Data = new Dictionary<string, object>
            {
                ["name"] = "John Doe",
                ["email"] = "john@example.com",
                ["role"] = "Developer"
            }
        };

        var user2 = new Document
        {
            Id = "user_002",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Jane Smith",
                ["email"] = "jane@example.com",
                ["role"] = "Manager"
            }
        };

        await session.InsertAsync("users", user1);
        Console.WriteLine($"  ✓ Inserted user: {user1.Data["name"]}");

        await session.InsertAsync("users", user2);
        Console.WriteLine($"  ✓ Inserted user: {user2.Data["name"]}");

        // Retrieve a document
        Console.WriteLine("\n  Retrieving document...");
        var retrievedUser = await session.GetAsync("users", "user_001");
        if (retrievedUser != null)
        {
            Console.WriteLine($"  ✓ Retrieved user: {retrievedUser.Data["name"]} ({retrievedUser.Data["email"]})");
        }

        // Update a document
        Console.WriteLine("\n  Updating document...");
        user1.Data["role"] = "Senior Developer";
        await session.UpdateAsync("users", user1);
        Console.WriteLine($"  ✓ Updated user role to: {user1.Data["role"]}");

        // Commit the transaction
        Console.WriteLine("\n  Committing transaction...");
        await session.CommitAsync();
        Console.WriteLine($"  ✓ Transaction committed. Session state: {session.State}");

        // Verify changes persisted
        Console.WriteLine("\n  Verifying persisted changes...");
        var persistedUser = await documentStore.GetAsync("users", "user_001");
        if (persistedUser != null)
        {
            Console.WriteLine($"  ✓ User in store: {persistedUser.Data["name"]} - Role: {persistedUser.Data["role"]}");
        }

        Console.WriteLine("\n✅ Example 1 completed successfully!\n");
    }

    /// <summary>
    /// Example 2: Change Tracking
    /// Demonstrates automatic change tracking for documents
    /// </summary>
    public static async Task Example2_ChangeTracking()
    {
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Example 2: Change Tracking                                  │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────┘\n");

        // Create a document store and transaction coordinator
        var documentStore = new DocumentStore();
        var writeAheadLog = new WriteAheadLog(new WalOptions { LogDirectory = "./data/examples/wal" });
        var lockManager = new LockManager();
        var transactionCoordinator = new TransactionCoordinator(writeAheadLog, lockManager);

        // Pre-populate some data
        var product = new Document
        {
            Id = "product_001",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Laptop",
                ["price"] = 999.99,
                ["stock"] = 50,
                ["category"] = "Electronics"
            }
        };
        await documentStore.InsertAsync("products", product);
        Console.WriteLine("  Pre-populated product in store");

        // Create session with change tracking enabled
        var options = new SessionOptions
        {
            EnableChangeTracking = true,
            AutoBeginTransaction = true
        };

        using var session = new Session(documentStore, transactionCoordinator, options);
        Console.WriteLine($"\nSession created with change tracking enabled");
        Console.WriteLine($"Change tracker has {session.ChangeTracker.TrackedEntities.Count} tracked entities");

        // Load document through session - it will be tracked
        Console.WriteLine("\n  Loading product through session...");
        var trackedProduct = await session.GetAsync("products", "product_001");
        if (trackedProduct != null)
        {
            Console.WriteLine($"  ✓ Loaded product: {trackedProduct.Data["name"]}");
            Console.WriteLine($"  Tracked entities: {session.ChangeTracker.TrackedEntities.Count}");
        }

        // Modify the tracked document
        Console.WriteLine("\n  Modifying tracked document...");
        trackedProduct!.Data["price"] = 899.99;
        trackedProduct.Data["stock"] = 45;
        Console.WriteLine($"  ✓ Price changed to: ${trackedProduct.Data["price"]}");
        Console.WriteLine($"  ✓ Stock changed to: {trackedProduct.Data["stock"]}");

        // Save changes - session detects what changed
        Console.WriteLine("\n  Saving changes...");
        var changesCount = await session.SaveChangesAsync();
        Console.WriteLine($"  ✓ Saved {changesCount} changed document(s)");

        // Commit the transaction
        Console.WriteLine("\n  Committing transaction...");
        await session.CommitAsync();
        Console.WriteLine($"  ✓ Changes committed");

        // Verify changes
        Console.WriteLine("\n  Verifying changes persisted...");
        var updatedProduct = await documentStore.GetAsync("products", "product_001");
        if (updatedProduct != null)
        {
            Console.WriteLine($"  ✓ Updated price: ${updatedProduct.Data["price"]}");
            Console.WriteLine($"  ✓ Updated stock: {updatedProduct.Data["stock"]}");
        }

        // Demonstrate clearing change tracker
        Console.WriteLine("\n  Clearing change tracker...");
        session.ClearChangeTracker();
        Console.WriteLine($"  ✓ Tracked entities after clear: {session.ChangeTracker.TrackedEntities.Count}");

        Console.WriteLine("\n✅ Example 2 completed successfully!\n");
    }

    /// <summary>
    /// Example 3: Unit of Work Pattern
    /// Demonstrates multiple operations that succeed or fail together
    /// </summary>
    public static async Task Example3_UnitOfWorkPattern()
    {
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Example 3: Unit of Work Pattern                             │");
        Console.WriteLine("│ (Multiple operations that succeed or fail together)         │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────┘\n");

        // Create a document store and transaction coordinator
        var documentStore = new DocumentStore();
        var writeAheadLog = new WriteAheadLog(new WalOptions { LogDirectory = "./data/examples/wal" });
        var lockManager = new LockManager();
        var transactionCoordinator = new TransactionCoordinator(writeAheadLog, lockManager);

        Console.WriteLine("Scenario: Bank Account Transfer");
        Console.WriteLine("-------------------------------");
        Console.WriteLine("Transferring $500 from Account A to Account B\n");

        // Create initial account data
        var accountA = new Document
        {
            Id = "account_a",
            Data = new Dictionary<string, object>
            {
                ["owner"] = "Alice",
                ["balance"] = 1000.00,
                ["account_type"] = "Checking"
            }
        };

        var accountB = new Document
        {
            Id = "account_b",
            Data = new Dictionary<string, object>
            {
                ["owner"] = "Bob",
                ["balance"] = 500.00,
                ["account_type"] = "Savings"
            }
        };

        await documentStore.InsertAsync("accounts", accountA);
        await documentStore.InsertAsync("accounts", accountB);

        Console.WriteLine("Initial balances:");
        Console.WriteLine($"  Account A (Alice): $1000.00");
        Console.WriteLine($"  Account B (Bob):   $500.00\n");

        // Use session factory for easier session management
        var sessionFactory = new SessionFactory(documentStore, transactionCoordinator);

        // Create session with Unit of Work options
        var options = new SessionOptions
        {
            EnableChangeTracking = true,
            AutoBeginTransaction = true,
            IsolationLevel = IsolationLevel.ReadCommitted
        };

        using var session = sessionFactory.CreateSession(options);
        Console.WriteLine($"Session created via SessionFactory");
        Console.WriteLine($"Transaction started automatically\n");

        try
        {
            // Step 1: Load both accounts
            Console.WriteLine("Step 1: Loading accounts...");
            var aliceAccount = await session.GetAsync("accounts", "account_a");
            var bobAccount = await session.GetAsync("accounts", "account_b");

            if (aliceAccount == null || bobAccount == null)
            {
                throw new InvalidOperationException("One or both accounts not found");
            }

            Console.WriteLine($"  ✓ Loaded {aliceAccount.Data["owner"]}'s account");
            Console.WriteLine($"  ✓ Loaded {bobAccount.Data["owner"]}'s account\n");

            // Step 2: Verify sufficient funds
            Console.WriteLine("Step 2: Verifying sufficient funds...");
            var transferAmount = 500.00;
            var aliceBalance = Convert.ToDouble(aliceAccount.Data["balance"]);

            if (aliceBalance < transferAmount)
            {
                throw new InvalidOperationException("Insufficient funds for transfer");
            }

            Console.WriteLine($"  ✓ Sufficient funds available (${aliceBalance})\n");

            // Step 3: Perform the transfer (debit from Alice, credit to Bob)
            Console.WriteLine("Step 3: Performing transfer...");

            aliceAccount.Data["balance"] = aliceBalance - transferAmount;
            var bobBalance = Convert.ToDouble(bobAccount.Data["balance"]);
            bobAccount.Data["balance"] = bobBalance + transferAmount;

            Console.WriteLine($"  ✓ Debited ${transferAmount} from Alice's account");
            Console.WriteLine($"  ✓ Credited ${transferAmount} to Bob's account\n");

            // Step 4: Save changes
            Console.WriteLine("Step 4: Saving changes...");
            var savedCount = await session.SaveChangesAsync();
            Console.WriteLine($"  ✓ Saved {savedCount} account updates\n");

            // Step 5: Commit the transaction
            Console.WriteLine("Step 5: Committing transaction...");
            await session.CommitAsync();
            Console.WriteLine($"  ✓ Transaction committed successfully!\n");

            // Verify final balances
            Console.WriteLine("Final balances:");
            var finalAlice = await documentStore.GetAsync("accounts", "account_a");
            var finalBob = await documentStore.GetAsync("accounts", "account_b");

            if (finalAlice != null && finalBob != null)
            {
                Console.WriteLine($"  Account A (Alice): ${finalAlice.Data["balance"]}");
                Console.WriteLine($"  Account B (Bob):   ${finalBob.Data["balance"]}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  ✗ Error occurred: {ex.Message}");
            Console.WriteLine("  Rolling back transaction...");

            await session.RollbackAsync();
            Console.WriteLine($"  ✓ Transaction rolled back. No changes persisted.\n");
        }

        // Demonstrate rollback scenario
        Console.WriteLine("\n--- Rollback Demonstration ---");
        Console.WriteLine("Attempting transfer with insufficient funds...\n");

        using var session2 = sessionFactory.CreateSession(options);

        try
        {
            var aliceAccount = await session2.GetAsync("accounts", "account_a");
            var bobAccount = await session2.GetAsync("accounts", "account_b");

            // Try to transfer more than available
            var excessiveAmount = 10000.00;
            var currentBalance = Convert.ToDouble(aliceAccount!.Data["balance"]);

            if (currentBalance < excessiveAmount)
            {
                throw new InvalidOperationException($"Insufficient funds: have ${currentBalance}, need ${excessiveAmount}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ {ex.Message}");
            Console.WriteLine("  Rolling back transaction...");
            await session2.RollbackAsync();
            Console.WriteLine($"  ✓ Transaction rolled back - accounts remain unchanged");

            // Verify no changes
            var aliceAfterRollback = await documentStore.GetAsync("accounts", "account_a");
            Console.WriteLine($"\n  Alice's balance after rollback: ${aliceAfterRollback?.Data["balance"]}");
        }

        Console.WriteLine("\n✅ Example 3 completed successfully!\n");
    }
}
