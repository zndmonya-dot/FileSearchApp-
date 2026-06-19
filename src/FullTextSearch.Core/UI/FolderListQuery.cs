namespace FullTextSearch.Core.UI;

/// <summary>右ペインのフォルダ一覧：拡張子・ファイル名絞り込みとソート。</summary>
public static class FolderListQuery
{
    /// <summary>ファイル行のみを対象に、拡張子・検索語・ソート列で並べ替える。</summary>
    public static IEnumerable<TreeNode> Apply(
        IEnumerable<TreeNode> items,
        string? extensionFilter,
        string fileNameQuery,
        string sortColumn,
        bool sortAscending)
    {
        var filtered = items.Where(i => !i.IsFolder);
        if (!string.IsNullOrEmpty(extensionFilter))
        {
            filtered = filtered.Where(i =>
                Path.GetExtension(i.Name).Equals(extensionFilter, StringComparison.OrdinalIgnoreCase));
        }

        var query = fileNameQuery.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(i =>
                i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (i.FilePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return sortColumn switch
        {
            "name" => sortAscending ? filtered.OrderBy(i => i.Name) : filtered.OrderByDescending(i => i.Name),
            "date" => sortAscending ? filtered.OrderBy(i => i.LastModified) : filtered.OrderByDescending(i => i.LastModified),
            _ => sortAscending ? filtered.OrderBy(i => i.Name) : filtered.OrderByDescending(i => i.Name)
        };
    }
}
