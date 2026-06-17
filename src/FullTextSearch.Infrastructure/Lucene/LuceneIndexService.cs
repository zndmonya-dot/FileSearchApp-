// Lucene.NET によるインデックス作成・更新・削除。Sudachi 形態素解析（モード C）でトークナイズ。
using System.Collections.Concurrent;
using System.Threading;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using FullTextSearch.Core;
using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Index;
using FullTextSearch.Infrastructure.Sudachi;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace FullTextSearch.Infrastructure.Lucene;

/// <summary>
/// Lucene.NET を使用したインデックスサービスの実装。再構築・差分更新・フォルダ単位のインデックス化を行う。
/// </summary>
public class LuceneIndexService : IIndexService, IDisposable
{
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;
    /// <summary>
    /// 抽出・登録パイプラインの並列度。ネイティブ Sudachi はスレッドローカルで並列化可能。
    /// 論理コア半分（最大 8）を上限とし、他アプリ用に余裕を残す。
    /// </summary>
    private static readonly int IndexerParallelism = SudachiTokenizer.PoolSize;

    private readonly TextExtractorFactory _extractorFactory;
    private FSDirectory? _directory;
    private IndexWriter? _writer;
    private DirectoryReader? _statsReader;
    private Analyzer? _analyzer;
    private bool _readOnly;
    /// <summary>初期化／破棄／ロールバック等のライフサイクル境界を保護するロック。書込（UpdateDocument 等）はスレッドセーフな IndexWriter に任せて取らない。</summary>
    private readonly object _lock = new();
    /// <summary><see cref="_skippedFiles"/> への並列追加を保護する。</summary>
    private readonly object _skippedLock = new();
    private bool _disposed;
    private IndexRebuildOptions? _currentRebuildOptions;
    private readonly List<SkippedFileEntry> _skippedFiles = new();

    private bool _lastInitializeFailed;

    /// <inheritdoc />
    public bool LastInitializeFailed => _lastInitializeFailed;

    /// <summary>並列処理から安全にスキップ一覧へ追加する。</summary>
    private void AddSkipped(string path, string reason)
    {
        lock (_skippedLock) _skippedFiles.Add(new SkippedFileEntry(path, reason));
    }

    /// <inheritdoc />
    public IReadOnlyList<SkippedFileEntry> LastSkippedFiles => _skippedFiles;

    /// <summary>ファイルパス（ドキュメント ID 兼キー）。</summary>
    public const string FieldFilePath = "filepath";
    /// <summary>ファイル名。</summary>
    public const string FieldFileName = "filename";
    /// <summary>ファイル名（小文字・未分割）。部分一致・ワイルドカード検索用。</summary>
    public const string FieldFileNameLc = "filename_lc";
    /// <summary>親フォルダパス。</summary>
    public const string FieldFolderPath = "folderpath";
    /// <summary>抽出本文（検索対象）。</summary>
    public const string FieldContent = "content";
    /// <summary>先頭行プレビュー（フォルダ一覧表示用・短い文字列のみ格納）。</summary>
    public const string FieldContentPreview = "content_preview";
    /// <summary>ファイルサイズ（バイト）。</summary>
    public const string FieldFileSize = "filesize";
    /// <summary>最終更新（Ticks）。</summary>
    public const string FieldLastModified = "lastmodified";
    /// <summary>インデックス登録ロジックの版（差分更新で再インデックス判定に使用）。</summary>
    public const string FieldIndexVersion = "indexversion";
    /// <summary>現在のインデックス版。<see cref="FieldIndexVersion"/> と不一致のドキュメントは差分更新で再登録する。</summary>
    public const int CurrentIndexVersion = 2;
    /// <summary>完全一致検索の候補絞り込み用の文字バイグラム索引（本文＋ファイル名、非格納）。<see cref="ContentNGram"/> 参照。</summary>
    public const string FieldContentNGram = "content_ngram";

    private static readonly HashSet<string> DiffMetadataFields = new(StringComparer.Ordinal)
    {
        FieldFilePath,
        FieldLastModified,
        FieldIndexVersion
    };

    private const int ProgressReportInterval = 20;

    /// <summary>テキスト抽出に使うファクトリを注入する。</summary>
    public LuceneIndexService(TextExtractorFactory extractorFactory)
    {
        _extractorFactory = extractorFactory;
    }

    /// <summary>指定パスにインデックスを初期化する。既に同パスで開いていれば何もしない。</summary>
    public Task InitializeAsync(string indexPath, bool readOnly = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(indexPath)) return Task.CompletedTask;
        var normalizedPath = Path.GetFullPath(indexPath.Trim());

        lock (_lock)
        {
            _lastInitializeFailed = false;

            var currentPath = _directory?.Directory?.FullName;
            var samePath = currentPath != null &&
                string.Equals(currentPath, normalizedPath, StringComparison.OrdinalIgnoreCase);
            if (samePath && ((readOnly && _readOnly && _statsReader != null) || (!readOnly && _writer != null)))
                return Task.CompletedTask;

            CloseIndexResourcesLocked();

            try
            {
                if (!System.IO.Directory.Exists(normalizedPath))
                {
                    if (readOnly)
                    {
                        _lastInitializeFailed = true;
                        return Task.CompletedTask;
                    }
                    System.IO.Directory.CreateDirectory(normalizedPath);
                }

                _directory = FSDirectory.Open(normalizedPath);

                if (readOnly)
                {
                    _readOnly = true;
                    if (DirectoryReader.IndexExists(_directory))
                    {
                        try
                        {
                            _statsReader = DirectoryReader.Open(_directory);
                        }
                        catch (IOException)
                        {
                            _lastInitializeFailed = true;
                            CloseIndexResourcesLocked();
                        }
                    }
                    return Task.CompletedTask;
                }

                _readOnly = false;
                // Sudachi C モードのみ
                _analyzer = new SudachiAnalyzer();
                SudachiTokenizer.Warmup();

                var config = new IndexWriterConfig(AppLuceneVersion, _analyzer)
                {
                    OpenMode = OpenMode.CREATE_OR_APPEND,
                    RAMBufferSizeMB = 256  // 他アプリ共存: メモリ占有を抑える（512→256）
                };
                // マージは 1 スレッドに抑え、再構築中の CPU スパイク（ファン急回転）を防ぐ。
                var cms = new ConcurrentMergeScheduler();
                const int mergeThreads = 1;
                cms.SetMaxMergesAndThreads(mergeThreads + 1, mergeThreads);
                config.MergeScheduler = cms;

                _writer = new IndexWriter(_directory, config);
            }
            catch (Exception)
            {
                _lastInitializeFailed = true;
                CloseIndexResourcesLocked();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>開いているインデックスリソースをすべて解放する（<see cref="_lock"/> 内で呼ぶ）。</summary>
    private void CloseIndexResourcesLocked()
    {
        _statsReader?.Dispose();
        _statsReader = null;
        _writer?.Dispose();
        _writer = null;
        _analyzer?.Dispose();
        _analyzer = null;
        _directory?.Dispose();
        _directory = null;
        _readOnly = false;
    }

    /// <summary>ファイルリストを渡してインデックス（再構築時の重複列挙を避ける）。抽出・トークン化・登録を並列パイプラインで実行する。</summary>
    private async Task IndexFolderWithFilesAsync(string folderPath, IReadOnlyList<string> files, IProgress<IndexProgress>? progress, CancellationToken cancellationToken, int progressOffset = 0, int? progressTotalOverride = null)
    {
        var totalForProgress = progressTotalOverride ?? files.Count;
        var errorCount = await IndexFilesParallelAsync(files, progress, totalForProgress, progressOffset, cancellationToken).ConfigureAwait(false);

        progress?.Report(new IndexProgress
        {
            ProcessedFiles = progressOffset + files.Count,
            TotalFiles = totalForProgress,
            CurrentFile = null,
            ErrorCount = errorCount
        });

        // 高速化: 再構築中はフォルダごとに Commit せず、最後に 1 回だけコミット
        var skipCommit = _currentRebuildOptions != null;
        if (!skipCommit)
        {
            lock (_lock)
            {
                _writer!.Commit();
            }
        }
    }

    /// <summary>
    /// ファイル一覧を並列に「抽出 → トークン化 → 登録」する。スキップ件数を返す。Commit は呼ばない。
    /// ワーカースレッドは <see cref="ThreadPriority.BelowNormal"/> で他アプリを優先する。
    /// </summary>
    private async Task<int> IndexFilesParallelAsync(
        IReadOnlyList<string> files,
        IProgress<IndexProgress>? progress,
        int totalForProgress,
        int progressOffset,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0) return 0;
        var errorCount = 0;
        var processed = 0;
        var po = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = IndexerParallelism
        };
        try
        {
            await Parallel.ForEachAsync(files, po, async (path, token) =>
            {
                var prevPriority = Thread.CurrentThread.Priority;
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                try
                {
                    var result = await TryGetIndexedDocumentAsync(path, token).ConfigureAwait(false);
                    if (result.Document == null)
                    {
                        AddSkipped(path, result.SkipReason ?? IndexMessages.SkippedReasonExtractFailed);
                        Interlocked.Increment(ref errorCount);
                    }
                    else
                    {
                        try
                        {
                            _writer!.UpdateDocument(new Term(FieldFilePath, result.Document.FilePath), CreateLuceneDocument(result.Document));
                        }
                        catch (OperationCanceledException) { throw; }
                        catch
                        {
                            AddSkipped(path, IndexMessages.SkippedReasonIndexWriteFailed);
                            Interlocked.Increment(ref errorCount);
                        }
                    }
                    var done = Interlocked.Increment(ref processed);
                    ReportIndexProgress(progress, progressOffset + done, totalForProgress, path, Volatile.Read(ref errorCount));
                }
                finally
                {
                    Thread.CurrentThread.Priority = prevPriority;
                }
            }).ConfigureAwait(false);
        }
        catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException))
        {
            throw new OperationCanceledException(cancellationToken);
        }
        return errorCount;
    }

    /// <summary>インデックスを全削除してから、指定フォルダ群を再スキャンして全件登録する。</summary>
    public async Task RebuildIndexAsync(IEnumerable<string> folders, IProgress<IndexProgress>? progress = null, IndexRebuildOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _currentRebuildOptions = options;
        _skippedFiles.Clear();

        try
        {
            // キャンセル時の Rollback で確実に直前の安定状態へ戻すため、開始前にコミットしておく。
            lock (_lock)
            {
                _writer!.Commit();
                _writer!.DeleteAll();
            }

            var folderList = folders.Select(IndexPaths.NormalizeFolderPath).ToList();
            var folderFileLists = new List<(string folder, List<string> files)>(folderList.Count);
            var globalTotal = 0;
            foreach (var folder in folderList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!System.IO.Directory.Exists(folder)) continue;
                var files = new List<string>();
                foreach (var path in GetTargetFiles(folder))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    files.Add(path);
                }
                globalTotal += files.Count;
                folderFileLists.Add((folder, files));
            }

            var processedOffset = 0;
            foreach (var (folder, fileList) in folderFileLists)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await IndexFolderWithFilesAsync(folder, fileList, progress, cancellationToken, processedOffset, globalTotal);
                processedOffset += fileList.Count;
            }

            lock (_lock)
            {
                _writer!.Commit();
            }

            WriteSkippedLog();
        }
        catch (OperationCanceledException)
        {
            await RollbackAndReopenAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            await RollbackAndReopenAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _currentRebuildOptions = null;
        }
    }

    /// <summary>ディスクとインデックスを比較し、追加・更新・削除のみ反映する差分更新。</summary>
    public async Task UpdateIndexAsync(IEnumerable<string> folders, IProgress<IndexProgress>? progress = null, IndexRebuildOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        var folderList = folders.ToList();
        if (folderList.Count == 0) return;

        _currentRebuildOptions = options;
        _skippedFiles.Clear();
        try
        {
            lock (_lock)
            {
                _writer!.Commit();
            }

            var normalizedFolders = folderList.Select(IndexPaths.NormalizeFolderPath).ToList();

            if (!DirectoryReader.IndexExists(_directory!))
            {
                await RebuildIndexAsync(folders, progress, options, cancellationToken);
                return;
            }

            // インデックス済みの全ファイルを取得（フォルダフィルタなし）。
            // 以前は対象フォルダ配下のみを取得していたため、設定から外されたフォルダ配下の
            // インデックス済みファイルが削除対象として検出されず、古い情報が残り続けていた。
            var indexedMap = GetAllIndexedPathsAndLastModified();
            var diskFiles = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in normalizedFolders)
            {
                if (!System.IO.Directory.Exists(folder)) continue;
                foreach (var path in GetTargetFiles(folder))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(path);
                        diskFiles[IndexPaths.NormalizeFilePath(path)] = info.LastWriteTimeUtc.Ticks;
                    }
                    catch { /* スキップ */ }
                }
            }

            // 削除対象: (1) 設定から外れたフォルダ配下
            //          (2) ディスク上に存在しない
            //          (3) 拡張子フィルタ等でスキャン対象外になったもの
            // スキャン 0 件かつファイルがディスク上に残る場合は全削除せず中止する。
            var supportedExtensions = GetSupportedExtensionSet();
            var diff = IndexDiffPlanner.Plan(
                indexedMap,
                diskFiles,
                normalizedFolders,
                CurrentIndexVersion,
                isExcludedFromScan: path =>
                {
                    var ext = PreviewHelper.NormalizeExtension(Path.GetExtension(path));
                    return !supportedExtensions.Contains(ext);
                });
            if (diff.Aborted)
                throw new IndexUpdateAbortedException(diff.AbortReason!);

            var toDeleteStoredPaths = diff.ToDeleteStoredPaths;
            var toAddOrUpdate = diff.ToAddOrUpdatePaths;

            var total = toDeleteStoredPaths.Count + toAddOrUpdate.Count;
            var processed = 0;
            var errorCount = 0;

            if (total == 0)
            {
                progress?.Report(new IndexProgress
                {
                    ProcessedFiles = 0,
                    TotalFiles = 0,
                    CurrentFile = null,
                    ErrorCount = 0,
                    NoChanges = true
                });
            }

            // 追加・更新を先に行い、削除は最後（途中失敗時にインデックスが空になるのを防ぐ）
            errorCount += await IndexFilesParallelAsync(toAddOrUpdate, progress, total, processed, cancellationToken).ConfigureAwait(false);
            processed += toAddOrUpdate.Count;

            lock (_lock)
            {
                if (indexedMap.Count > 0
                    && toDeleteStoredPaths.Count >= indexedMap.Count
                    && _writer!.NumDocs <= toDeleteStoredPaths.Count)
                {
                    throw new IndexUpdateAbortedException(IndexMessages.DiffAbortedResultEmpty(indexedMap.Count));
                }

                foreach (var storedPath in toDeleteStoredPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _writer!.DeleteDocuments(new Term(FieldFilePath, storedPath));
                    processed++;
                    ReportIndexProgress(progress, processed, total, storedPath, errorCount);
                }
            }

            progress?.Report(new IndexProgress { ProcessedFiles = processed, TotalFiles = total, CurrentFile = null, ErrorCount = errorCount });
            lock (_lock)
            {
                if (indexedMap.Count > 0 && _writer!.NumDocs == 0)
                    throw new IndexUpdateAbortedException(IndexMessages.DiffAbortedResultEmpty(indexedMap.Count));
                _writer!.Commit();
            }

            WriteSkippedLog();
        }
        catch (OperationCanceledException)
        {
            await RollbackAndReopenAsync().ConfigureAwait(false);
            throw;
        }
        catch (IndexUpdateAbortedException)
        {
            await RollbackAndReopenAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            await RollbackAndReopenAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _currentRebuildOptions = null;
        }
    }

    /// <summary>
    /// インデックス内の全ファイルのパスと最終更新日時（Ticks）・版を取得する。
    /// キーは正規化済みフルパス。削除時は <see cref="IndexDiffPlanner.IndexedFileEntry.StoredPath"/>（インデックス保存値）を Term に使う。
    /// Writer が開いたままのため DirectoryReader.Open(writer) を使用（Open(directory) はロック競合で失敗する場合がある）。
    /// </summary>
    private Dictionary<string, IndexDiffPlanner.IndexedFileEntry> GetAllIndexedPathsAndLastModified()
    {
        var result = new Dictionary<string, IndexDiffPlanner.IndexedFileEntry>(StringComparer.OrdinalIgnoreCase);
        if (_writer == null) return result;

        DirectoryReader? reader = null;
        try
        {
            try
            {
                reader = DirectoryReader.Open(_writer, applyAllDeletes: true);
            }
            catch (Exception)
            {
                if (_directory != null)
                    reader = DirectoryReader.Open(_directory);
            }
            if (reader == null) return result;
            var searcher = new IndexSearcher(reader);
            var topDocs = searcher.Search(new MatchAllDocsQuery(), reader.NumDocs);
            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = reader.Document(scoreDoc.Doc, DiffMetadataFields);
                var storedPath = doc.Get(FieldFilePath);
                if (string.IsNullOrEmpty(storedPath)) continue;
                var normalizedPath = IndexPaths.NormalizeFilePath(storedPath);
                if (!long.TryParse(doc.Get(FieldLastModified), out var ticks))
                    ticks = 0;
                if (!int.TryParse(doc.Get(FieldIndexVersion), out var version))
                    version = 0;
                result[normalizedPath] = new IndexDiffPlanner.IndexedFileEntry(storedPath, ticks, version);
            }
        }
        finally
        {
            reader?.Dispose();
        }
        return result;
    }

    /// <summary>
    /// 未コミットの変更を破棄して IndexWriter を再オープンする。
    /// インデックス更新（差分・全体）の途中でキャンセルされた場合に、直前のコミット済み状態へ戻すために使用する。
    /// IndexWriter.Rollback はライターを閉じるため、その後 Initialize で再構築する。
    /// </summary>
    private async Task RollbackAndReopenAsync()
    {
        string? indexPath;
        lock (_lock)
        {
            indexPath = _directory?.Directory?.FullName;
            if (_writer != null)
            {
                try { _writer.Rollback(); }
                catch { /* Rollback 失敗時もリソースは解放する */ }
                try { _writer.Dispose(); } catch { /* Rollback 後は既に閉じている */ }
                _writer = null;
            }
            _analyzer?.Dispose();
            _analyzer = null;
            _directory?.Dispose();
            _directory = null;
        }
        if (!string.IsNullOrWhiteSpace(indexPath))
        {
            await InitializeAsync(indexPath!, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>登録件数・概算サイズなどを返す（簡易統計）。</summary>
    public IndexStats GetStats()
    {
        lock (_lock)
        {
            if (_writer != null)
                return new IndexStats { DocumentCount = _writer.NumDocs };
            if (_statsReader != null)
                return new IndexStats { DocumentCount = _statsReader.NumDocs };
            return new IndexStats { DocumentCount = 0 };
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchResultItem> ListIndexedItems(
        IReadOnlyList<string> targetFolders,
        IReadOnlySet<string>? targetExtensions = null)
    {
        if (targetFolders == null || targetFolders.Count == 0)
            return [];

        DirectoryReader? reader = null;
        var disposeReader = false;
        try
        {
            lock (_lock)
            {
                if (_writer != null)
                {
                    reader = DirectoryReader.Open(_writer, applyAllDeletes: true);
                    disposeReader = true;
                }
                else if (_statsReader != null)
                {
                    reader = _statsReader;
                }
            }

            if (reader == null)
                return [];

            var normalizedFolders = targetFolders
                .Select(IndexPaths.NormalizeFolderPath)
                .ToList();

            var fields = new HashSet<string>(StringComparer.Ordinal)
            {
                FieldFilePath,
                FieldFileName,
                FieldFolderPath,
                FieldFileSize,
                FieldLastModified
            };

            var searcher = new IndexSearcher(reader);
            var topDocs = searcher.Search(new MatchAllDocsQuery(), reader.NumDocs);
            var items = new List<SearchResultItem>(topDocs.ScoreDocs.Length);

            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = reader.Document(scoreDoc.Doc, fields);
                var filePath = doc.Get(FieldFilePath);
                if (string.IsNullOrWhiteSpace(filePath))
                    continue;

                if (!IndexPaths.IsPathUnderAnyFolder(filePath, normalizedFolders))
                    continue;

                if (targetExtensions is { Count: > 0 })
                {
                    var ext = PreviewHelper.NormalizeExtension(Path.GetExtension(filePath));
                    if (string.IsNullOrEmpty(ext) || !targetExtensions.Contains(ext))
                        continue;
                }

                var fileName = doc.Get(FieldFileName);
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = Path.GetFileName(filePath);

                var folderPath = doc.Get(FieldFolderPath);
                if (string.IsNullOrWhiteSpace(folderPath))
                    folderPath = Path.GetDirectoryName(filePath) ?? "";

                long.TryParse(doc.Get(FieldFileSize), out var fileSize);
                long.TryParse(doc.Get(FieldLastModified), out var ticks);
                var lastModified = ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;

                items.Add(new SearchResultItem
                {
                    FilePath = filePath,
                    FileName = fileName,
                    FolderPath = folderPath,
                    FileSize = fileSize,
                    LastModified = lastModified
                });
            }

            return items;
        }
        catch
        {
            return [];
        }
        finally
        {
            if (disposeReader)
                reader?.Dispose();
        }
    }

    /// <summary>
    /// ファイルからインデックス用ドキュメントを取得する。抽出器がない場合は空本文でインデックス（ファイル名・パス検索用）。
    /// サイズ超過・抽出エラー時は <see cref="IndexDocumentResult.SkipReason"/> を設定する。
    /// </summary>
    private async Task<IndexDocumentResult> TryGetIndexedDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        filePath = IndexPaths.NormalizeFilePath(filePath);
        try
        {
            if (!File.Exists(filePath))
                return IndexDocumentResult.Skipped(IndexMessages.SkippedReasonFileNotFound);

            var fileInfo = new FileInfo(filePath);
            if (ContentLimits.ExceedsIndexTextExtractionFileSizeLimit(fileInfo.Length))
            {
                return IndexDocumentResult.Skipped(
                    IndexMessages.SkippedReasonFileTooLarge(fileInfo.Length));
            }

            var extension = fileInfo.Extension.ToLowerInvariant();
            var extractor = _extractorFactory.GetExtractor(extension);
            string content;
            if (extractor != null)
            {
                try
                {
                    content = await extractor.ExtractTextAsync(filePath, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    return IndexDocumentResult.Skipped(IndexMessages.SkippedReasonExtractFailed);
                }
            }
            else
            {
                content = string.Empty;
            }

            return IndexDocumentResult.Ok(new IndexedDocument
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                FolderPath = fileInfo.DirectoryName ?? string.Empty,
                Content = content,
                FileSize = fileInfo.Length,
                LastModified = fileInfo.LastWriteTimeUtc
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return IndexDocumentResult.Skipped(IndexMessages.SkippedReasonAccessDenied);
        }
        catch (IOException)
        {
            return IndexDocumentResult.Skipped(IndexMessages.SkippedReasonExtractFailed);
        }
        catch
        {
            return IndexDocumentResult.Skipped(IndexMessages.SkippedReasonExtractFailed);
        }
    }

    private readonly record struct IndexDocumentResult(IndexedDocument? Document, string? SkipReason)
    {
        public static IndexDocumentResult Ok(IndexedDocument doc) => new(doc, null);
        public static IndexDocumentResult Skipped(string reason) => new(null, reason);
    }

    /// <summary>対象拡張子に合致するファイルを再帰列挙（Office ロックファイル・一部システムフォルダは除外）。</summary>
    private IEnumerable<string> GetTargetFiles(string folderPath) =>
        SafeEnumerateFiles(folderPath, GetSupportedExtensionSet());

    /// <summary>現在の再構築オプションに基づく対象拡張子集合。</summary>
    private HashSet<string> GetSupportedExtensionSet() =>
        PreviewHelper.BuildTargetExtensionSet(
            _extractorFactory.GetAllSupportedExtensions(),
            _currentRebuildOptions?.TargetExtensions);

    /// <summary>ファイル／ディレクトリ列挙の例外を握りつぶし、失敗時は null。</summary>
    private static IEnumerable<string>? TryEnumerateOrNull(Func<IEnumerable<string>> enumerate)
    {
        try
        {
            return enumerate();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// アクセス権限エラーをスキップしながらファイルを列挙
    /// </summary>
    private static IEnumerable<string> SafeEnumerateFiles(string folderPath, HashSet<string> supportedExtensions)
    {
        var directories = new Stack<string>();
        directories.Push(folderPath);

        while (directories.Count > 0)
        {
            var currentDir = directories.Pop();

            var files = TryEnumerateOrNull(() => System.IO.Directory.EnumerateFiles(currentDir));
            if (files == null)
                continue;

            foreach (var file in files)
            {
                // Office の一時/ロックファイル（例: ~$document.docx）はインデックス対象にしない
                // （スキップ件数や skipped_files.log にも載せないため、列挙段階で除外する）
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith("~$", StringComparison.Ordinal))
                    continue;

                var ext = PreviewHelper.NormalizeExtension(Path.GetExtension(file));
                if (string.IsNullOrEmpty(ext)) continue;
                if (!supportedExtensions.Contains(ext))
                    continue;
                yield return file;
            }

            var subdirs = TryEnumerateOrNull(() => System.IO.Directory.EnumerateDirectories(currentDir));
            if (subdirs == null)
                continue;

            foreach (var subdir in subdirs)
            {
                // システムフォルダをスキップ
                var dirName = Path.GetFileName(subdir);
                if (ShouldSkipDirectory(dirName))
                {
                    continue;
                }

                directories.Push(subdir);
            }
        }
    }

    /// <summary>インデックス走査から除外するディレクトリ名（大文字小文字無視）。</summary>
    private static bool ShouldSkipDirectory(string dirName) =>
        dirName.StartsWith('$') ||
        dirName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("ProgramData", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("__pycache__", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals(".venv", StringComparison.OrdinalIgnoreCase);

    /// <summary><see cref="IndexedDocument"/> を Lucene の <see cref="Document"/> に変換する。</summary>
    private static Document CreateLuceneDocument(IndexedDocument doc)
    {
        var content = doc.Content;
        var contentPreview = ContentPreviewHelper.ExtractFirstLine(content);

        return new Document
        {
            new StringField(FieldFilePath, doc.FilePath, Field.Store.YES),
            new TextField(FieldFileName, doc.FileName, Field.Store.YES),
            new StringField(FieldFileNameLc, doc.FileName.ToLowerInvariant(), Field.Store.NO),
            new StringField(FieldFolderPath, doc.FolderPath, Field.Store.YES),
            new TextField(FieldContent, content, Field.Store.YES),
            new StringField(FieldContentPreview, contentPreview, Field.Store.YES),
            // 完全一致検索の候補絞り込み用バイグラム（事前生成したトークン列をそのまま索引、本文の Sudachi 解析とは独立）
            new TextField(FieldContentNGram, new ListTokenStream(ContentNGram.BuildIndexTokens(content, doc.FileName))),
            new Int64Field(FieldFileSize, doc.FileSize, Field.Store.YES),
            new Int64Field(FieldLastModified, doc.LastModified.Ticks, Field.Store.YES),
            new Int32Field(FieldIndexVersion, CurrentIndexVersion, Field.Store.YES)
        };
    }

    /// <summary>スキップ一覧をインデックスフォルダ直下のログファイルに書き出す。</summary>
    private void WriteSkippedLog()
    {
        if (_skippedFiles.Count == 0 || _directory == null)
            return;
        try
        {
            var logPath = Path.Combine(_directory.Directory.FullName, DefaultPaths.SkippedFilesLogFileName);
            var lines = new List<string>(_skippedFiles.Count + 4)
            {
                IndexMessages.SkippedLogHeaderLine(DateTime.Now),
                IndexMessages.SkippedLogTotalLine(_skippedFiles.Count),
                IndexMessages.SkippedLogFormatHint,
                ""
            };
            lines.AddRange(_skippedFiles.Select(e => IndexMessages.SkippedLogLine(e.Path, e.Reason)));
            File.WriteAllLines(logPath, lines, System.Text.Encoding.UTF8);
        }
        catch
        {
            // ログ書き込み失敗は無視（インデックス本体は完了済み）
        }
    }

    /// <summary>ライター未初期化なら例外（呼び出し前条件のガード）。</summary>
    private void EnsureInitialized()
    {
        if (_writer == null)
        {
            throw new InvalidOperationException("IndexService has not been initialized. Call InitializeAsync first.");
        }
    }

    /// <summary>IndexWriter・Analyzer・ディレクトリを解放する。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            CloseIndexResourcesLocked();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static void ReportIndexProgress(
        IProgress<IndexProgress>? progress,
        int processed,
        int total,
        string? currentFile,
        int errorCount)
    {
        if (progress == null || total <= 0)
            return;
        if (processed % ProgressReportInterval != 0 && processed != total)
            return;

        progress.Report(new IndexProgress
        {
            ProcessedFiles = processed,
            TotalFiles = total,
            CurrentFile = currentFile,
            ErrorCount = errorCount
        });
    }
}


