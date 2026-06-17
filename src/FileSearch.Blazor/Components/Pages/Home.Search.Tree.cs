// =============================================================================
// Home.Search.Tree.cs — partial class Home
// =============================================================================
// 役割: 検索の実行（Enter）、ツリー構築、フォルダ一覧のソート/フィルター/行クリック、ファイル選択。
// 文言: FileSearch.Messages.UserMessages（変更時は docs/メッセージ一覧.md）
// 状態: フィールドの定義は Home.razor.cs
// =============================================================================
using FileSearch.Messages;
using FullTextSearch.Core;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Settings;
using FullTextSearch.Core.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace FileSearch.Blazor.Components.Pages;

public partial class Home
{
    /// <summary>
    /// 1 分ごと。間隔経過かつインデックス非実行中で、ユーザーが検索操作をしていない（アイドル）場合に差分更新を起動。
    /// 検索中・直近の検索操作からアイドル秒数未満であれば見送り、次回 tick で再判定する。
    /// </summary>
    private void OnAutoRebuildTick(object? _)
    {
        try
        {
            // 非管理者は参照専用のため自動再構築も行わない。
            if (!isAdmin) return;
            var interval = SettingsService.Settings.AutoRebuildIntervalMinutes;
            if (interval <= 0 || isIndexing) return;
            // 検索中、もしくは直近で検索操作（入力・キー操作・検索実行）があった直後は見送る。
            // 1 分後の次 tick で再判定するので、ユーザー操作が落ち着いた段階で実行される。
            if (isSearching) return;
            if ((DateTime.UtcNow - _lastSearchActivityUtc).TotalSeconds < AutoRebuildIdleSeconds) return;
            if (AutoRebuildSchedule.IsDue(interval, SettingsService.Settings.LastIndexUpdate, DateTime.UtcNow))
                _ = InvokeAsync(UpdateIndex);
        }
        catch { /* timer thread: ignore */ }
    }

    /// <summary>Enter で検索、Esc でクエリとエラーをクリアしフォルダ体系表示に戻す。</summary>
    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        _lastSearchActivityUtc = DateTime.UtcNow;
        if (e.Key == "Enter" && !isIndexing)
            await ExecuteSearch();
        if (e.Key == "Escape" && !isIndexing)
        {
            searchQuery = string.Empty;
            searchErrorMessage = null;
            _lastExecutedSearchQuery = null;
            totalFileCount = 0;
            selectedFile = null;
            selectedFolder = null;
            await RefreshFolderSkeletonTreeAsync();
        }
    }

    /// <summary>検索前の初期表示用に、対象フォルダ配下を一括読み込みしてツリーへ反映する。</summary>
    private async Task RefreshFolderSkeletonTreeAsync()
    {
        if (_lastExecutedSearchQuery != null)
            return;

        var folders = SettingsService.Settings.TargetFolders;
        if (folders.Count == 0)
        {
            CancelFolderTreeLoad();
            treeNodes = [];
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
            built = await Task.Run(() => TreeBuilder.BuildFullFolderTree(folders, extensions), loadToken);
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

    /// <summary>閲覧モードで最初のルートフォルダを右ペインに表示する（GitHub の repo ルート相当）。</summary>
    private void TrySelectInitialBrowseFolder(IReadOnlyList<TreeNode> roots)
    {
        if (_lastExecutedSearchQuery != null || selectedFile != null || selectedFolder != null || roots.Count == 0)
            return;

        var root = roots[0];
        root.IsExpanded = true;
        selectedFolder = root;
        selectedFolderRowIndex = 0;
        ScheduleFolderContentPreviewsLoad(root);
    }

    /// <summary>バックグラウンドのフォルダツリー読み込みを中断する（検索割り込み時）。</summary>
    private void CancelFolderTreeLoad()
    {
        _folderTreeLoadCts?.Cancel();
        _folderTreeLoadCts?.Dispose();
        _folderTreeLoadCts = null;
        isLoadingFolderTree = false;
    }

    /// <summary>検索欄の双方向バインド用。</summary>
    private void OnSearchQueryChangedAsync(string v)
    {
        searchQuery = v;
        _lastSearchActivityUtc = DateTime.UtcNow;
    }

    /// <summary>検索モード変更。入力中のキーワードがあれば同語句で再検索する（スピナー表示）。</summary>
    private async Task OnSearchModeChangedAsync(SearchMode mode)
    {
        if (searchMode == mode) return;
        searchMode = mode;
        _lastSearchActivityUtc = DateTime.UtcNow;
        if (!isIndexing && !string.IsNullOrWhiteSpace(searchQuery?.Trim()))
            await ExecuteSearch();
    }

    /// <summary>入力イベント用フック（検索操作の活動時刻を記録）。</summary>
    private void OnSearchInputChanged()
    {
        _lastSearchActivityUtc = DateTime.UtcNow;
    }

    /// <summary>検索実行。結果からツリーを構築し件数を設定。</summary>
    private async Task ExecuteSearch()
    {
        var query = searchQuery?.Trim() ?? "";
        if (isIndexing || string.IsNullOrWhiteSpace(query)) return;
        CancelFolderTreeLoad();
        _lastSearchActivityUtc = DateTime.UtcNow;
        _lastExecutedSearchQuery = query;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        searchErrorMessage = null;
        isSearching = true;
        selectedFile = null;
        selectedFolder = null;
        selectedFolderRowIndex = -1;
        StateHasChanged();
        try
        {
            var result = await SearchService.SearchAsync(query, new SearchOptions
            {
                MaxResults = ContentLimits.UnlimitedSearchResults,
                SearchMode = searchMode,
            }, token);
            if (token.IsCancellationRequested) return;
            var items = FilterByTargetExtensions(result.Items);
            treeNodes = TreeBuilder.BuildTree(SettingsService.Settings.TargetFolders, items);
            totalFileCount = items.Count;
            _lastTreeSyncFilePath = null;
            _lastTreeSyncFolderPath = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            searchErrorMessage = UserMessages.SearchFailed;
            Logger.LogError(ex, "Search failed");
        }
        finally { isSearching = false; StateHasChanged(); }
    }

    /// <summary>
    /// 検索結果を、現在のユーザーの対象拡張子（個人設定）で絞り込む。
    /// 共有インデックスに全種別が入っていても、各ユーザーは自分の選択拡張子だけを表示できる。
    /// 対象拡張子が未設定（空）の場合は絞り込まない。
    /// </summary>
    private List<SearchResultItem> FilterByTargetExtensions(List<SearchResultItem> items)
    {
        var exts = SettingsService.Settings.TargetExtensions;
        if (exts == null || exts.Count == 0) return items;
        var allowed = new HashSet<string>(exts, StringComparer.OrdinalIgnoreCase);
        return items.Where(i => allowed.Contains(Path.GetExtension(i.FilePath))).ToList();
    }

    /// <summary>フォルダの展開/折りたたみ。展開時はフォルダビューに切り替え。</summary>
    private void ToggleNode(TreeNode node)
    {
        if (isIndexing || !node.IsFolder) return;

        if (selectedFile != null)
        {
            _ = OpenFolderFromTreeAsync(node);
            return;
        }

        var expanding = !node.IsExpanded;
        node.IsExpanded = expanding;

        if (expanding)
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
        else if (selectedFolder != null && IsPathUnderFolder(node.FullPath, selectedFolder.FullPath))
        {
            selectedFolder = null;
            selectedFolderRowIndex = -1;
        }

        StateHasChanged();
    }

    /// <summary>プレビュー中に左ツリーでフォルダを選んだとき、右ペインをフォルダ一覧へ切り替える。</summary>
    private async Task OpenFolderFromTreeAsync(TreeNode node)
    {
        if (isIndexing || !node.IsFolder)
            return;

        Interlocked.Increment(ref _folderNavigationGeneration);
        _previewCts?.Cancel();
        selectedFile = null;
        _previewResult = null;
        isLoadingPreview = false;

        node.IsExpanded = true;
        await EnsureFolderChildrenLoadedAsync(node);

        selectedFolder = node;
        selectedFolderRowIndex = 0;
        ScheduleFolderContentPreviewsLoad(node);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>右ペインのフォルダ一覧を開く。</summary>
    private void ApplyFolderSelection(TreeNode node)
    {
        if (selectedFile != null)
        {
            _previewCts?.Cancel();
            selectedFile = null;
            _previewResult = null;
            isLoadingPreview = false;
        }

        selectedFolder = node;
        selectedFolderRowIndex = 0;
        ScheduleFolderContentPreviewsLoad(node);
    }

    /// <summary><paramref name="path"/> が <paramref name="folderPath"/> 配下（または同一）か。</summary>
    private static bool IsPathUnderFolder(string folderPath, string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var root = folderPath.TrimEnd('\\', '/');
        var p = path.TrimEnd('\\', '/');
        return p.Equals(root, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>閲覧モードでフォルダ直下の子を読み込む（起動時一括読み込み後は通常 no-op）。</summary>
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
            if (!normalized.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith(rootPath + "\\", StringComparison.OrdinalIgnoreCase))
                continue;

            var current = root;
            while (true)
            {
                var currentPath = IndexPaths.NormalizeFolderPath(current.FullPath).TrimEnd('\\', '/');
                if (!current.FolderChildrenLoaded)
                    await EnsureFolderChildrenLoadedAsync(current);

                if (normalized.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                    return current;

                if (!normalized.StartsWith(currentPath + "\\", StringComparison.OrdinalIgnoreCase))
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

    /// <summary>内容列ソート用。プレビュー未取得・フォルダは空文字。</summary>
    private string GetPreviewSortKey(TreeNode node)
    {
        if (node.IsFolder || string.IsNullOrEmpty(node.FilePath))
            return "";
        return _fileContentPreviews.TryGetValue(node.FilePath, out var preview) && !string.IsNullOrEmpty(preview)
            ? preview
            : "";
    }

    /// <summary>フォルダ一覧テーブルのソート列。同一列なら昇降切替。</summary>
    private void SetSort(string column)
    {
        if (sortColumn == column) sortAscending = !sortAscending;
        else { sortColumn = column; sortAscending = true; }
        selectedFolderRowIndex = 0;
    }

    /// <summary>拡張子フィルターのドロップダウン開閉。</summary>
    private void ToggleFilterMenu() => showFilterMenu = !showFilterMenu;

    /// <summary>拡張子フィルター。空文字は「すべて」。</summary>
    private void SetFilter(string type)
    {
        filterType = type;
        showFilterMenu = false;
        selectedFolderRowIndex = 0;
    }

    /// <summary>現在の filterType / sortColumn に応じて子ノードを並べ替え。</summary>
    private IEnumerable<TreeNode> GetSortedAndFilteredItems(List<TreeNode> items)
    {
        var filtered = items.AsEnumerable();
        if (!string.IsNullOrEmpty(filterType))
        {
            if (filterType == "folder") filtered = filtered.Where(i => i.IsFolder);
            else filtered = filtered.Where(i => !i.IsFolder && Path.GetExtension(i.Name).Equals(filterType, StringComparison.OrdinalIgnoreCase));
        }
        filtered = sortColumn switch
        {
            "name" => sortAscending ? filtered.OrderBy(i => !i.IsFolder).ThenBy(i => i.Name) : filtered.OrderBy(i => !i.IsFolder).ThenByDescending(i => i.Name),
            "preview" => sortAscending ? filtered.OrderBy(i => GetPreviewSortKey(i)) : filtered.OrderByDescending(i => GetPreviewSortKey(i)),
            "date" => sortAscending ? filtered.OrderBy(i => i.LastModified) : filtered.OrderByDescending(i => i.LastModified),
            _ => filtered.OrderBy(i => !i.IsFolder).ThenBy(i => i.Name)
        };
        return filtered;
    }

    /// <summary>ファイル選択。ツリー展開・全ファイルフラットリスト・プレビュー読み込みを連動。</summary>
    private void SelectFile(TreeNode node)
    {
        if (isIndexing) return;
        if (node.IsFolder || string.IsNullOrEmpty(node.FilePath))
            return;

        Interlocked.Increment(ref _folderNavigationGeneration);
        selectedFolder = null;
        selectedFolderRowIndex = -1;
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

    /// <summary>フォルダなら階層に入る。ファイルなら SelectFile。</summary>
    private void OnFolderItemClick(TreeNode item)
    {
        if (isIndexing) return;
        if (item.IsFolder)
        {
            var generation = Interlocked.Increment(ref _folderNavigationGeneration);
            _ = InvokeAsync(async () =>
            {
                item.IsExpanded = true;
                await EnsureFolderChildrenLoadedAsync(item);
                if (generation != _folderNavigationGeneration)
                {
                    StateHasChanged();
                    return;
                }
                if (selectedFile != null)
                {
                    _previewCts?.Cancel();
                    selectedFile = null;
                    _previewResult = null;
                    isLoadingPreview = false;
                }
                selectedFolder = item;
                selectedFolderRowIndex = 0;
                ScheduleFolderContentPreviewsLoad(item);
                StateHasChanged();
            });
        }
        else SelectFile(item);
    }

    /// <summary>表の行クリック。選択行インデックスを更新して OnFolderItemClick。</summary>
    private void OnFolderRowClick(TreeNode item)
    {
        if (selectedFolder == null) return;
        if (item.IsFolder && selectedFolder.Children == null) return;
        if (!item.IsFolder && selectedFolder.Children != null)
        {
            var list = GetSortedAndFilteredItems(selectedFolder.Children).ToList();
            selectedFolderRowIndex = list.IndexOf(item);
            if (selectedFolderRowIndex < 0) selectedFolderRowIndex = 0;
        }
        OnFolderItemClick(item);
    }

    /// <summary>パンくずから任意の祖先フォルダへ移動。</summary>
    private void NavigateToFolder(TreeNode folder)
    {
        if (isIndexing || folder == null || !folder.IsFolder || folder == selectedFolder) return;
        folder.IsExpanded = true;
        selectedFile = null;
        selectedFolder = folder;
        selectedFolderRowIndex = 0;
        ScheduleFolderContentPreviewsLoad(folder);
        StateHasChanged();
    }

    /// <summary>フォルダ一覧のファイル名横プレビューを読み込む。</summary>
    private void ScheduleFolderContentPreviewsLoad(TreeNode? folder)
    {
        if (folder?.Children == null)
        {
            _fileContentPreviews = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var paths = folder.Children
            .Where(c => !c.IsFolder && !string.IsNullOrEmpty(c.FilePath))
            .Select(c => c.FilePath!)
            .ToList();
        ScheduleFileContentPreviewsLoad(paths);
    }

    /// <summary>指定ファイルの先頭行プレビューをインデックス／ディスクから非同期取得する。</summary>
    private void ScheduleFileContentPreviewsLoad(IReadOnlyList<string> filePaths)
    {
        _filePreviewCts?.Cancel();
        _filePreviewCts?.Dispose();
        _filePreviewCts = null;

        if (filePaths.Count == 0)
        {
            _fileContentPreviews = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var paths = filePaths.Count <= MaxFileContentPreviews
            ? filePaths
            : filePaths.Take(MaxFileContentPreviews).ToList();

        var generation = Interlocked.Increment(ref _filePreviewGeneration);
        _fileContentPreviews = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _filePreviewCts = new CancellationTokenSource();
        var token = _filePreviewCts.Token;
        var searchQuery = _lastExecutedSearchQuery;
        var mode = searchMode;

        _ = Task.Run(async () =>
        {
            try
            {
                var merged = await SearchService.TryGetContentPreviewsAsync(
                    paths, searchQuery, mode, token).ConfigureAwait(false);

                if (token.IsCancellationRequested || generation != _filePreviewGeneration)
                    return;
                _fileContentPreviews = merged;
                await InvokeAsync(StateHasChanged);
            }
            catch (OperationCanceledException)
            {
                // フォルダ切替・再検索でキャンセル
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "File content preview load failed");
            }
        }, token);
    }
}
