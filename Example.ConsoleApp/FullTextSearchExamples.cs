// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.FullText;

namespace AdvGenNoSqlServer.Example.ConsoleApp
{
    /// <summary>
    /// Full-Text Search Examples - Demonstrates text search capabilities
    /// 
    /// Features demonstrated:
    /// - Creating full-text indexes on document fields
    /// - Basic text search with TF-IDF scoring
    /// - Multi-field search across different analyzers
    /// - Advanced search options (phrase matching, highlighting, fuzzy match)
    /// </summary>
    public class FullTextSearchExamples
    {
        private readonly string _dataPath;

        public FullTextSearchExamples(string dataPath)
        {
            _dataPath = dataPath;
        }

        /// <summary>
        /// Run all full-text search examples
        /// </summary>
        public async Task RunAllExamplesAsync()
        {
            await Example1_BasicTextSearch();
            await Example2_SearchWithRelevanceScoring();
            await Example3_MultiFieldSearch();
            await Example4_AdvancedSearchOptions();
        }

        /// <summary>
        /// Example 1: Basic Text Search
        /// Demonstrates creating a full-text index and performing simple text searches
        /// </summary>
        private async Task Example1_BasicTextSearch()
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("  EXAMPLE 1: Basic Text Search");
            Console.WriteLine(new string('═', 60));

            // Create document store with full-text search enabled
            var store = new DocumentStore().WithFullTextSearch();

            // Create a full-text index on the "content" field
            Console.WriteLine("\n📋 Creating full-text index on 'articles' collection...");
            var index = store.CreateFullTextIndex("articles", "content", AnalyzerType.Standard);
            Console.WriteLine($"   ✓ Index created: {index.IndexName}");
            Console.WriteLine($"   ✓ Analyzer: {index.Analyzer.Name}");

            // Insert sample articles
            Console.WriteLine("\n📄 Inserting sample articles...");
            var articles = new[]
            {
                new Document
                {
                    Id = "article-001",
                    Data = new Dictionary<string, object>
                    {
                        ["title"] = "Introduction to NoSQL Databases",
                        ["content"] = "NoSQL databases provide flexible schemas and horizontal scalability. They are ideal for big data and real-time web applications.",
                        ["author"] = "John Smith",
                        ["tags"] = new[] { "database", "nosql", "scalability" }
                    }
                },
                new Document
                {
                    Id = "article-002",
                    Data = new Dictionary<string, object>
                    {
                        ["title"] = "Full-Text Search in Modern Applications",
                        ["content"] = "Full-text search enables users to find relevant content quickly. It uses inverted indexes and relevance scoring to rank results.",
                        ["author"] = "Jane Doe",
                        ["tags"] = new[] { "search", "full-text", "indexing" }
                    }
                },
                new Document
                {
                    Id = "article-003",
                    Data = new Dictionary<string, object>
                    {
                        ["title"] = "Database Indexing Strategies",
                        ["content"] = "Proper indexing is crucial for database performance. Indexes speed up queries but add overhead to write operations.",
                        ["author"] = "Bob Johnson",
                        ["tags"] = new[] { "database", "indexing", "performance" }
                    }
                },
                new Document
                {
                    Id = "article-004",
                    Data = new Dictionary<string, object>
                    {
                        ["title"] = "Web Application Scalability",
                        ["content"] = "Building scalable web applications requires careful architecture. Horizontal scaling and caching are key techniques.",
                        ["author"] = "Alice Brown",
                        ["tags"] = new[] { "web", "scalability", "architecture" }
                    }
                }
            };

            foreach (var article in articles)
            {
                await store.InsertAsync("articles", article);
            }
            Console.WriteLine($"   ✓ Inserted {articles.Length} articles");

            // Perform basic searches
            Console.WriteLine("\n🔍 Performing basic text searches...");

            // Search 1: Find articles about "database"
            Console.WriteLine("\n   Search 1: 'database'");
            var result1 = store.Search("articles", "database");
            Console.WriteLine($"   ✓ Found {result1.TotalMatches} matches");
            foreach (var match in result1.Results.Take(3))
            {
                var doc = await store.GetAsync("articles", match.DocumentId);
                Console.WriteLine($"     • {doc?.Data["title"]} (Score: {match.Score:F3})");
            }

            // Search 2: Find articles about "search"
            Console.WriteLine("\n   Search 2: 'search'");
            var result2 = store.Search("articles", "search");
            Console.WriteLine($"   ✓ Found {result2.TotalMatches} matches");
            foreach (var match in result2.Results.Take(3))
            {
                var doc = await store.GetAsync("articles", match.DocumentId);
                Console.WriteLine($"     • {doc?.Data["title"]} (Score: {match.Score:F3})");
            }

            // Search 3: Find articles about "scalability"
            Console.WriteLine("\n   Search 3: 'scalability'");
            var result3 = store.Search("articles", "scalability");
            Console.WriteLine($"   ✓ Found {result3.TotalMatches} matches");
            foreach (var match in result3.Results.Take(3))
            {
                var doc = await store.GetAsync("articles", match.DocumentId);
                Console.WriteLine($"     • {doc?.Data["title"]} (Score: {match.Score:F3})");
            }

            // Display index statistics
            Console.WriteLine("\n📊 Index Statistics:");
            var stats = index.GetStats();
            Console.WriteLine($"   • Documents indexed: {stats.DocumentCount}");
            Console.WriteLine($"   • Unique terms: {stats.TermCount}");
            Console.WriteLine($"   • Average document length: {stats.AverageDocumentLength:F1} tokens");

            Console.WriteLine("\n✅ Example 1 completed successfully!");
        }

        /// <summary>
        /// Example 2: Search with Relevance Scoring
        /// Demonstrates TF-IDF relevance scoring and result ranking
        /// </summary>
        private async Task Example2_SearchWithRelevanceScoring()
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("  EXAMPLE 2: Search with Relevance Scoring");
            Console.WriteLine(new string('═', 60));

            // Create document store with full-text search
            var store = new DocumentStore().WithFullTextSearch();

            // Create index
            Console.WriteLine("\n📋 Creating index on 'products' collection...");
            store.CreateFullTextIndex("products", "description", AnalyzerType.Standard);

            // Insert products with varying relevance to search terms
            Console.WriteLine("\n📄 Inserting product catalog...");
            var products = new[]
            {
                new Document
                {
                    Id = "prod-001",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Wireless Bluetooth Headphones",
                        ["description"] = "Premium wireless headphones with active noise cancellation. Bluetooth 5.0 connectivity for seamless wireless audio experience.",
                        ["category"] = "Electronics",
                        ["price"] = 199.99
                    }
                },
                new Document
                {
                    Id = "prod-002",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "USB-C Wireless Charger",
                        ["description"] = "Fast wireless charging pad with USB-C cable. Compatible with all wireless charging enabled devices.",
                        ["category"] = "Electronics",
                        ["price"] = 29.99
                    }
                },
                new Document
                {
                    Id = "prod-003",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Bluetooth Speaker",
                        ["description"] = "Portable Bluetooth speaker with 360-degree sound. Wireless streaming from any Bluetooth device.",
                        ["category"] = "Audio",
                        ["price"] = 79.99
                    }
                },
                new Document
                {
                    Id = "prod-004",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Wired Gaming Headset",
                        ["description"] = "Professional gaming headset with wired USB connection. Not wireless but offers ultra-low latency.",
                        ["category"] = "Gaming",
                        ["price"] = 149.99
                    }
                },
                new Document
                {
                    Id = "prod-005",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Wireless Mouse",
                        ["description"] = "Ergonomic wireless mouse with 2.4GHz connectivity. Not Bluetooth but reliable wireless performance.",
                        ["category"] = "Accessories",
                        ["price"] = 34.99
                    }
                }
            };

            foreach (var product in products)
            {
                await store.InsertAsync("products", product);
            }
            Console.WriteLine($"   ✓ Inserted {products.Length} products");

            // Search with relevance scoring
            Console.WriteLine("\n🔍 Searching for 'wireless bluetooth'...");
            var result = store.Search("products", "wireless bluetooth");

            Console.WriteLine($"\n   📊 Search Results (ranked by relevance):");
            Console.WriteLine($"   Total matches: {result.TotalMatches}");
            Console.WriteLine($"   Execution time: {result.ExecutionTimeMs:F2}ms\n");

            int rank = 1;
            foreach (var match in result.Results)
            {
                var product = await store.GetAsync("products", match.DocumentId);
                var relevanceBar = new string('█', (int)(match.Score * 20));
                var relevanceEmpty = new string('░', 20 - (int)(match.Score * 20));

                Console.WriteLine($"   #{rank}: {product?.Data["name"]}");
                Console.WriteLine($"        Score: [{relevanceBar}{relevanceEmpty}] {match.Score:F3}");
                Console.WriteLine($"        Category: {product?.Data["category"]}");
                if (match.TermFrequencies.Count > 0)
                {
                    var termFreq = string.Join(", ", match.TermFrequencies.Select(tf => $"{tf.Key}: {tf.Value}"));
                    Console.WriteLine($"        Term frequencies: {termFreq}");
                }
                Console.WriteLine();
                rank++;
            }

            // Demonstrate minimum score threshold
            Console.WriteLine("\n🔍 Searching with minimum score threshold (0.5)...");
            var filteredResult = store.Search("products", "wireless bluetooth", new FullTextSearchOptions
            {
                MinScore = 0.5
            });
            Console.WriteLine($"   ✓ Filtered to {filteredResult.TotalMatches} high-relevance matches");

            Console.WriteLine("\n✅ Example 2 completed successfully!");
        }

        /// <summary>
        /// Example 3: Multi-Field Search
        /// Demonstrates searching across multiple fields with different analyzers
        /// </summary>
        private async Task Example3_MultiFieldSearch()
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("  EXAMPLE 3: Multi-Field Search");
            Console.WriteLine(new string('═', 60));

            // Create document store with full-text search
            var store = new DocumentStore().WithFullTextSearch();

            // Create multiple indexes on different fields
            Console.WriteLine("\n📋 Creating multiple field indexes...");
            store.CreateFullTextIndex("documents", "title", AnalyzerType.Standard);
            store.CreateFullTextIndex("documents", "content", AnalyzerType.Standard);
            store.CreateFullTextIndex("documents", "tags", AnalyzerType.Simple);
            Console.WriteLine("   ✓ Created indexes on: title, content, tags");

            // Insert documents
            Console.WriteLine("\n📄 Inserting documents...");
            var documents = new[]
            {
                new Document
                {
                    Id = "doc-001",
                    Data = new Dictionary<string, object>
                    {
                        ["title"] = "Getting Started with Full-Text Search",
                        ["content"] = "This comprehensive guide covers the basics of implementing full-text search in your applications. Learn about indexing, querying, and relevance scoring.",
                        ["tags"] = "tutorial guide search indexing",
                        ["type"] = "Tutorial"
                    }
                },
                new Document
                {
                    Id = "doc-002",
                    Data = new Dictionary<string, object>
                    {
                        ["title"] = "Advanced Search Techniques",
                        ["content"] = "Explore advanced features like fuzzy matching, phrase queries, and multi-field search. Improve your search relevance with these techniques.",
                        ["tags"] = "advanced search techniques relevance",
                        ["type"] = "Advanced"
                    }
                },
                new Document
                {
                    Id = "doc-003",
                    Data = new Dictionary<string, object>
                    {
                        ["title"] = "Search Performance Optimization",
                        ["content"] = "Learn how to optimize your search indexes for better performance. Topics include index sizing, caching strategies, and query optimization.",
                        ["tags"] = "performance optimization indexing",
                        ["type"] = "Performance"
                    }
                },
                new Document
                {
                    Id = "doc-004",
                    Data = new Dictionary<string, object>
                    {
                        ["title"] = "Indexing Best Practices",
                        ["content"] = "Best practices for creating and maintaining search indexes. Covers analyzer selection, field mapping, and index updates.",
                        ["tags"] = "best-practices indexing maintenance",
                        ["type"] = "Guide"
                    }
                }
            };

            foreach (var doc in documents)
            {
                await store.InsertAsync("documents", doc);
            }
            Console.WriteLine($"   ✓ Inserted {documents.Length} documents");

            // Search in specific field
            Console.WriteLine("\n🔍 Search in 'title' field only...");
            var titleResult = store.Search("documents", "search", new FullTextSearchOptions
            {
                SearchField = "title"
            });
            Console.WriteLine($"   ✓ Found {titleResult.TotalMatches} matches in titles:");
            foreach (var match in titleResult.Results)
            {
                var doc = await store.GetAsync("documents", match.DocumentId);
                Console.WriteLine($"     • {doc?.Data["title"]} (Score: {match.Score:F3})");
            }

            // Search in content field
            Console.WriteLine("\n🔍 Search in 'content' field only...");
            var contentResult = store.Search("documents", "optimization", new FullTextSearchOptions
            {
                SearchField = "content"
            });
            Console.WriteLine($"   ✓ Found {contentResult.TotalMatches} matches in content:");
            foreach (var match in contentResult.Results)
            {
                var doc = await store.GetAsync("documents", match.DocumentId);
                Console.WriteLine($"     • {doc?.Data["title"]} (Score: {match.Score:F3})");
            }

            // Search in tags (using simple analyzer)
            Console.WriteLine("\n🔍 Search in 'tags' field (exact matching)...");
            var tagsResult = store.Search("documents", "indexing", new FullTextSearchOptions
            {
                SearchField = "tags"
            });
            Console.WriteLine($"   ✓ Found {tagsResult.TotalMatches} matches in tags:");
            foreach (var match in tagsResult.Results)
            {
                var doc = await store.GetAsync("documents", match.DocumentId);
                Console.WriteLine($"     • {doc?.Data["title"]} (Tags: {doc?.Data["tags"]})");
            }

            // Search across all fields (no SearchField specified)
            Console.WriteLine("\n🔍 Search across all fields...");
            var allFieldsResult = store.Search("documents", "search");
            Console.WriteLine($"   ✓ Found {allFieldsResult.TotalMatches} total matches:");
            foreach (var match in allFieldsResult.Results.Take(5))
            {
                var doc = await store.GetAsync("documents", match.DocumentId);
                Console.WriteLine($"     • {doc?.Data["title"]} (Score: {match.Score:F3})");
            }

            Console.WriteLine("\n✅ Example 3 completed successfully!");
        }

        /// <summary>
        /// Example 4: Advanced Search Options
        /// Demonstrates phrase matching, highlighting, and fuzzy search
        /// </summary>
        private async Task Example4_AdvancedSearchOptions()
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("  EXAMPLE 4: Advanced Search Options");
            Console.WriteLine(new string('═', 60));

            // Create document store
            var store = new DocumentStore().WithFullTextSearch();

            // Create index
            Console.WriteLine("\n📋 Creating index on 'knowledge_base' collection...");
            store.CreateFullTextIndex("knowledge_base", "article", AnalyzerType.Standard);

            // Insert knowledge base articles
            Console.WriteLine("\n📄 Inserting knowledge base articles...");
            var articles = new[]
            {
                new Document
                {
                    Id = "kb-001",
                    Data = new Dictionary<string, object>
                    {
                        ["title"] = "Getting Started Guide",
                        ["article"] = "Welcome to our platform! This getting started guide will help you understand the basics. Follow these steps to get started quickly and efficiently.",
                        ["category"] = "Getting Started"
                    }
                },
                new Document
                {
                    Id = "kb-002",
                    Data = new Dictionary<string, object>
                    {
                        ["title"] = "Troubleshooting Common Issues",
                        ["article"] = "If you encounter issues while getting started, check this troubleshooting guide. Common problems include connection errors and configuration mistakes.",
                        ["category"] = "Support"
                    }
                },
                new Document
                {
                    Id = "kb-003",
                    Data = new Dictionary<string, object>
                    {
                        ["title"] = "Advanced Configuration",
                        ["article"] = "Once you have the basics working, explore advanced configuration options. Custom settings allow fine-tuning for your specific requirements.",
                        ["category"] = "Configuration"
                    }
                },
                new Document
                {
                    Id = "kb-004",
                    Data = new Dictionary<string, object>
                    {
                        ["title"] = "Performance Tuning Tips",
                        ["article"] = "Optimize your deployment with these performance tuning tips. Learn about caching, indexing, and resource management for maximum efficiency.",
                        ["category"] = "Performance"
                    }
                }
            };

            foreach (var article in articles)
            {
                await store.InsertAsync("knowledge_base", article);
            }
            Console.WriteLine($"   ✓ Inserted {articles.Length} articles");

            // 1. Phrase matching with highlighting
            Console.WriteLine("\n🔍 1. Phrase Matching with Highlighting");
            Console.WriteLine("   Query: 'getting started' (phrase)");
            var phraseOptions = FullTextSearchOptions.ForPhraseMatch();
            var phraseResult = store.Search("knowledge_base", "getting started", phraseOptions);

            Console.WriteLine($"   ✓ Found {phraseResult.TotalMatches} matches:\n");
            foreach (var match in phraseResult.Results)
            {
                var doc = await store.GetAsync("knowledge_base", match.DocumentId);
                Console.WriteLine($"   📄 {doc?.Data["title"]}");
                Console.WriteLine($"      Score: {match.Score:F3}");

                if (match.Highlights.Count > 0)
                {
                    Console.WriteLine($"      Highlights:");
                    foreach (var highlight in match.Highlights.Take(2))
                    {
                        // Replace HTML tags with console-friendly markers
                        var formatted = highlight
                            .Replace("<em>", "[")
                            .Replace("</em>", "]");
                        Console.WriteLine($"        ...{formatted}...");
                    }
                }
                Console.WriteLine();
            }

            // 2. Custom highlighting options
            Console.WriteLine("\n🔍 2. Custom Highlight Formatting");
            Console.WriteLine("   Query: 'configuration'");
            var customHighlightOptions = new FullTextSearchOptions
            {
                HighlightMatches = true,
                HighlightPrefix = ">>>",
                HighlightSuffix = "<<<"
            };
            var customResult = store.Search("knowledge_base", "configuration", customHighlightOptions);

            foreach (var match in customResult.Results.Take(2))
            {
                var doc = await store.GetAsync("knowledge_base", match.DocumentId);
                if (match.Highlights.Count > 0)
                {
                    var formatted = match.Highlights[0]
                        .Replace(">>>", "[")
                        .Replace("<<<", "]");
                    Console.WriteLine($"   • {doc?.Data["title"]}");
                    Console.WriteLine($"     {formatted}");
                }
            }

            // 3. Limiting results
            Console.WriteLine("\n🔍 3. Limiting Results");
            Console.WriteLine("   Query: 'the' (common word)");
            var limitedOptions = new FullTextSearchOptions
            {
                MaxResults = 2
            };
            var limitedResult = store.Search("knowledge_base", "the", limitedOptions);
            Console.WriteLine($"   ✓ Total matches: {limitedResult.TotalMatches}");
            Console.WriteLine($"   ✓ Returned (limited): {limitedResult.Results.Count}");

            // 4. Require all terms (AND logic)
            Console.WriteLine("\n🔍 4. Require All Terms (AND logic)");
            Console.WriteLine("   Query: 'advanced configuration options'");
            var andOptions = new FullTextSearchOptions
            {
                RequireAllTerms = true
            };
            var andResult = store.Search("knowledge_base", "advanced configuration options", andOptions);
            Console.WriteLine($"   ✓ Found {andResult.TotalMatches} matches with ALL terms");
            foreach (var match in andResult.Results)
            {
                var doc = await store.GetAsync("knowledge_base", match.DocumentId);
                Console.WriteLine($"     • {doc?.Data["title"]} (Score: {match.Score:F3})");
            }

            // 5. OR logic (default)
            Console.WriteLine("\n🔍 5. OR Logic (default)");
            Console.WriteLine("   Query: 'advanced configuration options'");
            var orOptions = new FullTextSearchOptions
            {
                RequireAllTerms = false
            };
            var orResult = store.Search("knowledge_base", "advanced configuration options", orOptions);
            Console.WriteLine($"   ✓ Found {orResult.TotalMatches} matches with ANY term");
            foreach (var match in orResult.Results.Take(4))
            {
                var doc = await store.GetAsync("knowledge_base", match.DocumentId);
                Console.WriteLine($"     • {doc?.Data["title"]} (Score: {match.Score:F3})");
            }

            // 6. Fuzzy matching demonstration
            Console.WriteLine("\n🔍 6. Fuzzy Matching (Typo Tolerance)");
            Console.WriteLine("   Query: 'optimiztion' (misspelled)");
            var fuzzyOptions = new FullTextSearchOptions
            {
                EnableFuzzyMatch = true,
                FuzzyMaxEdits = 2
            };
            var fuzzyResult = store.Search("knowledge_base", "optimiztion", fuzzyOptions);
            Console.WriteLine($"   ✓ Found {fuzzyResult.TotalMatches} matches (fuzzy)");
            foreach (var match in fuzzyResult.Results)
            {
                var doc = await store.GetAsync("knowledge_base", match.DocumentId);
                Console.WriteLine($"     • {doc?.Data["title"]} (Score: {match.Score:F3})");
            }

            // Compare with exact match
            Console.WriteLine("\n   Query: 'optimiztion' (exact, no fuzzy)");
            var exactResult = store.Search("knowledge_base", "optimiztion");
            Console.WriteLine($"   ✓ Found {exactResult.TotalMatches} matches (exact)");

            Console.WriteLine("\n✅ Example 4 completed successfully!");
        }
    }
}
