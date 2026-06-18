using FullTextSearch.Core.Index;

namespace FullTextSearch.Core;

/// <summary>検索対象フォルダの有効/無効（利用者の個人設定）。</summary>
public static class TargetFolderEnablement
{
    /// <summary>無効リストに含まれないフォルダを返す。</summary>
    public static IReadOnlyList<string> GetActiveFolders(
        IReadOnlyList<string> allFolders,
        IReadOnlyList<string> disabledFolders)
    {
        if (allFolders.Count == 0)
            return Array.Empty<string>();
        if (disabledFolders.Count == 0)
            return allFolders;

        var disabled = ToNormalizedSet(disabledFolders);
        return allFolders
            .Where(f => !disabled.Contains(IndexPaths.NormalizeFolderPath(f)))
            .ToList();
    }

    /// <summary>フォルダが有効か（無効リストに無い）。</summary>
    public static bool IsEnabled(string folder, IReadOnlyList<string> disabledFolders)
    {
        var norm = IndexPaths.NormalizeFolderPath(folder);
        return !disabledFolders.Any(d =>
            string.Equals(IndexPaths.NormalizeFolderPath(d), norm, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>有効/無効を切り替えて <paramref name="disabledFolders"/> を更新する。</summary>
    public static void SetEnabled(List<string> disabledFolders, string folder, bool enabled)
    {
        var norm = IndexPaths.NormalizeFolderPath(folder);
        disabledFolders.RemoveAll(d =>
            string.Equals(IndexPaths.NormalizeFolderPath(d), norm, StringComparison.OrdinalIgnoreCase));
        if (!enabled)
            disabledFolders.Add(norm);
    }

    /// <summary>マスター一覧に無いパスを無効リストから除去する。</summary>
    public static void PruneDisabled(List<string> disabledFolders, IReadOnlyList<string> allFolders)
    {
        if (disabledFolders.Count == 0 || allFolders.Count == 0)
        {
            disabledFolders.Clear();
            return;
        }

        var allowed = ToNormalizedSet(allFolders);
        disabledFolders.RemoveAll(d => !allowed.Contains(IndexPaths.NormalizeFolderPath(d)));
    }

    private static HashSet<string> ToNormalizedSet(IReadOnlyList<string> folders) =>
        folders
            .Select(IndexPaths.NormalizeFolderPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
