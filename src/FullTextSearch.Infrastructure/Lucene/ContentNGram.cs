// 完全一致検索の高速化用: 本文・ファイル名の文字バイグラム（2-gram）索引を生成する。
namespace FullTextSearch.Infrastructure.Lucene;

/// <summary>
/// 完全一致検索の候補絞り込み用の文字バイグラム（2-gram）を生成する。
///
/// <para>
/// 完全一致は本来「保存本文への文字列一致（IndexOf）」で判定するが、それを全ドキュメントに対して
/// 行うとインデックスが大きいほど遅くなる。そこで本文・ファイル名のすべての隣接 2 文字を索引しておき、
/// 検索語の 2 文字組をすべて含む文書だけを候補として取り出す。日本語の部分文字列（例: 「東京都」中の
/// 「京都」）も、対象文書には必ずその 2 文字組が含まれるため取りこぼさない（＝候補は完全一致集合の上位集合）。
/// 候補に対して最終的に <see cref="ExactMatchHelper"/> の連続一致で誤検出（2 文字組は揃うが連続しない）を除去する。
/// </para>
///
/// <para>
/// 大文字小文字は索引・検索の双方で <see cref="string.ToLowerInvariant"/> により畳み込み、
/// <see cref="ExactMatchHelper"/> の <c>OrdinalIgnoreCase</c> と整合させる。
/// 1 文字以下の検索語はバイグラムにできないため、呼び出し側で全走査にフォールバックする。
/// </para>
/// </summary>
public static class ContentNGram
{
    /// <summary>バイグラムの文字数。</summary>
    public const int GramSize = 2;

    /// <summary>索引・検索で共通の正規化（全角空白の統一＋小文字化）。</summary>
    public static string NormalizeForGram(string? text)
    {
        var normalized = SearchQueryParser.NormalizeQueryString(text);
        return normalized.Length == 0 ? "" : normalized.ToLowerInvariant();
    }

    /// <summary>本文とファイル名から索引用の重複なしバイグラム列を生成する。</summary>
    public static IReadOnlyList<string> BuildIndexTokens(string? content, string? fileName)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        AddGrams(set, NormalizeForGram(content));
        AddGrams(set, NormalizeForGram(fileName));
        return set.Count == 0 ? [] : new List<string>(set);
    }

    /// <summary>検索語の重複なしバイグラム列を生成する。1 文字以下なら空（＝索引で絞れない）。</summary>
    public static IReadOnlyList<string> BuildQueryGrams(string? normalizedQuery)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        AddGrams(set, NormalizeForGram(normalizedQuery));
        return set.Count == 0 ? [] : new List<string>(set);
    }

    private static void AddGrams(HashSet<string> set, string s)
    {
        if (s.Length < GramSize)
            return;
        for (var i = 0; i + GramSize <= s.Length; i++)
            set.Add(s.Substring(i, GramSize));
    }
}
