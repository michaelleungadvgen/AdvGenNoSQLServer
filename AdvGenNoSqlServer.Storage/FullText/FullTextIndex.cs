// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AdvGenNoSqlServer.Storage.FullText;

/// <summary>
/// Inverted index implementation with TF-IDF scoring
/// Thread-safe for concurrent read/write operations
/// </summary>
public class FullTextIndex : IFullTextIndex
{
    // Inverted index: term -> list of (documentId, term frequency, positions)
    private readonly ConcurrentDictionary<string, List<Posting>> _invertedIndex;
    
    // Document info: documentId -> (token count, original text)
    private readonly ConcurrentDictionary<string, DocumentInfo> _documents;
    
    // Thread-safe locking for write operations
    private readonly ReaderWriterLockSlim _lock;

    // Track total tokens across all documents for O(1) average length calculation
    private long _totalTokenCount;

    /// <inheritdoc />
    public string IndexName { get; }

    /// <inheritdoc />
    public string CollectionName { get; }

    /// <inheritdoc />
    public string FieldName { get; }

    /// <inheritdoc />
    public ITextAnalyzer Analyzer { get; }

    /// <inheritdoc />
    public int DocumentCount => _documents.Count;

    /// <inheritdoc />
    public int TermCount => _invertedIndex.Count;

    /// <summary>
    /// Creates a new FullTextIndex
    /// </summary>
    public FullTextIndex(string collectionName, string fieldName, ITextAnalyzer? analyzer = null)
    {
        IndexName = $"{collectionName}_{fieldName}_ftidx";
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        Analyzer = analyzer ?? new StandardAnalyzer();
        
        _invertedIndex = new ConcurrentDictionary<string, List<Posting>>();
        _documents = new ConcurrentDictionary<string, DocumentInfo>();
        _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
    }

    /// <inheritdoc />
    public void IndexDocument(string documentId, string text)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        _lock.EnterWriteLock();
        try
        {
            // Remove existing document if present
            RemoveDocumentInternal(documentId);

            if (string.IsNullOrWhiteSpace(text))
                return;

            // Analyze text with positions
            var tokensWithPositions = Analyzer.AnalyzeWithPositions(text);
            
            if (tokensWithPositions.Count == 0)
                return;

            // Group tokens by value to calculate term frequencies and positions
            var tokenGroups = tokensWithPositions
                .GroupBy(t => t.Token)
                .ToDictionary(g => g.Key, g => g.Select(t => t.Position).ToList());

            // Add to inverted index
            foreach (var (token, positions) in tokenGroups)
            {
                var posting = new Posting(documentId, positions.Count, positions);
                
                var postings = _invertedIndex.GetOrAdd(token, _ => new List<Posting>());
                lock (postings)
                {
                    postings.Add(posting);
                }
            }

            // Store document info
            _documents[documentId] = new DocumentInfo(tokensWithPositions.Count, text);
            Interlocked.Add(ref _totalTokenCount, tokensWithPositions.Count);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public bool RemoveDocument(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return false;

        _lock.EnterWriteLock();
        try
        {
            return RemoveDocumentInternal(documentId);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private bool RemoveDocumentInternal(string documentId)
    {
        if (!_documents.TryRemove(documentId, out var removedDoc))
            return false;

        Interlocked.Add(ref _totalTokenCount, -removedDoc.TokenCount);

        // Remove from inverted index
        foreach (var (term, postings) in _invertedIndex)
        {
            lock (postings)
            {
                postings.RemoveAll(p => p.DocumentId == documentId);
            }
            
            // Clean up empty term entries
            if (postings.Count == 0)
            {
                _invertedIndex.TryRemove(term, out _);
            }
        }

        return true;
    }

    /// <inheritdoc />
    public FullTextSearchResult Search(string query, FullTextSearchOptions? options = null)
    {
        var opts = options ?? FullTextSearchOptions.Default;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return FullTextSearchResult.Empty(query);

            // Analyze query
            var queryTokens = Analyzer.Analyze(query);
            
            if (queryTokens.Count == 0)
                return FullTextSearchResult.Empty(query);

            // Calculate document scores
            var scores = new Dictionary<string, double>();
            var termFrequencies = new Dictionary<string, Dictionary<string, int>>();

            double avgDocLength = GetAverageDocumentLength(); // Hoisted outside of loop for O(1) calculation

            foreach (var token in queryTokens)
            {
                if (_invertedIndex.TryGetValue(token, out var postings))
                {
                    // Calculate IDF: log(N / df) where N is total docs, df is doc frequency
                    double idf = Math.Log((DocumentCount + 1.0) / (postings.Count + 1.0)) + 1.0;

                    lock (postings)
                    {
                        foreach (var posting in postings)
                        {
                            // Calculate TF-IDF score
                            // Use BM25-inspired scoring with document length normalization
                            double tf = posting.TermFrequency;
                            double docLength = _documents.TryGetValue(posting.DocumentId, out var docInfo) 
                                ? docInfo.TokenCount 
                                : 1;
                            
                            // BM25 parameters
                            const double k1 = 1.2;
                            const double b = 0.75;
                            
                            double tfNormalized = tf * (k1 + 1) / (tf + k1 * (1 - b + b * docLength / avgDocLength));
                            double score = tfNormalized * idf;

                            if (!scores.ContainsKey(posting.DocumentId))
                            {
                                scores[posting.DocumentId] = 0;
                                termFrequencies[posting.DocumentId] = new Dictionary<string, int>();
                            }

                            scores[posting.DocumentId] += score;
                            termFrequencies[posting.DocumentId][token] = posting.TermFrequency;
                        }
                    }
                }
            }

            // Apply boolean logic
            if (opts.RequireAllTerms)
            {
                // AND logic: document must contain all query terms
                var requiredTerms = new HashSet<string>(queryTokens);
                scores = scores.Where(kvp => 
                {
                    if (termFrequencies.TryGetValue(kvp.Key, out var tf))
                    {
                        var docTerms = new HashSet<string>(tf.Keys);
                        return requiredTerms.IsSubsetOf(docTerms);
                    }
                    return false;
                }).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            // Filter by minimum score and create results
            var results = scores
                .Where(kvp => kvp.Value >= opts.MinScore)
                .Select(kvp => new
                {
                    DocumentId = kvp.Key,
                    Score = kvp.Value,
                    TermFreq = termFrequencies[kvp.Key]
                })
                .OrderByDescending(x => x.Score)
                .Take(opts.MaxResults)
                .Select(x =>
                {
                    var highlights = opts.HighlightMatches 
                        ? GenerateHighlights(x.DocumentId, queryTokens, opts)
                        : new List<string>();

                    return new SearchResult(
                        x.DocumentId,
                        x.Score,
                        FieldName,
                        highlights,
                        x.TermFreq);
                })
                .ToList();

            stopwatch.Stop();

            return new FullTextSearchResult(results, scores.Count, query, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new FullTextSearchResult(query, $"Search failed: {ex.Message}");
        }
    }

    private List<string> GenerateHighlights(string documentId, IReadOnlyList<string> queryTokens, FullTextSearchOptions options)
    {
        var highlights = new List<string>();
        
        if (!_documents.TryGetValue(documentId, out var docInfo))
            return highlights;

        string text = docInfo.OriginalText;
        int contextChars = options.HighlightContextChars;

        // Find all positions of query tokens in the text
        var analyzedTokens = Analyzer.AnalyzeWithPositions(text);
        var matchingPositions = new List<int>();

        for (int i = 0; i < analyzedTokens.Count; i++)
        {
            if (queryTokens.Contains(analyzedTokens[i].Token))
            {
                matchingPositions.Add(i);
            }
        }

        // Generate highlights around matches
        var processedRanges = new List<(int start, int end)>();
        foreach (var pos in matchingPositions.Take(3)) // Max 3 highlights per document
        {
            int startPos = Math.Max(0, pos - 5); // 5 tokens before
            int endPos = Math.Min(analyzedTokens.Count - 1, pos + 5); // 5 tokens after

            // Find character positions
            int charStart = FindCharPosition(text, analyzedTokens, startPos);
            int charEnd = FindCharPosition(text, analyzedTokens, endPos);
            
            // Expand context
            charStart = Math.Max(0, charStart - contextChars);
            charEnd = Math.Min(text.Length, charEnd + contextChars);

            // Check for overlapping highlights
            if (processedRanges.Any(r => charStart < r.end && charEnd > r.start))
                continue;

            processedRanges.Add((charStart, charEnd));

            // Extract and highlight snippet
            string snippet = text.Substring(charStart, charEnd - charStart);
            
            // Highlight query terms
            foreach (var token in queryTokens)
            {
                // Simple case-insensitive replacement
                var regex = new Regex($@"\b{Regex.Escape(token)}\b", RegexOptions.IgnoreCase);
                snippet = regex.Replace(snippet, match => $"{options.HighlightPrefix}{match.Value}{options.HighlightSuffix}");
            }

            // Add ellipsis
            if (charStart > 0) snippet = "..." + snippet.TrimStart();
            if (charEnd < text.Length) snippet = snippet.TrimEnd() + "...";

            highlights.Add(snippet);
        }

        return highlights;
    }

    private static int FindCharPosition(string text, IReadOnlyList<TokenPosition> tokens, int tokenIndex)
    {
        // This is a simplified implementation
        // In a real implementation, we'd track character positions during tokenization
        var tokenPattern = new Regex(@"[a-zA-Z0-9]+", RegexOptions.Compiled);
        var matches = tokenPattern.Matches(text);
        
        if (tokenIndex < matches.Count)
        {
            return matches[tokenIndex].Index;
        }
        return text.Length;
    }

    private double GetAverageDocumentLength()
    {
        int count = _documents.Count;
        if (count == 0) return 1.0;
        return (double)Interlocked.Read(ref _totalTokenCount) / count;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _invertedIndex.Clear();
            _documents.Clear();
            Interlocked.Exchange(ref _totalTokenCount, 0);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public FullTextIndexStats GetStats()
    {
        long totalTokens = Interlocked.Read(ref _totalTokenCount);
        int count = _documents.Count;
        double avgLength = count == 0 ? 0 : (double)totalTokens / count;

        return new FullTextIndexStats(
            IndexName,
            CollectionName,
            FieldName,
            DocumentCount,
            TermCount,
            avgLength,
            totalTokens
        );
    }

    // Internal data structures
    private readonly record struct Posting(string DocumentId, int TermFrequency, List<int> Positions);
    
    private readonly record struct DocumentInfo(int TokenCount, string OriginalText);
}
