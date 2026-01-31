using System.Diagnostics;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Settings;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Cjk;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace FullTextSearch.Infrastructure.Lucene;

/// <summary>
/// Lucene.NETを使用した検索サービスの実装
/// </summary>
public class LuceneSearchService : ISearchService, IDisposable
{
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;
    private const int HighlightFragmentSize = 100;
    private const int MaxHighlights = 5;
    private static readonly string DefaultIndexPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FullTextSearch", "Index");

    private readonly IAppSettingsService _settingsService;
    private string? _currentIndexPath;
    private FSDirectory? _directory;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private Analyzer? _analyzer;
    private readonly object _lock = new();
    private bool _disposed;

    public LuceneSearchService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public async Task<SearchResult> SearchAsync(string query, SearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchResult
            {
                Query = query,
                Items = [],
                TotalHits = 0,
                ElapsedMilliseconds = 0
            };
        }

        options ??= new SearchOptions();
        var stopwatch = Stopwatch.StartNew();

        // 検索全体をスレッドプールで実行し UI スレッドのブロックを防ぐ
        var result = await Task.Run(() =>
        {
            EnsureSearcherReady();

            IndexSearcher? searcher;
            Analyzer? analyzer;
            lock (_lock)
            {
                searcher = _searcher;
                analyzer = _analyzer;
            }

            if (searcher == null || analyzer == null)
            {
                return new SearchResult
                {
                    Query = query,
                    Items = [],
                    TotalHits = 0,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var luceneQuery = BuildPartialMatchQuery(query, analyzer);
                var boolQuery = new BooleanQuery { { luceneQuery, Occur.MUST } };

                if (options.FileTypeFilter != null && options.FileTypeFilter.Count > 0)
                {
                    var typeQuery = new BooleanQuery();
                    foreach (var fileType in options.FileTypeFilter)
                        typeQuery.Add(new TermQuery(new Term(LuceneIndexService.FieldFileType, fileType)), Occur.SHOULD);
                    boolQuery.Add(typeQuery, Occur.MUST);
                }

                if (options.DateFrom.HasValue || options.DateTo.HasValue)
                {
                    var from = options.DateFrom?.Ticks ?? long.MinValue;
                    var to = options.DateTo?.Ticks ?? long.MaxValue;
                    boolQuery.Add(NumericRangeQuery.NewInt64Range(LuceneIndexService.FieldLastModified, from, to, true, true), Occur.MUST);
                }

                if (!string.IsNullOrEmpty(options.FolderFilter))
                    boolQuery.Add(new PrefixQuery(new Term(LuceneIndexService.FieldFolderPath, options.FolderFilter)), Occur.MUST);

                cancellationToken.ThrowIfCancellationRequested();
                var topDocs = searcher.Search(boolQuery, options.MaxResults);
                var totalHits = topDocs.TotalHits;
                var hitCount = topDocs.ScoreDocs.Length;

                var skipHighlights = options.SkipHighlights;
                Highlighter? highlighter = null;
                if (!skipHighlights)
                {
                    var formatter = new SimpleHTMLFormatter("[", "]");
                    var scorer = new QueryScorer(luceneQuery);
                    highlighter = new Highlighter(formatter, scorer) { TextFragmenter = new SimpleFragmenter(HighlightFragmentSize) };
                }

                var results = new List<SearchResultItem>(hitCount);
                var index = 0;
                foreach (var scoreDoc in topDocs.ScoreDocs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var doc = searcher.Doc(scoreDoc.Doc);
                    var filePath = doc.Get(LuceneIndexService.FieldFilePath);
                    var doHighlight = !skipHighlights && highlighter != null;
                    var content = doHighlight ? doc.Get(LuceneIndexService.FieldContent) : null;

                    var highlights = new List<MatchHighlight>();
                    if (doHighlight && !string.IsNullOrEmpty(content))
                    {
                        var tokenStream = analyzer.GetTokenStream(LuceneIndexService.FieldContent, new StringReader(content));
                        foreach (var fragment in highlighter!.GetBestFragments(tokenStream, content, MaxHighlights))
                        {
                            if (string.IsNullOrWhiteSpace(fragment)) continue;
                            var highlightStart = fragment.IndexOf('[');
                            var highlightEnd = fragment.IndexOf(']');
                            highlights.Add(new MatchHighlight
                            {
                                Text = fragment.Replace("[", "").Replace("]", ""),
                                HighlightStart = highlightStart >= 0 ? highlightStart : 0,
                                HighlightEnd = highlightEnd >= 0 ? highlightEnd - 1 : 0
                            });
                        }
                    }
                    index++;

                    results.Add(new SearchResultItem
                    {
                        FilePath = filePath,
                        FileName = doc.Get(LuceneIndexService.FieldFileName),
                        FolderPath = doc.Get(LuceneIndexService.FieldFolderPath),
                        FileSize = long.TryParse(doc.Get(LuceneIndexService.FieldFileSize), out var size) ? size : 0,
                        LastModified = long.TryParse(doc.Get(LuceneIndexService.FieldLastModified), out var ticks) ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue,
                        FileType = doc.Get(LuceneIndexService.FieldFileType),
                        Score = scoreDoc.Score,
                        Highlights = highlights
                    });
                }

                stopwatch.Stop();
                return new SearchResult
                {
                    Query = query,
                    Items = results,
                    TotalHits = totalHits,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }
            catch (ParseException)
            {
                stopwatch.Stop();
                return new SearchResult
                {
                    Query = query,
                    Items = [],
                    TotalHits = 0,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }
        }, cancellationToken);

        return result;
    }

    private string GetIndexPath()
    {
        var path = _settingsService.Settings.IndexPath;
        return string.IsNullOrWhiteSpace(path) ? DefaultIndexPath : path.Trim();
    }

    private void EnsureSearcherReady()
    {
        lock (_lock)
        {
            var indexPath = GetIndexPath();
            if (_currentIndexPath != null && _currentIndexPath != indexPath)
            {
                _reader?.Dispose();
                _reader = null;
                _searcher = null;
                _directory?.Dispose();
                _directory = null;
                _currentIndexPath = null;
            }

            if (!System.IO.Directory.Exists(indexPath))
            {
                return;
            }

            _currentIndexPath = indexPath;

            if (_directory == null)
            {
                _directory = FSDirectory.Open(indexPath);
            }

            if (_analyzer == null)
            {
                // CJKAnalyzer: 日本語・中国語・韓国語をバイグラムで検索、英語も対応
                _analyzer = new CJKAnalyzer(AppLuceneVersion);
            }

            // リーダーの更新チェック
            if (_reader == null)
            {
                if (!DirectoryReader.IndexExists(_directory))
                {
                    return;
                }
                _reader = DirectoryReader.Open(_directory);
                _searcher = new IndexSearcher(_reader);
            }
            else
            {
                var newReader = DirectoryReader.OpenIfChanged(_reader);
                if (newReader != null)
                {
                    _reader.Dispose();
                    _reader = newReader;
                    _searcher = new IndexSearcher(_reader);
                }
            }
        }
    }

    /// <summary>
    /// 部分一致検索用のクエリを構築。
    /// コンテンツとファイル名の両方を検索し、ファイル名一致はスコアをブーストする。
    /// CJKAnalyzer: 日本語をバイグラムで検索、英語も対応。
    /// </summary>
    private const float FilenameBoost = 2.5f;
    private const int MaxQueryTerms = 64;
    private const int MaxQueryClauses = 256;

    /// <summary>
    /// アナライザで文字列をトークン化してトークン文字列のリストを返す。
    /// </summary>
    private static List<string> GetTokensFromAnalyzer(Analyzer analyzer, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
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

    /// <summary>
    /// 検索クエリ文字列を正規化（前後空白・全角スペースの統一など）
    /// </summary>
    private static string NormalizeQueryString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var s = input.Trim();
        // 全角スペースを半角に統一してトークン分割の一貫性を保つ
        if (s.Contains('\u3000'))
            s = s.Replace('\u3000', ' ');
        return s;
    }

    private Query BuildPartialMatchQuery(string query, Analyzer analyzer)
    {
        var normalized = NormalizeQueryString(query);
        var userTerms = normalized.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Take(MaxQueryTerms)
            .ToArray();

        if (userTerms.Length == 0)
            return new MatchAllDocsQuery();

        // 各ユーザー入力語について: トークン化して「一続きの語」として PhraseQuery で検索する
        var queryList = new List<Query>();
        foreach (var userTerm in userTerms)
        {
            if (queryList.Count >= MaxQueryClauses) break;
            if (string.IsNullOrWhiteSpace(userTerm)) continue;

            var trimmed = userTerm.Trim();
            var isAsciiWord = trimmed.Length > 0 && trimmed.All(c => char.IsLetterOrDigit(c) || c == '_');
            var termForWildcard = isAsciiWord ? trimmed.ToLowerInvariant() : trimmed;

            Query? contentQuery;
            if (isAsciiWord)
            {
                contentQuery = !string.IsNullOrEmpty(termForWildcard)
                    ? new WildcardQuery(new Term(LuceneIndexService.FieldContent, $"*{termForWildcard}*"))
                    : null;
            }
            else
            {
                var tokens = GetTokensFromAnalyzer(analyzer, userTerm);
                if (tokens.Count == 0)
                    contentQuery = !string.IsNullOrEmpty(termForWildcard) ? new WildcardQuery(new Term(LuceneIndexService.FieldContent, $"*{termForWildcard}*")) : null;
                else if (tokens.Count == 1)
                    contentQuery = new WildcardQuery(new Term(LuceneIndexService.FieldContent, $"*{tokens[0].ToLowerInvariant()}*"));
                else
                {
                    var phraseQuery = new PhraseQuery { Slop = 1 };
                    foreach (var token in tokens)
                    {
                        if (string.IsNullOrEmpty(token)) continue;
                        phraseQuery.Add(new Term(LuceneIndexService.FieldContent, token));
                    }
                    var phraseTerms = phraseQuery.GetTerms();
                    contentQuery = phraseTerms.Length == 0 ? null
                        : phraseTerms.Length == 1 ? new WildcardQuery(new Term(LuceneIndexService.FieldContent, $"*{phraseTerms[0].Text.ToLowerInvariant()}*"))
                        : phraseQuery;
                }
            }

            if (contentQuery == null) continue;

            // ファイル名も検索し、一致時はスコアをブースト
            Query? filenameQuery = null;
            if (termForWildcard.Length > 0)
            {
                var fq = new WildcardQuery(new Term(LuceneIndexService.FieldFileName, $"*{termForWildcard}*"));
                fq.Boost = FilenameBoost;
                filenameQuery = fq;
            }
            var termQuery = filenameQuery != null
                ? new BooleanQuery
                {
                    { contentQuery, Occur.SHOULD },
                    { filenameQuery, Occur.SHOULD }
                }
                : contentQuery;
            queryList.Add(termQuery);
        }

        if (queryList.Count == 0)
            return new MatchAllDocsQuery();
        if (queryList.Count == 1)
            return queryList[0];
        var boolQuery = new BooleanQuery();
        foreach (var q in queryList)
            boolQuery.Add(q, Occur.MUST);
        return boolQuery;
    }

    /// <summary>
    /// 特殊文字をエスケープ
    /// </summary>
    private static string EscapeQuery(string query)
    {
        // Luceneの特殊文字をエスケープ
        var specialChars = new[] { '+', '-', '&', '|', '!', '(', ')', '{', '}', '[', ']', '^', '"', '~', '*', '?', ':', '\\', '/' };

        foreach (var c in specialChars)
        {
            query = query.Replace(c.ToString(), $"\\{c}");
        }

        return query;
    }

    /// <summary>
    /// インデックスを再読み込み
    /// </summary>
    public void RefreshIndex()
    {
        lock (_lock)
        {
            _reader?.Dispose();
            _reader = null;
            _searcher = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            _reader?.Dispose();
            _analyzer?.Dispose();
            _directory?.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

