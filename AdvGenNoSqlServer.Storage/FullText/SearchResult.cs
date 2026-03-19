// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Storage.FullText;

/// <summary>
/// Represents a single search result from a full-text search
/// </summary>
public class SearchResult
{
    /// <summary>
    /// The document ID
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    /// The relevance score (higher is better)
    /// </summary>
    public double Score { get; }

    /// <summary>
    /// The field that matched
    /// </summary>
    public string FieldName { get; }

    /// <summary>
    /// Highlighted snippets from the matching text
    /// </summary>
    public IReadOnlyList<string> Highlights { get; }

    /// <summary>
    /// Term frequencies for matched terms
    /// </summary>
    public IReadOnlyDictionary<string, int> TermFrequencies { get; }

    /// <summary>
    /// Creates a new SearchResult
    /// </summary>
    public SearchResult(
        string documentId,
        double score,
        string fieldName,
        IReadOnlyList<string>? highlights = null,
        IReadOnlyDictionary<string, int>? termFrequencies = null)
    {
        DocumentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
        Score = score;
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        Highlights = highlights ?? Array.Empty<string>();
        TermFrequencies = termFrequencies ?? new Dictionary<string, int>();
    }
}

/// <summary>
/// Options for configuring full-text search behavior
/// </summary>
public class FullTextSearchOptions
{
    /// <summary>
    /// Maximum number of results to return (default: 100)
    /// </summary>
    public int MaxResults { get; set; } = 100;

    /// <summary>
    /// Minimum score threshold for results (default: 0.0)
    /// </summary>
    public double MinScore { get; set; } = 0.0;

    /// <summary>
    /// Whether to highlight matching terms in results (default: true)
    /// </summary>
    public bool HighlightMatches { get; set; } = true;

    /// <summary>
    /// The highlight prefix marker (default: "&lt;em&gt;")
    /// </summary>
    public string HighlightPrefix { get; set; } = "<em>";

    /// <summary>
    /// The highlight suffix marker (default: "&lt;/em&gt;")
    /// </summary>
    public string HighlightSuffix { get; set; } = "</em>";

    /// <summary>
    /// Number of characters around each highlight (default: 50)
    /// </summary>
    public int HighlightContextChars { get; set; } = 50;

    /// <summary>
    /// Whether to enable fuzzy matching for misspelled words (default: false)
    /// </summary>
    public bool EnableFuzzyMatch { get; set; } = false;

    /// <summary>
    /// Maximum edit distance for fuzzy matching (default: 1)
    /// </summary>
    public int FuzzyMaxEdits { get; set; } = 1;

    /// <summary>
    /// Whether to use AND logic (all terms must match) instead of OR (default: false = OR)
    /// </summary>
    public bool RequireAllTerms { get; set; } = false;

    /// <summary>
    /// Boost factor for exact phrase matches (default: 2.0)
    /// </summary>
    public double PhraseMatchBoost { get; set; } = 2.0;

    /// <summary>
    /// Boost factor for field name matches (default: 1.5)
    /// </summary>
    public double FieldNameMatchBoost { get; set; } = 1.5;

    /// <summary>
    /// Specific field to search, or null to search all fields (default: null)
    /// </summary>
    public string? SearchField { get; set; }

    /// <summary>
    /// Creates default search options
    /// </summary>
    public static FullTextSearchOptions Default => new();

    /// <summary>
    /// Creates search options for phrase matching
    /// </summary>
    public static FullTextSearchOptions ForPhraseMatch()
    {
        return new FullTextSearchOptions
        {
            RequireAllTerms = true,
            PhraseMatchBoost = 3.0,
            HighlightMatches = true
        };
    }
}

/// <summary>
/// Represents the result of a full-text search operation
/// </summary>
public class FullTextSearchResult
{
    /// <summary>
    /// The search results
    /// </summary>
    public IReadOnlyList<SearchResult> Results { get; }

    /// <summary>
    /// Total number of matches found
    /// </summary>
    public int TotalMatches { get; }

    /// <summary>
    /// The search query that was executed
    /// </summary>
    public string Query { get; }

    /// <summary>
    /// Time taken to execute the search in milliseconds
    /// </summary>
    public double ExecutionTimeMs { get; }

    /// <summary>
    /// Whether the search was successful
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Error message if search failed
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a successful search result
    /// </summary>
    public FullTextSearchResult(
        IReadOnlyList<SearchResult> results,
        int totalMatches,
        string query,
        double executionTimeMs)
    {
        Results = results ?? throw new ArgumentNullException(nameof(results));
        TotalMatches = totalMatches;
        Query = query ?? throw new ArgumentNullException(nameof(query));
        ExecutionTimeMs = executionTimeMs;
        Success = true;
    }

    /// <summary>
    /// Creates a failed search result
    /// </summary>
    public FullTextSearchResult(string query, string errorMessage)
    {
        Results = Array.Empty<SearchResult>();
        TotalMatches = 0;
        Query = query ?? throw new ArgumentNullException(nameof(query));
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
        ExecutionTimeMs = 0;
        Success = false;
    }

    /// <summary>
    /// Creates an empty successful result
    /// </summary>
    public static FullTextSearchResult Empty(string query)
    {
        return new FullTextSearchResult(Array.Empty<SearchResult>(), 0, query, 0);
    }
}
