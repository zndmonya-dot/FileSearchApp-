// Lucene.NET による全文検索とハイライト。Sudachi でクエリをトークナイズし、設定のインデックスパスを参照。
using System;
using System.Diagnostics;
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
using Lucene.Net.Search.Highlight;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace FullTextSearch.Infrastructure.Lucene;

/// <summary>
/// Lucene.NET を使用した検索サービスの実装。部分一致・ハイライト・ファイル種類フィルター等に対応。
/// </summary>
public class LuceneSearchService : ISearchService, IDisposable
{
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;
    /// <summary>
    /// ハイライト抜粋 1 件あたりの最大文字数。
    /// 短すぎると一致語の文脈が読み取れず、長すぎるとプレビュー UI を圧迫するため、
    /// 業務文書の 1 文〜2 文程度を想定して 100 に設定。
    /// </summary>
    private const int HighlightFragmentSize = 100;
    /// <summary>
    /// 1 ドキュメントあたりに表示するハイライト断片の最大数。
    /// プレビューの可読性とハイライト計算コスト（Highlighter は本文を再走査するため重い）の両立のため 5 件に制限。
    /// </summary>
    private const int MaxHighlights = 5;

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

    /// <summary>全文検索を実行し、ハイライト付きの検索結果を返す。UI スレッドをブロックしないよう Task.Run で実行。</summary>
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
                    var boolQuery = AppendSearchFilters(new BooleanQuery { { luceneQuery, Occur.MUST } }, options);

                    cancellationToken.ThrowIfCancellationRequested();

                    // 完全一致: バイグラム候補（旧インデックスでは全件）を走査し、保存本文への連続一致で確定。
                    //           ヒット doc を MaxResults まで収集して打ち切るため、巨大な優先度キューを作らない。
                    // 通常検索: スコア順に上位 MaxResults 件を取得。
                    IReadOnlyList<int> exactDocIds = Array.Empty<int>();
                    TopDocs? topDocs = null;
                    int totalHits;
                    if (isExactMatchMode)
                    {
                        var collector = new ExactMatchCollector(normalizedQuery, options.MaxResults);
                        try { searcher.Search(boolQuery, collector); }
                        catch (CollectionTerminatedException) { /* MaxResults 到達で打ち切り */ }
                        exactDocIds = collector.MatchedGlobalDocIds;
                        totalHits = exactDocIds.Count;
                    }
                    else
                    {
                        topDocs = searcher.Search(boolQuery, options.MaxResults);
                        totalHits = topDocs.TotalHits;
                    }

                    var skipHighlights = options.SkipHighlights;
                    Highlighter? highlighter = null;
                    if (!skipHighlights && !isExactMatchMode)
                    {
                        var formatter = new SimpleHTMLFormatter("[", "]");
                        var scorer = new QueryScorer(luceneQuery);
                        highlighter = new Highlighter(formatter, scorer) { TextFragmenter = new SimpleFragmenter(HighlightFragmentSize) };
                    }

                    // 完全一致は収集済み doc 群（既に連続一致確定済み）、通常検索はスコア順 doc を共通処理する。
                    var hits = isExactMatchMode
                        ? exactDocIds.Select(id => (DocId: id, Score: 0f))
                        : topDocs!.ScoreDocs.Select(sd => (DocId: sd.Doc, Score: sd.Score));

                    var contentsForBatch = new List<string?>();
                    var docInfos = new List<(string filePath, string fileName, string folderPath, long fileSize, long lastMod, string fileType, float score)>();
                    foreach (var (docId, score) in hits)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var doc = searcher.Doc(docId);
                        var storedContent = doc.Get(LuceneIndexService.FieldContent) ?? "";
                        var fileName = doc.Get(LuceneIndexService.FieldFileName) ?? "";

                        var content = !skipHighlights && (highlighter != null || isExactMatchMode) ? storedContent : null;
                        contentsForBatch.Add(content);
                        docInfos.Add((
                            doc.Get(LuceneIndexService.FieldFilePath) ?? "",
                            fileName,
                            doc.Get(LuceneIndexService.FieldFolderPath) ?? "",
                            long.TryParse(doc.Get(LuceneIndexService.FieldFileSize), out var sz) ? sz : 0,
                            long.TryParse(doc.Get(LuceneIndexService.FieldLastModified), out var ticks) ? ticks : 0,
                            doc.Get(LuceneIndexService.FieldFileType) ?? "",
                            score
                        ));
                    }

                    var filteredHitCount = docInfos.Count;

                    List<List<string>>? batchTokenLists = null;
                    if (!skipHighlights && highlighter != null && contentsForBatch.Count > 0)
                    {
                        var nonNullContents = contentsForBatch.Select(c => c ?? "").ToList();
                        batchTokenLists = SudachiTokenizer.InvokeSudachiBatch(nonNullContents);
                    }

                    var results = new List<SearchResultItem>(filteredHitCount);
                    for (var i = 0; i < filteredHitCount; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var info = docInfos[i];
                        var content = contentsForBatch[i];
                        var highlights = new List<MatchHighlight>(MaxHighlights);
                        if (!skipHighlights && isExactMatchMode && !string.IsNullOrEmpty(content))
                        {
                            highlights.AddRange(ExactMatchHelper.BuildHighlights(content, normalizedQuery, HighlightFragmentSize, MaxHighlights));
                        }
                        else if (!skipHighlights && highlighter != null && !string.IsNullOrEmpty(content))
                        {
                            try
                            {
                                TokenStream tokenStream;
                                if (batchTokenLists != null && i < batchTokenLists.Count)
                                {
                                    tokenStream = new ListTokenStream(batchTokenLists[i]);
                                }
                                else
                                {
                                    tokenStream = analyzer!.GetTokenStream(LuceneIndexService.FieldContent, new StringReader(content));
                                }
                                using (tokenStream)
                                {
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
                            }
                            catch { /* ハイライト失敗時は結果のみ返す */ }
                        }

                        results.Add(new SearchResultItem
                        {
                            FilePath = info.filePath,
                            FileName = info.fileName,
                            FolderPath = info.folderPath,
                            FileSize = info.fileSize,
                            LastModified = info.lastMod > 0 ? new DateTime(info.lastMod, DateTimeKind.Utc) : DateTime.MinValue,
                            FileType = info.fileType,
                            Score = info.score,
                            Highlights = highlights
                        });
                    }

                    stopwatch.Stop();
                    return new SearchResult
                    {
                        Query = query,
                        Items = results,
                        TotalHits = isExactMatchMode ? results.Count : totalHits,
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
                catch (IOException)
                {
                    if (attempt == 0) continue;
                    stopwatch.Stop();
                    return new SearchResult { Query = query, Items = [], TotalHits = 0, ElapsedMilliseconds = stopwatch.ElapsedMilliseconds };
                }
                catch (ObjectDisposedException)
                {
                    if (attempt == 0) continue;
                    stopwatch.Stop();
                    return new SearchResult { Query = query, Items = [], TotalHits = 0, ElapsedMilliseconds = stopwatch.ElapsedMilliseconds };
                }
            }

            stopwatch.Stop();
            return new SearchResult { Query = query, Items = [], TotalHits = 0, ElapsedMilliseconds = stopwatch.ElapsedMilliseconds };
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

    private static BooleanQuery AppendSearchFilters(BooleanQuery boolQuery, SearchOptions options)
    {
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

        return boolQuery;
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

