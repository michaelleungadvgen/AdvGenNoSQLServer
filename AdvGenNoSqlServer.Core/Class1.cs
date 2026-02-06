using AdvGenNoSqlServer.Core.Caching;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Transactions;

namespace AdvGenNoSqlServer.Core;

/// <summary>
/// Example class demonstrating the use of the cache manager and transaction manager
/// </summary>
public class DocumentService
{
    private readonly ICacheManager _cacheManager;
    private readonly ITransactionManager _transactionManager;

    public DocumentService(ICacheManager cacheManager, ITransactionManager transactionManager)
    {
        _cacheManager = cacheManager;
        _transactionManager = transactionManager;
    }

    public Document? GetDocument(string id)
    {
        // Try to get from cache first
        var cachedDocument = _cacheManager.Get(id);
        if (cachedDocument != null)
        {
            return cachedDocument;
        }

        // In a real implementation, you would fetch from storage here
        // For now, we'll just return null to demonstrate the caching pattern
        return null;
    }

    public string BeginTransaction()
    {
        return _transactionManager.BeginTransaction();
    }

    public bool CommitTransaction(string transactionId)
    {
        return _transactionManager.CommitTransaction(transactionId);
    }

    public bool RollbackTransaction(string transactionId)
    {
        return _transactionManager.RollbackTransaction(transactionId);
    }

    public void SaveDocument(Document document, string? transactionId = null)
    {
        // If part of a transaction, add the operation to the transaction
        if (!string.IsNullOrEmpty(transactionId))
        {
            var operation = new TransactionOperation
            {
                OperationType = "INSERT",
                Document = document
            };
            _transactionManager.AddOperation(transactionId, operation);
        }

        // In a real implementation, you would save to storage here
        // Then cache the document
        _cacheManager.Set(document.Id, document);
    }

    public void UpdateDocument(Document document, string? transactionId = null)
    {
        // If part of a transaction, add the operation to the transaction
        if (!string.IsNullOrEmpty(transactionId))
        {
            var operation = new TransactionOperation
            {
                OperationType = "UPDATE",
                Document = document
            };
            _transactionManager.AddOperation(transactionId, operation);
        }

        // In a real implementation, you would update in storage here
        // Then update the cache
        _cacheManager.Set(document.Id, document);
    }

    public void DeleteDocument(string id, string? transactionId = null)
    {
        // If part of a transaction, add the operation to the transaction
        if (!string.IsNullOrEmpty(transactionId))
        {
            var operation = new TransactionOperation
            {
                OperationType = "DELETE",
                DocumentId = id
            };
            _transactionManager.AddOperation(transactionId, operation);
        }

        // In a real implementation, you would delete from storage here
        // Then remove from cache
        _cacheManager.Remove(id);
    }

    public void InvalidateDocument(string id)
    {
        _cacheManager.Remove(id);
    }
}
