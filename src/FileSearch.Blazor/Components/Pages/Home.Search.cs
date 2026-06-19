// =============================================================================
// Home.Search.cs — partial class Home
// =============================================================================
// 役割: 検索実行、結果フィルタ、閲覧モード復帰、定期再構築タイマー。
// =============================================================================
using FileSearch.Messages;
using FullTextSearch.Core;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Search;
using FullTextSearch.Core.UI;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace FileSearch.Blazor.Components.Pages;

public partial class Home
{
    /// <summary>
    /// 1 分ごと。間隔経過かつインデックス非実行中で、ユーザーが検索操作をしていない（アイドル）場合に差分更新を起動。
    /// </summary>
    private void OnAutoRebuildTick(object? _)
    {
        try
        {
            if (!isAdmin) return;
            var hours = SettingsService.Settings.AutoRebuildDailyHours;
            if (hours.Count == 0 || isIndexing) return;
            if (isSearching) return;
            if ((DateTime.UtcNow - _lastSearchActivityUtc).TotalSeconds < AutoRebuildIdleSeconds) return;
            if (AutoRebuildSchedule.IsDueAtDailyHours(hours, SettingsService.Settings.LastIndexUpdate, DateTime.UtcNow))
                _ = InvokeAsync(UpdateIndex);
        }
        catch { /* timer thread: ignore */ }
    }

    /// <summary>Enter で検索（空なら閲覧モードへ）、Esc で閲覧モードの初期表示に戻す。</summary>
    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        _lastSearchActivityUtc = DateTime.UtcNow;
        if (e.Key == "Enter" && !isIndexing)
            await ExecuteSearch();
        if (e.Key == "Escape" && !isIndexing)
            await ReturnToBrowseModeAsync();
    }

    /// <summary>検索前の閲覧モードへ戻す（左検索欄・右ペイン絞り込み・選択状態を初期化）。</summary>
    private async Task ReturnToBrowseModeAsync()
    {
        if (_lastExecutedSearchQuery == null && selectedFile == null && selectedFolder == null
            && string.IsNullOrWhiteSpace(searchQuery) && string.IsNullOrWhiteSpace(folderFileSearchQuery))
            return;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        searchQuery = string.Empty;
        searchErrorMessage = null;
        _lastExecutedSearchQuery = null;
        totalFileCount = 0;
        selectedFile = null;
        selectedFolder = null;
        selectedFolderRowIndex = -1;
        ResetFolderListFilters();
        await RefreshFolderSkeletonTreeAsync();
    }

    /// <summary>検索欄の双方向バインド用。</summary>
    private void OnSearchQueryChangedAsync(string v)
    {
        searchQuery = v;
        _lastSearchActivityUtc = DateTime.UtcNow;
    }

    /// <summary>検索モード変更。入力中のキーワードがあれば同語句で再検索する。</summary>
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

    /// <summary>検索実行。空クエリのときは閲覧モードへ戻す。</summary>
    private async Task ExecuteSearch()
    {
        var query = searchQuery?.Trim() ?? "";
        if (isIndexing) return;
        if (string.IsNullOrWhiteSpace(query))
        {
            await ReturnToBrowseModeAsync();
            return;
        }

        CancelFolderTreeLoad();
        _lastSearchActivityUtc = DateTime.UtcNow;
        _lastExecutedSearchQuery = query;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        searchErrorMessage = null;
        isSearching = true;
        totalFileCount = 0;
        treeNodes = [];
        selectedFile = null;
        selectedFolder = null;
        selectedFolderRowIndex = -1;
        ResetFolderListFilters();
        StateHasChanged();
        try
        {
            var result = await SearchService.SearchAsync(query, new SearchOptions
            {
                MaxResults = ContentLimits.UnlimitedSearchResults,
                SearchMode = searchMode,
            }, null, token);
            if (token.IsCancellationRequested) return;
            var items = FilterByTargetExtensions(result.Items);
            items = FilterByActiveTargetFolders(items);
            treeNodes = TreeBuilder.BuildTree(GetActiveTargetFolders(), items);
            TreeBuilder.MarkFolderTreeLoaded(treeNodes);
            totalFileCount = TreeBuilder.CollectAllFileNodes(treeNodes).Count;
            _lastTreeSyncFilePath = null;
            _lastTreeSyncFolderPath = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            searchErrorMessage = UserMessages.SearchFailed;
            Logger.LogError(ex, "Search failed");
        }
        finally
        {
            isSearching = false;
            StateHasChanged();
        }
    }

    /// <summary>検索結果を、現在のユーザーの対象拡張子（個人設定）で絞り込む。</summary>
    private List<SearchResultItem> FilterByTargetExtensions(List<SearchResultItem> items)
    {
        var exts = SettingsService.Settings.TargetExtensions;
        if (exts == null || exts.Count == 0) return items;
        var allowed = new HashSet<string>(exts, StringComparer.OrdinalIgnoreCase);
        return items.Where(i => allowed.Contains(Path.GetExtension(i.FilePath))).ToList();
    }

    /// <summary>検索結果を、有効な対象フォルダ（利用者の個人フィルタ）で絞り込む。</summary>
    private List<SearchResultItem> FilterByActiveTargetFolders(List<SearchResultItem> items)
    {
        var folders = GetActiveTargetFolders();
        if (folders.Count == 0)
            return [];

        var normalized = folders.Select(IndexPaths.NormalizeFolderPath).ToList();
        return items
            .Where(i => IndexPaths.IsPathUnderAnyFolder(i.FilePath, normalized))
            .ToList();
    }
}
