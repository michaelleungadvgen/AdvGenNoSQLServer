// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text;
using System.Text.RegularExpressions;

namespace AdvGenNoSqlServer.Storage.FullText;

/// <summary>
/// Standard text analyzer with tokenization, stemming, and stop word removal
/// </summary>
public class StandardAnalyzer : ITextAnalyzer
{
    private readonly IStemmer _stemmer;
    private readonly HashSet<string> _stopWords;
    private static readonly Regex TokenPattern = new(@"[a-zA-Z0-9]+", RegexOptions.Compiled);

    /// <inheritdoc />
    public string Name => "Standard";

    /// <summary>
    /// Creates a new StandardAnalyzer with default English stop words
    /// </summary>
    public StandardAnalyzer(IStemmer? stemmer = null)
    {
        _stemmer = stemmer ?? new PorterStemmer();
        _stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "and", "are", "as", "at", "be", "by", "for", "from",
            "has", "he", "in", "is", "it", "its", "of", "on", "that", "the",
            "to", "was", "will", "with", "the", "this", "but", "they",
            "have", "had", "what", "said", "each", "which", "she", "do",
            "how", "their", "if", "up", "out", "many", "then", "them",
            "these", "so", "some", "her", "would", "make", "like", "into",
            "him", "has", "two", "more", "go", "no", "way", "could",
            "my", "than", "first", "been", "call", "who", "its", "now",
            "find", "long", "down", "day", "did", "get", "come", "made",
            "may", "part", "over", "new", "sound", "take", "only",
            "little", "work", "know", "place", "year", "live", "me",
            "back", "give", "most", "very", "after", "thing", "our",
            "just", "name", "good", "sentence", "man", "think", "say",
            "great", "where", "help", "through", "much", "before",
            "line", "right", "too", "means", "old", "any", "same",
            "tell", "boy", "follow", "came", "want", "show", "also",
            "around", "farm", "three", "small", "set", "put", "end",
            "does", "another", "well", "large", "must", "big", "even",
            "such", "because", "turn", "here", "why", "asked", "went",
            "men", "read", "need", "land", "different", "home", "us",
            "move", "try", "kind", "hand", "picture", "again", "change",
            "off", "play", "spell", "air", "away", "animal", "house",
            "point", "page", "letter", "mother", "answer", "found",
            "study", "still", "learn", "should", "america", "world"
        };
    }

    /// <summary>
    /// Creates a new StandardAnalyzer with custom stop words
    /// </summary>
    public StandardAnalyzer(IStemmer stemmer, IEnumerable<string> stopWords)
    {
        _stemmer = stemmer ?? throw new ArgumentNullException(nameof(stemmer));
        _stopWords = new HashSet<string>(stopWords, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var tokens = new List<string>();
        var matches = TokenPattern.Matches(text);

        foreach (Match match in matches)
        {
            string token = match.Value.ToLowerInvariant();
            if (!_stopWords.Contains(token) && token.Length > 1)
            {
                string stemmed = _stemmer.Stem(token);
                if (!string.IsNullOrEmpty(stemmed))
                    tokens.Add(stemmed);
            }
        }

        return tokens;
    }

    /// <inheritdoc />
    public IReadOnlyList<TokenPosition> AnalyzeWithPositions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<TokenPosition>();

        var tokens = new List<TokenPosition>();
        var matches = TokenPattern.Matches(text);
        int position = 0;

        foreach (Match match in matches)
        {
            string token = match.Value.ToLowerInvariant();
            if (!_stopWords.Contains(token) && token.Length > 1)
            {
                string stemmed = _stemmer.Stem(token);
                if (!string.IsNullOrEmpty(stemmed))
                    tokens.Add(new TokenPosition(stemmed, position++));
            }
        }

        return tokens;
    }
}

/// <summary>
/// Simple analyzer that only lowercases and tokenizes (no stemming or stop words)
/// </summary>
public class SimpleAnalyzer : ITextAnalyzer
{
    private static readonly Regex TokenPattern = new(@"[a-zA-Z0-9]+", RegexOptions.Compiled);

    /// <inheritdoc />
    public string Name => "Simple";

    /// <inheritdoc />
    public IReadOnlyList<string> Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var tokens = new List<string>();
        var matches = TokenPattern.Matches(text);

        foreach (Match match in matches)
        {
            string token = match.Value.ToLowerInvariant();
            if (token.Length > 0)
                tokens.Add(token);
        }

        return tokens;
    }

    /// <inheritdoc />
    public IReadOnlyList<TokenPosition> AnalyzeWithPositions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<TokenPosition>();

        var tokens = new List<TokenPosition>();
        var matches = TokenPattern.Matches(text);
        int position = 0;

        foreach (Match match in matches)
        {
            string token = match.Value.ToLowerInvariant();
            if (token.Length > 0)
                tokens.Add(new TokenPosition(token, position++));
        }

        return tokens;
    }
}

/// <summary>
/// Keyword analyzer that treats the entire text as a single token
/// Useful for exact matching of identifiers, codes, etc.
/// </summary>
public class KeywordAnalyzer : ITextAnalyzer
{
    /// <inheritdoc />
    public string Name => "Keyword";

    /// <inheritdoc />
    public IReadOnlyList<string> Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        return new[] { text.Trim() };
    }

    /// <inheritdoc />
    public IReadOnlyList<TokenPosition> AnalyzeWithPositions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<TokenPosition>();

        return new[] { new TokenPosition(text.Trim(), 0) };
    }
}

/// <summary>
/// Whitespace analyzer that splits on whitespace only
/// Preserves case and punctuation
/// </summary>
public class WhitespaceAnalyzer : ITextAnalyzer
{
    private static readonly char[] WhitespaceChars = { ' ', '\t', '\n', '\r', '\f', '\v' };

    /// <inheritdoc />
    public string Name => "Whitespace";

    /// <inheritdoc />
    public IReadOnlyList<string> Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        return text.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries)
                   .Select(t => t.Trim())
                   .Where(t => t.Length > 0)
                   .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<TokenPosition> AnalyzeWithPositions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<TokenPosition>();

        var tokens = text.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries)
                         .Select(t => t.Trim())
                         .Where(t => t.Length > 0)
                         .ToList();

        var result = new List<TokenPosition>(tokens.Count);
        for (int i = 0; i < tokens.Count; i++)
        {
            result.Add(new TokenPosition(tokens[i], i));
        }

        return result;
    }
}

/// <summary>
/// Factory for creating text analyzers
/// </summary>
public static class TextAnalyzerFactory
{
    /// <summary>
    /// Creates an analyzer of the specified type
    /// </summary>
    public static ITextAnalyzer Create(AnalyzerType type, IStemmer? stemmer = null)
    {
        return type switch
        {
            AnalyzerType.Standard => new StandardAnalyzer(stemmer),
            AnalyzerType.Simple => new SimpleAnalyzer(),
            AnalyzerType.Keyword => new KeywordAnalyzer(),
            AnalyzerType.Whitespace => new WhitespaceAnalyzer(),
            _ => new StandardAnalyzer(stemmer)
        };
    }
}
