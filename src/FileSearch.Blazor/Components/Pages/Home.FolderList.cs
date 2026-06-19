// =============================================================================
// Home.FolderList.cs — partial class Home
// =============================================================================
// 役割: 右ペインのフォルダ一覧（ソート・フィルター・行選択）。
// =============================================================================
using FullTextSearch.Core.UI;

namespace FileSearch.Blazor.Components.Pages;

public partial class Home
{
    /// <summary>選択フォルダ配下のファイルノード一覧（再帰）。</summary>
    private static List<TreeNode> GetFilesUnderSelectedFolder(TreeNode? folder) =>
        folder == null ? [] : TreeBuilder.CollectFileNodesUnderFolder(folder);

    /// <summary>選択フォルダの一覧に含まれるファイルか。</summary>
    private bool IsFileInSelectedFolderList(TreeNode? folder, string? filePath)
    {
        if (folder == null || string.IsNullOrEmpty(filePath))
            return false;

        return GetFilesUnderSelectedFolder(folder)
            .Any(f => string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>右ペイン一覧でファイル行を選択状態に合わせる。</summary>
    private void SyncFolderRowIndexForFile(TreeNode? folder, string? filePath)
    {
        if (folder == null || string.IsNullOrEmpty(filePath))
            return;

        var list = GetSortedAndFilteredItems(GetFilesUnderSelectedFolder(folder)).ToList();
        var idx = list.FindIndex(n => string.Equals(n.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        selectedFolderRowIndex = idx >= 0 ? idx : -1;
        if (idx >= 0)
            _folderListScrollToRow = idx;
    }

    /// <summary>フォルダ一覧のファイル名絞り込み変更後（行選択を先頭へ）。</summary>
    private void OnFolderFileSearchQueryChanged()
    {
        selectedFolderRowIndex = -1;
    }

    /// <summary>フォルダ一覧の行スクロール完了後にトリガーをリセットする。</summary>
    private Task OnFolderListScrollCompleted()
    {
        _folderListScrollToRow = -1;
        return Task.CompletedTask;
    }

    /// <summary>フォルダ切替時に一覧の絞り込み状態を初期化する。</summary>
    private void ResetFolderListFilters()
    {
        folderFileSearchQuery = "";
        filterType = "";
        showFilterMenu = false;
        selectedFolderRowIndex = -1;
        _folderListHighlightedFilePath = null;
    }

    /// <summary>フォルダ一覧テーブルのソート列。同一列なら昇降切替。</summary>
    private void SetSort(string column)
    {
        if (sortColumn == column) sortAscending = !sortAscending;
        else { sortColumn = column; sortAscending = true; }
        selectedFolderRowIndex = -1;
    }

    /// <summary>拡張子フィルターのドロップダウン開閉。</summary>
    private void ToggleFilterMenu() => showFilterMenu = !showFilterMenu;

    /// <summary>拡張子フィルター。空文字は「すべて」。</summary>
    private void SetFilter(string type)
    {
        filterType = type;
        showFilterMenu = false;
        selectedFolderRowIndex = -1;
    }

    /// <summary>現在の filterType / sortColumn / ファイル名検索に応じてファイル一覧を並べ替え。</summary>
    private IEnumerable<TreeNode> GetSortedAndFilteredItems(List<TreeNode> items) =>
        FolderListQuery.Apply(
            items,
            string.IsNullOrEmpty(filterType) ? null : filterType,
            folderFileSearchQuery,
            sortColumn,
            sortAscending);

    /// <summary>右ペインのファイル行クリックでプレビューを開く。</summary>
    private void OnFolderItemClick(TreeNode item) => SelectFile(item);

    /// <summary>表の行クリック。選択行インデックスを更新してプレビューを開く。</summary>
    private void OnFolderRowClick(TreeNode item)
    {
        if (selectedFolder == null) return;
        var list = GetSortedAndFilteredItems(GetFilesUnderSelectedFolder(selectedFolder)).ToList();
        selectedFolderRowIndex = list.IndexOf(item);
        if (selectedFolderRowIndex < 0) selectedFolderRowIndex = -1;
        OnFolderItemClick(item);
    }
}
