// 検索モードに応じて Lucene.NET Query を構築する。
using FullTextSearch.Core.Search;
using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Lucene.Net.Search;

namespace FullTextSearch.Infrastructure.Lucene;

/// <summary>
/// 検索入力と <see cref="SearchMode"/> から Lucene.NET の Query を構築する。
/// </summary>
public static class SearchQueryParser
{
    private const float DefaultFilenameBoost = 2.5f;
    private const int DefaultMaxQueryTerms = 64;
    private const int DefaultMaxQueryClauses = 256;

    /// <summary>正規化済みクエリから Lucene Query を構築する。</summary>
    public static Query BuildQuery(
        string query,
        Analyzer analyzer,
        SearchMode mode,
        int maxQueryTerms = DefaultMaxQueryTerms,
        int maxQueryClauses = DefaultMaxQueryClauses,
        float filenameBoost = DefaultFilenameBoost)
    {
        _ = analyzer;
        var normalized = NormalizeQueryString(query);
        if (string.IsNullOrWhiteSpace(normalized))
            return new MatchAllDocsQuery();

        return mode switch
        {
            SearchMode.Phrase => new MatchAllDocsQuery(),
            SearchMode.Any => BuildOrQuery(normalized, maxQueryTerms, maxQueryClauses, filenameBoost),
            _ => BuildAndQuery(normalized, maxQueryTerms, maxQueryClauses, filenameBoost),
        };
    }

    /// <summary>モードに応じて検索語リストを返す（テスト用）。</summary>
    public static IReadOnlyList<string> SplitTerms(string query, SearchMode mode, int maxQueryTerms = DefaultMaxQueryTerms)
    {
        var normalized = NormalizeQueryString(query);
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<string>();

        return mode switch
        {
            SearchMode.Phrase => [normalized],
            _ => SearchQueryTerms.GetTerms(normalized).Take(maxQueryTerms).ToArray(),
        };
    }

    /// <summary>プレビューハイライト・行マッチ用の検索語。</summary>
    public static IReadOnlyList<string> GetHighlightTokens(string query, SearchMode mode)
    {
        var normalized = NormalizeQueryString(query);
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<string>();

        return mode switch
        {
            SearchMode.Phrase => [normalized],
            _ => SearchQueryTerms.GetTerms(normalized),
        };
    }

    /// <summary>検索クエリ文字列を正規化（前後空白・全角スペースの統一など）。</summary>
    public static string NormalizeQueryString(string? input) =>
        SearchQueryTerms.NormalizeQuery(input);

    private static Query BuildAndQuery(
        string normalized,
        int maxQueryTerms,
        int maxQueryClauses,
        float filenameBoost)
    {
        var parts = SearchQueryTerms.GetTerms(normalized)
            .Take(maxQueryTerms)
            .ToList();
        return CombineLiteralPartQueries(parts, Occur.MUST, maxQueryClauses, filenameBoost);
    }

    private static Query BuildOrQuery(
        string normalized,
        int maxQueryTerms,
        int maxQueryClauses,
        float filenameBoost)
    {
        var parts = SearchQueryTerms.GetTerms(normalized)
            .Take(maxQueryTerms)
            .ToList();
        var query = CombineLiteralPartQueries(parts, Occur.SHOULD, maxQueryClauses, filenameBoost);
        if (query is BooleanQuery boolQuery && boolQuery.Clauses.Count > 1)
            boolQuery.MinimumNumberShouldMatch = 1;
        return query;
    }

    private static Query CombineLiteralPartQueries(
        IReadOnlyList<string> parts,
        Occur occur,
        int maxQueryClauses,
        float filenameBoost)
    {
        if (parts.Count == 0)
            return new MatchAllDocsQuery();

        var queryList = new List<Query>(Math.Min(parts.Count, maxQueryClauses));
        foreach (var part in parts)
        {
            if (queryList.Count >= maxQueryClauses)
                break;

            var termQuery = BuildLiteralPartQuery(part, filenameBoost);
            if (termQuery != null)
                queryList.Add(termQuery);
        }

        if (queryList.Count == 0)
            return new MatchAllDocsQuery();
        if (queryList.Count == 1)
            return queryList[0];

        var boolQuery = new BooleanQuery();
        foreach (var q in queryList)
            boolQuery.Add(q, occur);
        return boolQuery;
    }

    private static Query? BuildLiteralPartQuery(string part, float filenameBoost)
    {
        var raw = part.Trim().ToLowerInvariant();
        if (raw.Length == 0)
            return null;

        var pattern = $"*{raw}*";
        var contentQuery = new WildcardQuery(new Term(LuceneIndexService.FieldContent, pattern));
        var filenameQuery = CreateFilenameWildcardQuery(pattern, filenameBoost);
        return new BooleanQuery
        {
            { contentQuery, Occur.SHOULD },
            { filenameQuery, Occur.SHOULD },
        };
    }

    private static Query CreateFilenameWildcardQuery(string pattern, float filenameBoost)
    {
        var lc = new WildcardQuery(new Term(LuceneIndexService.FieldFileNameLc, pattern));
        lc.Boost = filenameBoost;
        var legacy = new WildcardQuery(new Term(LuceneIndexService.FieldFileName, pattern));
        legacy.Boost = filenameBoost;
        return new BooleanQuery
        {
            { lc, Occur.SHOULD },
            { legacy, Occur.SHOULD },
        };
    }
}
