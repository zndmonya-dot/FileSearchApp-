// 完全一致検索: インデックス済み本文・ファイル名への文字列一致判定。
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
}
