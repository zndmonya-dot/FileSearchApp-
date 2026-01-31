using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Cjk;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace FullTextSearch.Infrastructure.Lucene;

/// <summary>
/// Lucene.NETを使用したインデックスサービスの実装
/// </summary>
public class LuceneIndexService : IIndexService, IDisposable
{
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;
    /// <summary>10万ファイル規模を想定した並列抽出数</summary>
    private const int ParallelExtractCount = 24;

    private readonly TextExtractorFactory _extractorFactory;
    private FSDirectory? _directory;
    private IndexWriter? _writer;
    private Analyzer? _analyzer;
    private readonly object _lock = new();
    private bool _disposed;
    private IndexRebuildOptions? _currentRebuildOptions;

    // フィールド名の定数
    public const string FieldFilePath = "filepath";
    public const string FieldFileName = "filename";
    public const string FieldFolderPath = "folderpath";
    public const string FieldContent = "content";
    public const string FieldFileSize = "filesize";
    public const string FieldLastModified = "lastmodified";
    public const string FieldFileType = "filetype";
    public const string FieldIndexedAt = "indexedat";

    public LuceneIndexService(TextExtractorFactory extractorFactory)
    {
        _extractorFactory = extractorFactory;
    }

    public Task InitializeAsync(string indexPath, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_writer != null)
            {
                return Task.CompletedTask;
            }

            // ディレクトリが存在しない場合は作成
            if (!System.IO.Directory.Exists(indexPath))
            {
                System.IO.Directory.CreateDirectory(indexPath);
            }

            _directory = FSDirectory.Open(indexPath);

            // CJKAnalyzer: 日本語・中国語・韓国語をバイグラムで検索、英語も対応
            _analyzer = new CJKAnalyzer(AppLuceneVersion);

            var config = new IndexWriterConfig(AppLuceneVersion, _analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND,
                RAMBufferSizeMB = 256  // 大量ファイル時のフラッシュ頻度を下げて高速化
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

    public async Task IndexFolderAsync(string folderPath, IProgress<IndexProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var processedFiles = 0;
        var errorCount = 0;
        var files = new List<string>();
        foreach (var filePath in GetTargetFiles(folderPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(filePath);
        }
        var totalFiles = files.Count;

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
                    ProcessedFiles = processedFiles,
                    TotalFiles = totalFiles,
                    CurrentFile = path,
                    ErrorCount = errorCount
                });
                processedFiles++;
            }
            var toAdd = docs.Where(d => d != null).Cast<IndexedDocument>().ToList();
            if (toAdd.Count > 0)
                AddDocumentsToWriterWithoutCommit(toAdd);
        }

        progress?.Report(new IndexProgress
        {
            ProcessedFiles = processedFiles,
            TotalFiles = totalFiles,
            CurrentFile = null,
            ErrorCount = errorCount
        });

        lock (_lock)
        {
            _writer!.Commit();
        }
    }

    public async Task RebuildIndexAsync(IEnumerable<string> folders, IProgress<IndexProgress>? progress = null, IndexRebuildOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _currentRebuildOptions = options;

        try
        {
            // 全てのドキュメントを削除
            lock (_lock)
            {
                _writer!.DeleteAll();
                _writer.Commit();
            }

            // 全フォルダを再インデックス
            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await IndexFolderAsync(folder, progress, cancellationToken);
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
                var toAdd = docs.Where(d => d != null).Cast<IndexedDocument>().ToList();
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
    /// </summary>
    private Dictionary<string, long> GetIndexedPathsAndLastModified(List<string> normalizedFolderPaths)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (_directory == null) return result;

        DirectoryReader? reader = null;
        try
        {
            reader = DirectoryReader.Open(_directory);
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
    /// ファイルからインデックス用ドキュメントを取得する。対象外・エラー時は null。
    /// </summary>
    private async Task<IndexedDocument?> TryGetIndexedDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var extension = fileInfo.Extension.ToLowerInvariant();
        var extractor = _extractorFactory.GetExtractor(extension);
        if (extractor == null)
            return null;

        var content = await extractor.ExtractTextAsync(filePath, cancellationToken);
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
            supportedExtensions = _currentRebuildOptions.TargetExtensions
                .Select(e => e.StartsWith(".", StringComparison.Ordinal) ? e : "." + e)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            supportedExtensions = _extractorFactory.GetAllSupportedExtensions().ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                if (!supportedExtensions.Contains(Path.GetExtension(file)))
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


