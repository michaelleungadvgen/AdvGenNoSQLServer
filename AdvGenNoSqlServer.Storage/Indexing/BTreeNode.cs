// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Storage.Indexing;

/// <summary>
/// Represents a node in the B-tree data structure
/// Internal nodes store keys and child references
/// Leaf nodes store keys and values
/// </summary>
/// <typeparam name="TKey">The type of keys</typeparam>
/// <typeparam name="TValue">The type of values</typeparam>
internal class BTreeNode<TKey, TValue> where TKey : IComparable<TKey>
{
    /// <summary>
    /// Gets the keys stored in this node (always sorted)
    /// </summary>
    public List<TKey> Keys { get; }

    /// <summary>
    /// Gets the values stored in this node (for leaf nodes)
    /// For non-unique indexes, each key can have multiple values
    /// </summary>
    public List<List<TValue>> Values { get; }

    /// <summary>
    /// Gets the child node references (for internal nodes)
    /// </summary>
    public List<BTreeNode<TKey, TValue>> Children { get; }

    /// <summary>
    /// Gets whether this node is a leaf node
    /// </summary>
    public bool IsLeaf { get; }

    /// <summary>
    /// Gets the next leaf node (for efficient range scans, only valid for leaf nodes)
    /// </summary>
    public BTreeNode<TKey, TValue>? NextLeaf { get; set; }

    /// <summary>
    /// Gets the parent node (null for root)
    /// </summary>
    public BTreeNode<TKey, TValue>? Parent { get; set; }

    /// <summary>
    /// Gets the number of keys in this node
    /// </summary>
    public int KeyCount => Keys.Count;

    /// <summary>
    /// Gets whether this node is full (has 2t-1 keys where t is minimum degree)
    /// </summary>
    public bool IsFull => Keys.Count >= (2 * _minDegree - 1);

    /// <summary>
    /// Gets whether this node has the minimum number of keys (t-1)
    /// </summary>
    public bool IsMinSize => Keys.Count <= (_minDegree - 1);

    private readonly int _minDegree;

    /// <summary>
    /// Creates a new B-tree node
    /// </summary>
    /// <param name="minDegree">The minimum degree of the B-tree (t)</param>
    /// <param name="isLeaf">Whether this is a leaf node</param>
    public BTreeNode(int minDegree, bool isLeaf)
    {
        _minDegree = minDegree;
        IsLeaf = isLeaf;
        Keys = new List<TKey>(2 * minDegree - 1);
        Values = new List<List<TValue>>(2 * minDegree - 1);
        Children = isLeaf ? new List<BTreeNode<TKey, TValue>>() : new List<BTreeNode<TKey, TValue>>(2 * minDegree);
    }

    /// <summary>
    /// Searches for a key in this node and its children
    /// </summary>
    /// <param name="key">The key to search for</param>
    /// <param name="values">The values associated with the key, if found</param>
    /// <returns>True if found, false otherwise</returns>
    public bool Search(TKey key, out List<TValue>? values)
    {
        int i = 0;
        while (i < Keys.Count && key.CompareTo(Keys[i]) > 0)
        {
            i++;
        }

        if (i < Keys.Count && key.CompareTo(Keys[i]) == 0)
        {
            values = Values[i];
            return true;
        }

        if (IsLeaf)
        {
            values = null;
            return false;
        }

        return Children[i].Search(key, out values);
    }

    /// <summary>
    /// Finds the index where a key should be inserted or found
    /// </summary>
    /// <param name="key">The key to find</param>
    /// <returns>The index of the key if found, or the insertion point if not found (negative value)</returns>
    public int FindKeyIndex(TKey key)
    {
        int index = Keys.BinarySearch(key);
        return index;
    }

    /// <summary>
    /// Gets the child node that should contain the given key
    /// </summary>
    /// <param name="key">The key to find child for</param>
    /// <returns>The child node index</returns>
    public int GetChildIndex(TKey key)
    {
        int i = 0;
        while (i < Keys.Count && key.CompareTo(Keys[i]) > 0)
        {
            i++;
        }
        return i;
    }

    /// <summary>
    /// Inserts a key-value pair into this leaf node
    /// Assumes this node is not full
    /// </summary>
    /// <param name="key">The key to insert</param>
    /// <param name="value">The value to insert</param>
    /// <param name="isUnique">Whether the index is unique</param>
    /// <returns>True if inserted, false if duplicate in unique index</returns>
    public bool InsertIntoLeaf(TKey key, TValue value, bool isUnique)
    {
        int index = FindKeyIndex(key);

        if (index >= 0)
        {
            // Key exists
            if (isUnique)
            {
                return false;
            }
            // Add value to existing key
            Values[index].Add(value);
            return true;
        }

        // Insert new key
        int insertIndex = ~index;
        Keys.Insert(insertIndex, key);
        Values.Insert(insertIndex, new List<TValue> { value });
        return true;
    }

    /// <summary>
    /// Splits this full child node
    /// </summary>
    /// <param name="childIndex">Index of the child to split</param>
    /// <param name="child">The child node to split</param>
    public void SplitChild(int childIndex, BTreeNode<TKey, TValue> child)
    {
        var newNode = new BTreeNode<TKey, TValue>(_minDegree, child.IsLeaf);
        newNode.Parent = this;

        int midIndex = _minDegree - 1;

        // Copy second half of keys and values to new node
        for (int i = 0; i < midIndex; i++)
        {
            newNode.Keys.Add(child.Keys[midIndex + 1 + i]);
            newNode.Values.Add(child.Values[midIndex + 1 + i]);
        }

        // Remove copied keys and values from child
        child.Keys.RemoveRange(midIndex + 1, midIndex);
        child.Values.RemoveRange(midIndex + 1, midIndex);

        if (!child.IsLeaf)
        {
            // Copy second half of children
            for (int i = 0; i < _minDegree; i++)
            {
                newNode.Children.Add(child.Children[midIndex + 1 + i]);
                child.Children[midIndex + 1 + i].Parent = newNode;
            }
            child.Children.RemoveRange(midIndex + 1, _minDegree);
        }
        else
        {
            // Link leaf nodes
            newNode.NextLeaf = child.NextLeaf;
            child.NextLeaf = newNode;
        }

        // Move middle key up to this node
        TKey middleKey = child.Keys[midIndex];
        List<TValue> middleValues = child.Values[midIndex];
        child.Keys.RemoveAt(midIndex);
        child.Values.RemoveAt(midIndex);

        Keys.Insert(childIndex, middleKey);
        Values.Insert(childIndex, middleValues);
        Children.Insert(childIndex + 1, newNode);
    }

    /// <summary>
    /// Merges this node with its right sibling
    /// </summary>
    /// <param name="parent">The parent node</param>
    /// <param name="rightSibling">The right sibling to merge with</param>
    /// <param name="separatorIndex">Index of the separator key in parent</param>
    public void MergeWithRightSibling(BTreeNode<TKey, TValue> parent, BTreeNode<TKey, TValue> rightSibling, int separatorIndex)
    {
        // Add separator key from parent
        Keys.Add(parent.Keys[separatorIndex]);
        Values.Add(parent.Values[separatorIndex]);

        // Add all keys and values from right sibling
        Keys.AddRange(rightSibling.Keys);
        Values.AddRange(rightSibling.Values);

        if (!IsLeaf)
        {
            // Add all children from right sibling
            Children.AddRange(rightSibling.Children);
            foreach (var child in rightSibling.Children)
            {
                child.Parent = this;
            }
        }
        else
        {
            // Update leaf link
            NextLeaf = rightSibling.NextLeaf;
        }

        // Remove separator from parent and the right sibling reference
        parent.Keys.RemoveAt(separatorIndex);
        parent.Values.RemoveAt(separatorIndex);
        parent.Children.RemoveAt(separatorIndex + 1);
    }

    /// <summary>
    /// Borrows a key from the left sibling
    /// </summary>
    /// <param name="parent">The parent node</param>
    /// <param name="leftSibling">The left sibling</param>
    /// <param name="separatorIndex">Index of the separator key in parent</param>
    public void BorrowFromLeft(BTreeNode<TKey, TValue> parent, BTreeNode<TKey, TValue> leftSibling, int separatorIndex)
    {
        // Move separator from parent to this node's beginning
        Keys.Insert(0, parent.Keys[separatorIndex]);
        Values.Insert(0, parent.Values[separatorIndex]);

        // Move last key from left sibling to parent
        int lastIndex = leftSibling.Keys.Count - 1;
        parent.Keys[separatorIndex] = leftSibling.Keys[lastIndex];
        parent.Values[separatorIndex] = leftSibling.Values[lastIndex];

        if (!IsLeaf)
        {
            // Move last child from left sibling to this node's beginning
            var lastChild = leftSibling.Children[lastIndex + 1];
            Children.Insert(0, lastChild);
            lastChild.Parent = this;
            leftSibling.Children.RemoveAt(lastIndex + 1);
        }

        leftSibling.Keys.RemoveAt(lastIndex);
        leftSibling.Values.RemoveAt(lastIndex);
    }

    /// <summary>
    /// Borrows a key from the right sibling
    /// </summary>
    /// <param name="parent">The parent node</param>
    /// <param name="rightSibling">The right sibling</param>
    /// <param name="separatorIndex">Index of the separator key in parent</param>
    public void BorrowFromRight(BTreeNode<TKey, TValue> parent, BTreeNode<TKey, TValue> rightSibling, int separatorIndex)
    {
        // Move separator from parent to this node's end
        Keys.Add(parent.Keys[separatorIndex]);
        Values.Add(parent.Values[separatorIndex]);

        // Move first key from right sibling to parent
        parent.Keys[separatorIndex] = rightSibling.Keys[0];
        parent.Values[separatorIndex] = rightSibling.Values[0];

        if (!IsLeaf)
        {
            // Move first child from right sibling to this node's end
            var firstChild = rightSibling.Children[0];
            Children.Add(firstChild);
            firstChild.Parent = this;
            rightSibling.Children.RemoveAt(0);
        }

        rightSibling.Keys.RemoveAt(0);
        rightSibling.Values.RemoveAt(0);
    }

    /// <summary>
    /// Gets the left sibling of this node
    /// </summary>
    /// <param name="parent">The parent node</param>
    /// <returns>The left sibling, or null if this is the first child</returns>
    public BTreeNode<TKey, TValue>? GetLeftSibling(BTreeNode<TKey, TValue> parent)
    {
        int index = parent.Children.IndexOf(this);
        if (index > 0)
        {
            return parent.Children[index - 1];
        }
        return null;
    }

    /// <summary>
    /// Gets the right sibling of this node
    /// </summary>
    /// <param name="parent">The parent node</param>
    /// <returns>The right sibling, or null if this is the last child</returns>
    public BTreeNode<TKey, TValue>? GetRightSibling(BTreeNode<TKey, TValue> parent)
    {
        int index = parent.Children.IndexOf(this);
        if (index < parent.Children.Count - 1)
        {
            return parent.Children[index + 1];
        }
        return null;
    }

    /// <summary>
    /// Gets the index of this node in its parent's children list
    /// </summary>
    /// <param name="parent">The parent node</param>
    /// <returns>The index, or -1 if not found</returns>
    public int GetIndexInParent(BTreeNode<TKey, TValue> parent)
    {
        return parent.Children.IndexOf(this);
    }
}
