using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Ja;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace FullTextSearch.Infrastructure.Lucene;

/// <summary>
/// Lucene.NETを使用したインデックスサービスの実装
/// </summary>
public class LuceneIndexService : IIndexService, IDisposable
{
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

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

            // 日本語解析器（Kuromoji）を使用
            _analyzer = new JapaneseAnalyzer(AppLuceneVersion);

            var config = new IndexWriterConfig(AppLuceneVersion, _analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND
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

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                progress?.Report(new IndexProgress
                {
                    ProcessedFiles = processedFiles,
                    TotalFiles = totalFiles,
                    CurrentFile = filePath,
                    ErrorCount = errorCount
                });

                await IndexFileAsync(filePath, cancellationToken);
            }
            catch (Exception)
            {
                errorCount++;
                // エラーをログに記録（後で実装）
            }

            processedFiles++;
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
        var fileInfo = new FileInfo(filePath);
        var extension = fileInfo.Extension.ToLowerInvariant();
        var extractor = _extractorFactory.GetExtractor(extension);

        if (extractor == null)
        {
            return;
        }

        var content = await extractor.ExtractTextAsync(filePath, cancellationToken);

        var document = new IndexedDocument
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

        await IndexDocumentAsync(document, cancellationToken);
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


