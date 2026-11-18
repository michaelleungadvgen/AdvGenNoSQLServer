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

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentService"/> class
    /// </summary>
    /// <param name="cacheManager">The cache manager used for document caching</param>
    /// <param name="transactionManager">The transaction manager used for transactional operations</param>
    public DocumentService(ICacheManager cacheManager, ITransactionManager transactionManager)
    {
        _cacheManager = cacheManager;
        _transactionManager = transactionManager;
    }

    /// <summary>
    /// Gets a document from the cache; returns null on a cache miss (storage fallback is not implemented in this example)
    /// </summary>
    /// <param name="id">The document identifier</param>
    /// <returns>The cached document, or null if not cached</returns>
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

    /// <summary>
    /// Begins a new transaction
    /// </summary>
    /// <returns>The identifier of the new transaction</returns>
    public string BeginTransaction()
    {
        return _transactionManager.BeginTransaction();
    }

    /// <summary>
    /// Commits an active transaction
    /// </summary>
    /// <param name="transactionId">The transaction identifier</param>
    /// <returns>True if the transaction was committed; otherwise false</returns>
    public bool CommitTransaction(string transactionId)
    {
        return _transactionManager.CommitTransaction(transactionId);
    }

    /// <summary>
    /// Rolls back an active transaction
    /// </summary>
    /// <param name="transactionId">The transaction identifier</param>
    /// <returns>True if the transaction was rolled back; otherwise false</returns>
    public bool RollbackTransaction(string transactionId)
    {
        return _transactionManager.RollbackTransaction(transactionId);
    }

    /// <summary>
    /// Saves a document to the cache, recording an INSERT operation when part of a transaction
    /// </summary>
    /// <param name="document">The document to save</param>
    /// <param name="transactionId">Optional transaction identifier to enlist the operation in</param>
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

    /// <summary>
    /// Updates a document in the cache, recording an UPDATE operation when part of a transaction
    /// </summary>
    /// <param name="document">The document with updated content</param>
    /// <param name="transactionId">Optional transaction identifier to enlist the operation in</param>
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

    /// <summary>
    /// Removes a document from the cache, recording a DELETE operation when part of a transaction
    /// </summary>
    /// <param name="id">The identifier of the document to delete</param>
    /// <param name="transactionId">Optional transaction identifier to enlist the operation in</param>
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

    /// <summary>
    /// Removes a document from the cache without touching storage or transactions
    /// </summary>
    /// <param name="id">The identifier of the document to invalidate</param>
    public void InvalidateDocument(string id)
    {
        _cacheManager.Remove(id);
    }
}
