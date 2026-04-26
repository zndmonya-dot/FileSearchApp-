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
using Lucene.Net.Analysis.TokenAttributes;
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

                    var contentsForBatch = new List<string?>(hitCount);
                    var docInfos = new List<(string filePath, string fileName, string folderPath, long fileSize, long lastMod, string fileType, float score)>(hitCount);
                    foreach (var scoreDoc in topDocs.ScoreDocs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var doc = searcher.Doc(scoreDoc.Doc);
                        var content = !skipHighlights && highlighter != null ? doc.Get(LuceneIndexService.FieldContent) : null;
                        contentsForBatch.Add(content);
                        docInfos.Add((
                            doc.Get(LuceneIndexService.FieldFilePath) ?? "",
                            doc.Get(LuceneIndexService.FieldFileName) ?? "",
                            doc.Get(LuceneIndexService.FieldFolderPath) ?? "",
                            long.TryParse(doc.Get(LuceneIndexService.FieldFileSize), out var sz) ? sz : 0,
                            long.TryParse(doc.Get(LuceneIndexService.FieldLastModified), out var ticks) ? ticks : 0,
                            doc.Get(LuceneIndexService.FieldFileType) ?? "",
                            scoreDoc.Score
                        ));
                    }

                    List<List<string>>? batchTokenLists = null;
                    if (!skipHighlights && highlighter != null && contentsForBatch.Count > 0)
                    {
                        var nonNullContents = contentsForBatch.Select(c => c ?? "").ToList();
                        batchTokenLists = SudachiTokenizer.InvokeSudachiBatch(nonNullContents);
                    }

                    var results = new List<SearchResultItem>(hitCount);
                    for (var i = 0; i < hitCount; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var info = docInfos[i];
                        var content = contentsForBatch[i];
                        var highlights = new List<MatchHighlight>(MaxHighlights);
                        if (!skipHighlights && highlighter != null && !string.IsNullOrEmpty(content))
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
    /// アナライザで文字列をトークン化してトークン文字列のリストを返す。失敗時は空リスト（検索はワイルドカードにフォールバック）。
    /// </summary>
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

    /// <summary>
    /// 部分一致検索用のクエリを構築する。コンテンツとファイル名の両方を検索し、ファイル名一致はスコアをブーストする。
    /// </summary>
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

        // 各ユーザー入力語について: アナライザでトークン化し、
        //   1 トークン   → 部分一致できる WildcardQuery("*token*")
        //   複数トークン → 一続きの語として PhraseQuery（語間の揺れに備えて Slop=1）
        // を組み立てる。SudachiAnalyzer は ASCII を小文字化してパススルーするため、
        // ASCII / 日本語で分岐を分ける必要はない（フォールバックは生入力の小文字版でワイルドカード）。
        var queryList = new List<Query>(Math.Min(userTerms.Length, MaxQueryClauses));
        foreach (var userTerm in userTerms)
        {
            if (queryList.Count >= MaxQueryClauses) break;
            if (string.IsNullOrWhiteSpace(userTerm)) continue;

            var trimmed = userTerm.Trim();
            // フォールバック / ファイル名検索用: アナライザを通さない素の小文字キー
            var rawWildcard = trimmed.ToLowerInvariant();

            var contentQuery = BuildContentQueryForTerm(analyzer, userTerm, rawWildcard);
            if (contentQuery == null) continue;

            // ファイル名も検索し、一致時はスコアをブースト
            Query? filenameQuery = null;
            if (rawWildcard.Length > 0)
            {
                var fq = new WildcardQuery(new Term(LuceneIndexService.FieldFileName, $"*{rawWildcard}*"));
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
    /// 1 ユーザー入力語に対する本文側クエリを生成する。
    /// アナライザでトークン化し、1 トークンならワイルドカード、複数トークンなら PhraseQuery を返す。
    /// トークン化に失敗 / 0 件の場合は <paramref name="rawWildcard"/> を素のワイルドカードとしてフォールバック。
    /// </summary>
    private static Query? BuildContentQueryForTerm(Analyzer analyzer, string userTerm, string rawWildcard)
    {
        var tokens = GetTokensFromAnalyzer(analyzer, userTerm);
        if (tokens.Count == 0)
        {
            return string.IsNullOrEmpty(rawWildcard)
                ? null
                : new WildcardQuery(new Term(LuceneIndexService.FieldContent, $"*{rawWildcard}*"));
        }

        if (tokens.Count == 1)
        {
            return new WildcardQuery(new Term(LuceneIndexService.FieldContent, $"*{tokens[0]}*"));
        }

        var phraseQuery = new PhraseQuery { Slop = 1 };
        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token)) continue;
            phraseQuery.Add(new Term(LuceneIndexService.FieldContent, token));
        }
        var phraseTerms = phraseQuery.GetTerms();
        return phraseTerms.Length switch
        {
            0 => null,
            1 => new WildcardQuery(new Term(LuceneIndexService.FieldContent, $"*{phraseTerms[0].Text}*")),
            _ => phraseQuery,
        };
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

