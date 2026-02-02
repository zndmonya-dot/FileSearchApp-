using System.Text;
using UtfUnknown;
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
        ".pas", ".dpr", ".dpk",
        ".html", ".htm", ".css", ".scss", ".sass", ".less",
        ".xml", ".json", ".yaml", ".yml",
        ".sql", ".sh", ".bat", ".ps1",
        ".ini", ".cfg", ".conf", ".config",
        ".gitignore", ".env"
    };

    public IEnumerable<string> SupportedExtensions => SupportedExtensionSet;

    public PreviewCategory PreviewCategory => PreviewCategory.Text;

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

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return ReadTextWithAutoEncoding(bytes);
    }

    /// <summary>
    /// UTF.Unknown による動的エンコーディング検出でテキストを読み取る
    /// </summary>
    private static string ReadTextWithAutoEncoding(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;

        var result = CharsetDetector.DetectFromBytes(bytes);
        var detected = result.Detected;

        Encoding? encoding = detected?.Encoding;
        if (encoding == null && !string.IsNullOrEmpty(detected?.EncodingName))
        {
            try
            {
                encoding = Encoding.GetEncoding(detected.EncodingName);
            }
            catch
            {
                // 検出名で取得できない場合は無視
            }
        }

        encoding ??= Encoding.UTF8;

        try
        {
            return encoding.GetString(bytes);
        }
        catch
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }
}

