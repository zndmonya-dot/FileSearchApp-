namespace FullTextSearch.Core;

using System.Text;
using FullTextSearch.Core.Preview;
using FullTextSearch.Core.Search;

/// <summary>フォルダ一覧などで表示する本文抜粋（先頭行または検索マッチ行）。</summary>
public static class ContentPreviewHelper
{
    /// <summary>ディスクから先頭行を読む際の最大バイト数。</summary>
    public const int DiskPreviewReadMaxBytes = 4096;

    private static readonly HashSet<string> DiskPreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".json", ".xml", ".html", ".htm", ".log", ".ini", ".cfg", ".conf",
        ".yaml", ".yml", ".cs", ".js", ".ts", ".tsx", ".jsx", ".css", ".bat", ".cmd", ".ps1",
        ".sh", ".sql", ".rtf", ".properties", ".env", ".toml"
    };

    /// <summary>先頭の非空行から最大文字数まで抜粋する。</summary>
    public static string ExtractFirstLine(string? content, int maxChars = ContentLimits.FolderListPreviewMaxChars)
    {
        if (string.IsNullOrEmpty(content) || maxChars <= 0)
            return "";

        var span = content.AsSpan();
        var lineStart = 0;
        while (lineStart < span.Length)
        {
            var lineEnd = lineStart;
            while (lineEnd < span.Length && span[lineEnd] is not ('\n' or '\r'))
                lineEnd++;

            var line = span.Slice(lineStart, lineEnd - lineStart).Trim();
            if (line.Length > 0)
            {
                if (line.Length <= maxChars)
                    return line.ToString();
                return line[..maxChars].ToString() + "…";
            }

            lineStart = lineEnd;
            while (lineStart < span.Length && span[lineStart] is '\n' or '\r')
                lineStart++;
        }

        return "";
    }

    /// <summary>検索語がヒットした最初の行を抜粋する（プレビューと同じ行マッチ規則）。</summary>
    public static string ExtractSearchMatchLine(
        string? content,
        IReadOnlyList<string> searchTerms,
        SearchMode searchMode,
        int maxChars = ContentLimits.FolderListPreviewMaxChars)
    {
        if (string.IsNullOrEmpty(content) || searchTerms.Count == 0)
            return ExtractFirstLine(content, maxChars);

        var lineStarts = PreviewLineBuilder.BuildLineStartOffsets(content);
        var matchLines = PreviewLineBuilder.CollectMatchLineNumbers(
            content,
            lineStarts,
            searchTerms.ToArray(),
            searchMode);
        if (matchLines.Length == 0)
            return ExtractFirstLine(content, maxChars);

        var line = PreviewLineBuilder.ExtractLine(content, lineStarts, matchLines[0] - 1).Trim();
        if (line.Length == 0)
            return "";

        if (line.Length <= maxChars)
            return line;
        return line[..maxChars] + "…";
    }

    /// <summary>テキスト系ファイルから検索マッチ行を軽量に読む（先頭数 KB のみ）。</summary>
    public static string? TryReadSearchMatchLineFromDisk(
        string filePath,
        IReadOnlyList<string> searchTerms,
        SearchMode searchMode,
        int maxChars = ContentLimits.FolderListPreviewMaxChars)
    {
        var text = TryReadPreviewTextFromDisk(filePath);
        if (text == null)
            return null;
        var preview = ExtractSearchMatchLine(text, searchTerms, searchMode, maxChars);
        return string.IsNullOrEmpty(preview) ? null : preview;
    }

    /// <summary>テキスト系ファイルの先頭行をディスクから軽量に読む（インデックス未登録時のフォールバック）。</summary>
    public static string? TryReadFirstLineFromDisk(string filePath, int maxChars = ContentLimits.FolderListPreviewMaxChars)
    {
        var text = TryReadPreviewTextFromDisk(filePath);
        if (text == null)
            return null;
        var preview = ExtractFirstLine(text, maxChars);
        return string.IsNullOrEmpty(preview) ? null : preview;
    }

    /// <summary>インデックス未取得分をディスクから補完する。</summary>
    public static void MergeDiskFallbackPreviews(
        IDictionary<string, string> previews,
        IReadOnlyList<string> filePaths,
        IReadOnlyList<string> highlightTerms,
        SearchMode searchMode)
    {
        foreach (var path in filePaths)
        {
            if (string.IsNullOrWhiteSpace(path) || previews.ContainsKey(path))
                continue;

            string? diskPreview = highlightTerms.Count > 0
                ? TryReadSearchMatchLineFromDisk(path, highlightTerms, searchMode)
                : TryReadFirstLineFromDisk(path);
            if (!string.IsNullOrEmpty(diskPreview))
                previews[path] = diskPreview;
        }
    }

    private static string? TryReadPreviewTextFromDisk(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var ext = PreviewHelper.NormalizeExtension(filePath);
        if (string.IsNullOrEmpty(ext) || !DiskPreviewExtensions.Contains(ext))
            return null;

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length == 0
                || ContentLimits.ExceedsIndexTextExtractionFileSizeLimit(info.Length))
                return null;

            var readLen = (int)Math.Min(DiskPreviewReadMaxBytes, info.Length);
            var buffer = new byte[readLen];
            using (var stream = new FileStream(
                       filePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                var read = stream.Read(buffer, 0, readLen);
                if (read <= 0)
                    return null;
                if (read < readLen)
                    buffer = buffer.AsSpan(0, read).ToArray();
            }

            return DecodePreviewBytes(buffer);
        }
        catch
        {
            return null;
        }
    }

    private static string DecodePreviewBytes(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return Encoding.Default.GetString(bytes);
        }
    }
}
