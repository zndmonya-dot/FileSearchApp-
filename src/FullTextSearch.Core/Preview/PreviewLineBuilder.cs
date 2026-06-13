using System.Text;
using System.Text.RegularExpressions;
using FullTextSearch.Core.Search;

namespace FullTextSearch.Core.Preview;

/// <summary>プレビュー用の行境界計算・マッチ行検出。</summary>
public static class PreviewLineBuilder
{
    /// <summary>CSS line-height と揃える行高（px）。</summary>
    public const int PreviewLineHeightPx = 20;

    /// <summary>プレビューに描画する最大行数（超過分は省略メッセージ）。</summary>
    public const int PreviewMaxRenderLines = 15_000;

    private static readonly RegexOptions LineMatchOptions =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>各行の先頭文字インデックス（0-based）。行数 = 配列長。</summary>
    public static int[] BuildLineStartOffsets(string content)
    {
        if (string.IsNullOrEmpty(content))
            return [0];

        var starts = new List<int>(Math.Max(16, content.Length / 80)) { 0 };
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
                starts.Add(i + 1);
        }
        return starts.ToArray();
    }

    /// <summary>マッチ行番号（1-based）を収集する。</summary>
    public static int[] CollectMatchLineNumbers(
        string content,
        int[] lineStarts,
        string[] searchTerms,
        SearchMode searchMode = SearchMode.Keyword)
    {
        if (searchTerms.Length == 0 || lineStarts.Length == 0)
            return [];

        var patterns = searchTerms.Select(Regex.Escape).ToArray();
        var matches = new List<int>();
        for (var lineIndex = 0; lineIndex < lineStarts.Length; lineIndex++)
        {
            var line = ExtractLine(content, lineStarts, lineIndex);
            if (LineMatches(line, patterns, searchMode))
                matches.Add(lineIndex + 1);
        }

        return matches.ToArray();
    }

    /// <summary>行インデックス（0-based）から行文字列を取り出す。</summary>
    public static string ExtractLine(string content, int[] lineStarts, int lineIndex)
    {
        var start = lineStarts[lineIndex];
        var end = lineIndex + 1 < lineStarts.Length ? lineStarts[lineIndex + 1] : content.Length;
        if (end > start && content[end - 1] == '\n')
            end--;
        if (end > start && content[end - 1] == '\r')
            end--;
        return content[start..end];
    }

    private static bool LineMatches(string line, string[] patterns, SearchMode searchMode) =>
        searchMode switch
        {
            SearchMode.Phrase => Regex.IsMatch(line, patterns[0], LineMatchOptions),
            SearchMode.Any => patterns.Any(p => Regex.IsMatch(line, p, LineMatchOptions)),
            _ => patterns.All(p => Regex.IsMatch(line, p, LineMatchOptions)),
        };
}

