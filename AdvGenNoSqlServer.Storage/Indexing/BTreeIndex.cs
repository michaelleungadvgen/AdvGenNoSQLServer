// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics.CodeAnalysis;

namespace AdvGenNoSqlServer.Storage.Indexing;

/// <summary>
/// B-tree index implementation with O(log n) operations
/// Supports generic key types and both unique and non-unique indexes
/// </summary>
/// <typeparam name="TKey">The type of keys (must implement IComparable{TKey})</typeparam>
/// <typeparam name="TValue">The type of values stored</typeparam>
public class BTreeIndex<TKey, TValue> : IBTreeIndex<TKey, TValue> where TKey : IComparable<TKey>
{
    private BTreeNode<TKey, TValue>? _root;
    private readonly int _minDegree;
    private readonly object _lock = new();

    /// <summary>
    /// Gets the name of the index
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the name of the collection this index belongs to
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// Gets the field/property name being indexed
    /// </summary>
    public string FieldName { get; }

    /// <summary>
    /// Gets whether this index enforces unique keys
    /// </summary>
    public bool IsUnique { get; }

    /// <summary>
    /// Gets the number of key-value pairs in the index
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets the height of the B-tree
    /// </summary>
    public int Height
    {
        get
        {
            lock (_lock)
            {
                if (_root == null) return 0;
                int height = 1;
                var node = _root;
                while (!node.IsLeaf)
                {
                    height++;
                    node = node.Children[0];
                }
                return height;
            }
        }
    }

    /// <summary>
    /// Gets the minimum degree of the B-tree
    /// </summary>
    public int MinDegree => _minDegree;

    /// <summary>
    /// Creates a new B-tree index
    /// </summary>
    /// <param name="name">The index name</param>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field being indexed</param>
    /// <param name="isUnique">Whether the index enforces unique keys</param>
    /// <param name="minDegree">The minimum degree (t) of the B-tree. Default is 4.</param>
    public BTreeIndex(string name, string collectionName, string fieldName, bool isUnique = false, int minDegree = 4)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Index name cannot be null or empty", nameof(name));
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be null or empty", nameof(collectionName));
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Field name cannot be null or empty", nameof(fieldName));
        if (minDegree < 2)
            throw new ArgumentException("Minimum degree must be at least 2", nameof(minDegree));

        Name = name;
        CollectionName = collectionName;
        FieldName = fieldName;
        IsUnique = isUnique;
        _minDegree = minDegree;
        _root = null;
        Count = 0;
    }

    /// <summary>
    /// Inserts a key-value pair into the index
    /// </summary>
    public bool Insert(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));
        ArgumentNullException.ThrowIfNull(value, nameof(value));

        lock (_lock)
        {
            if (_root == null)
            {
                _root = new BTreeNode<TKey, TValue>(_minDegree, true);
                _root.Keys.Add(key);
                _root.Values.Add(new List<TValue> { value });
                Count++;
                return true;
            }

            // If root is full, split it
            if (_root.IsFull)
            {
                var newRoot = new BTreeNode<TKey, TValue>(_minDegree, false);
                newRoot.Children.Add(_root);
                _root.Parent = newRoot;
                newRoot.SplitChild(0, _root);
                _root = newRoot;
            }

            return InsertNonFull(_root, key, value);
        }
    }


    /// <summary>
    /// Inserts into a non-full node
    /// </summary>
    private bool InsertNonFull(BTreeNode<TKey, TValue> node, TKey key, TValue value)
    {
        if (node.IsLeaf)
        {
            bool inserted = node.InsertIntoLeaf(key, value, IsUnique);
            if (inserted)
            {
                Count++;
            }
            return inserted;
        }
        else
        {
            int childIndex = node.GetChildIndex(key);
            var child = node.Children[childIndex];

            if (child.IsFull)
            {
                node.SplitChild(childIndex, child);
                // After split, determine which child to go to
                if (key.CompareTo(node.Keys[childIndex]) > 0)
                {
                    childIndex++;
                }
            }

            return InsertNonFull(node.Children[childIndex], key, value);
        }
    }

    /// <summary>
    /// Deletes a key from the index
    /// </summary>
    public bool Delete(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));

        lock (_lock)
        {
            if (_root == null) return false;

            bool deleted = DeleteInternal(_root, key, default!, false, out _);
            if (deleted)
            {
                Count--;
                // If root is empty and has children, promote first child
                if (_root.KeyCount == 0 && !_root.IsLeaf)
                {
                    _root = _root.Children[0];
                    _root.Parent = null;
                }
                else if (_root.KeyCount == 0)
                {
                    _root = null;
                }
            }
            return deleted;
        }
    }

    /// <summary>
    /// Deletes a specific key-value pair
    /// </summary>
    public bool Delete(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));
        ArgumentNullException.ThrowIfNull(value, nameof(value));

        lock (_lock)
        {
            if (_root == null) return false;

            bool deleted = DeleteInternal(_root, key, value, true, out _);
            if (deleted)
            {
                Count--;
                // Same root cleanup as above
                if (_root?.KeyCount == 0 && !_root.IsLeaf)
                {
                    _root = _root.Children[0];
                    _root.Parent = null;
                }
                else if (_root?.KeyCount == 0)
                {
                    _root = null;
                }
            }
            return deleted;
        }
    }

    /// <summary>
    /// Internal delete operation
    /// </summary>
    private bool DeleteInternal(BTreeNode<TKey, TValue> node, TKey key, TValue? specificValue, bool hasSpecificValue, out bool isEmpty)
    {
        isEmpty = false;
        int index = node.FindKeyIndex(key);

        if (index >= 0)
        {
            // Key found in this node
            if (node.IsLeaf)
            {
                var values = node.Values[index];
                
                if (hasSpecificValue)
                {
                    // Remove only the specific value
                    bool removed = values.Remove(specificValue!);
                    if (!removed) return false;
                    
                    if (values.Count == 0)
                    {
                        // Remove the entire key entry
                        node.Keys.RemoveAt(index);
                        node.Values.RemoveAt(index);
                        isEmpty = node.KeyCount == 0;
                    }
                    return true;
                }
                else
                {
                    // Remove entire key
                    int valueCount = values.Count;
                    node.Keys.RemoveAt(index);
                    node.Values.RemoveAt(index);
                    isEmpty = node.KeyCount == 0;
                    
                    // Adjust Count for all values that were under this key
                    for (int i = 0; i < valueCount - 1; i++)
                    {
                        Count--; // Extra decrements for additional values
                    }
                    return true;
                }
            }
            else
            {
                // Internal node - handle deletion
                return DeleteFromInternalNode(node, index, key, specificValue, hasSpecificValue, out isEmpty);
            }
        }

        if (node.IsLeaf)
        {
            return false;
        }

        // Key not in this node, recurse to appropriate child
        int childIndex = ~index;
        if (childIndex >= node.Children.Count)
        {
            return false;
        }

        var child = node.Children[childIndex];
        bool deleted = DeleteInternal(child, key, specificValue, hasSpecificValue, out bool childIsEmpty);

        if (deleted && child.KeyCount < _minDegree - 1 && !childIsEmpty)
        {
            Rebalance(node, childIndex);
        }

        return deleted;
    }

    /// <summary>
    /// Deletes from an internal node using predecessor/successor
    /// </summary>
    private bool DeleteFromInternalNode(BTreeNode<TKey, TValue> node, int index, TKey key, TValue? specificValue, bool hasSpecificValue, out bool isEmpty)
    {
        isEmpty = false;
        
        if (hasSpecificValue)
        {
            // For specific value deletion in internal node, we need to find the value in leaves
            var values = node.Values[index];
            bool removed = values.Remove(specificValue!);
            if (!removed) return false;
            
            if (values.Count == 0)
            {
                // Fall through to handle empty key case
            }
            else
            {
                return true;
            }
        }
        
        var leftChild = node.Children[index];
        var rightChild = node.Children[index + 1];

        if (leftChild.KeyCount >= _minDegree)
        {
            // Use predecessor
            var (predKey, predValues) = GetPredecessor(leftChild);
            node.Keys[index] = predKey;
            node.Values[index] = new List<TValue>(predValues);
            bool deleted = DeleteInternal(leftChild, predKey, default!, false, out bool leftIsEmpty);
            if (leftIsEmpty && leftChild.KeyCount == 0)
            {
                HandleEmptyChild(node, index);
            }
            else if (leftChild.KeyCount < _minDegree - 1)
            {
                Rebalance(node, index);
            }
            return deleted;
        }
        else if (rightChild.KeyCount >= _minDegree)
        {
            // Use successor
            var (succKey, succValues) = GetSuccessor(rightChild);
            node.Keys[index] = succKey;
            node.Values[index] = new List<TValue>(succValues);
            bool deleted = DeleteInternal(rightChild, succKey, default!, false, out bool rightIsEmpty);
            if (rightIsEmpty && rightChild.KeyCount == 0)
            {
                HandleEmptyChild(node, index + 1);
            }
            else if (rightChild.KeyCount < _minDegree - 1)
            {
                Rebalance(node, index + 1);
            }
            return deleted;
        }
        else
        {
            // Merge with right sibling
            leftChild.MergeWithRightSibling(node, rightChild, index);
            bool deleted = DeleteInternal(leftChild, key, specificValue, hasSpecificValue, out isEmpty);
            return deleted;
        }
    }

    /// <summary>
    /// Gets the predecessor (largest key in left subtree)
    /// </summary>
    private (TKey key, List<TValue> values) GetPredecessor(BTreeNode<TKey, TValue> node)
    {
        while (!node.IsLeaf)
        {
            node = node.Children[node.Children.Count - 1];
        }
        int lastIndex = node.Keys.Count - 1;
        return (node.Keys[lastIndex], node.Values[lastIndex]);
    }

    /// <summary>
    /// Gets the successor (smallest key in right subtree)
    /// </summary>
    private (TKey key, List<TValue> values) GetSuccessor(BTreeNode<TKey, TValue> node)
    {
        while (!node.IsLeaf)
        {
            node = node.Children[0];
        }
        return (node.Keys[0], node.Values[0]);
    }

    /// <summary>
    /// Handles an empty child node after deletion
    /// </summary>
    private void HandleEmptyChild(BTreeNode<TKey, TValue> parent, int childIndex)
    {
        if (childIndex < parent.Children.Count)
        {
            parent.Children.RemoveAt(childIndex);
        }
    }

    /// <summary>
    /// Rebalances the tree after deletion
    /// </summary>
    private void Rebalance(BTreeNode<TKey, TValue> parent, int childIndex)
    {
        var child = parent.Children[childIndex];

        // Try to borrow from left sibling
        if (childIndex > 0)
        {
            var leftSibling = parent.Children[childIndex - 1];
            if (leftSibling.KeyCount >= _minDegree)
            {
                child.BorrowFromLeft(parent, leftSibling, childIndex - 1);
                return;
            }
        }

        // Try to borrow from right sibling
        if (childIndex < parent.Children.Count - 1)
        {
            var rightSibling = parent.Children[childIndex + 1];
            if (rightSibling.KeyCount >= _minDegree)
            {
                child.BorrowFromRight(parent, rightSibling, childIndex);
                return;
            }
        }

        // Merge with sibling
        if (childIndex > 0)
        {
            // Merge with left sibling
            var leftSibling = parent.Children[childIndex - 1];
            leftSibling.MergeWithRightSibling(parent, child, childIndex - 1);
            parent.Children.RemoveAt(childIndex);
        }
        else if (childIndex < parent.Children.Count - 1)
        {
            // Merge with right sibling
            var rightSibling = parent.Children[childIndex + 1];
            child.MergeWithRightSibling(parent, rightSibling, childIndex);
            parent.Children.RemoveAt(childIndex + 1);
        }
    }

    /// <summary>
    /// Searches for a key in the index
    /// </summary>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));

        lock (_lock)
        {
            if (_root == null || !_root.Search(key, out var values))
            {
                value = default;
                return false;
            }

            value = values![0];
            return true;
        }
    }

    /// <summary>
    /// Gets all values associated with a key
    /// </summary>
    public IEnumerable<TValue> GetValues(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));

        lock (_lock)
        {
            if (_root == null || !_root.Search(key, out var values))
            {
                return Enumerable.Empty<TValue>();
            }

            return values!.AsEnumerable();
        }
    }

    /// <summary>
    /// Performs a range query
    /// </summary>
    public IEnumerable<KeyValuePair<TKey, TValue>> RangeQuery(TKey startKey, TKey endKey)
    {
        ArgumentNullException.ThrowIfNull(startKey, nameof(startKey));
        ArgumentNullException.ThrowIfNull(endKey, nameof(endKey));

        lock (_lock)
        {
            if (_root == null) yield break;

            // Find the starting leaf node
            var leaf = FindLeafNode(_root, startKey);

            while (leaf != null)
            {
                for (int i = 0; i < leaf.Keys.Count; i++)
                {
                    var key = leaf.Keys[i];
                    int cmpStart = key.CompareTo(startKey);
                    int cmpEnd = key.CompareTo(endKey);
                    
                    if (cmpStart < 0) continue;
                    if (cmpEnd > 0) yield break;

                    foreach (var value in leaf.Values[i])
                    {
                        yield return new KeyValuePair<TKey, TValue>(key, value);
                    }
                }
                leaf = leaf.NextLeaf;
            }
        }
    }

    /// <summary>
    /// Gets all keys >= specified key
    /// </summary>
    public IEnumerable<KeyValuePair<TKey, TValue>> GetGreaterThanOrEqual(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));

        lock (_lock)
        {
            if (_root == null) yield break;

            var leaf = FindLeafNode(_root, key);

            while (leaf != null)
            {
                for (int i = 0; i < leaf.Keys.Count; i++)
                {
                    var k = leaf.Keys[i];
                    if (k.CompareTo(key) < 0) continue;

                    foreach (var value in leaf.Values[i])
                    {
                        yield return new KeyValuePair<TKey, TValue>(k, value);
                    }
                }
                leaf = leaf.NextLeaf;
            }
        }
    }

    /// <summary>
    /// Gets all keys <= specified key
    /// </summary>
    public IEnumerable<KeyValuePair<TKey, TValue>> GetLessThanOrEqual(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));

        lock (_lock)
        {
            // Start from the leftmost leaf
            var leaf = GetLeftmostLeaf(_root);

            while (leaf != null)
            {
                for (int i = 0; i < leaf.Keys.Count; i++)
                {
                    var k = leaf.Keys[i];
                    if (k.CompareTo(key) > 0) yield break;

                    foreach (var value in leaf.Values[i])
                    {
                        yield return new KeyValuePair<TKey, TValue>(k, value);
                    }
                }
                leaf = leaf.NextLeaf;
            }
        }
    }

    /// <summary>
    /// Clears all entries
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _root = null;
            Count = 0;
        }
    }

    /// <summary>
    /// Checks if key exists
    /// </summary>
    public bool ContainsKey(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));

        lock (_lock)
        {
            return _root != null && _root.Search(key, out _);
        }
    }

    /// <summary>
    /// Gets all key-value pairs in sorted order
    /// </summary>
    public IEnumerable<KeyValuePair<TKey, TValue>> GetAll()
    {
        lock (_lock)
        {
            var leaf = GetLeftmostLeaf(_root);

            while (leaf != null)
            {
                for (int i = 0; i < leaf.Keys.Count; i++)
                {
                    foreach (var value in leaf.Values[i])
                    {
                        yield return new KeyValuePair<TKey, TValue>(leaf.Keys[i], value);
                    }
                }
                leaf = leaf.NextLeaf;
            }
        }
    }

    /// <summary>
    /// Updates a value for an existing key
    /// </summary>
    public bool Update(TKey key, TValue newValue)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));
        ArgumentNullException.ThrowIfNull(newValue, nameof(newValue));

        lock (_lock)
        {
            if (_root == null) return false;

            var node = _root;
            while (true)
            {
                int index = node.FindKeyIndex(key);
                if (index >= 0)
                {
                    // Found it
                    if (IsUnique)
                    {
                        node.Values[index][0] = newValue;
                    }
                    else
                    {
                        // For non-unique, we can't easily identify which value to update
                        // So we just add the new value
                        node.Values[index].Add(newValue);
                    }
                    return true;
                }

                if (node.IsLeaf) return false;

                int childIndex = ~index;
                if (childIndex >= node.Children.Count) return false;
                node = node.Children[childIndex];
            }
        }
    }

    /// <summary>
    /// Gets the minimum key
    /// </summary>
    public bool TryGetMin([MaybeNullWhen(false)] out TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_lock)
        {
            var leaf = GetLeftmostLeaf(_root);
            if (leaf == null || leaf.Keys.Count == 0)
            {
                key = default;
                value = default;
                return false;
            }

            key = leaf.Keys[0];
            value = leaf.Values[0][0];
            return true;
        }
    }

    /// <summary>
    /// Gets the maximum key
    /// </summary>
    public bool TryGetMax([MaybeNullWhen(false)] out TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_lock)
        {
            var leaf = GetRightmostLeaf(_root);
            if (leaf == null || leaf.Keys.Count == 0)
            {
                key = default;
                value = default;
                return false;
            }

            int lastIndex = leaf.Keys.Count - 1;
            key = leaf.Keys[lastIndex];
            value = leaf.Values[lastIndex][0];
            return true;
        }
    }

    /// <summary>
    /// Finds the leaf node that should contain the key
    /// </summary>
    private BTreeNode<TKey, TValue>? FindLeafNode(BTreeNode<TKey, TValue>? node, TKey key)
    {
        if (node == null) return null;

        while (!node.IsLeaf)
        {
            int childIndex = node.GetChildIndex(key);
            if (childIndex >= node.Children.Count)
            {
                childIndex = node.Children.Count - 1;
            }
            node = node.Children[childIndex];
        }

        return node;
    }

    /// <summary>
    /// Gets the leftmost leaf node
    /// </summary>
    private BTreeNode<TKey, TValue>? GetLeftmostLeaf(BTreeNode<TKey, TValue>? node)
    {
        if (node == null) return null;

        while (!node.IsLeaf)
        {
            node = node.Children[0];
        }

        return node;
    }

    /// <summary>
    /// Gets the rightmost leaf node
    /// </summary>
    private BTreeNode<TKey, TValue>? GetRightmostLeaf(BTreeNode<TKey, TValue>? node)
    {
        if (node == null) return null;

        while (!node.IsLeaf)
        {
            node = node.Children[node.Children.Count - 1];
        }

        return node;
    }
}
