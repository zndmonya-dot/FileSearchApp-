using FullTextSearch.Core.Preview;

namespace FullTextSearch.Core.Search;

/// <summary>
/// 検索結果の事後検証。Lucene の文書単位ヒットを、プレビューと同じ行単位（またはファイル名）基準に揃える。
/// </summary>
public static class SearchMatchVerifier
{
    /// <summary>
    /// 本文・ファイル名が検索モードに合致するか。AND は同一行（またはファイル名）に全語が必要。
    /// </summary>
    public static bool Matches(
        string? content,
        string? fileName,
        IReadOnlyList<string> highlightTerms,
        SearchMode mode)
    {
        if (highlightTerms.Count == 0)
            return true;

        if (MatchesFileName(fileName, highlightTerms, mode))
            return true;

        if (string.IsNullOrEmpty(content))
            return false;

        var lineStarts = PreviewLineBuilder.BuildLineStartOffsets(content);
        var matchLines = PreviewLineBuilder.CollectMatchLineNumbers(
            content, lineStarts, highlightTerms.ToArray(), mode);
        return matchLines.Length > 0;
    }

    private static bool MatchesFileName(
        string? fileName,
        IReadOnlyList<string> terms,
        SearchMode mode)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        return mode switch
        {
            SearchMode.Phrase => fileName.Contains(terms[0], StringComparison.OrdinalIgnoreCase),
            SearchMode.Any => terms.Any(t => fileName.Contains(t, StringComparison.OrdinalIgnoreCase)),
            _ => terms.All(t => fileName.Contains(t, StringComparison.OrdinalIgnoreCase)),
        };
    }
}
