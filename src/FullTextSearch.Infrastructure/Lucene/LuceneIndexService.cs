// Lucene.NET によるインデックス作成・更新・削除。Sudachi 形態素解析（モード C）でトークナイズ。
using System.Collections.Concurrent;
using System.Threading;
using FullTextSearch.Core;
using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
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
    /// 抽出・登録パイプラインの並列度。他アプリと共存するため Sudachi プール＋抽出用に抑え、論理コアの半分を超えない。
    /// （Sudachi 2 + 抽出 2 ≒ 最大 4 スレッド＝13 世代 i5 で約 4 コア相当。残りは OS／他アプリ用）
    /// </summary>
    private static readonly int IndexerParallelism = Math.Min(
        SudachiTokenizer.PoolSize + 2,
        Math.Max(2, Environment.ProcessorCount / 2));

    private readonly TextExtractorFactory _extractorFactory;
    private FSDirectory? _directory;
    private IndexWriter? _writer;
    private Analyzer? _analyzer;
    /// <summary>初期化／破棄／ロールバック等のライフサイクル境界を保護するロック。書込（UpdateDocument 等）はスレッドセーフな IndexWriter に任せて取らない。</summary>
    private readonly object _lock = new();
    /// <summary><see cref="_skippedFiles"/> への並列追加を保護する。</summary>
    private readonly object _skippedLock = new();
    private bool _disposed;
    private IndexRebuildOptions? _currentRebuildOptions;
    private readonly List<string> _skippedFiles = new();

    /// <summary>並列処理から安全にスキップ一覧へ追加する。</summary>
    private void AddSkipped(string path)
    {
        lock (_skippedLock) _skippedFiles.Add(path);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> LastSkippedFiles => _skippedFiles;

    /// <summary>ファイルパス（ドキュメント ID 兼キー）。</summary>
    public const string FieldFilePath = "filepath";
    /// <summary>ファイル名。</summary>
    public const string FieldFileName = "filename";
    /// <summary>親フォルダパス。</summary>
    public const string FieldFolderPath = "folderpath";
    /// <summary>抽出本文（検索対象）。</summary>
    public const string FieldContent = "content";
    /// <summary>ファイルサイズ（バイト）。</summary>
    public const string FieldFileSize = "filesize";
    /// <summary>最終更新（Ticks）。</summary>
    public const string FieldLastModified = "lastmodified";
    /// <summary>種別表示名（日本語ラベル）。</summary>
    public const string FieldFileType = "filetype";
    /// <summary>完全一致検索の候補絞り込み用の文字バイグラム索引（本文＋ファイル名、非格納）。<see cref="ContentNGram"/> 参照。</summary>
    public const string FieldContentNGram = "content_ngram";

    /// <summary>テキスト抽出に使うファクトリを注入する。</summary>
    public LuceneIndexService(TextExtractorFactory extractorFactory)
    {
        _extractorFactory = extractorFactory;
    }

    /// <summary>指定パスにインデックスを初期化する。既に同パスで開いていれば何もしない。</summary>
    public Task InitializeAsync(string indexPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(indexPath)) return Task.CompletedTask;
        var normalizedPath = Path.GetFullPath(indexPath.Trim());

        lock (_lock)
        {
            var currentPath = _directory?.Directory?.FullName;
            if (_writer != null && currentPath != null &&
                string.Equals(currentPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            if (_writer != null)
            {
                _writer.Dispose();
                _analyzer?.Dispose();
                _directory?.Dispose();
                _writer = null;
                _analyzer = null;
                _directory = null;
            }

            if (!System.IO.Directory.Exists(normalizedPath))
            {
                System.IO.Directory.CreateDirectory(normalizedPath);
            }

            _directory = FSDirectory.Open(normalizedPath);

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

        return Task.CompletedTask;
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
                    var doc = await TryGetIndexedDocumentAsync(path, token).ConfigureAwait(false);
                    if (doc == null)
                    {
                        AddSkipped(path);
                        Interlocked.Increment(ref errorCount);
                    }
                    else
                    {
                        try
                        {
                            _writer!.UpdateDocument(new Term(FieldFilePath, doc.FilePath), CreateLuceneDocument(doc));
                        }
                        catch (OperationCanceledException) { throw; }
                        catch
                        {
                            AddSkipped(path);
                            Interlocked.Increment(ref errorCount);
                        }
                    }
                    var done = Interlocked.Increment(ref processed);
                    progress?.Report(new IndexProgress
                    {
                        ProcessedFiles = progressOffset + done,
                        TotalFiles = totalForProgress,
                        CurrentFile = path,
                        ErrorCount = Volatile.Read(ref errorCount)
                    });
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

    /// <summary>C: や C:\ をドライブルート C:\ に正規化する。GetFullPath("C:") はカレントディレクトリを返すため、ルート指定が 0 件になるのを防ぐ。</summary>
    private static string NormalizeFolderPath(string folder)
    {
        var s = folder.TrimEnd('\\', '/').Trim();
        if (s.Length == 2 && char.IsLetter(s[0]) && s[1] == ':')
            return s + "\\";
        if (s.Length == 1 && char.IsLetter(s[0]))
            return s + ":\\";
        return Path.GetFullPath(s);
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

            var folderList = folders.Select(NormalizeFolderPath).ToList();
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
            // 中断時は未コミットの変更（DeleteAll 直後の状態など）を破棄して、直前のコミット状態へ戻す。
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

            var normalizedFolders = folderList.Select(NormalizeFolderPath).ToList();

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
                        diskFiles[path] = info.LastWriteTimeUtc.Ticks;
                    }
                    catch { /* スキップ */ }
                }
            }

            // 削除対象: (1) 現在の対象フォルダ配下に無いインデックス済みファイル
            //            （= 設定からフォルダが外された／対象拡張子が変更された等で対象外になったもの）
            //          (2) 対象フォルダ配下にあるが、ディスク上に存在しないファイル
            var toDelete = indexedMap.Keys
                .Where(path => !IsPathUnderAnyFolder(path, normalizedFolders) || !diskFiles.ContainsKey(path))
                .ToList();
            var toAddOrUpdate = diskFiles.Keys
                .Where(path => !indexedMap.TryGetValue(path, out var ticks) || ticks != diskFiles[path])
                .ToList();

            var total = toDelete.Count + toAddOrUpdate.Count;
            var processed = 0;
            var errorCount = 0;

            lock (_lock)
            {
                foreach (var path in toDelete)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _writer!.DeleteDocuments(new Term(FieldFilePath, path));
                    processed++;
                    progress?.Report(new IndexProgress { ProcessedFiles = processed, TotalFiles = total, CurrentFile = path, ErrorCount = errorCount });
                }
            }

            errorCount += await IndexFilesParallelAsync(toAddOrUpdate, progress, total, processed, cancellationToken).ConfigureAwait(false);
            processed += toAddOrUpdate.Count;

            progress?.Report(new IndexProgress { ProcessedFiles = processed, TotalFiles = total, CurrentFile = null, ErrorCount = errorCount });
            lock (_lock)
            {
                _writer!.Commit();
            }

            WriteSkippedLog();
        }
        catch (OperationCanceledException)
        {
            // 中断時は未コミットの変更を破棄して、開始時のコミット済み状態へ戻す。
            // これにより、差分更新を途中でキャンセルしてもインデックスファイルが
            // 中途半端な状態にならず、改めて差分更新を実行すれば再開できる。
            await RollbackAndReopenAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _currentRebuildOptions = null;
        }
    }

    /// <summary>
    /// インデックス内の全ファイルのパスと最終更新日時（Ticks）を取得する。
    /// Writer が開いたままのため DirectoryReader.Open(writer) を使用（Open(directory) はロック競合で失敗する場合がある）。
    /// </summary>
    private Dictionary<string, long> GetAllIndexedPathsAndLastModified()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
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
                var doc = reader.Document(scoreDoc.Doc);
                var path = doc.Get(FieldFilePath);
                var lastModStr = doc.Get(FieldLastModified);
                if (string.IsNullOrEmpty(path)) continue;
                if (long.TryParse(lastModStr, out var ticks))
                    result[path] = ticks;
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
            await InitializeAsync(indexPath!, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>ファイルパスが、正規化済みフォルダ一覧のいずれかの配下（または同一）か。</summary>
    private static bool IsPathUnderAnyFolder(string filePath, List<string> normalizedFolderPaths)
    {
        var full = Path.GetFullPath(filePath);
        foreach (var folder in normalizedFolderPaths)
        {
            if (full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase)
                || full.Equals(folder, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>登録件数・概算サイズなどを返す（簡易統計）。</summary>
    public IndexStats GetStats()
    {
        EnsureInitialized();

        lock (_lock)
        {
            return new IndexStats { DocumentCount = _writer!.NumDocs };
        }
    }

    /// <summary>
    /// ファイルからインデックス用ドキュメントを取得する。抽出器がない場合は空本文でインデックス（ファイル名・パス検索用）。
    /// サイズ超過・抽出エラー時は null を返しスキップ対象とする。
    /// </summary>
    private async Task<IndexedDocument?> TryGetIndexedDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (ContentLimits.ExceedsIndexTextExtractionFileSizeLimit(fileInfo.Length))
                return null;

            var extension = fileInfo.Extension.ToLowerInvariant();
            var extractor = _extractorFactory.GetExtractor(extension);
            var content = extractor != null
                ? await extractor.ExtractTextAsync(filePath, cancellationToken)
                : string.Empty;

            return new IndexedDocument
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                FolderPath = fileInfo.DirectoryName ?? string.Empty,
                Content = content,
                FileSize = fileInfo.Length,
                LastModified = fileInfo.LastWriteTimeUtc,
                FileType = GetFileType(extension)
            };
        }
        catch (OperationCanceledException)
        {
            // キャンセルはスキップではなく上位へ伝播させ、即座に処理を打ち切る。
            throw;
        }
        catch
        {
            return null; // エラーになったらそのファイルを飛ばして次へ
        }
    }

    /// <summary>対象拡張子に合致するファイルを再帰列挙（Office ロックファイル・一部システムフォルダは除外）。</summary>
    private IEnumerable<string> GetTargetFiles(string folderPath)
    {
        HashSet<string> supportedExtensions;
        if (_currentRebuildOptions?.TargetExtensions != null && _currentRebuildOptions.TargetExtensions.Count > 0)
        {
            // ユーザーが設定した拡張子を「.」+ 小文字に正規化して使用
            supportedExtensions = _currentRebuildOptions.TargetExtensions
                .Select(PreviewHelper.NormalizeExtension)
                .Where(e => !string.IsNullOrEmpty(e))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var extractorSupported = _extractorFactory.GetAllSupportedExtensions();
            supportedExtensions = extractorSupported.Select(PreviewHelper.NormalizeExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return SafeEnumerateFiles(folderPath, supportedExtensions);
    }

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

                var ext = Path.GetExtension(file);
                if (string.IsNullOrEmpty(ext)) continue;
                if (!supportedExtensions.Contains(ext.ToLowerInvariant()))
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
                if (dirName.StartsWith("$") || 
                    dirName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("ProgramData", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                directories.Push(subdir);
            }
        }
    }

    /// <summary><see cref="IndexedDocument"/> を Lucene の <see cref="Document"/> に変換する（本文は長さ上限で切り詰め）。</summary>
    private static Document CreateLuceneDocument(IndexedDocument doc)
    {
        var content = doc.Content.Length > ContentLimits.IndexMaxContentChars
            ? doc.Content.Substring(0, ContentLimits.IndexMaxContentChars)
            : doc.Content;

        return new Document
        {
            new StringField(FieldFilePath, doc.FilePath, Field.Store.YES),
            new TextField(FieldFileName, doc.FileName, Field.Store.YES),
            new StringField(FieldFolderPath, doc.FolderPath, Field.Store.YES),
            new TextField(FieldContent, content, Field.Store.YES),
            // 完全一致検索の候補絞り込み用バイグラム（事前生成したトークン列をそのまま索引、本文の Sudachi 解析とは独立）
            new TextField(FieldContentNGram, new ListTokenStream(ContentNGram.BuildIndexTokens(content, doc.FileName))),
            new Int64Field(FieldFileSize, doc.FileSize, Field.Store.YES),
            new Int64Field(FieldLastModified, doc.LastModified.Ticks, Field.Store.YES),
            new StringField(FieldFileType, doc.FileType, Field.Store.YES)
        };
    }

    /// <summary>拡張子から Lucene の filetype フィールド用ラベルを返す。</summary>
    private static string GetFileType(string extension) => IndexMessages.GetFileTypeDisplayName(extension);

    /// <summary>スキップ一覧をインデックスフォルダ直下のログファイルに書き出す。</summary>
    private void WriteSkippedLog()
    {
        if (_skippedFiles.Count == 0 || _directory == null)
            return;
        try
        {
            var logPath = Path.Combine(_directory.Directory.FullName, DefaultPaths.SkippedFilesLogFileName);
            var lines = new List<string>(_skippedFiles.Count + 3)
            {
                IndexMessages.SkippedLogHeaderLine(DateTime.Now),
                IndexMessages.SkippedLogTotalLine(_skippedFiles.Count),
                ""
            };
            lines.AddRange(_skippedFiles);
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
            _writer?.Dispose();
            _analyzer?.Dispose();
            _directory?.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}


