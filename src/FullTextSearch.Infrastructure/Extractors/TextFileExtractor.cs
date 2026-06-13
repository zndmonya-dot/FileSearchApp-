// テキスト・スクリプト・ソースコードファイルからテキストを抽出する実装。
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
    /// <inheritdoc />
    public IEnumerable<string> SupportedExtensions => SupportedExtensionSets.TextFile;

    /// <inheritdoc />
    public bool CanExtract(string extension) => SupportedExtensionSets.TextFile.Contains(extension);

    /// <inheritdoc />
    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > ContentLimits.MaxTextFileBytesToRead)
            throw new InvalidOperationException($"File is too large: {fileInfo.Length} bytes");

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return ReadTextWithAutoEncoding(bytes);
    }

    private static string ReadTextWithAutoEncoding(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;

        var result = CharsetDetector.DetectFromBytes(bytes);
        var detected = result.Detected;

        Encoding? encoding = detected?.Encoding;
        if (encoding == null && !string.IsNullOrEmpty(detected?.EncodingName))
        {
            try { encoding = Encoding.GetEncoding(detected.EncodingName); }
            catch { /* ignore */ }
        }

        encoding ??= Encoding.UTF8;
        try { return encoding.GetString(bytes); }
        catch { return Encoding.UTF8.GetString(bytes); }
    }
}
