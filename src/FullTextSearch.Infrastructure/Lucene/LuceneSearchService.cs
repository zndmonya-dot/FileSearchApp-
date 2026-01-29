using System.Diagnostics;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Search;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Ja;
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

    private readonly string _indexPath;
    private FSDirectory? _directory;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private Analyzer? _analyzer;
    private readonly object _lock = new();
    private bool _disposed;

    public LuceneSearchService()
    {
        _indexPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FullTextSearch", "Index");
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

        await Task.Run(() => EnsureSearcherReady(), cancellationToken);

        IndexSearcher? searcher;
        Analyzer? analyzer;
        lock (_lock)
        {
            searcher = _searcher;
            analyzer = _analyzer;
        }

        if (searcher == null || analyzer == null)
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

        var results = new List<SearchResultItem>();
        int totalHits;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 部分一致検索用にワイルドカードクエリを構築
            var luceneQuery = BuildPartialMatchQuery(query);

            // フィルター付きクエリの構築
            var boolQuery = new BooleanQuery
            {
                { luceneQuery, Occur.MUST }
            };

            if (options.FileTypeFilter != null && options.FileTypeFilter.Count > 0)
            {
                var typeQuery = new BooleanQuery();
                foreach (var fileType in options.FileTypeFilter)
                {
                    typeQuery.Add(new TermQuery(new Term(LuceneIndexService.FieldFileType, fileType)), Occur.SHOULD);
                }
                boolQuery.Add(typeQuery, Occur.MUST);
            }

            if (options.DateFrom.HasValue || options.DateTo.HasValue)
            {
                var from = options.DateFrom?.Ticks ?? long.MinValue;
                var to = options.DateTo?.Ticks ?? long.MaxValue;
                var rangeQuery = NumericRangeQuery.NewInt64Range(
                    LuceneIndexService.FieldLastModified, from, to, true, true);
                boolQuery.Add(rangeQuery, Occur.MUST);
            }

            if (!string.IsNullOrEmpty(options.FolderFilter))
            {
                var folderQuery = new PrefixQuery(new Term(LuceneIndexService.FieldFolderPath, options.FolderFilter));
                boolQuery.Add(folderQuery, Occur.MUST);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var topDocs = searcher.Search(boolQuery, options.MaxResults);
            totalHits = topDocs.TotalHits;

            var formatter = new SimpleHTMLFormatter("[", "]");
            var scorer = new QueryScorer(luceneQuery);
            var highlighter = new Highlighter(formatter, scorer)
            {
                TextFragmenter = new SimpleFragmenter(HighlightFragmentSize)
            };

            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var doc = searcher.Doc(scoreDoc.Doc);
                var filePath = doc.Get(LuceneIndexService.FieldFilePath);
                var content = doc.Get(LuceneIndexService.FieldContent);

                var highlights = new List<MatchHighlight>();
                if (!string.IsNullOrEmpty(content))
                {
                    var tokenStream = analyzer.GetTokenStream(LuceneIndexService.FieldContent, new StringReader(content));
                    var fragments = highlighter.GetBestFragments(tokenStream, content, MaxHighlights);

                    foreach (var fragment in fragments)
                    {
                        if (!string.IsNullOrWhiteSpace(fragment))
                        {
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
                }

                results.Add(new SearchResultItem
                {
                    FilePath = filePath,
                    FileName = doc.Get(LuceneIndexService.FieldFileName),
                    FolderPath = doc.Get(LuceneIndexService.FieldFolderPath),
                    FileSize = long.TryParse(doc.Get(LuceneIndexService.FieldFileSize), out var size) ? size : 0,
                    LastModified = long.TryParse(doc.Get(LuceneIndexService.FieldLastModified), out var ticks)
                        ? new DateTime(ticks, DateTimeKind.Utc)
                        : DateTime.MinValue,
                    FileType = doc.Get(LuceneIndexService.FieldFileType),
                    Score = scoreDoc.Score,
                    Highlights = highlights
                });
            }
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

        stopwatch.Stop();

        return new SearchResult
        {
            Query = query,
            Items = results,
            TotalHits = totalHits,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
        };
    }

    private void EnsureSearcherReady()
    {
        lock (_lock)
        {
            if (!System.IO.Directory.Exists(_indexPath))
            {
                return;
            }

            if (_directory == null)
            {
                _directory = FSDirectory.Open(_indexPath);
            }

            if (_analyzer == null)
            {
                _analyzer = new JapaneseAnalyzer(AppLuceneVersion);
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
    /// 部分一致検索用のクエリを構築
    /// </summary>
    private Query BuildPartialMatchQuery(string query)
    {
        var terms = query.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (terms.Length == 0)
        {
            return new MatchAllDocsQuery();
        }

        if (terms.Length == 1)
        {
            // 単一キーワードの場合、ワイルドカードクエリを使用
            var term = terms[0].ToLowerInvariant();
            return new WildcardQuery(new Term(LuceneIndexService.FieldContent, $"*{term}*"));
        }

        // 複数キーワードの場合、AND条件で結合
        var boolQuery = new BooleanQuery();
        foreach (var t in terms)
        {
            var term = t.ToLowerInvariant();
            boolQuery.Add(new WildcardQuery(new Term(LuceneIndexService.FieldContent, $"*{term}*")), Occur.MUST);
        }
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

