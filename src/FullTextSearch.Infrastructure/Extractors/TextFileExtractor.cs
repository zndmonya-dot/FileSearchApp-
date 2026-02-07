// テキスト・ソースコードファイル（.txt, .csv, .cs 等）からテキストを抽出する実装。
using System.Text;
using UtfUnknown;
using FullTextSearch.Core;
using FullTextSearch.Core.Extractors;

namespace FullTextSearch.Infrastructure.Extractors;

/// <summary>
/// テキストファイル用のテキスト抽出器。UTF/Shift_JIS 等を自動判定して読み込む。
/// </summary>
public class TextFileExtractor : ITextExtractor
{
    /// <summary>対応拡張子（10MB 超のファイルは読み込まない）</summary>
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

    /// <summary>ファイルからテキストを読み取る。エンコーディングは自動検出。10MB 超は例外。</summary>
    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found", filePath);
        }

        // 上限超過は読み込まない（メモリ保護。ContentLimits.MaxTextFileBytesToRead と一致）
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > ContentLimits.MaxTextFileBytesToRead)
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

