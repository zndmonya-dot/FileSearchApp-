namespace FullTextSearch.Core.Search;

/// <summary>検索語の分割とクエリ正規化（AND / OR 共通）。</summary>
public static class SearchQueryTerms
{
    /// <summary>検索入力の正規化（前後空白・全角スペースの統一）。</summary>
    public static string NormalizeQuery(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";
        var s = input.Trim();
        if (s.Contains('\u3000'))
            s = s.Replace('\u3000', ' ');
        return s;
    }

    /// <summary>
    /// 語リスト。スペースなし → 入力全体を1語。スペースあり → 各語に分割。
    /// </summary>
    public static IReadOnlyList<string> GetTerms(string? query)
    {
        var normalized = NormalizeQuery(query);
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<string>();

        if (!normalized.Contains(' '))
            return [normalized];

        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToArray();
    }

    /// <summary>入力が1語（スペースなし）か。</summary>
    public static bool IsSingleKeyword(string? query) =>
        GetTerms(query).Count == 1;
}
