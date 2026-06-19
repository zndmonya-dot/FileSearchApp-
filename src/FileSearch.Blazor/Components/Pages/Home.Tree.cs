// =============================================================================
// Home.Tree.cs — partial class Home
// =============================================================================
// 役割: 左ツリーの展開/選択、ファイル選択、パンくずからのフォルダ移動。
// =============================================================================
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.UI;

namespace FileSearch.Blazor.Components.Pages;

public partial class Home
{
    /// <summary>フォルダの展開/折りたたみ（閲覧時はシェブロンのみ。行クリックは <see cref="SelectFolderFromTree"/>）。</summary>
    private void ToggleNode(TreeNode node)
    {
        if (isIndexing || !node.IsFolder) return;

        if (selectedFile != null)
        {
            _ = OpenFolderFromTreeAsync(node);
            return;
        }

        if (_lastExecutedSearchQuery == null)
        {
            var expanding = !node.IsExpanded;
            node.IsExpanded = expanding;
            if (expanding && !node.FolderChildrenLoaded)
            {
                var generation = Interlocked.Increment(ref _folderNavigationGeneration);
                _ = InvokeAsync(async () =>
                {
                    await EnsureFolderChildrenLoadedAsync(node);
                    if (generation != _folderNavigationGeneration) return;
                    StateHasChanged();
                });
            }
            else
            {
                StateHasChanged();
            }
            return;
        }

        var expandingSearch = !node.IsExpanded;
        node.IsExpanded = expandingSearch;

        if (expandingSearch)
        {
            if (!node.FolderChildrenLoaded)
            {
                var generation = Interlocked.Increment(ref _folderNavigationGeneration);
                _ = InvokeAsync(async () =>
                {
                    await EnsureFolderChildrenLoadedAsync(node);
                    if (generation != _folderNavigationGeneration) return;
                    ApplyFolderSelection(node);
                    StateHasChanged();
                });
                return;
            }

            ApplyFolderSelection(node);
        }
        else if (selectedFolder != null && IndexPaths.IsPathUnderFolderRoot(selectedFolder.FullPath, node.FullPath))
        {
            selectedFolder = null;
            selectedFolderRowIndex = -1;
        }

        StateHasChanged();
    }

    /// <summary>閲覧モードで左ツリーのフォルダ行を選び、右ペインに配下ファイル一覧を出す。</summary>
    private void SelectFolderFromTree(TreeNode node)
    {
        if (isIndexing || !node.IsFolder || _lastExecutedSearchQuery != null)
            return;

        ClearPreviewSelection();
        ResetFolderListFilters();
        node.IsExpanded = true;
        var generation = Interlocked.Increment(ref _folderNavigationGeneration);
        _ = InvokeAsync(async () =>
        {
            await EnsureFolderChildrenLoadedAsync(node);
            if (generation != _folderNavigationGeneration)
                return;
            ApplyFolderSelection(node);
            StateHasChanged();
        });
    }

    /// <summary>プレビュー中に左ツリーでフォルダを選んだとき、右ペインをフォルダ一覧へ切り替える。</summary>
    private async Task OpenFolderFromTreeAsync(TreeNode node)
    {
        if (isIndexing || !node.IsFolder)
            return;

        Interlocked.Increment(ref _folderNavigationGeneration);
        ClearPreviewSelection();

        node.IsExpanded = true;
        await EnsureFolderChildrenLoadedAsync(node);

        ResetFolderListFilters();
        selectedFolder = node;
        selectedFolderRowIndex = -1;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>右ペインのフォルダ一覧を開く。</summary>
    private void ApplyFolderSelection(TreeNode node)
    {
        ClearPreviewSelection();
        selectedFolder = node;
        selectedFolderRowIndex = -1;
    }

    /// <summary>左ツリーからのファイル選択（閲覧時は一覧同期、右行クリックでプレビュー）。</summary>
    private void SelectFileFromTree(TreeNode node)
    {
        if (isIndexing || node.IsFolder || string.IsNullOrEmpty(node.FilePath))
            return;

        if (_lastExecutedSearchQuery == null
            && selectedFolder != null
            && IsFileInSelectedFolderList(selectedFolder, node.FilePath))
        {
            _folderListHighlightedFilePath = node.FilePath;
            SyncFolderRowIndexForFile(selectedFolder, node.FilePath);
            TreeBuilder.ExpandPathToFile(treeNodes, node.FilePath);
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        SelectFile(node);
    }

    /// <summary>ファイル選択。ツリー展開・全ファイルフラットリスト・プレビュー読み込みを連動。</summary>
    private void SelectFile(TreeNode node)
    {
        if (isIndexing) return;
        if (node.IsFolder || string.IsNullOrEmpty(node.FilePath))
            return;

        Interlocked.Increment(ref _folderNavigationGeneration);
        var keepFolder = _lastExecutedSearchQuery == null
            && selectedFolder != null
            && IsFileInSelectedFolderList(selectedFolder, node.FilePath);

        if (keepFolder)
            SyncFolderRowIndexForFile(selectedFolder, node.FilePath);
        else
        {
            selectedFolder = null;
            selectedFolderRowIndex = -1;
        }

        _folderListHighlightedFilePath = null;
        selectedFile = node.FileData ?? TreeBuilder.CreateSearchResultItem(node.FilePath);
        _previewResult = null;
        isLoadingPreview = true;
        TreeBuilder.ExpandPathToFile(treeNodes, node.FilePath);
        _fileNavList = TreeBuilder.CollectAllFileNodes(treeNodes);
        _fileNavIndex = _fileNavList.FindIndex(n => string.Equals(n.FilePath, node.FilePath, StringComparison.OrdinalIgnoreCase));
        if (_fileNavIndex < 0) _fileNavIndex = 0;
        SchedulePreviewLoad(node.FilePath);
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>パンくずから任意の祖先フォルダへ移動。</summary>
    private void NavigateToFolder(TreeNode folder)
    {
        if (isIndexing || folder == null || !folder.IsFolder || folder == selectedFolder) return;
        folder.IsExpanded = true;
        ClearPreviewSelection();
        ResetFolderListFilters();
        selectedFolder = folder;
        StateHasChanged();
    }
}
