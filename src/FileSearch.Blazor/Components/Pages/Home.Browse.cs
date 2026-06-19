// =============================================================================
// Home.Browse.cs — partial class Home
// =============================================================================
// 役割: 閲覧モードのフォルダツリー読み込み、フォルダノード解決、件数同期。
// =============================================================================
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Preview;
using FullTextSearch.Core.UI;

namespace FileSearch.Blazor.Components.Pages;

public partial class Home
{
    /// <summary>検索前の初期表示用に、インデックス済みファイルからツリーへ反映する。</summary>
    private async Task RefreshFolderSkeletonTreeAsync()
    {
        if (_lastExecutedSearchQuery != null)
            return;

        var folders = GetActiveTargetFolders();
        if (folders.Count == 0)
        {
            CancelFolderTreeLoad();
            treeNodes = [];
            totalFileCount = 0;
            indexCount = 0;
            await InvokeAsync(StateHasChanged);
            return;
        }

        CancelFolderTreeLoad();
        _folderTreeLoadCts = new CancellationTokenSource();
        var loadToken = _folderTreeLoadCts.Token;

        isLoadingFolderTree = true;
        treeNodes = [];
        await InvokeAsync(StateHasChanged);

        List<TreeNode> built;
        try
        {
            var extensions = GetBrowseExtensionSet();
            var indexPath = SettingsService.Settings.IndexPath;
            if (!string.IsNullOrWhiteSpace(indexPath) && !IndexService.LastInitializeFailed)
            {
                var items = await Task.Run(
                    () => IndexService.ListIndexedItems(folders, extensions),
                    loadToken);
                built = await Task.Run(() =>
                {
                    var tree = TreeBuilder.BuildTree(folders, items);
                    TreeBuilder.MarkFolderTreeLoaded(tree);
                    return tree;
                }, loadToken);
                totalFileCount = items.Count;
                indexCount = items.Count;
            }
            else
            {
                built = await Task.Run(() => TreeBuilder.BuildFullFolderTree(folders, extensions), loadToken);
                totalFileCount = TreeBuilder.CollectAllFileNodes(built).Count;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (loadToken.IsCancellationRequested || _lastExecutedSearchQuery != null)
            return;

        treeNodes = built;
        isLoadingFolderTree = false;
        _lastTreeSyncFilePath = null;
        _lastTreeSyncFolderPath = null;
        TrySelectInitialBrowseFolder(built);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>対象フォルダ・拡張子に一致するインデックス件数をフッターへ反映する。</summary>
    private void SyncScopedIndexCount()
    {
        var folders = GetActiveTargetFolders();
        if (folders.Count == 0 || string.IsNullOrWhiteSpace(SettingsService.Settings.IndexPath) || IndexService.LastInitializeFailed)
        {
            indexCount = 0;
            return;
        }

        indexCount = IndexService.ListIndexedItems(folders, GetBrowseExtensionSet()).Count;
    }

    /// <summary>閲覧モードで最初のルートフォルダを右ペインに表示する。</summary>
    private void TrySelectInitialBrowseFolder(IReadOnlyList<TreeNode> roots)
    {
        if (_lastExecutedSearchQuery != null || selectedFile != null || selectedFolder != null || roots.Count == 0)
            return;

        var root = roots[0];
        root.IsExpanded = true;
        selectedFolder = root;
        selectedFolderRowIndex = -1;
    }

    /// <summary>バックグラウンドのフォルダツリー読み込みを中断する（検索割り込み時）。</summary>
    private void CancelFolderTreeLoad()
    {
        _folderTreeLoadCts?.Cancel();
        _folderTreeLoadCts?.Dispose();
        _folderTreeLoadCts = null;
        isLoadingFolderTree = false;
    }

    /// <summary>閲覧モードでフォルダ直下の子を読み込む。</summary>
    private async Task EnsureFolderChildrenLoadedAsync(TreeNode node)
    {
        if (_lastExecutedSearchQuery != null || !node.IsFolder || node.FolderChildrenLoaded)
            return;

        var extensions = GetBrowseExtensionSet();
        await Task.Run(() => TreeBuilder.LoadDirectFolderChildren(node, extensions));
    }

    /// <summary>ツリー上のフォルダノードを解決する（必要なら遅延読み込み）。</summary>
    private async Task<TreeNode?> ResolveFolderNodeAsync(string folderPath)
    {
        var normalized = IndexPaths.NormalizeFolderPath(folderPath).TrimEnd('\\', '/');
        TreeBuilder.ExpandPathToFolder(treeNodes, normalized);

        var found = TreeBuilder.FindFolderNode(treeNodes, normalized);
        if (found != null)
        {
            if (!found.FolderChildrenLoaded)
                await EnsureFolderChildrenLoadedAsync(found);
            return found;
        }

        if (_lastExecutedSearchQuery != null)
            return null;

        foreach (var root in treeNodes)
        {
            var rootPath = IndexPaths.NormalizeFolderPath(root.FullPath).TrimEnd('\\', '/');
            if (!IndexPaths.IsPathUnderFolderRoot(normalized, rootPath))
                continue;

            var current = root;
            while (true)
            {
                var currentPath = IndexPaths.NormalizeFolderPath(current.FullPath).TrimEnd('\\', '/');
                if (!current.FolderChildrenLoaded)
                    await EnsureFolderChildrenLoadedAsync(current);

                if (normalized.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                    return current;

                if (!IndexPaths.IsPathUnderFolderRoot(normalized, currentPath))
                    break;

                var relative = normalized[(currentPath.Length + 1)..];
                var nextName = relative.Contains('\\') ? relative[..relative.IndexOf('\\')] : relative;
                var child = current.Children?.FirstOrDefault(c => c.IsFolder
                    && c.Name.Equals(nextName, StringComparison.OrdinalIgnoreCase));
                if (child == null)
                    break;
                current = child;
            }
        }

        return TreeBuilder.FindFolderNode(treeNodes, normalized);
    }

    /// <summary>インデックス対象と同じ拡張子集合（個人設定の TargetExtensions を反映）。</summary>
    private HashSet<string>? GetBrowseExtensionSet()
    {
        var set = PreviewHelper.BuildTargetExtensionSet(
            TextExtractors.SelectMany(e => e.SupportedExtensions),
            SettingsService.Settings.TargetExtensions);
        return set.Count > 0 ? set : null;
    }
}
