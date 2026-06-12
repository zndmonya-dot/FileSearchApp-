// =============================================================================
// Home.Search.Tree.cs — partial class Home
// =============================================================================
// 役割: 検索の実行（Enter）、ツリー構築、フォルダ一覧のソート/フィルター/行クリック、ファイル選択。
// 文言: FileSearch.Messages.UserMessages（変更時は docs/メッセージ一覧.md）
// 状態: フィールドの定義は Home.razor.cs
// =============================================================================
using FileSearch.Messages;
using FullTextSearch.Core;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Settings;
using FileSearch.Blazor.Components.Shared;
using FileSearch.Blazor.Services;
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

    /// <summary>Enter で検索、Esc でクエリとエラーをクリア。</summary>
    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        _lastSearchActivityUtc = DateTime.UtcNow;
        if (e.Key == "Enter" && !isIndexing)
            await ExecuteSearch();
        if (e.Key == "Escape") { searchQuery = string.Empty; searchErrorMessage = null; await InvokeAsync(StateHasChanged); }
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
        _lastSearchActivityUtc = DateTime.UtcNow;
        _lastExecutedSearchQuery = query;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        searchErrorMessage = null;
        isSearching = true; treeNodes.Clear(); selectedFile = null; totalFileCount = 0;
        StateHasChanged();
        try
        {
            const int searchLimit = 100_000; // 検索結果の最大件数
            var result = await SearchService.SearchAsync(query, new SearchOptions
            {
                MaxResults = searchLimit,
                SearchMode = searchMode,
            }, token);
            if (token.IsCancellationRequested) return;
            var items = FilterByTargetExtensions(result.Items);
            treeNodes = TreeBuilder.BuildTree(SettingsService.Settings.TargetFolders, items);
            totalFileCount = items.Count;
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
        if (!node.IsFolder) return;
        // 展開時の大量描画を1フレーム遅延し、UIの応答性を保つ
        _ = InvokeAsync(async () =>
        {
            await Task.Yield();
            node.IsExpanded = !node.IsExpanded;

            if (node.IsExpanded)
            {
                selectedFile = null;
                selectedFolder = node;
                selectedFolderRowIndex = 0;
            }
            else if (selectedFolder == node)
            {
                selectedFolder = null;
                selectedFolderRowIndex = -1;
            }

            StateHasChanged();
        });
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

    /// <summary>フィルタードロップダウン用の拡張子一覧（重複除去・ソート）。</summary>
    private IEnumerable<string> GetUniqueExtensions(List<TreeNode> items) => items
        .Where(i => !i.IsFolder && !string.IsNullOrEmpty(Path.GetExtension(i.Name)))
        .Select(i => Path.GetExtension(i.Name).ToLowerInvariant())
        .Distinct()
        .OrderBy(e => e);

    /// <summary>ソート中の列に矢印 SVG を返す。</summary>
    private MarkupString GetSortIcon(string column)
    {
        if (sortColumn != column) return new MarkupString("");
        return new MarkupString(sortAscending
            ? "<svg class='sort-icon' viewBox='0 0 16 16' fill='currentColor'><path d='m4.427 7.427 3.396 3.396a.25.25 0 0 0 .354 0l3.396-3.396A.25.25 0 0 0 11.396 7H4.604a.25.25 0 0 0-.177.427Z'/></svg>"
            : "<svg class='sort-icon' viewBox='0 0 16 16' fill='currentColor'><path d='m4.427 9.573 3.396-3.396a.25.25 0 0 1 .354 0l3.396 3.396a.25.25 0 0 1-.177.427H4.604a.25.25 0 0 1-.177-.427Z'/></svg>");
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
            "date" => sortAscending ? filtered.OrderBy(i => i.LastModified) : filtered.OrderByDescending(i => i.LastModified),
            "type" => sortAscending ? filtered.OrderBy(i => i.IsFolder ? "" : Path.GetExtension(i.Name)) : filtered.OrderByDescending(i => i.IsFolder ? "" : Path.GetExtension(i.Name)),
            "size" => sortAscending ? filtered.OrderBy(i => i.FileSize) : filtered.OrderByDescending(i => i.FileSize),
            _ => filtered.OrderBy(i => !i.IsFolder).ThenBy(i => i.Name)
        };
        return filtered;
    }

    /// <summary>ファイル選択。ツリー展開・全ファイルフラットリスト・プレビュー読み込みを連動。</summary>
    private void SelectFile(TreeNode node)
    {
        if (node.FileData == null) return;
        selectedFolder = null;
        selectedFile = node.FileData;
        _previewLines = Array.Empty<PreviewLineResult>();
        _previewLinesDisplayCache = null;
        previewLineCount = 0;
        isLoadingPreview = true;
        TreeBuilder.ExpandPathToFile(treeNodes, node.FilePath!);
        _fileNavList = TreeBuilder.CollectAllFileNodes(treeNodes);
        _fileNavIndex = _fileNavList.FindIndex(n => string.Equals(n.FilePath, node.FilePath, StringComparison.OrdinalIgnoreCase));
        if (_fileNavIndex < 0) _fileNavIndex = 0;
        SchedulePreviewLoad(node.FilePath!);
    }

    /// <summary>フォルダなら階層に入る。ファイルなら SelectFile。</summary>
    private void OnFolderItemClick(TreeNode item)
    {
        if (item.IsFolder)
        {
            item.IsExpanded = true;
            selectedFile = null;
            selectedFolder = item;
            selectedFolderRowIndex = 0;
        }
        else SelectFile(item);
    }

    /// <summary>表の行クリック。選択行インデックスを更新して OnFolderItemClick。</summary>
    private void OnFolderRowClick(TreeNode item)
    {
        if (selectedFolder?.Children == null) return;
        var list = GetSortedAndFilteredItems(selectedFolder.Children).ToList();
        selectedFolderRowIndex = list.IndexOf(item);
        if (selectedFolderRowIndex < 0) selectedFolderRowIndex = 0;
        OnFolderItemClick(item);
    }

    /// <summary>「親フォルダへ」。親を展開して一覧の選択行を子フォルダに合わせる。</summary>
    private void GoToParentFolder()
    {
        if (selectedFolder?.Parent == null) return;
        var fromChild = selectedFolder;
        var parent = selectedFolder.Parent;
        parent.IsExpanded = true;
        selectedFile = null;
        selectedFolder = parent;
        var list = GetSortedAndFilteredItems(parent.Children ?? new List<TreeNode>()).ToList();
        selectedFolderRowIndex = list.IndexOf(fromChild);
        if (selectedFolderRowIndex < 0) selectedFolderRowIndex = 0;
        StateHasChanged();
    }
}
