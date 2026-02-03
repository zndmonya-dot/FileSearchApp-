// Lucene.NET によるインデックス作成・更新・削除。Sudachi 形態素解析（モード C）でトークナイズ。
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
    /// <summary>並列テキスト抽出数（I/O 飽和を狙った値）</summary>
    private const int ParallelExtractCount = 48;

    private readonly TextExtractorFactory _extractorFactory;
    private FSDirectory? _directory;
    private IndexWriter? _writer;
    private Analyzer? _analyzer;
    private readonly object _lock = new();
    private bool _disposed;
    private IndexRebuildOptions? _currentRebuildOptions;

    /// <summary>Lucene ドキュメントのフィールド名（変更すると既存インデックスと非互換）</summary>
    public const string FieldFilePath = "filepath";
    public const string FieldFileName = "filename";
    public const string FieldFolderPath = "folderpath";
    public const string FieldContent = "content";
    public const string FieldFileSize = "filesize";
    public const string FieldLastModified = "lastmodified";
    public const string FieldFileType = "filetype";
    public const string FieldIndexedAt = "indexedat";

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

            // ディレクトリが存在しない場合は作成
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
                RAMBufferSizeMB = 512  // 高速化: メモリに溜めてからフラッシュ
            };

            _writer = new IndexWriter(_directory, config);
        }

        return Task.CompletedTask;
    }

    public Task IndexDocumentAsync(IndexedDocument document, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var doc = CreateLuceneDocument(document);

        lock (_lock)
        {
            // 既存のドキュメントを削除してから追加（更新）
            _writer!.UpdateDocument(new Term(FieldFilePath, document.FilePath), doc);
            _writer.Commit();
        }

        return Task.CompletedTask;
    }

    public Task DeleteDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        lock (_lock)
        {
            _writer!.DeleteDocuments(new Term(FieldFilePath, filePath));
            _writer.Commit();
        }

        return Task.CompletedTask;
    }

    /// <param name="progressOffset">再構築時など、進捗を累積表示するためのオフセット（ProcessedFiles に加算）</param>
    /// <param name="progressTotalOverride">再構築時など、進捗の総数に使う値（未指定時はこのフォルダのファイル数のみ）</param>
    public async Task IndexFolderAsync(string folderPath, IProgress<IndexProgress>? progress = null, CancellationToken cancellationToken = default, int progressOffset = 0, int? progressTotalOverride = null)
    {
        EnsureInitialized();
        var files = new List<string>();
        foreach (var filePath in GetTargetFiles(folderPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(filePath);
        }
        await IndexFolderWithFilesAsync(folderPath, files, progress, cancellationToken, progressOffset, progressTotalOverride);
    }

    /// <summary>ファイルリストを渡してインデックス（再構築時の重複列挙を避ける）</summary>
    private async Task IndexFolderWithFilesAsync(string folderPath, IReadOnlyList<string> files, IProgress<IndexProgress>? progress, CancellationToken cancellationToken, int progressOffset = 0, int? progressTotalOverride = null)
    {
        var processedFiles = 0;
        var errorCount = 0;
        var totalFiles = files.Count;
        var totalForProgress = progressTotalOverride ?? totalFiles;

        for (var i = 0; i < files.Count; i += ParallelExtractCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = files.Skip(i).Take(ParallelExtractCount).ToList();
            var tasks = chunk.Select(p => TryGetIndexedDocumentAsync(p, cancellationToken)).ToArray();
            IndexedDocument?[] docs;
            try
            {
                docs = await Task.WhenAll(tasks);
            }
            catch
            {
                errorCount += chunk.Count;
                docs = new IndexedDocument?[chunk.Count];
            }
            errorCount += docs.Count(d => d == null);
            foreach (var path in chunk)
            {
                progress?.Report(new IndexProgress
                {
                    ProcessedFiles = progressOffset + processedFiles,
                    TotalFiles = totalForProgress,
                    CurrentFile = path,
                    ErrorCount = errorCount
                });
                processedFiles++;
            }
            var toAdd = new List<IndexedDocument>(docs.Length);
            foreach (var d in docs)
                if (d != null) toAdd.Add(d);
            if (toAdd.Count > 0)
                AddDocumentsToWriterWithoutCommit(toAdd);
        }

        progress?.Report(new IndexProgress
        {
            ProcessedFiles = progressOffset + processedFiles,
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

    public async Task RebuildIndexAsync(IEnumerable<string> folders, IProgress<IndexProgress>? progress = null, IndexRebuildOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _currentRebuildOptions = options;

        try
        {
            lock (_lock)
            {
                _writer!.DeleteAll();
            }

            var folderList = folders.ToList();
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
        }
        finally
        {
            _currentRebuildOptions = null;
        }
    }

    public async Task UpdateIndexAsync(IEnumerable<string> folders, IProgress<IndexProgress>? progress = null, IndexRebuildOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        var folderList = folders.ToList();
        if (folderList.Count == 0) return;

        _currentRebuildOptions = options;
        try
        {
            lock (_lock)
            {
                _writer!.Commit();
            }

            var normalizedFolders = folderList.Select(f => Path.GetFullPath(f.TrimEnd('\\', '/'))).ToList();

            if (!DirectoryReader.IndexExists(_directory!))
            {
                await RebuildIndexAsync(folders, progress, options, cancellationToken);
                return;
            }

            var indexedMap = GetIndexedPathsAndLastModified(normalizedFolders);
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

            var toDelete = indexedMap.Keys
                .Where(path => IsPathUnderAnyFolder(path, normalizedFolders) && !diskFiles.ContainsKey(path))
                .ToList();
            var toAddOrUpdate = diskFiles.Keys
                .Where(path => !indexedMap.TryGetValue(path, out var ticks) || ticks != diskFiles[path])
                .ToList();

            var total = toDelete.Count + toAddOrUpdate.Count;
            var processed = 0;

            lock (_lock)
            {
                foreach (var path in toDelete)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _writer!.DeleteDocuments(new Term(FieldFilePath, path));
                    processed++;
                    progress?.Report(new IndexProgress { ProcessedFiles = processed, TotalFiles = total, CurrentFile = path, ErrorCount = 0 });
                }
            }

            // 追加・更新を並列チャンクで処理（IndexFolderAsync と同様）
            for (var i = 0; i < toAddOrUpdate.Count; i += ParallelExtractCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = toAddOrUpdate.Skip(i).Take(ParallelExtractCount).ToList();
                IndexedDocument?[] docs;
                try
                {
                    var tasks = chunk.Select(p => TryGetIndexedDocumentAsync(p, cancellationToken)).ToArray();
                    docs = await Task.WhenAll(tasks);
                }
                catch
                {
                    docs = new IndexedDocument?[chunk.Count];
                }
                foreach (var path in chunk)
                {
                    processed++;
                    progress?.Report(new IndexProgress { ProcessedFiles = processed, TotalFiles = total, CurrentFile = path, ErrorCount = 0 });
                }
                var toAdd = new List<IndexedDocument>(docs.Length);
                foreach (var d in docs)
                    if (d != null) toAdd.Add(d);
                if (toAdd.Count > 0)
                    AddDocumentsToWriterWithoutCommit(toAdd);
            }

            progress?.Report(new IndexProgress { ProcessedFiles = processed, TotalFiles = total, CurrentFile = null, ErrorCount = 0 });
            lock (_lock)
            {
                _writer!.Commit();
            }
        }
        finally
        {
            _currentRebuildOptions = null;
        }
    }

    /// <summary>
    /// インデックス内のパスと最終更新日時（Ticks）を取得。指定フォルダ配下のみ。
    /// Writer が開いたままのため DirectoryReader.Open(writer) を使用（Open(directory) はロック競合で失敗する場合がある）。
    /// </summary>
    private Dictionary<string, long> GetIndexedPathsAndLastModified(List<string> normalizedFolderPaths)
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
                if (!IsPathUnderAnyFolder(path, normalizedFolderPaths)) continue;
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

    public IndexStats GetStats()
    {
        EnsureInitialized();

        lock (_lock)
        {
            var docCount = _writer!.NumDocs;
            var indexSize = _directory!.ListAll()
                .Sum(f => new FileInfo(Path.Combine(_directory.Directory.FullName, f)).Length);

            return new IndexStats
            {
                DocumentCount = docCount,
                LastUpdated = DateTime.UtcNow,
                IndexSizeBytes = indexSize
            };
        }
    }

    public Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        lock (_lock)
        {
            _writer!.ForceMerge(1);
            _writer.Commit();
        }

        return Task.CompletedTask;
    }

    private async Task IndexFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var document = await TryGetIndexedDocumentAsync(filePath, cancellationToken);
        if (document != null)
            await IndexDocumentAsync(document, cancellationToken);
    }

    /// <summary>
    /// ファイルからインデックス用ドキュメントを取得する。抽出器がない場合は空本文でインデックス（ファイル名・パス検索用）。エラー時は null。
    /// </summary>
    private async Task<IndexedDocument?> TryGetIndexedDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var extension = fileInfo.Extension.ToLowerInvariant();
        var extractor = _extractorFactory.GetExtractor(extension);

        string content;
        if (extractor != null)
            content = await extractor.ExtractTextAsync(filePath, cancellationToken);
        else
            content = string.Empty; // 抽出器非対応拡張子は空本文でインデックス（ファイル名・パスで検索可能にする）

        return new IndexedDocument
        {
            FilePath = filePath,
            FileName = fileInfo.Name,
            FolderPath = fileInfo.DirectoryName ?? string.Empty,
            Content = content,
            FileSize = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            FileType = GetFileType(extension),
            IndexedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// ライターにドキュメントを追加するのみ。Commit は呼ばない（単体用）。
    /// </summary>
    private void AddDocumentToWriterWithoutCommit(IndexedDocument document)
    {
        var doc = CreateLuceneDocument(document);
        lock (_lock)
        {
            _writer!.UpdateDocument(new Term(FieldFilePath, document.FilePath), doc);
        }
    }

    /// <summary>
    /// ライターに複数ドキュメントを一括追加。Commit は呼ばない。1ロックで追加して10万件時の競合を軽減。
    /// </summary>
    private void AddDocumentsToWriterWithoutCommit(IEnumerable<IndexedDocument> documents)
    {
        lock (_lock)
        {
            foreach (var document in documents)
            {
                var doc = CreateLuceneDocument(document);
                _writer!.UpdateDocument(new Term(FieldFilePath, document.FilePath), doc);
            }
        }
    }

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

            // ファイルを列挙
            IEnumerable<string> files;
            try
            {
                files = System.IO.Directory.EnumerateFiles(currentDir);
            }
            catch (UnauthorizedAccessException)
            {
                continue; // アクセス拒否はスキップ
            }
            catch (DirectoryNotFoundException)
            {
                continue; // ディレクトリが見つからない場合はスキップ
            }
            catch (IOException)
            {
                continue; // I/Oエラーはスキップ
            }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (string.IsNullOrEmpty(ext)) continue;
                if (!supportedExtensions.Contains(ext.ToLowerInvariant()))
                    continue;
                yield return file;
            }

            // サブディレクトリを追加
            IEnumerable<string> subdirs;
            try
            {
                subdirs = System.IO.Directory.EnumerateDirectories(currentDir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

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

    private static Document CreateLuceneDocument(IndexedDocument doc)
    {
        return new Document
        {
            new StringField(FieldFilePath, doc.FilePath, Field.Store.YES),
            new TextField(FieldFileName, doc.FileName, Field.Store.YES),
            new StringField(FieldFolderPath, doc.FolderPath, Field.Store.YES),
            new TextField(FieldContent, doc.Content, Field.Store.YES),
            new Int64Field(FieldFileSize, doc.FileSize, Field.Store.YES),
            new Int64Field(FieldLastModified, doc.LastModified.Ticks, Field.Store.YES),
            new StringField(FieldFileType, doc.FileType, Field.Store.YES),
            new Int64Field(FieldIndexedAt, doc.IndexedAt.Ticks, Field.Store.YES)
        };
    }

    private static string GetFileType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".docx" => "Word文書",
            ".xlsx" => "Excelブック",
            ".pptx" => "PowerPointプレゼンテーション",
            ".pdf" => "PDFファイル",
            ".txt" => "テキストファイル",
            ".csv" => "CSVファイル",
            ".log" => "ログファイル",
            ".md" => "Markdownファイル",
            ".cs" => "C#ソースコード",
            ".js" => "JavaScriptファイル",
            ".ts" => "TypeScriptファイル",
            ".py" => "Pythonファイル",
            ".java" => "Javaファイル",
            ".html" => "HTMLファイル",
            ".css" => "CSSファイル",
            ".xml" => "XMLファイル",
            ".json" => "JSONファイル",
            ".yaml" or ".yml" => "YAMLファイル",
            ".pas" or ".dpr" or ".dpk" => "Pascal/Delphi",
            _ => "ファイル"
        };
    }

    private void EnsureInitialized()
    {
        if (_writer == null)
        {
            throw new InvalidOperationException("IndexService has not been initialized. Call InitializeAsync first.");
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
            _writer?.Dispose();
            _analyzer?.Dispose();
            _directory?.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}


