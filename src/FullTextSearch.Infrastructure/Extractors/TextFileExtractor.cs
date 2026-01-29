using FullTextSearch.Core.Extractors;

namespace FullTextSearch.Infrastructure.Extractors;

/// <summary>
/// テキストファイル用のテキスト抽出器
/// </summary>
public class TextFileExtractor : ITextExtractor
{
    private static readonly HashSet<string> SupportedExtensionSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".csv", ".log", ".md",
        ".cs", ".js", ".ts", ".jsx", ".tsx",
        ".py", ".java", ".cpp", ".c", ".h", ".hpp",
        ".html", ".htm", ".css", ".scss", ".sass", ".less",
        ".xml", ".json", ".yaml", ".yml",
        ".sql", ".sh", ".bat", ".ps1",
        ".ini", ".cfg", ".conf", ".config",
        ".gitignore", ".env"
    };

    public IEnumerable<string> SupportedExtensions => SupportedExtensionSet;

    public bool CanExtract(string extension)
    {
        return SupportedExtensionSet.Contains(extension);
    }

    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found", filePath);
        }

        // ファイルサイズチェック（10MB以上は読み込まない）
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > 10 * 1024 * 1024)
        {
            throw new InvalidOperationException($"File is too large: {fileInfo.Length} bytes");
        }

        // エンコーディングを自動検出して読み込み
        using var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}

