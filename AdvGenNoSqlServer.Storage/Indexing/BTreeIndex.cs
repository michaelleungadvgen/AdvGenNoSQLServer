// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
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
            // Tree-wide duplicate check under the same lock as the insert, so
            // check+insert is atomic against concurrent writers. Search covers
            // separator keys on the descent path and the target leaf, which
            // together account for every key stored anywhere in the tree.
            if (IsUnique && _root != null && _root.Search(key, out _))
            {
                throw new DuplicateKeyException($"Duplicate key '{key}' in unique index '{Name}'");
            }

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
            // A key can be promoted into an internal node during a split. If this key already
            // lives here, append to its existing value list rather than descending (which would
            // create a duplicate key entry in a leaf and fragment the key's values).
            int existing = node.FindKeyIndex(key);
            if (existing >= 0)
            {
                if (IsUnique)
                {
                    throw new DuplicateKeyException($"Duplicate key '{key}' in unique index '{Name}'");
                }
                node.Values[existing].Add(value);
                Count++;
                return true;
            }

            int childIndex = node.GetChildIndex(key);
            var child = node.Children[childIndex];

            if (child.IsFull)
            {
                node.SplitChild(childIndex, child);
                // After split, the median key of `child` moved up into `node` at childIndex.
                // If our key equals that promoted key, append here; otherwise pick the side.
                int cmp = key.CompareTo(node.Keys[childIndex]);
                if (cmp == 0)
                {
                    if (IsUnique)
                    {
                        throw new DuplicateKeyException($"Duplicate key '{key}' in unique index '{Name}'");
                    }
                    node.Values[childIndex].Add(value);
                    Count++;
                    return true;
                }
                if (cmp > 0)
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

            bool deleted = DeleteInternal(_root, key, default!, false);
            ShrinkRoot();
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

            bool deleted = DeleteInternal(_root, key, value, true);
            ShrinkRoot();
            return deleted;
        }
    }

    private void ShrinkRoot()
    {
        while (_root != null && _root.KeyCount == 0)
        {
            if (_root.IsLeaf || _root.Children.Count == 0)
            {
                _root = null;
            }
            else
            {
                _root = _root.Children[0];
                _root.Parent = null;
            }
        }
    }

    /// <summary>
    /// Internal delete operation. All Count adjustments happen inside this call tree
    /// so a promoted key (separator copy + leaf copy) is accounted exactly once.
    /// </summary>
    private bool DeleteInternal(BTreeNode<TKey, TValue> node, TKey key, TValue? specificValue, bool hasSpecificValue)
    {
        int index = node.FindKeyIndex(key);

        if (index >= 0)
        {
            if (node.IsLeaf)
            {
                var values = node.Values[index];

                if (hasSpecificValue)
                {
                    if (!values.Remove(specificValue!)) return false;
                    Count--;
                    if (values.Count == 0)
                    {
                        node.Keys.RemoveAt(index);
                        node.Values.RemoveAt(index);
                    }
                    return true;
                }

                Count -= values.Count;
                node.Keys.RemoveAt(index);
                node.Values.RemoveAt(index);
                return true;
            }

            return DeleteFromInternalNode(node, index, key, specificValue, hasSpecificValue);
        }

        if (node.IsLeaf) return false;

        int childIndex = ~index;
        if (childIndex >= node.Children.Count) return false;

        bool deleted = DeleteInternal(node.Children[childIndex], key, specificValue, hasSpecificValue);
        if (deleted) Rebalance(node, childIndex);
        return deleted;
    }

    /// <summary>
    /// Deletes a key found in an internal node. A promoted key lives both in this
    /// separator and as a leaf entry in the right subtree; both copies must go.
    /// </summary>
    private bool DeleteFromInternalNode(BTreeNode<TKey, TValue> node, int index, TKey key, TValue? specificValue, bool hasSpecificValue)
    {
        var separatorValues = node.Values[index];

        if (hasSpecificValue)
        {
            if (separatorValues.Remove(specificValue!))
            {
                Count--;
                if (separatorValues.Count > 0) return true;
                // Separator exhausted but the leaf copy may still hold values:
                // retire the separator entry without deleting the key itself.
                ReplaceSeparator(node, index);
                return true;
            }

            bool removed = DeleteInternal(node.Children[index + 1], key, specificValue, true);
            if (removed) Rebalance(node, index + 1);
            return removed;
        }

        // Remove the leaf copy first, repairing only inside the right subtree so
        // no rebalance merges across the separator we are about to retire.
        var rightSubtree = node.Children[index + 1];
        int leafValueCount = RemoveLeafCopy(rightSubtree, key, separatorValues, out var leaf);
        RepairPath(leaf, rightSubtree);
        Count -= separatorValues.Count + leafValueCount;

        var left = node.Children[index];
        var right = node.Children[index + 1];

        if (left.KeyCount >= _minDegree)
        {
            var (predKey, predValues) = GetPredecessor(left);
            node.Keys[index] = predKey;
            node.Values[index] = predValues;
            DeleteInternal(left, predKey, default!, false);
            Count += predValues.Count;
            Rebalance(node, index);
            Rebalance(node, index + 1);
        }
        else if (right.KeyCount >= _minDegree)
        {
            var (succKey, succValues) = GetSuccessor(right);
            node.Keys[index] = succKey;
            node.Values[index] = succValues;
            DeleteInternal(right, succKey, default!, false);
            Count += succValues.Count;
            Rebalance(node, index + 1);
        }
        else
        {
            node.Values[index] = new List<TValue>();
            left.MergeWithRightSibling(node, right, index);
            DeleteInternal(left, key, default!, false);
            Rebalance(node, index);
        }
        return true;
    }

    /// <summary>
    /// Retires a separator whose value list is exhausted, keeping the key's leaf copy.
    /// </summary>
    private void ReplaceSeparator(BTreeNode<TKey, TValue> node, int index)
    {
        var left = node.Children[index];
        var right = node.Children[index + 1];

        if (left.KeyCount >= _minDegree)
        {
            var (predKey, predValues) = GetPredecessor(left);
            node.Keys[index] = predKey;
            node.Values[index] = predValues;
            DeleteInternal(left, predKey, default!, false);
            Count += predValues.Count;
            Rebalance(node, index);
        }
        else if (right.KeyCount >= _minDegree)
        {
            var (succKey, succValues) = GetSuccessor(right);
            node.Keys[index] = succKey;
            node.Values[index] = succValues;
            DeleteInternal(right, succKey, default!, false);
            Count += succValues.Count;
            Rebalance(node, index + 1);
        }
        else
        {
            left.MergeWithRightSibling(node, right, index);
            Rebalance(node, index);
        }
    }

    /// <summary>
    /// Removes the leaf copy of a promoted key without rebalancing; returns the
    /// number of values removed, excluding any already counted via the separator
    /// (split promotion shares one value list between separator and leaf).
    /// </summary>
    private int RemoveLeafCopy(BTreeNode<TKey, TValue> subtree, TKey key, List<TValue> separatorValues, out BTreeNode<TKey, TValue> leaf)
    {
        var node = subtree;
        while (!node.IsLeaf)
        {
            int ci = node.GetChildIndex(key);
            if (ci >= node.Children.Count)
            {
                leaf = node;
                return 0;
            }
            node = node.Children[ci];
        }

        leaf = node;
        int i = node.FindKeyIndex(key);
        if (i < 0) return 0;

        var list = node.Values[i];
        int count = ReferenceEquals(list, separatorValues) ? 0 : list.Count;
        node.Keys.RemoveAt(i);
        node.Values.RemoveAt(i);
        return count;
    }

    private void RepairPath(BTreeNode<TKey, TValue> leaf, BTreeNode<TKey, TValue> stop)
    {
        var cur = leaf;
        while (cur != stop && cur.Parent != null)
        {
            var parent = cur.Parent;
            int ci = parent.Children.IndexOf(cur);
            if (ci >= 0) Rebalance(parent, ci);
            cur = parent;
        }
    }

    /// <summary>
    /// Gets the predecessor (largest key in left subtree) with all of its values,
    /// including any separator copy along the descent path.
    /// </summary>
    private (TKey key, List<TValue> values) GetPredecessor(BTreeNode<TKey, TValue> node)
    {
        var ancestors = new List<BTreeNode<TKey, TValue>>();
        while (!node.IsLeaf)
        {
            ancestors.Add(node);
            node = node.Children[node.Children.Count - 1];
        }
        int lastIndex = node.Keys.Count - 1;
        var key = node.Keys[lastIndex];
        var values = new List<TValue>(node.Values[lastIndex]);
        var seen = new HashSet<List<TValue>> { node.Values[lastIndex] };
        for (int i = ancestors.Count - 1; i >= 0; i--)
        {
            int sep = ancestors[i].FindKeyIndex(key);
            if (sep >= 0 && seen.Add(ancestors[i].Values[sep]))
            {
                values.AddRange(ancestors[i].Values[sep]);
            }
        }
        return (key, values);
    }

    /// <summary>
    /// Gets the successor (smallest key in right subtree) with all of its values,
    /// including any separator copy along the descent path.
    /// </summary>
    private (TKey key, List<TValue> values) GetSuccessor(BTreeNode<TKey, TValue> node)
    {
        var ancestors = new List<BTreeNode<TKey, TValue>>();
        while (!node.IsLeaf)
        {
            ancestors.Add(node);
            node = node.Children[0];
        }
        var key = node.Keys[0];
        var values = new List<TValue>(node.Values[0]);
        var seen = new HashSet<List<TValue>> { node.Values[0] };
        for (int i = ancestors.Count - 1; i >= 0; i--)
        {
            int sep = ancestors[i].FindKeyIndex(key);
            if (sep >= 0 && seen.Add(ancestors[i].Values[sep]))
            {
                values.AddRange(ancestors[i].Values[sep]);
            }
        }
        return (key, values);
    }

    /// <summary>
    /// Rebalances a child after deletion. MergeWithRightSibling already removes the
    /// separator and the merged-away child from the parent, so no extra removal here.
    /// </summary>
    private void Rebalance(BTreeNode<TKey, TValue> parent, int childIndex)
    {
        if (childIndex < 0 || childIndex >= parent.Children.Count) return;
        var child = parent.Children[childIndex];
        if (child.KeyCount >= _minDegree - 1) return;

        if (childIndex > 0)
        {
            var leftSibling = parent.Children[childIndex - 1];
            if (leftSibling.KeyCount >= _minDegree)
            {
                child.BorrowFromLeft(parent, leftSibling, childIndex - 1);
                return;
            }
        }

        if (childIndex < parent.Children.Count - 1)
        {
            var rightSibling = parent.Children[childIndex + 1];
            if (rightSibling.KeyCount >= _minDegree)
            {
                child.BorrowFromRight(parent, rightSibling, childIndex);
                return;
            }
        }

        if (childIndex > 0)
        {
            parent.Children[childIndex - 1].MergeWithRightSibling(parent, child, childIndex - 1);
        }
        else if (childIndex < parent.Children.Count - 1)
        {
            child.MergeWithRightSibling(parent, parent.Children[childIndex + 1], childIndex);
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
            if (_root == null || !_root.Search(key, out var values) || values!.Count == 0)
            {
                value = default;
                return false;
            }

            value = values[0];
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

            return values!.ToList();
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
            var results = new List<KeyValuePair<TKey, TValue>>();
            if (_root == null) return results;

            var leaf = FindLeafNode(_root, startKey);

            while (leaf != null)
            {
                for (int i = 0; i < leaf.Keys.Count; i++)
                {
                    var key = leaf.Keys[i];
                    if (key.CompareTo(startKey) < 0) continue;
                    if (key.CompareTo(endKey) > 0) return results;

                    foreach (var value in leaf.Values[i])
                    {
                        results.Add(new KeyValuePair<TKey, TValue>(key, value));
                    }
                }
                leaf = leaf.NextLeaf;
            }
            return results;
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
            var results = new List<KeyValuePair<TKey, TValue>>();
            if (_root == null) return results;

            var leaf = FindLeafNode(_root, key);

            while (leaf != null)
            {
                for (int i = 0; i < leaf.Keys.Count; i++)
                {
                    var k = leaf.Keys[i];
                    if (k.CompareTo(key) < 0) continue;

                    foreach (var value in leaf.Values[i])
                    {
                        results.Add(new KeyValuePair<TKey, TValue>(k, value));
                    }
                }
                leaf = leaf.NextLeaf;
            }
            return results;
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
            var results = new List<KeyValuePair<TKey, TValue>>();
            var leaf = GetLeftmostLeaf(_root);

            while (leaf != null)
            {
                for (int i = 0; i < leaf.Keys.Count; i++)
                {
                    var k = leaf.Keys[i];
                    if (k.CompareTo(key) > 0) return results;

                    foreach (var value in leaf.Values[i])
                    {
                        results.Add(new KeyValuePair<TKey, TValue>(k, value));
                    }
                }
                leaf = leaf.NextLeaf;
            }
            return results;
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
            var results = new List<KeyValuePair<TKey, TValue>>();
            var leaf = GetLeftmostLeaf(_root);

            while (leaf != null)
            {
                for (int i = 0; i < leaf.Keys.Count; i++)
                {
                    foreach (var value in leaf.Values[i])
                    {
                        results.Add(new KeyValuePair<TKey, TValue>(leaf.Keys[i], value));
                    }
                }
                leaf = leaf.NextLeaf;
            }
            return results;
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
