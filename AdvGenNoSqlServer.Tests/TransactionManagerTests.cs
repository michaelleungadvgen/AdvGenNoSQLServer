using AdvGenNoSqlServer.Core.Transactions;
using System;
using System.Linq;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class TransactionManagerTests
{
    [Fact]
    public void TransactionManager_BeginTransaction_ReturnsValidId()
    {
        // Arrange
        var transactionManager = new TransactionManager();

        // Act
        var transactionId = transactionManager.BeginTransaction();

        // Assert
        Assert.False(string.IsNullOrEmpty(transactionId));
        Assert.Equal(TransactionStatus.Active, transactionManager.GetTransactionStatus(transactionId));
    }

    [Fact]
    public void TransactionManager_CommitTransaction_ChangesStatus()
    {
        // Arrange
        var transactionManager = new TransactionManager();
        var transactionId = transactionManager.BeginTransaction();

        // Act
        var result = transactionManager.CommitTransaction(transactionId);

        // Assert
        Assert.True(result);
        Assert.Equal(TransactionStatus.Committed, transactionManager.GetTransactionStatus(transactionId));
    }

    [Fact]
    public void TransactionManager_RollbackTransaction_ChangesStatus()
    {
        // Arrange
        var transactionManager = new TransactionManager();
        var transactionId = transactionManager.BeginTransaction();

        // Act
        var result = transactionManager.RollbackTransaction(transactionId);

        // Assert
        Assert.True(result);
        Assert.Equal(TransactionStatus.RolledBack, transactionManager.GetTransactionStatus(transactionId));
    }

    [Fact]
    public void TransactionManager_AddOperation_AddsToTransaction()
    {
        // Arrange
        var transactionManager = new TransactionManager();
        var transactionId = transactionManager.BeginTransaction();
        var operation = new TransactionOperation
        {
            OperationType = "INSERT",
            CollectionName = "test-collection"
        };

        // Act
        transactionManager.AddOperation(transactionId, operation);

        // Assert
        var operations = transactionManager.GetOperations(transactionId);
        Assert.Single(operations);
        Assert.Equal("INSERT", operations.First().OperationType);
        Assert.Equal("test-collection", operations.First().CollectionName);
    }

    [Fact]
    public void TransactionManager_AddOperation_ToCommittedTransaction_ThrowsException()
    {
        // Arrange
        var transactionManager = new TransactionManager();
        var transactionId = transactionManager.BeginTransaction();
        transactionManager.CommitTransaction(transactionId);
        var operation = new TransactionOperation
        {
            OperationType = "INSERT",
            CollectionName = "test-collection"
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => transactionManager.AddOperation(transactionId, operation));
    }

    [Fact]
    public void AdvancedTransactionManager_ExpiredTransaction_Fails()
    {
        // Arrange
        var transactionManager = new AdvancedTransactionManager(TimeSpan.FromMilliseconds(100));
        var transactionId = transactionManager.BeginTransaction();

        // Act
        System.Threading.Thread.Sleep(200); // Wait for transaction to expire

        // Assert
        Assert.Equal(TransactionStatus.Failed, transactionManager.GetTransactionStatus(transactionId));
    }
}