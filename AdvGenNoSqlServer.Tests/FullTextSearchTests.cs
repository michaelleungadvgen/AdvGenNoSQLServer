// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.FullText;

namespace AdvGenNoSqlServer.Tests;

public class FullTextSearchTests
{
    #region Porter Stemmer Tests

    [Fact]
    public void PorterStemmer_Stem_RemovesPlurals()
    {
        var stemmer = new PorterStemmer();

        Assert.Equal("caress", stemmer.Stem("caresses"));
        Assert.Equal("poni", stemmer.Stem("ponies"));
        Assert.Equal("cat", stemmer.Stem("cats"));
    }

    [Fact]
    public void PorterStemmer_Stem_HandlesPastTense()
    {
        var stemmer = new PorterStemmer();

        Assert.Equal("agreement", stemmer.Stem("agreement"));
        Assert.Equal("troubl", stemmer.Stem("troubled"));
    }

    [Fact]
    public void PorterStemmer_Stem_HandlesIng()
    {
        var stemmer = new PorterStemmer();

        Assert.Equal("run", stemmer.Stem("running"));
        Assert.Equal("eat", stemmer.Stem("eating"));
    }

    [Fact]
    public void PorterStemmer_Stem_HandlesY()
    {
        var stemmer = new PorterStemmer();

        Assert.Equal("happi", stemmer.Stem("happy"));
        Assert.Equal("sky", stemmer.Stem("sky"));
    }

    [Fact]
    public void PorterStemmer_Stem_EmptyString_ReturnsEmpty()
    {
        var stemmer = new PorterStemmer();

        Assert.Equal("", stemmer.Stem(""));
        Assert.Null(stemmer.Stem(null));
    }

    [Fact]
    public void PorterStemmer_Stem_ShortWords_ReturnsUnchanged()
    {
        var stemmer = new PorterStemmer();

        Assert.Equal("a", stemmer.Stem("a"));
        Assert.Equal("ab", stemmer.Stem("ab"));
    }

    [Fact]
    public void PorterStemmer_Name_ReturnsPorter()
    {
        var stemmer = new PorterStemmer();
        Assert.Equal("Porter", stemmer.Name);
    }

    #endregion

    #region Identity Stemmer Tests

    [Fact]
    public void IdentityStemmer_Stem_ReturnsLowercase()
    {
        var stemmer = new IdentityStemmer();

        Assert.Equal("hello", stemmer.Stem("HELLO"));
        Assert.Equal("world", stemmer.Stem("World"));
    }

    [Fact]
    public void IdentityStemmer_Stem_Null_ReturnsEmpty()
    {
        var stemmer = new IdentityStemmer();
        Assert.Equal("", stemmer.Stem(null));
    }

    #endregion

    #region Standard Analyzer Tests

    [Fact]
    public void StandardAnalyzer_Analyze_TokenizesAndStems()
    {
        var analyzer = new StandardAnalyzer();

        var tokens = analyzer.Analyze("The quick brown foxes are running");

        Assert.Contains("quick", tokens);
        Assert.Contains("brown", tokens);
        Assert.Contains("fox", tokens);  // stemmed from foxes
        Assert.Contains("run", tokens);  // stemmed from running
        Assert.DoesNotContain("the", tokens);  // stop word removed
        Assert.DoesNotContain("are", tokens);  // stop word removed
    }

    [Fact]
    public void StandardAnalyzer_Analyze_EmptyText_ReturnsEmpty()
    {
        var analyzer = new StandardAnalyzer();

        Assert.Empty(analyzer.Analyze(""));
        Assert.Empty(analyzer.Analyze("   "));
        Assert.Empty(analyzer.Analyze(null));
    }

    [Fact]
    public void StandardAnalyzer_AnalyzeWithPositions_ReturnsCorrectPositions()
    {
        var analyzer = new StandardAnalyzer();

        var tokens = analyzer.AnalyzeWithPositions("quick brown fox");

        Assert.Equal(3, tokens.Count);
        Assert.Equal("quick", tokens[0].Token);
        Assert.Equal(0, tokens[0].Position);
        Assert.Equal("brown", tokens[1].Token);
        Assert.Equal(1, tokens[1].Position);
        Assert.Equal("fox", tokens[2].Token);
        Assert.Equal(2, tokens[2].Position);
    }

    [Fact]
    public void StandardAnalyzer_Name_ReturnsStandard()
    {
        var analyzer = new StandardAnalyzer();
        Assert.Equal("Standard", analyzer.Name);
    }

    [Fact]
    public void StandardAnalyzer_CustomStopWords_RespectsCustomStopWords()
    {
        var stemmer = new PorterStemmer();
        var customStopWords = new[] { "custom", "stopword" };
        var analyzer = new StandardAnalyzer(stemmer, customStopWords);

        var tokens = analyzer.Analyze("custom test stopword here");

        Assert.Contains("test", tokens);
        Assert.Contains("here", tokens);
        Assert.DoesNotContain("custom", tokens);
        Assert.DoesNotContain("stopword", tokens);
    }

    #endregion

    #region Simple Analyzer Tests

    [Fact]
    public void SimpleAnalyzer_Analyze_TokenizesWithoutStemming()
    {
        var analyzer = new SimpleAnalyzer();

        var tokens = analyzer.Analyze("The quick brown foxes");

        Assert.Contains("the", tokens);
        Assert.Contains("quick", tokens);
        Assert.Contains("brown", tokens);
        Assert.Contains("foxes", tokens);  // not stemmed
    }

    [Fact]
    public void SimpleAnalyzer_Name_ReturnsSimple()
    {
        var analyzer = new SimpleAnalyzer();
        Assert.Equal("Simple", analyzer.Name);
    }

    #endregion

    #region Keyword Analyzer Tests

    [Fact]
    public void KeywordAnalyzer_Analyze_ReturnsSingleToken()
    {
        var analyzer = new KeywordAnalyzer();

        var tokens = analyzer.Analyze("The quick brown fox");

        Assert.Single(tokens);
        Assert.Equal("The quick brown fox", tokens[0]);
    }

    [Fact]
    public void KeywordAnalyzer_Analyze_TrimsWhitespace()
    {
        var analyzer = new KeywordAnalyzer();

        var tokens = analyzer.Analyze("  hello world  ");

        Assert.Single(tokens);
        Assert.Equal("hello world", tokens[0]);
    }

    #endregion

    #region Whitespace Analyzer Tests

    [Fact]
    public void WhitespaceAnalyzer_Analyze_SplitsOnWhitespaceOnly()
    {
        var analyzer = new WhitespaceAnalyzer();

        var tokens = analyzer.Analyze("The quick, brown fox!");

        Assert.Equal(4, tokens.Count);
        Assert.Equal("The", tokens[0]);
        Assert.Equal("quick,", tokens[1]);  // punctuation preserved
        Assert.Equal("brown", tokens[2]);
        Assert.Equal("fox!", tokens[3]);
    }

    #endregion

    #region Text Analyzer Factory Tests

    [Theory]
    [InlineData(AnalyzerType.Standard, typeof(StandardAnalyzer))]
    [InlineData(AnalyzerType.Simple, typeof(SimpleAnalyzer))]
    [InlineData(AnalyzerType.Keyword, typeof(KeywordAnalyzer))]
    [InlineData(AnalyzerType.Whitespace, typeof(WhitespaceAnalyzer))]
    public void TextAnalyzerFactory_Create_ReturnsCorrectAnalyzer(AnalyzerType type, Type expectedType)
    {
        var analyzer = TextAnalyzerFactory.Create(type);
        Assert.IsType(expectedType, analyzer);
    }

    #endregion

    #region Full Text Index Tests

    [Fact]
    public void FullTextIndex_Constructor_SetsProperties()
    {
        var index = new FullTextIndex("posts", "content");

        Assert.Equal("posts_content_ftidx", index.IndexName);
        Assert.Equal("posts", index.CollectionName);
        Assert.Equal("content", index.FieldName);
        Assert.Equal(0, index.DocumentCount);
        Assert.Equal(0, index.TermCount);
    }

    [Fact]
    public void FullTextIndex_IndexDocument_AddsDocument()
    {
        var index = new FullTextIndex("posts", "content");

        index.IndexDocument("doc1", "The quick brown fox");

        Assert.Equal(1, index.DocumentCount);
        Assert.True(index.TermCount > 0);
    }

    [Fact]
    public void FullTextIndex_IndexDocument_UpdatesExistingDocument()
    {
        var index = new FullTextIndex("posts", "content");

        index.IndexDocument("doc1", "The quick brown fox");
        index.IndexDocument("doc1", "The lazy dog");

        Assert.Equal(1, index.DocumentCount);
        var result = index.Search("lazy");
        Assert.Single(result.Results);
    }

    [Fact]
    public void FullTextIndex_RemoveDocument_RemovesDocument()
    {
        var index = new FullTextIndex("posts", "content");
        index.IndexDocument("doc1", "The quick brown fox");

        var removed = index.RemoveDocument("doc1");

        Assert.True(removed);
        Assert.Equal(0, index.DocumentCount);
    }

    [Fact]
    public void FullTextIndex_RemoveDocument_NonExistent_ReturnsFalse()
    {
        var index = new FullTextIndex("posts", "content");

        var removed = index.RemoveDocument("nonexistent");

        Assert.False(removed);
    }

    [Fact]
    public void FullTextIndex_Search_FindsMatchingDocuments()
    {
        var index = new FullTextIndex("posts", "content");
        index.IndexDocument("doc1", "The quick brown fox jumps over the lazy dog");
        index.IndexDocument("doc2", "The lazy dog sleeps all day");

        var result = index.Search("lazy dog");

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalMatches);
        Assert.True(result.Results.All(r => r.Score > 0));
    }

    [Fact]
    public void FullTextIndex_Search_WithMinScore_FiltersResults()
    {
        var index = new FullTextIndex("posts", "content");
        index.IndexDocument("doc1", "The quick brown fox jumps over the lazy dog");
        index.IndexDocument("doc2", "Something completely different");

        var result = index.Search("fox", new FullTextSearchOptions { MinScore = 0.5 });

        Assert.Single(result.Results);
        Assert.Equal("doc1", result.Results[0].DocumentId);
    }

    [Fact]
    public void FullTextIndex_Search_WithMaxResults_LimitsResults()
    {
        var index = new FullTextIndex("posts", "content");
        index.IndexDocument("doc1", "The quick brown fox");
        index.IndexDocument("doc2", "The quick brown dog");
        index.IndexDocument("doc3", "The quick brown cat");
        index.IndexDocument("doc4", "The quick brown bird");

        var result = index.Search("quick", new FullTextSearchOptions { MaxResults = 2 });

        Assert.True(result.Results.Count <= 2);
    }

    [Fact]
    public void FullTextIndex_Search_RequireAllTerms_UsesANDLogic()
    {
        var index = new FullTextIndex("posts", "content");
        index.IndexDocument("doc1", "The quick brown fox");
        index.IndexDocument("doc2", "The quick red fox");
        index.IndexDocument("doc3", "The slow brown fox");

        var result = index.Search("quick brown", new FullTextSearchOptions { RequireAllTerms = true });

        Assert.Single(result.Results);
        Assert.Equal("doc1", result.Results[0].DocumentId);
    }

    [Fact]
    public void FullTextIndex_Search_NoMatches_ReturnsEmpty()
    {
        var index = new FullTextIndex("posts", "content");
        index.IndexDocument("doc1", "The quick brown fox");

        var result = index.Search("elephant zebra");

        Assert.True(result.Success);
        Assert.Empty(result.Results);
        Assert.Equal(0, result.TotalMatches);
    }

    [Fact]
    public void FullTextIndex_Search_EmptyQuery_ReturnsEmpty()
    {
        var index = new FullTextIndex("posts", "content");
        index.IndexDocument("doc1", "The quick brown fox");

        var result = index.Search("");

        Assert.Empty(result.Results);
    }

    [Fact]
    public void FullTextIndex_Clear_RemovesAllDocuments()
    {
        var index = new FullTextIndex("posts", "content");
        index.IndexDocument("doc1", "The quick brown fox");
        index.IndexDocument("doc2", "The lazy dog");

        index.Clear();

        Assert.Equal(0, index.DocumentCount);
        Assert.Equal(0, index.TermCount);
    }

    [Fact]
    public void FullTextIndex_GetStats_ReturnsCorrectStats()
    {
        var index = new FullTextIndex("posts", "content");
        index.IndexDocument("doc1", "The quick brown fox");
        index.IndexDocument("doc2", "The lazy dog");

        var stats = index.GetStats();

        Assert.Equal("posts_content_ftidx", stats.IndexName);
        Assert.Equal("posts", stats.CollectionName);
        Assert.Equal("content", stats.FieldName);
        Assert.Equal(2, stats.DocumentCount);
        Assert.True(stats.TermCount > 0);
        Assert.True(stats.TotalTokens > 0);
        Assert.True(stats.AverageDocumentLength > 0);
    }

    [Fact]
    public void FullTextIndex_IndexDocument_NullDocumentId_ThrowsArgumentException()
    {
        var index = new FullTextIndex("posts", "content");

        Assert.Throws<ArgumentException>(() => index.IndexDocument("", "content"));
    }

    #endregion

    #region Full Text Index Manager Tests

    [Fact]
    public void FullTextIndexManager_CreateIndex_CreatesIndex()
    {
        var manager = new FullTextIndexManager();

        var index = manager.CreateIndex("posts", "content");

        Assert.NotNull(index);
        Assert.Equal("posts", index.CollectionName);
        Assert.Equal("content", index.FieldName);
        Assert.True(manager.HasIndex("posts", "content"));
    }

    [Fact]
    public void FullTextIndexManager_CreateIndex_Duplicate_ThrowsException()
    {
        var manager = new FullTextIndexManager();
        manager.CreateIndex("posts", "content");

        Assert.Throws<InvalidOperationException>(() => manager.CreateIndex("posts", "content"));
    }

    [Fact]
    public void FullTextIndexManager_CreateIndex_WithCustomAnalyzer_UsesAnalyzer()
    {
        var manager = new FullTextIndexManager();
        var analyzer = new SimpleAnalyzer();

        var index = manager.CreateIndex("posts", "content", analyzer);

        Assert.Equal(analyzer, index.Analyzer);
    }

    [Fact]
    public void FullTextIndexManager_GetIndex_Existing_ReturnsIndex()
    {
        var manager = new FullTextIndexManager();
        manager.CreateIndex("posts", "content");

        var index = manager.GetIndex("posts", "content");

        Assert.NotNull(index);
    }

    [Fact]
    public void FullTextIndexManager_GetIndex_NonExistent_ReturnsNull()
    {
        var manager = new FullTextIndexManager();

        var index = manager.GetIndex("posts", "content");

        Assert.Null(index);
    }

    [Fact]
    public void FullTextIndexManager_DropIndex_Existing_RemovesIndex()
    {
        var manager = new FullTextIndexManager();
        manager.CreateIndex("posts", "content");

        var removed = manager.DropIndex("posts", "content");

        Assert.True(removed);
        Assert.False(manager.HasIndex("posts", "content"));
    }

    [Fact]
    public void FullTextIndexManager_DropIndex_NonExistent_ReturnsFalse()
    {
        var manager = new FullTextIndexManager();

        var removed = manager.DropIndex("posts", "content");

        Assert.False(removed);
    }

    [Fact]
    public void FullTextIndexManager_IndexDocument_IndexesInAllIndexes()
    {
        var manager = new FullTextIndexManager();
        manager.CreateIndex("posts", "title");
        manager.CreateIndex("posts", "content");

        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object>
            {
                ["title"] = "Hello World",
                ["content"] = "This is a test post"
            }
        };

        manager.IndexDocument("posts", document);

        var titleResult = manager.Search("posts", "hello");
        var contentResult = manager.Search("posts", "test");

        Assert.True(titleResult.TotalMatches > 0);
        Assert.True(contentResult.TotalMatches > 0);
    }

    [Fact]
    public void FullTextIndexManager_RemoveDocument_RemovesFromAllIndexes()
    {
        var manager = new FullTextIndexManager();
        manager.CreateIndex("posts", "content");
        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { ["content"] = "Test content" }
        };
        manager.IndexDocument("posts", document);

        manager.RemoveDocument("posts", "doc1");

        var result = manager.Search("posts", "test");
        Assert.Empty(result.Results);
    }

    [Fact]
    public void FullTextIndexManager_Search_ReturnsCombinedResults()
    {
        var manager = new FullTextIndexManager();
        manager.CreateIndex("posts", "title");
        manager.CreateIndex("posts", "content");

        var doc1 = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object>
            {
                ["title"] = "CSharp Programming",
                ["content"] = "Learn CSharp today"
            }
        };
        var doc2 = new Document
        {
            Id = "doc2",
            Data = new Dictionary<string, object>
            {
                ["title"] = "Java Programming",
                ["content"] = "Learn Java today"
            }
        };

        manager.IndexDocument("posts", doc1);
        manager.IndexDocument("posts", doc2);

        var result = manager.Search("posts", "programming");

        Assert.True(result.TotalMatches >= 2);
    }

    [Fact]
    public void FullTextIndexManager_Search_SpecificField_ReturnsFieldResults()
    {
        var manager = new FullTextIndexManager();
        manager.CreateIndex("posts", "title");
        manager.CreateIndex("posts", "content");

        var doc = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object>
            {
                ["title"] = "CSharp Programming",
                ["content"] = "Learn Java today"
            }
        };
        manager.IndexDocument("posts", doc);

        var csharpResult = manager.Search("posts", "csharp", new FullTextSearchOptions { SearchField = "title" });
        var javaResult = manager.Search("posts", "java", new FullTextSearchOptions { SearchField = "content" });

        Assert.True(csharpResult.TotalMatches > 0);
        Assert.True(javaResult.TotalMatches > 0);
    }

    [Fact]
    public void FullTextIndexManager_GetIndexedFields_ReturnsFieldNames()
    {
        var manager = new FullTextIndexManager();
        manager.CreateIndex("posts", "title");
        manager.CreateIndex("posts", "content");

        var fields = manager.GetIndexedFields("posts");

        Assert.Equal(2, fields.Count);
        Assert.Contains("title", fields);
        Assert.Contains("content", fields);
    }

    [Fact]
    public void FullTextIndexManager_ClearAllIndexes_RemovesAllIndexes()
    {
        var manager = new FullTextIndexManager();
        manager.CreateIndex("posts", "title");
        manager.CreateIndex("users", "name");

        manager.ClearAllIndexes();

        Assert.Empty(manager.GetIndexedFields("posts"));
        Assert.Empty(manager.GetIndexedFields("users"));
    }

    #endregion

    #region Full Text Document Store Tests

    [Fact]
    public async Task FullTextDocumentStore_InsertAsync_IndexesDocument()
    {
        var innerStore = new DocumentStore();
        var ftStore = new FullTextDocumentStore(innerStore);
        ftStore.CreateFullTextIndex("posts", "content");

        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { ["content"] = "Test content here" }
        };

        await ftStore.InsertAsync("posts", document);

        var result = ftStore.Search("posts", "test");
        Assert.True(result.TotalMatches > 0);
    }

    [Fact]
    public async Task FullTextDocumentStore_DeleteAsync_RemovesFromIndex()
    {
        var innerStore = new DocumentStore();
        var ftStore = new FullTextDocumentStore(innerStore);
        ftStore.CreateFullTextIndex("posts", "content");

        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { ["content"] = "Test content" }
        };
        await ftStore.InsertAsync("posts", document);

        await ftStore.DeleteAsync("posts", "doc1");

        var result = ftStore.Search("posts", "test");
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task FullTextDocumentStore_UpdateAsync_UpdatesIndex()
    {
        var innerStore = new DocumentStore();
        var ftStore = new FullTextDocumentStore(innerStore);
        ftStore.CreateFullTextIndex("posts", "content");

        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { ["content"] = "Original content" }
        };
        await ftStore.InsertAsync("posts", document);

        document.Data["content"] = "Updated content";
        await ftStore.UpdateAsync("posts", document);

        var originalResult = ftStore.Search("posts", "original");
        var updatedResult = ftStore.Search("posts", "updated");

        Assert.Empty(originalResult.Results);
        Assert.True(updatedResult.TotalMatches > 0);
    }

    [Fact]
    public async Task FullTextDocumentStore_SearchDocumentsAsync_ReturnsDocuments()
    {
        var innerStore = new DocumentStore();
        var ftStore = new FullTextDocumentStore(innerStore);
        ftStore.CreateFullTextIndex("posts", "content");

        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { ["content"] = "Test content here" }
        };
        await ftStore.InsertAsync("posts", document);

        var results = await ftStore.SearchDocumentsAsync("posts", "test");

        Assert.Single(results);
        Assert.Equal("doc1", results.First().Document.Id);
        Assert.True(results.First().Score > 0);
    }

    [Fact]
    public async Task FullTextDocumentStore_DropCollectionAsync_RemovesIndexes()
    {
        var innerStore = new DocumentStore();
        var ftStore = new FullTextDocumentStore(innerStore);
        ftStore.CreateFullTextIndex("posts", "content");

        await ftStore.CreateCollectionAsync("posts");
        await ftStore.DropCollectionAsync("posts");

        Assert.Empty(ftStore.IndexManager.GetIndexedFields("posts"));
    }

    #endregion

    #region Search Options Tests

    [Fact]
    public void FullTextSearchOptions_Default_ReturnsDefaultValues()
    {
        var options = FullTextSearchOptions.Default;

        Assert.Equal(100, options.MaxResults);
        Assert.Equal(0.0, options.MinScore);
        Assert.True(options.HighlightMatches);
        Assert.Equal("<em>", options.HighlightPrefix);
        Assert.Equal("</em>", options.HighlightSuffix);
        Assert.False(options.RequireAllTerms);
    }

    [Fact]
    public void FullTextSearchOptions_ForPhraseMatch_ReturnsPhraseOptions()
    {
        var options = FullTextSearchOptions.ForPhraseMatch();

        Assert.True(options.RequireAllTerms);
        Assert.Equal(3.0, options.PhraseMatchBoost);
        Assert.True(options.HighlightMatches);
    }

    #endregion

    #region Search Result Tests

    [Fact]
    public void SearchResult_Constructor_SetsProperties()
    {
        var termFreqs = new Dictionary<string, int> { ["test"] = 2 };
        var highlights = new List<string> { "<em>test</em> content" };

        var result = new SearchResult("doc1", 1.5, "content", highlights, termFreqs);

        Assert.Equal("doc1", result.DocumentId);
        Assert.Equal(1.5, result.Score);
        Assert.Equal("content", result.FieldName);
        Assert.Equal(highlights, result.Highlights);
        Assert.Equal(termFreqs, result.TermFrequencies);
    }

    [Fact]
    public void FullTextSearchResult_Success_Constructor_SetsProperties()
    {
        var results = new List<SearchResult> { new("doc1", 1.0, "content") };

        var result = new FullTextSearchResult(results, 1, "test query", 10.5);

        Assert.True(result.Success);
        Assert.Equal(results, result.Results);
        Assert.Equal(1, result.TotalMatches);
        Assert.Equal("test query", result.Query);
        Assert.Equal(10.5, result.ExecutionTimeMs);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void FullTextSearchResult_Failure_Constructor_SetsProperties()
    {
        var result = new FullTextSearchResult("test query", "Something went wrong");

        Assert.False(result.Success);
        Assert.Empty(result.Results);
        Assert.Equal(0, result.TotalMatches);
        Assert.Equal("test query", result.Query);
        Assert.Equal("Something went wrong", result.ErrorMessage);
    }

    [Fact]
    public void FullTextSearchResult_Empty_ReturnsEmptyResult()
    {
        var result = FullTextSearchResult.Empty("test query");

        Assert.True(result.Success);
        Assert.Empty(result.Results);
        Assert.Equal(0, result.TotalMatches);
        Assert.Equal("test query", result.Query);
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public void WithFullTextSearch_WrapsStore()
    {
        var store = new DocumentStore();

        var ftStore = store.WithFullTextSearch();

        Assert.IsType<FullTextDocumentStore>(ftStore);
        Assert.NotNull(((FullTextDocumentStore)ftStore).IndexManager);
    }

    #endregion
}
