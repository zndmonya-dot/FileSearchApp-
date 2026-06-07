// 完全一致検索: インデックス済み本文・ファイル名への文字列一致判定。
using FullTextSearch.Core.Models;

namespace FullTextSearch.Infrastructure.Lucene;

/// <summary>
/// 完全一致検索モード用。
/// 入力文字列（例: import sys）が本文・ファイル名にそのまま含まれる場合のみ一致とする。
/// import だけ・sys だけが含まれるだけでは一致しない。
/// </summary>
public static class ExactMatchHelper
{
    /// <summary>本文またはファイル名に正規化済み検索語がそのまま含まれるか。</summary>
    public static bool MatchesContentOrFileName(string? content, string? fileName, string normalizedQuery)
    {
        if (string.IsNullOrEmpty(normalizedQuery))
            return false;

        return ContainsLiteral(content, normalizedQuery)
            || ContainsLiteral(fileName, normalizedQuery);
    }

    /// <summary>テキストに正規化済み検索語がそのまま含まれるか（大文字小文字は無視）。</summary>
    public static bool ContainsLiteral(string? text, string normalizedQuery)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(normalizedQuery))
            return false;

        var normalizedText = SearchQueryParser.NormalizeQueryString(text);
        if (string.IsNullOrEmpty(normalizedText))
            return false;

        return normalizedText.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>完全一致語句のハイライト断片を生成する（import / sys を個別にはハイライトしない）。</summary>
    public static IEnumerable<MatchHighlight> BuildHighlights(
        string content,
        string normalizedQuery,
        int fragmentSize,
        int maxHighlights)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(normalizedQuery) || maxHighlights <= 0)
            yield break;

        var normalizedContent = SearchQueryParser.NormalizeQueryString(content);
        if (string.IsNullOrEmpty(normalizedContent))
            yield break;

        var count = 0;
        var startIndex = 0;
        while (count < maxHighlights)
        {
            var matchIndex = normalizedContent.IndexOf(normalizedQuery, startIndex, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
                yield break;

            var contextStart = Math.Max(0, matchIndex - fragmentSize / 2);
            var contextEnd = Math.Min(normalizedContent.Length, matchIndex + normalizedQuery.Length + fragmentSize / 2);
            var fragment = normalizedContent[contextStart..contextEnd];
            var highlightStart = matchIndex - contextStart;
            var highlightEnd = highlightStart + normalizedQuery.Length - 1;

            yield return new MatchHighlight
            {
                Text = fragment,
                HighlightStart = highlightStart,
                HighlightEnd = highlightEnd
            };

            count++;
            startIndex = matchIndex + normalizedQuery.Length;
        }
    }
}
