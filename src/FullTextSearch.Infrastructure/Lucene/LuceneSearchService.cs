// Lucene.NET による全文検索とハイライト。Sudachi でクエリをトークナイズし、設定のインデックスパスを参照。
using System;
using System.IO;
using System.Threading;
using FullTextSearch.Core;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Settings;
using FullTextSearch.Infrastructure.Sudachi;
using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace FullTextSearch.Infrastructure.Lucene;

/// <summary>
/// Lucene.NET を使用した検索サービスの実装。キーワード／いずれか／完全一致の各モードに対応。
/// </summary>
public class LuceneSearchService : ISearchService, IDisposable
{
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

    private readonly IAppSettingsService _settingsService;
    private string? _currentIndexPath;
    private FSDirectory? _directory;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private Analyzer? _analyzer;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>設定サービスからインデックスパスを取得するために使用する。</summary>
    public LuceneSearchService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    /// <summary>全文検索を実行し、検索結果（ファイル情報）を返す。UI スレッドをブロックしないよう Task.Run で実行。</summary>
    public async Task<SearchResult> SearchAsync(string query, SearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchResult { Items = [] };

        options ??= new SearchOptions();

        // 検索全体をスレッドプールで実行し UI スレッドのブロックを防ぐ。リーダー競合時は 1 回だけリトライ。
        var result = await Task.Run(() =>
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0)
                {
                    RefreshIndex();
                    Thread.Sleep(ReaderOpenRetryMs);
                }
                EnsureSearcherReady();

                IndexSearcher? searcher;
                Analyzer? analyzer;
                lock (_lock)
                {
                    searcher = _searcher;
                    analyzer = _analyzer;
                }

                if (searcher == null || analyzer == null)
                    return new SearchResult { Items = [] };

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var normalizedQuery = SearchQueryParser.NormalizeQueryString(query);
                    var isExactMatchMode = options.SearchMode == SearchMode.Phrase;
                    var luceneQuery = isExactMatchMode
                        ? BuildExactCandidateQuery(normalizedQuery, searcher.IndexReader)
                        : SearchQueryParser.BuildQuery(
                            query,
                            analyzer,
                            options.SearchMode,
                            MaxQueryTerms,
                            MaxQueryClauses,
                            FilenameBoost);
                    var boolQuery = new BooleanQuery { { luceneQuery, Occur.MUST } };

                    cancellationToken.ThrowIfCancellationRequested();

                    // 完全一致: バイグラム候補（旧インデックスでは全件）を走査し、保存本文への連続一致で確定。
                    //           ヒット doc を MaxResults まで収集して打ち切るため、巨大な優先度キューを作らない。
                    // 通常検索: スコア順に上位 MaxResults 件を取得。
                    IReadOnlyList<int> hitDocIds;
                    if (isExactMatchMode)
                    {
                        var collector = new ExactMatchCollector(normalizedQuery, options.MaxResults);
                        try { searcher.Search(boolQuery, collector); }
                        catch (CollectionTerminatedException) { /* MaxResults 到達で打ち切り */ }
                        hitDocIds = collector.MatchedGlobalDocIds;
                    }
                    else
                    {
                        var topDocs = searcher.Search(boolQuery, options.MaxResults);
                        hitDocIds = topDocs.ScoreDocs.Select(sd => sd.Doc).ToList();
                    }

                    var results = new List<SearchResultItem>(hitDocIds.Count);
                    foreach (var docId in hitDocIds)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var doc = searcher.Doc(docId);
                        results.Add(new SearchResultItem
                        {
                            FilePath = doc.Get(LuceneIndexService.FieldFilePath) ?? "",
                            FileName = doc.Get(LuceneIndexService.FieldFileName) ?? "",
                            FolderPath = doc.Get(LuceneIndexService.FieldFolderPath) ?? "",
                            FileSize = long.TryParse(doc.Get(LuceneIndexService.FieldFileSize), out var sz) ? sz : 0,
                            LastModified = long.TryParse(doc.Get(LuceneIndexService.FieldLastModified), out var ticks) && ticks > 0
                                ? new DateTime(ticks, DateTimeKind.Utc)
                                : DateTime.MinValue
                        });
                    }

                    return new SearchResult { Items = results };
                }
                catch (ParseException)
                {
                    return new SearchResult { Items = [] };
                }
                catch (IOException)
                {
                    if (attempt == 0) continue;
                    return new SearchResult { Items = [] };
                }
                catch (ObjectDisposedException)
                {
                    if (attempt == 0) continue;
                    return new SearchResult { Items = [] };
                }
            }

            return new SearchResult { Items = [] };
        }, cancellationToken);

        return result;
    }

    /// <summary>設定のインデックスパスをフルパスに正規化する。</summary>
    private string GetIndexPath()
    {
        var path = _settingsService.Settings.IndexPath;
        var raw = string.IsNullOrWhiteSpace(path) ? DefaultPaths.IndexPath : path.Trim();
        return Path.GetFullPath(raw);
    }

    private const int ReaderOpenRetryMs = 150;

    /// <summary>ディレクトリリーダーと検索器を、現在のインデックスパスに合わせて開く（ロック時はリトライ）。</summary>
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
                try
                {
                    _directory = FSDirectory.Open(indexPath);
                }
                catch (IOException)
                {
                    return;
                }
            }

            if (_analyzer == null)
            {
                _analyzer = new SudachiAnalyzer();
            }

            // リーダーの更新チェック（インデックス書込中はロックで失敗するため try + 1 回リトライ）
            try
            {
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
            catch (IOException)
            {
                _reader?.Dispose();
                _reader = null;
                _searcher = null;
                if (ReaderOpenRetryMs > 0)
                {
                    Thread.Sleep(ReaderOpenRetryMs);
                    try
                    {
                        if (DirectoryReader.IndexExists(_directory))
                        {
                            _reader = DirectoryReader.Open(_directory);
                            _searcher = new IndexSearcher(_reader);
                        }
                    }
                    catch (IOException) { /* 諦める */ }
                }
            }
        }
    }

    /// <summary>
    /// ファイル名一致時のスコアブースト係数。
    /// ファイル名は本文より語数が圧倒的に少なくスコアが沈みやすいので、本文側より大きく重み付けする。
    /// 「本文の通常一致を上回り、かつ本文一致を完全に隠さない」バランスとして 2.5 を採用。
    /// </summary>
    private const float FilenameBoost = 2.5f;
    /// <summary>
    /// 1 検索クエリで分解処理する単語の最大数。
    /// ユーザがスペース区切りで大量の語を貼り付けた場合の DoS 的な負荷（Sudachi 呼び出し・Wildcard 展開）を抑えるための上限。
    /// 64 語あれば通常の AND 検索には十分。
    /// </summary>
    private const int MaxQueryTerms = 64;
    /// <summary>
    /// 最終的に組み立てる BooleanQuery の節数の上限。
    /// Lucene.NET の既定上限（1024）を大きく下回る安全値。1 語あたり「本文一致 ＋ ファイル名一致」で最大 2 節を消費するため
    /// MaxQueryTerms の 4 倍程度を確保している。
    /// </summary>
    private const int MaxQueryClauses = 256;

    /// <summary>
    /// 1 つの完全一致クエリで MUST にできるバイグラム数の上限。
    /// バイグラムは多いほど候補が絞れるが、Lucene の BooleanQuery 節数上限（既定 1024）に達しないよう抑える。
    /// MUST 節を減らしても候補集合は広がるだけで取りこぼしは起きない（完全性は保たれる）ため、上限で打ち切ってよい。
    /// </summary>
    private const int ExactNGramMaxClauses = 64;

    /// <summary>
    /// 完全一致検索の候補絞り込みクエリを作る。
    /// 検索語の文字バイグラムをすべて含む文書（＝連続一致の上位集合）に候補を限定する。
    /// 1 文字以下、またはバイグラム索引を持たない旧インデックスでは全件走査（<see cref="MatchAllDocsQuery"/>）へフォールバックする。
    /// 最終的な連続一致の確定は <see cref="ExactMatchCollector"/>（<see cref="ExactMatchHelper"/>）が行う。
    /// </summary>
    private static Query BuildExactCandidateQuery(string normalizedQuery, IndexReader reader)
    {
        var grams = ContentNGram.BuildQueryGrams(normalizedQuery);
        if (grams.Count == 0)
            return new MatchAllDocsQuery();

        // 旧インデックス（バイグラム未収録）では候補を作れないため全件走査にフォールバック。
        try
        {
            if (reader.GetDocCount(LuceneIndexService.FieldContentNGram) <= 0)
                return new MatchAllDocsQuery();
        }
        catch
        {
            return new MatchAllDocsQuery();
        }

        var bq = new BooleanQuery();
        var added = 0;
        foreach (var gram in grams)
        {
            if (added >= ExactNGramMaxClauses) break;
            bq.Add(new TermQuery(new Term(LuceneIndexService.FieldContentNGram, gram)), Occur.MUST);
            added++;
        }
        return bq;
    }

    /// <summary>
    /// 完全一致検索の候補を走査し、保存本文・ファイル名への連続一致（<see cref="ExactMatchHelper"/>）で確定した
    /// グローバル doc ID を収集する。MaxResults に達したら <see cref="CollectionTerminatedException"/> で走査を打ち切る。
    /// スコアリング・優先度キューを使わず、確定に必要な本文・ファイル名のみを読み出す。
    /// </summary>
    private sealed class ExactMatchCollector : ICollector
    {
        private static readonly ISet<string> ScanFields = new HashSet<string>
        {
            LuceneIndexService.FieldContent,
            LuceneIndexService.FieldFileName
        };

        private readonly string _normalizedQuery;
        private readonly int _maxResults;
        private AtomicReader? _reader;
        private int _docBase;

        public List<int> MatchedGlobalDocIds { get; } = new();

        public ExactMatchCollector(string normalizedQuery, int maxResults)
        {
            _normalizedQuery = normalizedQuery;
            _maxResults = Math.Max(maxResults, 0);
        }

        public bool AcceptsDocsOutOfOrder => true;

        public void SetScorer(Scorer scorer) { /* スコア不要 */ }

        public void SetNextReader(AtomicReaderContext context)
        {
            _reader = context.AtomicReader;
            _docBase = context.DocBase;
        }

        public void Collect(int doc)
        {
            if (_reader == null) return;
            if (MatchedGlobalDocIds.Count >= _maxResults)
                throw new CollectionTerminatedException();

            var stored = _reader.Document(doc, ScanFields);
            var content = stored.Get(LuceneIndexService.FieldContent) ?? "";
            var fileName = stored.Get(LuceneIndexService.FieldFileName) ?? "";
            if (!ExactMatchHelper.MatchesContentOrFileName(content, fileName, _normalizedQuery))
                return;

            MatchedGlobalDocIds.Add(_docBase + doc);
            if (MatchedGlobalDocIds.Count >= _maxResults)
                throw new CollectionTerminatedException();
        }
    }

    /// <inheritdoc />
    public void RefreshIndex()
    {
        lock (_lock)
        {
            _reader?.Dispose();
            _reader = null;
            _searcher = null;
        }
    }

    /// <summary>
    /// 検索用の Reader / Analyzer を事前に用意し、初回検索の遅延を軽減する。
    /// </summary>
    public void Warmup()
    {
        EnsureSearcherReady();
    }

    /// <summary>DirectoryReader・Analyzer・FSDirectory を解放する。</summary>
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

