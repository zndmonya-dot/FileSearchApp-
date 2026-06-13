using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Lucene;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary>SearchQueryParser のモード別 Query 生成。</summary>
public class SearchQueryParserTests
{
    private static readonly Analyzer Analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

    [Fact]
    public void SplitTerms_keyword_mode_splits_by_space()
    {
        var terms = SearchQueryParser.SplitTerms("import sys", SearchMode.Keyword);
        Assert.Equal(2, terms.Count);
    }

    [Fact]
    public void SplitTerms_phrase_mode_keeps_whole_input()
    {
        var terms = SearchQueryParser.SplitTerms("import sys", SearchMode.Phrase);
        Assert.Single(terms);
        Assert.Equal("import sys", terms[0]);
    }

    [Fact]
    public void SplitTerms_any_mode_splits_by_space()
    {
        var terms = SearchQueryParser.SplitTerms("alpha beta", SearchMode.Any);
        Assert.Equal(2, terms.Count);
    }

    [Fact]
    public void BuildQuery_phrase_uses_match_all_docs_query()
    {
        var phraseQuery = SearchQueryParser.BuildQuery("import sys", Analyzer, SearchMode.Phrase);
        Assert.IsType<MatchAllDocsQuery>(phraseQuery);
    }

    [Fact]
    public void BuildQuery_keyword_splits_spaced_input_into_and_parts()
    {
        var query = SearchQueryParser.BuildQuery("import sys", Analyzer, SearchMode.Keyword);
        Assert.True(ContainsMultipleMustWildcardQueries(query));
        Assert.False(ContainsPhraseQuery(query, slop: 1));
    }

    [Fact]
    public void BuildQuery_keyword_hits_when_tokens_are_not_adjacent()
    {
        using var dir = new RAMDirectory();
        var config = new IndexWriterConfig(AppLuceneVersion, Analyzer);
        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new Document
            {
                new TextField(LuceneIndexService.FieldContent, "uses import module and sys call", Field.Store.YES),
            };
            writer.AddDocument(doc);
        }

        using var reader = DirectoryReader.Open(dir);
        var searcher = new IndexSearcher(reader);
        var query = SearchQueryParser.BuildQuery("import sys", Analyzer, SearchMode.Keyword);
        var hits = searcher.Search(query, 10);
        Assert.True(hits.TotalHits > 0);
    }

    [Fact]
    public void BuildQuery_keyword_misses_when_only_one_token_present()
    {
        using var dir = new RAMDirectory();
        var config = new IndexWriterConfig(AppLuceneVersion, Analyzer);
        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new Document
            {
                new TextField(LuceneIndexService.FieldContent, "uses import module only", Field.Store.YES),
            };
            writer.AddDocument(doc);
        }

        using var reader = DirectoryReader.Open(dir);
        var searcher = new IndexSearcher(reader);
        var query = SearchQueryParser.BuildQuery("import sys", Analyzer, SearchMode.Keyword);
        var hits = searcher.Search(query, 10);
        Assert.Equal(0, hits.TotalHits);
    }

    [Fact]
    public void BuildQuery_any_single_word_uses_literal_wildcard()
    {
        var query = SearchQueryParser.BuildQuery("ライセンス情報", Analyzer, SearchMode.Any);
        Assert.True(ContainsContentWildcardPattern(query, "*ライセンス情報*"));
    }

    [Fact]
    public void GetHighlightTokens_single_or_keyword_is_one_term()
    {
        var tokens = SearchQueryParser.GetHighlightTokens("ライセンス情報", SearchMode.Any);
        Assert.Single(tokens);
        Assert.Equal("ライセンス情報", tokens[0]);
    }

    [Fact]
    public void BuildQuery_any_uses_should_with_minimum_match()
    {
        var query = SearchQueryParser.BuildQuery("alpha beta", Analyzer, SearchMode.Any);
        var boolQuery = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(2, boolQuery.Clauses.Count);
        Assert.All(boolQuery.Clauses, c => Assert.Equal(Occur.SHOULD, c.Occur));
        Assert.Equal(1, boolQuery.MinimumNumberShouldMatch);
    }

    [Fact]
    public void BuildQuery_keyword_finds_import_sys_in_index()
    {
        using var dir = new RAMDirectory();
        var config = new IndexWriterConfig(AppLuceneVersion, Analyzer);
        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new Document
            {
                new TextField(LuceneIndexService.FieldContent, "line with import sys call", Field.Store.YES),
            };
            writer.AddDocument(doc);
        }

        using var reader = DirectoryReader.Open(dir);
        var searcher = new IndexSearcher(reader);
        var query = SearchQueryParser.BuildQuery("import sys", Analyzer, SearchMode.Keyword);
        var hits = searcher.Search(query, 10);
        Assert.True(hits.TotalHits > 0);
    }

    [Fact]
    public void BuildQuery_keyword_matches_filename_lc_only()
    {
        using var dir = new RAMDirectory();
        var config = new IndexWriterConfig(AppLuceneVersion, Analyzer);
        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new Document
            {
                new TextField(LuceneIndexService.FieldContent, "", Field.Store.YES),
                new StringField(LuceneIndexService.FieldFileNameLc, "annual_report_2024.docx", Field.Store.NO),
                new TextField(LuceneIndexService.FieldFileName, "annual_report_2024.docx", Field.Store.YES),
            };
            writer.AddDocument(doc);
        }

        using var reader = DirectoryReader.Open(dir);
        var searcher = new IndexSearcher(reader);
        var query = SearchQueryParser.BuildQuery("report", Analyzer, SearchMode.Keyword);
        var hits = searcher.Search(query, 10);
        Assert.True(hits.TotalHits > 0);
    }

    [Fact]
    public void NormalizeQueryString_converts_full_width_space()
    {
        var normalized = SearchQueryParser.NormalizeQueryString("a\u3000b");
        Assert.Equal("a b", normalized);
    }

    [Fact]
    public void GetHighlightTokens_single_and_keyword_is_one_term()
    {
        var tokens = SearchQueryParser.GetHighlightTokens("ライセンス情報", SearchMode.Keyword);
        Assert.Single(tokens);
        Assert.Equal("ライセンス情報", tokens[0]);
    }

    [Fact]
    public void GetHighlightTokens_spaced_and_splits_terms()
    {
        var tokens = SearchQueryParser.GetHighlightTokens("import sys", SearchMode.Keyword);
        Assert.Equal(2, tokens.Count);
        Assert.Equal("import", tokens[0]);
        Assert.Equal("sys", tokens[1]);
    }

    private static bool ContainsContentWildcardPattern(Query query, string pattern)
    {
        return query switch
        {
            WildcardQuery wq when wq.Term?.Field == LuceneIndexService.FieldContent
                => wq.Term.Text == pattern,
            BooleanQuery bq => bq.Clauses.Any(c => ContainsContentWildcardPattern(c.Query, pattern)),
            _ => false,
        };
    }

    private static bool ContainsPhraseQuery(Query query, int slop)
    {
        return query switch
        {
            PhraseQuery pq => pq.Slop == slop,
            BooleanQuery bq => bq.Clauses.Any(c => ContainsPhraseQuery(c.Query, slop)),
            _ => false,
        };
    }

    private static bool ContainsMultipleMustWildcardQueries(Query query)
    {
        return GetContentMustWildcardCount(query) >= 2;
    }

    private static int GetContentMustWildcardCount(Query query)
    {
        switch (query)
        {
            case WildcardQuery wq when wq.Term?.Field == LuceneIndexService.FieldContent:
                return 1;
            case BooleanQuery bq when bq.Clauses.Count > 0 && bq.Clauses.All(c => c.Occur == Occur.MUST):
                return bq.Clauses.Sum(c => GetContentMustWildcardCount(c.Query));
            case BooleanQuery bq:
                return bq.Clauses.Max(c => GetContentMustWildcardCount(c.Query));
            default:
                return 0;
        }
    }
}
