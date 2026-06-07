// 検索モードに応じて Lucene.NET Query を構築する。
using FullTextSearch.Core.Search;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
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
        var normalized = NormalizeQueryString(query);
        if (string.IsNullOrWhiteSpace(normalized))
            return new MatchAllDocsQuery();

        return mode switch
        {
            // 完全一致は LuceneSearchService が MatchAllDocs + 文字列一致で処理する
            SearchMode.Phrase => new MatchAllDocsQuery(),
            SearchMode.Any => BuildOrQuery(normalized, analyzer, maxQueryTerms, maxQueryClauses, filenameBoost),
            _ => BuildAndQuery(normalized, analyzer, maxQueryTerms, maxQueryClauses, filenameBoost),
        };
    }

    /// <summary>モードに応じて検索語リストを返す（テスト用）。AND/完全一致は入力全体を1語、ORのみスペース区切り。</summary>
    public static IReadOnlyList<string> SplitTerms(string query, SearchMode mode, int maxQueryTerms = DefaultMaxQueryTerms)
    {
        var normalized = NormalizeQueryString(query);
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<string>();

        if (mode != SearchMode.Any)
            return [normalized];

        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Take(maxQueryTerms)
            .ToArray();
    }

    /// <summary>検索クエリ文字列を正規化（前後空白・全角スペースの統一など）。</summary>
    public static string NormalizeQueryString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var s = input.Trim();
        if (s.Contains('\u3000'))
            s = s.Replace('\u3000', ' ');
        return s;
    }

    private static Query BuildAndQuery(
        string normalized,
        Analyzer analyzer,
        int maxQueryTerms,
        int maxQueryClauses,
        float filenameBoost)
    {
        var terms = SplitTerms(normalized, SearchMode.Keyword, maxQueryTerms);
        return CombineTermQueries(terms, Occur.MUST, analyzer, maxQueryClauses, filenameBoost);
    }

    private static Query BuildOrQuery(
        string normalized,
        Analyzer analyzer,
        int maxQueryTerms,
        int maxQueryClauses,
        float filenameBoost)
    {
        var terms = SplitTerms(normalized, SearchMode.Any, maxQueryTerms);
        var query = CombineTermQueries(terms, Occur.SHOULD, analyzer, maxQueryClauses, filenameBoost);
        if (query is BooleanQuery boolQuery && boolQuery.Clauses.Count > 1)
            boolQuery.MinimumNumberShouldMatch = 1;
        return query;
    }

    private static Query CombineTermQueries(
        IReadOnlyList<string> terms,
        Occur occur,
        Analyzer analyzer,
        int maxQueryClauses,
        float filenameBoost)
    {
        if (terms.Count == 0)
            return new MatchAllDocsQuery();

        var queryList = new List<Query>(Math.Min(terms.Count, maxQueryClauses));
        foreach (var term in terms)
        {
            if (queryList.Count >= maxQueryClauses)
                break;

            var termQuery = BuildPartialTermQuery(term, analyzer, filenameBoost);
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

    private static Query? BuildPartialTermQuery(string term, Analyzer analyzer, float filenameBoost)
    {
        var trimmed = term.Trim();
        if (trimmed.Length == 0)
            return null;

        var rawWildcard = trimmed.ToLowerInvariant();
        var contentQuery = BuildPartialContentQuery(analyzer, trimmed, rawWildcard);
        if (contentQuery == null)
            return null;

        if (rawWildcard.Length == 0)
            return contentQuery;

        var filenameQuery = CreateFilenameWildcardQuery($"*{rawWildcard}*", filenameBoost);
        return CombineContentAndFilename(contentQuery, filenameQuery, filenameBoost);
    }

    /// <summary>アナライザがトークンを返せない場合のフォールバック（LowerCaseFilter に合わせて小文字化）。</summary>
    private static List<string> BuildRawFallbackTokens(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return [];

        if (phrase.Contains(' '))
        {
            return phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim().ToLowerInvariant())
                .Where(p => p.Length > 0)
                .ToList();
        }

        return [phrase.Trim().ToLowerInvariant()];
    }

    private static List<string> TokenizeSpaceSeparatedParts(Analyzer analyzer, string phrase)
    {
        var list = new List<string>();
        foreach (var part in phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0) continue;
            list.AddRange(GetTokensFromAnalyzer(analyzer, trimmed));
        }

        return list;
    }

    /// <summary>AND 部分一致用。入力全体を1回でトークン化する。</summary>
    private static List<string> GetContentSearchTokens(Analyzer analyzer, string userTerm)
    {
        var tokens = GetTokensFromAnalyzer(analyzer, userTerm);
        if (tokens.Count > 0)
            return tokens;

        if (userTerm.Contains(' '))
            return TokenizeSpaceSeparatedParts(analyzer, userTerm);

        return BuildRawFallbackTokens(userTerm);
    }

    private static Query? BuildPartialContentQuery(Analyzer analyzer, string userTerm, string rawWildcard)
    {
        var tokens = GetSubstantiveTokens(GetContentSearchTokens(analyzer, userTerm));
        if (tokens.Count == 0)
        {
            return string.IsNullOrEmpty(rawWildcard)
                ? null
                : new WildcardQuery(new Term(LuceneIndexService.FieldContent, $"*{rawWildcard}*"));
        }

        if (tokens.Count == 1)
            return new WildcardQuery(new Term(LuceneIndexService.FieldContent, $"*{tokens[0]}*"));

        // 複数語: すべての語がどこかに部分一致（入力全体は1キーワードのまま、連続一致は要求しない）
        var boolQuery = new BooleanQuery();
        foreach (var token in tokens)
            boolQuery.Add(new WildcardQuery(new Term(LuceneIndexService.FieldContent, $"*{token}*")), Occur.MUST);
        return boolQuery;
    }

    /// <summary>空白トークン（Sudachi のスペース等）を除いた検索語。</summary>
    private static List<string> GetSubstantiveTokens(IReadOnlyList<string> tokens)
    {
        return tokens
            .Where(t => !string.IsNullOrWhiteSpace(t) && t != " ")
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static WildcardQuery CreateFilenameWildcardQuery(string pattern, float filenameBoost)
    {
        var fq = new WildcardQuery(new Term(LuceneIndexService.FieldFileName, pattern));
        fq.Boost = filenameBoost;
        return fq;
    }

    private static Query CombineContentAndFilename(Query contentQuery, Query filenameQuery, float filenameBoost)
    {
        filenameQuery.Boost = filenameBoost;
        return new BooleanQuery
        {
            { contentQuery, Occur.SHOULD },
            { filenameQuery, Occur.SHOULD },
        };
    }

    private static List<string> GetTokensFromAnalyzer(Analyzer analyzer, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        try
        {
            var list = new List<string>();
            using var reader = new StringReader(text);
            using var tokenStream = analyzer.GetTokenStream(LuceneIndexService.FieldContent, reader);
            var termAttr = tokenStream.GetAttribute<ICharTermAttribute>();
            if (termAttr == null) return list;
            tokenStream.Reset();
            while (tokenStream.IncrementToken())
            {
                var term = termAttr.ToString();
                if (!string.IsNullOrEmpty(term)) list.Add(term);
            }
            tokenStream.End();
            return list;
        }
        catch
        {
            return [];
        }
    }
}
