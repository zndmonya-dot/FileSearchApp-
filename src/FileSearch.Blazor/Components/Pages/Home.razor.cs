// メイン画面のコードビハインド。検索・ツリー選択・プレビュー・インデックス更新・設定の状態とイベント処理を行う。
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Settings;
using FileSearch.Blazor.Components.Shared;
using FileSearch.Blazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace FileSearch.Blazor.Components.Pages;

/// <summary>
/// メイン画面（/）。検索入力・結果ツリー・プレビュー・サイドバー・設定・インデックス更新を統合する。
/// </summary>
public partial class Home : IDisposable
{
    #region 状態（検索・選択・プレビュー・設定・UI）

    private string searchQuery = "";
    private List<TreeNode> treeNodes = new();
    private SearchResultItem? selectedFile;
    private TreeNode? selectedFolder;
    private int totalFileCount = 0;
    private IReadOnlyList<PreviewLineResult> _previewLines = Array.Empty<PreviewLineResult>();
    private int previewLineCount = 0;
    private int indexCount = 0;
    private bool isSearching = false;
    private bool isLoadingPreview = false;
    private bool isIndexing = false;
    private bool showSettings = false;
    private bool isDarkMode = true;
    private readonly SettingsEditState _settingsEdit = new();
    private int sidebarWidth = 300;
    private Timer? _autoRebuildTimer;
    private bool isResizing = false;
    private double resizeStartX = 0;
    private int resizeStartWidth = 0;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _indexCts;
    private Timer? _previewDebounceTimer;
    private string? _pendingPreviewPath;
    private const int PreviewDebounceMs = 500;
    private const int ProgressReportInterval = 500;
    private const int ProgressReportThrottleMs = 250;
    private int _lastReportedProgressCount = -1;
    private DateTime _lastReportedProgressTime;

    private const string MsgNoTargetFolders = "対象フォルダを設定してください。";
    private const string MsgUpdateFailed = "差分更新に失敗しました。";
    private const string MsgRebuildFailed = "インデックス構築に失敗しました。";

    private string sortColumn = "name";
    private bool sortAscending = true;
    private string filterType = "";
    private bool showFilterMenu = false;
    private int selectedFolderRowIndex = -1;
    private int indexProgressPercent = 0;
    private string indexProgressText = "";
    private string? searchErrorMessage = null;
    private string? indexErrorMessage = null;
    private string? SearchErrorMessage => searchErrorMessage;
    private string? IndexErrorMessage => indexErrorMessage;
    private string? _lastHighlightNavFilePath;
    private string? _highlightNavInfo;
    private bool _hasTriedInitialHighlightScroll;
    private List<TreeNode>? _fileNavList;
    private int _fileNavIndex = -1;
    private bool _showRebuildConfirm;
    private bool _indexUpdateFullRebuild;
    /// <summary>最後に実際に実行した検索クエリ（入力中は未実行と区別するため）</summary>
    private string? _lastExecutedSearchQuery;

    /// <summary>現在の入力がすでに検索実行済みか（未実行なら「Enter で検索」と表示）</summary>
    private bool HasSearchedCurrentQuery => _lastExecutedSearchQuery != null && (searchQuery?.Trim() ?? "") == _lastExecutedSearchQuery;

    #endregion

    #region ライフサイクル（初回テーマ・ハイライトスクロール）

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var treeChanged = false;
        if (selectedFile != null && !string.IsNullOrEmpty(selectedFile.FilePath) && TreeBuilder.ExpandPathToFile(treeNodes, selectedFile.FilePath))
            treeChanged = true;
        if (selectedFolder != null && !string.IsNullOrEmpty(selectedFolder.FullPath) && TreeBuilder.ExpandPathToFolder(treeNodes, selectedFolder.FullPath))
            treeChanged = true;
        if (treeChanged)
        {
            await Task.Yield();
            StateHasChanged();
        }
        if (firstRender && string.Equals(SettingsService.Settings.ThemeMode, "System", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var systemDark = await GetPreferredColorSchemeFromSystemAsync();
                if (isDarkMode != systemDark) { isDarkMode = systemDark; StateHasChanged(); }
            }
            catch { /* JS not ready */ }
        }
        if (!firstRender && selectedFile?.FilePath != _lastHighlightNavFilePath)
        {
            _lastHighlightNavFilePath = selectedFile?.FilePath;
            _highlightNavInfo = null;
            _hasTriedInitialHighlightScroll = false;
            try { await JSRuntime.InvokeVoidAsync("resetHighlightNav"); }
            catch { /* JS not ready */ }
        }
        if (!firstRender && !isLoadingPreview && ShowHighlightNav && !_hasTriedInitialHighlightScroll)
        {
            _hasTriedInitialHighlightScroll = true;
            try
            {
                var result = await JSRuntime.InvokeAsync<string?>("scrollToFirstHighlightInstant");
                if (!string.IsNullOrEmpty(result))
                {
                    _highlightNavInfo = FormatHighlightNavInfo(result);
                    StateHasChanged();
                }
            }
            catch { /* JS not ready */ }
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await SettingsService.LoadAsync();
        ApplyThemeFromSettings();
        var indexPath = SettingsService.Settings.IndexPath;
        if (!string.IsNullOrWhiteSpace(indexPath))
        {
            await IndexService.InitializeAsync(indexPath);
            indexCount = IndexService.GetStats().DocumentCount;
            SearchService.Warmup();
        }
        _autoRebuildTimer = new Timer(OnAutoRebuildTick, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    #endregion

    #region 検索とツリー（ExecuteSearch / ノード展開・選択・ソート・フィルター）

    private void OnAutoRebuildTick(object? _)
    {
        try
        {
            var interval = SettingsService.Settings.AutoRebuildIntervalMinutes;
            if (interval <= 0 || isIndexing) return;
            var last = SettingsService.Settings.LastIndexUpdate;
            if (!last.HasValue || (DateTime.Now - last.Value).TotalMinutes >= interval)
                _ = InvokeAsync(UpdateIndex);
        }
        catch { /* timer thread: ignore */ }
    }

    public void Dispose()
    {
        _autoRebuildTimer?.Dispose();
        _autoRebuildTimer = null;
        _previewDebounceTimer?.Dispose();
        _previewDebounceTimer = null;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
        _indexCts?.Cancel();
        _indexCts?.Dispose();
        _indexCts = null;
    }

    private void ApplyThemeFromSettings()
    {
        var mode = SettingsService.Settings.ThemeMode ?? "System";
        if (string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase))
            isDarkMode = true;
        else if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase))
            isDarkMode = false;
        else
            isDarkMode = true; // System: 初期値はダーク。OnAfterRenderAsync で JS から取得して更新
    }

    private async Task<bool> GetPreferredColorSchemeFromSystemAsync()
    {
        var scheme = await JSRuntime.InvokeAsync<string>("getPreferredColorScheme");
        return string.Equals(scheme, "dark", StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !isIndexing)
            await ExecuteSearch();
        if (e.Key == "Escape") { searchQuery = string.Empty; searchErrorMessage = null; await InvokeAsync(StateHasChanged); }
    }

    private void OnSearchQueryChangedAsync(string v)
    {
        searchQuery = v;
    }

    private void OnSearchInputChanged() { }

    private async Task ExecuteSearch()
    {
        var query = searchQuery?.Trim() ?? "";
        if (isIndexing || string.IsNullOrWhiteSpace(query)) return;
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
            const int searchLimit = 100_000;
            var result = await SearchService.SearchAsync(query, new SearchOptions { MaxResults = searchLimit }, token);
            if (token.IsCancellationRequested) return;
            treeNodes = TreeBuilder.BuildTree(SettingsService.Settings.TargetFolders, result.Items);
            totalFileCount = result.Items.Count;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            searchErrorMessage = "検索に失敗しました。";
            Logger.LogError(ex, "Search failed");
        }
        finally { isSearching = false; StateHasChanged(); }
    }

    private void ToggleNode(TreeNode node)
    {
        // 展開時の大量描画を1フレーム遅延し、UIの応答性を保つ
        _ = InvokeAsync(async () =>
        {
            await Task.Yield();
            node.IsExpanded = !node.IsExpanded;
            selectedFile = null;
            selectedFolder = node;
            selectedFolderRowIndex = 0;
            StateHasChanged();
        });
    }

    private void SetSort(string column)
    {
        if (sortColumn == column) sortAscending = !sortAscending;
        else { sortColumn = column; sortAscending = true; }
        selectedFolderRowIndex = 0;
    }

    private void ToggleFilterMenu() => showFilterMenu = !showFilterMenu;

    private void SetFilter(string type)
    {
        filterType = type;
        showFilterMenu = false;
        selectedFolderRowIndex = 0;
    }

    private IEnumerable<string> GetUniqueExtensions(List<TreeNode> items) => items
        .Where(i => !i.IsFolder && !string.IsNullOrEmpty(Path.GetExtension(i.Name)))
        .Select(i => Path.GetExtension(i.Name).ToLowerInvariant())
        .Distinct()
        .OrderBy(e => e);

    private MarkupString GetSortIcon(string column)
    {
        if (sortColumn != column) return new MarkupString("");
        return new MarkupString(sortAscending
            ? "<svg class='sort-icon' viewBox='0 0 16 16' fill='currentColor'><path d='m4.427 7.427 3.396 3.396a.25.25 0 0 0 .354 0l3.396-3.396A.25.25 0 0 0 11.396 7H4.604a.25.25 0 0 0-.177.427Z'/></svg>"
            : "<svg class='sort-icon' viewBox='0 0 16 16' fill='currentColor'><path d='m4.427 9.573 3.396-3.396a.25.25 0 0 1 .354 0l3.396 3.396a.25.25 0 0 1-.177.427H4.604a.25.25 0 0 1-.177-.427Z'/></svg>");
    }

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

    private void SelectFile(TreeNode node)
    {
        if (node.FileData == null) return;
        selectedFolder = null;
        selectedFile = node.FileData;
        TreeBuilder.ExpandPathToFile(treeNodes, node.FilePath!);
        _fileNavList = TreeBuilder.CollectAllFileNodes(treeNodes);
        _fileNavIndex = _fileNavList.FindIndex(n => string.Equals(n.FilePath, node.FilePath, StringComparison.OrdinalIgnoreCase));
        if (_fileNavIndex < 0) _fileNavIndex = 0;
        SchedulePreviewLoad(node.FilePath!);
    }

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

    private void OnFolderRowClick(TreeNode item)
    {
        if (selectedFolder?.Children == null) return;
        var list = GetSortedAndFilteredItems(selectedFolder.Children).ToList();
        selectedFolderRowIndex = list.IndexOf(item);
        if (selectedFolderRowIndex < 0) selectedFolderRowIndex = 0;
        OnFolderItemClick(item);
    }

    /// <summary>フォルダ一覧で「親フォルダへ」を押したとき。親を選択し、ツリーと連動させる。</summary>
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

    #endregion

    #region プレビューとナビゲーション（LoadPreview / 前へ・次へ / ファイル・フォルダを開く）

    /// <summary>プレビュー読み込みをデバウンスしてスケジュールする。連続クリック時に無駄な抽出を減らし、即時キャンセルでフリーズを防ぐ。</summary>
    private void SchedulePreviewLoad(string path)
    {
        _pendingPreviewPath = path;
        _previewCts?.Cancel();
        _previewDebounceTimer?.Dispose();
        _previewDebounceTimer = new Timer(_ =>
        {
            var p = _pendingPreviewPath;
            _pendingPreviewPath = null;
            if (!string.IsNullOrEmpty(p))
                _ = InvokeAsync(async () => { await LoadPreview(p); });
        }, null, PreviewDebounceMs, Timeout.Infinite);
    }

    private async Task LoadPreview(string path)
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;
        isLoadingPreview = true;
        _previewLines = Array.Empty<PreviewLineResult>();
        previewLineCount = 0;
        StateHasChanged();
        await Task.Yield();
        if (token.IsCancellationRequested) return;
        try
        {
            var result = await PreviewService.GetPreviewAsync(path, searchQuery?.Trim(), token);
            if (token.IsCancellationRequested) return;
            _previewLines = result.Lines ?? Array.Empty<PreviewLineResult>();
            previewLineCount = result.LineCount;
        }
        catch (OperationCanceledException)
        {
            _previewLines = new List<PreviewLineResult> { new("読み込みをキャンセルしました", false) };
            previewLineCount = 1;
        }
        catch (Exception ex)
        {
            _previewLines = new List<PreviewLineResult> { new($"[エラー] {ex.Message}", false) };
            previewLineCount = 1;
        }
        finally { isLoadingPreview = false; StateHasChanged(); }
    }

    private bool ShowFileNav => selectedFile != null && _fileNavList != null && _fileNavList.Count > 1;
    private bool ShowHighlightNav => selectedFile != null && !string.IsNullOrWhiteSpace(searchQuery);

    private bool ShowNavButtons => selectedFile != null && (ShowFileNav || ShowHighlightNav);

    private string? NavInfo => !string.IsNullOrEmpty(_highlightNavInfo) ? _highlightNavInfo
        : (_fileNavList != null && _fileNavIndex >= 0 && _fileNavIndex < _fileNavList.Count ? $"{_fileNavIndex + 1}/{_fileNavList.Count}" : null);

    private async Task<string?> TryScrollToHighlightAsync(bool next)
    {
        try
        {
            var fn = next ? "scrollToNextHighlight" : "scrollToPrevHighlight";
            var wrap = !ShowFileNav;
            return await JSRuntime.InvokeAsync<string?>(fn, wrap);
        }
        catch { return null; }
    }

    private async Task GoNext()
    {
        var result = await TryScrollToHighlightAsync(next: true);
        if (!string.IsNullOrEmpty(result))
        {
            _highlightNavInfo = FormatHighlightNavInfo(result);
            StateHasChanged();
            return;
        }
        if (ShowFileNav)
            SelectNextFile();
    }

    private async Task GoPrev()
    {
        var result = await TryScrollToHighlightAsync(next: false);
        if (!string.IsNullOrEmpty(result))
        {
            _highlightNavInfo = FormatHighlightNavInfo(result);
            StateHasChanged();
            return;
        }
        if (ShowFileNav)
            SelectPrevFile();
    }

    private static string? FormatHighlightNavInfo(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var parts = raw.Split('|');
        if (parts.Length != 3) return null;
        var lineNum = int.TryParse(parts[0], out var ln) ? ln : 0;
        var current = int.TryParse(parts[1], out var c) ? c : 0;
        var total = int.TryParse(parts[2], out var t) ? t : 0;
        if (total <= 0) return null;
        return lineNum > 0 ? $"{lineNum} 行目 ({current}/{total})" : $"{current}/{total}";
    }

    private void SelectNextFile()
    {
        if (_fileNavList == null || _fileNavList.Count < 2) return;
        var next = (_fileNavIndex + 1) % _fileNavList.Count;
        SelectFile(_fileNavList[next]);
    }

    private void SelectPrevFile()
    {
        if (_fileNavList == null || _fileNavList.Count < 2) return;
        var prev = _fileNavIndex <= 0 ? _fileNavList.Count - 1 : _fileNavIndex - 1;
        SelectFile(_fileNavList[prev]);
    }

    private void OpenFile()
    {
        if (selectedFile != null)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = selectedFile.FilePath, UseShellExecute = true });
    }

    private void OpenFolder()
    {
        if (selectedFile != null)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{selectedFile.FilePath}\"", UseShellExecute = true });
    }

    #endregion

    #region インデックス構築（再構築・差分更新・進捗・ダイアログ）

    private void OpenIndexFolder()
    {
        var path = SettingsService.Settings.IndexPath;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
            return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
    }

    private static IndexRebuildOptions GetIndexRebuildOptions(IAppSettingsService settings)
    {
        var exts = settings.Settings.TargetExtensions;
        return new IndexRebuildOptions { TargetExtensions = exts != null && exts.Count > 0 ? exts : null };
    }

    private IProgress<IndexProgress> CreateThrottledProgress(string countUnit)
    {
        _lastReportedProgressCount = -1;
        return new Progress<IndexProgress>(p =>
        {
            indexProgressPercent = p.TotalFiles > 0 ? (int)((double)p.ProcessedFiles / p.TotalFiles * 100) : 0;
            var baseText = p.ErrorCount > 0
                ? $"{p.ProcessedFiles:N0} / {p.TotalFiles:N0} {countUnit}（スキップ {p.ErrorCount:N0} 件）"
                : $"{p.ProcessedFiles:N0} / {p.TotalFiles:N0} {countUnit}";
            indexProgressText = string.IsNullOrEmpty(p.CurrentFile)
                ? baseText
                : Path.GetFileName(p.CurrentFile);
            var shouldUpdate = p.CurrentFile == null
                || (p.ProcessedFiles - _lastReportedProgressCount) >= ProgressReportInterval
                || (DateTime.UtcNow - _lastReportedProgressTime).TotalMilliseconds >= ProgressReportThrottleMs;
            if (shouldUpdate)
            {
                _lastReportedProgressCount = p.ProcessedFiles;
                _lastReportedProgressTime = DateTime.UtcNow;
                InvokeAsync(StateHasChanged);
            }
        });
    }

    private async Task RunIndexUpdateAsync(
        string initialMessage,
        string countUnit,
        Func<IProgress<IndexProgress>, CancellationToken, Task> runAsync,
        Func<Exception, string> getErrorMessage,
        string logContext)
    {
        if (isIndexing) return;
        if (SettingsService.Settings.TargetFolders.Count == 0)
        {
            indexErrorMessage = MsgNoTargetFolders;
            StateHasChanged();
            return;
        }
        indexErrorMessage = null;
        isIndexing = true;
        indexProgressPercent = 0;
        indexProgressText = initialMessage;
        _indexCts?.Dispose();
        _indexCts = new CancellationTokenSource();
        var token = _indexCts.Token;
        StateHasChanged();
        await Task.Yield();

        var progress = CreateThrottledProgress(countUnit);
        try
        {
            await Task.Run(async () => await runAsync(progress, token), token);
            if (token.IsCancellationRequested) return;
            indexCount = IndexService.GetStats().DocumentCount;
            SettingsService.Settings.LastIndexUpdate = DateTime.Now;
            await SettingsService.SaveAsync();
            indexErrorMessage = null;
        }
        catch (OperationCanceledException)
        {
            indexProgressText = "キャンセルしました";
            indexErrorMessage = null;
        }
        catch (Exception ex)
        {
            indexErrorMessage = getErrorMessage(ex);
            Logger.LogError(ex, logContext);
        }
        finally
        {
            _indexCts?.Dispose();
            _indexCts = null;
            isIndexing = false;
            indexProgressPercent = 0;
            StateHasChanged();
        }
    }

    private Task UpdateIndex()
    {
        var folders = SettingsService.Settings.TargetFolders;
        var options = GetIndexRebuildOptions(SettingsService);
        return RunIndexUpdateAsync(
            "差分を検出中...",
            "件",
            (p, ct) => IndexService.UpdateIndexAsync(folders, p, options, ct),
            ex => string.IsNullOrEmpty(ex.Message) ? MsgUpdateFailed : $"{MsgUpdateFailed} {ex.Message}",
            "Index update failed");
    }

    private Task RebuildIndex()
    {
        var folders = SettingsService.Settings.TargetFolders;
        var options = GetIndexRebuildOptions(SettingsService);
        return RunIndexUpdateAsync(
            "準備中...",
            "ファイル",
            (p, ct) => IndexService.RebuildIndexAsync(folders, p, options, ct),
            _ => MsgRebuildFailed,
            "Index rebuild failed");
    }

    private void RequestRebuildIndex()
    {
        if (isIndexing) return;
        if (SettingsService.Settings.TargetFolders.Count == 0)
        {
            indexErrorMessage = MsgNoTargetFolders;
            StateHasChanged();
            return;
        }
        indexErrorMessage = null;
        _indexUpdateFullRebuild = false;
        _showRebuildConfirm = true;
        StateHasChanged();
    }

    private void CancelRebuildConfirm()
    {
        _showRebuildConfirm = false;
        StateHasChanged();
    }

    private void OnIndexUpdateModeChanged(bool fullRebuild)
    {
        _indexUpdateFullRebuild = fullRebuild;
        StateHasChanged();
    }

    private async Task ConfirmIndexUpdateAsync()
    {
        _showRebuildConfirm = false;
        StateHasChanged();
        if (_indexUpdateFullRebuild)
            await RebuildIndex();
        else
            await UpdateIndex();
    }

    private void CancelIndexBuild()
    {
        _indexCts?.Cancel();
    }

    #endregion

    #region 設定（開く・保存・フォルダ・拡張子の追加削除）

    private string GetLastUpdateText() => DisplayFormatters.FormatLastIndexUpdate(SettingsService.Settings.LastIndexUpdate);

    private void OpenSettings()
    {
        _settingsEdit.TargetFolders = SettingsService.Settings.TargetFolders.ToList();
        _settingsEdit.IndexPath = SettingsService.Settings.IndexPath;
        _settingsEdit.TargetExtensions = SettingsService.Settings.TargetExtensions.ToList();
        _settingsEdit.AutoRebuildIntervalMinutes = SettingsService.Settings.AutoRebuildIntervalMinutes;
        _settingsEdit.ThemeMode = SettingsService.Settings.ThemeMode ?? "System";
        _settingsEdit.NewFolderPath = "";
        _settingsEdit.NewTargetExtension = "";
        _settingsEdit.ExtensionMessage = null;
        _settingsEdit.ExtensionLanguageMap = SettingsService.Settings.ExtensionLanguageMap != null && SettingsService.Settings.ExtensionLanguageMap.Count > 0
            ? new Dictionary<string, string>(SettingsService.Settings.ExtensionLanguageMap)
            : new Dictionary<string, string>();
        _settingsEdit.NewExtensionLanguageExt = "";
        _settingsEdit.NewExtensionLanguageLang = "";
        _settingsEdit.ExtensionLanguageMessage = null;
        showSettings = true;
    }

    private void CloseSettings() => showSettings = false;

    private void HandleAddFolder()
    {
        _settingsEdit.FolderMessage = null;
        var path = (_settingsEdit.NewFolderPath ?? "").Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(path))
        {
            _settingsEdit.FolderMessage = "フォルダパスを入力してください";
            return;
        }
        if (!Directory.Exists(path))
        {
            _settingsEdit.FolderMessage = "フォルダが見つかりません。パスを確認してください。";
            return;
        }
        var normalizedExisting = _settingsEdit.TargetFolders.Select(f => f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).ToList();
        if (normalizedExisting.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            _settingsEdit.FolderMessage = "既に追加されています";
            return;
        }
        _settingsEdit.TargetFolders.Add(path);
        _settingsEdit.NewFolderPath = "";
    }

    private void RemoveFolder(string f)
    {
        _settingsEdit.TargetFolders.Remove(f);
    }

    private void HandleAddTargetExtension()
    {
        var ext = (_settingsEdit.NewTargetExtension ?? "").Trim();
        if (!string.IsNullOrEmpty(ext) && !ext.StartsWith(".")) ext = "." + ext;
        if (string.IsNullOrEmpty(ext)) { _settingsEdit.ExtensionMessage = null; return; }
        if (_settingsEdit.TargetExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) { _settingsEdit.ExtensionMessage = "既に追加されています"; return; }
        _settingsEdit.TargetExtensions.Add(ext);
        _settingsEdit.NewTargetExtension = "";
        _settingsEdit.ExtensionMessage = null;
    }

    private void RemoveTargetExtension(string ext)
    {
        _settingsEdit.TargetExtensions.Remove(ext);
    }

    private void HandleAddExtensionLanguage()
    {
        _settingsEdit.ExtensionLanguageMessage = null;
        var ext = FullTextSearch.Core.Preview.PreviewHelper.NormalizeExtension(_settingsEdit.NewExtensionLanguageExt ?? "");
        var lang = (_settingsEdit.NewExtensionLanguageLang ?? "").Trim();
        if (string.IsNullOrEmpty(ext)) { _settingsEdit.ExtensionLanguageMessage = "拡張子を入力してください（例: .cs）"; return; }
        if (string.IsNullOrEmpty(lang)) { _settingsEdit.ExtensionLanguageMessage = "言語名を入力してください（例: csharp）"; return; }
        _settingsEdit.ExtensionLanguageMap ??= new Dictionary<string, string>();
        _settingsEdit.ExtensionLanguageMap[ext] = lang;
        _settingsEdit.NewExtensionLanguageExt = "";
        _settingsEdit.NewExtensionLanguageLang = "";
        StateHasChanged();
    }

    private void RemoveExtensionLanguage(string ext)
    {
        _settingsEdit.ExtensionLanguageMap?.Remove(ext);
        StateHasChanged();
    }

    private async Task SaveSettings()
    {
        SettingsService.Settings.TargetFolders = _settingsEdit.TargetFolders.ToList();
        if (!string.IsNullOrWhiteSpace(_settingsEdit.IndexPath)) SettingsService.Settings.IndexPath = _settingsEdit.IndexPath;
        SettingsService.Settings.TargetExtensions = _settingsEdit.TargetExtensions.ToList();
        SettingsService.Settings.AutoRebuildIntervalMinutes = _settingsEdit.AutoRebuildIntervalMinutes;
        SettingsService.Settings.ThemeMode = _settingsEdit.ThemeMode ?? "System";
        SettingsService.Settings.ExtensionLanguageMap = _settingsEdit.ExtensionLanguageMap != null && _settingsEdit.ExtensionLanguageMap.Count > 0
            ? new Dictionary<string, string>(_settingsEdit.ExtensionLanguageMap)
            : null;
        await SettingsService.SaveAsync();
        if (!string.IsNullOrWhiteSpace(SettingsService.Settings.IndexPath))
        {
            await IndexService.InitializeAsync(SettingsService.Settings.IndexPath);
            indexCount = IndexService.GetStats().DocumentCount;
        }
        SearchService.RefreshIndex();
        if (string.Equals(SettingsService.Settings.ThemeMode, "System", StringComparison.OrdinalIgnoreCase))
        {
            try { isDarkMode = await GetPreferredColorSchemeFromSystemAsync(); } catch { /* keep current */ }
        }
        else
        {
            ApplyThemeFromSettings();
        }
        showSettings = false;
    }

    #endregion

    #region サイドバーリサイズとヘルパー

    private void StartResize(MouseEventArgs e) { isResizing = true; resizeStartX = e.ClientX; resizeStartWidth = sidebarWidth; }

    private void OnResize(MouseEventArgs e)
    {
        if (!isResizing) return;
        var delta = e.ClientX - resizeStartX;
        sidebarWidth = Math.Max(240, Math.Min(600, resizeStartWidth + (int)delta));
        StateHasChanged();
    }

    private void StopResize(MouseEventArgs _) => isResizing = false;

    private static string GetFileIconClass(string name) => DisplayFormatters.GetFileIconClass(name);

    private IReadOnlyList<PreviewLineDisplay> previewLinesDisplay =>
        (_previewLines ?? Array.Empty<PreviewLineResult>()).Select(p => new PreviewLineDisplay(p.Content, p.HasMatch)).ToList();

    #endregion
}
