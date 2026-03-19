// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Storage.FullText;

/// <summary>
/// Defines the contract for text analysis in full-text search
/// Responsible for tokenization, stemming, and stop word removal
/// </summary>
public interface ITextAnalyzer
{
    /// <summary>
    /// Analyzes text and returns a list of processed tokens
    /// </summary>
    /// <param name="text">The text to analyze</param>
    /// <returns>A list of processed tokens</returns>
    IReadOnlyList<string> Analyze(string text);

    /// <summary>
    /// Analyzes text with position information for phrase queries
    /// </summary>
    /// <param name="text">The text to analyze</param>
    /// <returns>A list of tokens with their positions</returns>
    IReadOnlyList<TokenPosition> AnalyzeWithPositions(string text);

    /// <summary>
    /// Gets the analyzer name
    /// </summary>
    string Name { get; }
}

/// <summary>
/// Represents a token with its position in the original text
/// </summary>
public readonly record struct TokenPosition
{
    /// <summary>
    /// The token value
    /// </summary>
    public string Token { get; }

    /// <summary>
    /// The position of the token in the text (0-based)
    /// </summary>
    public int Position { get; }

    /// <summary>
    /// Creates a new TokenPosition
    /// </summary>
    public TokenPosition(string token, int position)
    {
        Token = token ?? throw new ArgumentNullException(nameof(token));
        Position = position;
    }
}

/// <summary>
/// Types of text analyzers available
/// </summary>
public enum AnalyzerType
{
    /// <summary>
    /// Standard analyzer with stemming and stop word removal
    /// </summary>
    Standard,

    /// <summary>
    /// Simple analyzer that only lowercases and tokenizes
    /// </summary>
    Simple,

    /// <summary>
    /// Keyword analyzer that treats entire text as single token
    /// </summary>
    Keyword,

    /// <summary>
    /// Whitespace analyzer that splits on whitespace only
    /// </summary>
    Whitespace
}
