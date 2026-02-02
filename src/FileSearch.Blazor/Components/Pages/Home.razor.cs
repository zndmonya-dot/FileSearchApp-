using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Settings;
using FileSearch.Blazor.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace FileSearch.Blazor.Components.Pages;

public partial class Home : IDisposable
{
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
    /// <summary>10万件時もUIが重くならないよう進捗報告をスロットル（件数間隔）</summary>
    private const int ProgressReportInterval = 500;
    /// <summary>進捗UI更新の最小間隔（ミリ秒）</summary>
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
    /// <summary>SearchSidebar に渡すためプロパティで公開（未使用警告回避）</summary>
    private string? SearchErrorMessage => searchErrorMessage;
    /// <summary>SearchSidebar に渡すためプロパティで公開（未使用警告回避）</summary>
    private string? IndexErrorMessage => indexErrorMessage;
    /// <summary>text=行テキスト, html=Excel等HTML, image=画像DataURL</summary>
    private string previewMode = "text";
    private string? previewHtml;
    private string? previewImageDataUrl;
    private string? _lastHighlightNavFilePath;
    private string? _highlightNavInfo;
    private bool _hasTriedInitialHighlightScroll;
    private List<TreeNode>? _fileNavList;
    private int _fileNavIndex = -1;
    private bool _showRebuildConfirm;
    /// <summary>true=全体を再構築, false=差分更新</summary>
    private bool _indexUpdateFullRebuild;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
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
        // プレビュー読み込み後、検索中なら最初の一致へスクロールして位置を表示
        if (!firstRender && !isLoadingPreview && ShowHighlightNav && !_hasTriedInitialHighlightScroll)
        {
            _hasTriedInitialHighlightScroll = true;
            try
            {
                var result = await JSRuntime.InvokeAsync<string?>("scrollToNextHighlight");
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
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
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

    /// <summary>手動検索用。Enter または「検索」ボタンで呼ぶ。入力変更では呼ばない。</summary>
    private void OnSearchInputChanged() { }

    private async Task ExecuteSearch()
    {
        var query = searchQuery?.Trim() ?? "";
        if (isIndexing || string.IsNullOrWhiteSpace(query)) return;
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
            treeNodes = BuildTree(result.Items.ToList());
            totalFileCount = result.Items.Count();
        }
        catch (OperationCanceledException)
        {
            // 新しい検索でキャンセルされた場合は無視
        }
        catch (Exception ex)
        {
            searchErrorMessage = "検索に失敗しました。";
            Logger.LogError(ex, "Search failed");
        }
        finally { isSearching = false; StateHasChanged(); }
    }

    private List<TreeNode> BuildTree(List<SearchResultItem> items)
    {
        try
        {
            var targetFolders = SettingsService.Settings.TargetFolders;
            var result = new List<TreeNode>();
            foreach (var targetFolder in targetFolders)
            {
                var normalizedTarget = targetFolder.TrimEnd('\\', '/').ToLowerInvariant();
                var matchingItems = items.Where(i => i.FolderPath.ToLowerInvariant().StartsWith(normalizedTarget)).ToList();
                if (matchingItems.Count == 0) continue;
                var rootNode = new TreeNode { Name = Path.GetFileName(targetFolder) ?? targetFolder, FullPath = targetFolder, IsFolder = true, IsExpanded = true, Children = new List<TreeNode>() };
                foreach (var item in matchingItems)
                {
                    var relativePath = item.FolderPath.Length > targetFolder.Length ? item.FolderPath.Substring(targetFolder.Length).TrimStart('\\', '/') : "";
                    var parts = string.IsNullOrEmpty(relativePath) ? Array.Empty<string>() : relativePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    var current = rootNode;
                    foreach (var part in parts)
                    {
                        current.Children ??= new List<TreeNode>();
                        var child = current.Children.FirstOrDefault(c => c.IsFolder && c.Name == part);
                        if (child == null)
                        {
                            var childFullPath = Path.Combine(current.FullPath, part);
                            child = new TreeNode { Name = part, FullPath = childFullPath, IsFolder = true, IsExpanded = false, Children = new List<TreeNode>() };
                            current.Children.Add(child);
                        }
                        current = child;
                    }
                    current.Children ??= new List<TreeNode>();
                    current.Children.Add(new TreeNode { Name = item.FileName, FilePath = item.FilePath, IsFolder = false, FileData = item, LastModified = item.LastModified, FileSize = item.FileSize });
                }
                SortTree(rootNode); UpdateFileCount(rootNode); result.Add(rootNode);
            }
            return result;
        }
        catch { return new List<TreeNode>(); }
    }

    private void SortTree(TreeNode node)
    {
        if (node.Children == null) return;
        node.Children = node.Children.OrderBy(c => !c.IsFolder).ThenBy(c => c.Name).ToList();
        foreach (var child in node.Children.Where(c => c.IsFolder)) SortTree(child);
    }

    private int UpdateFileCount(TreeNode node)
    {
        if (!node.IsFolder) return 0;
        int count = node.Children?.Count(c => !c.IsFolder) ?? 0;
        foreach (var child in node.Children?.Where(c => c.IsFolder) ?? Enumerable.Empty<TreeNode>()) count += UpdateFileCount(child);
        node.FileCount = count; return count;
    }

    private void ToggleNode(TreeNode node)
    {
        node.IsExpanded = !node.IsExpanded;
        selectedFile = null;
        selectedFolder = node;
        selectedFolderRowIndex = 0;
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

    private async Task SelectFile(TreeNode node)
    {
        if (node.FileData == null) return;
        selectedFolder = null;
        selectedFile = node.FileData;
        _fileNavList = CollectAllFileNodes(treeNodes);
        _fileNavIndex = _fileNavList.FindIndex(n => string.Equals(n.FilePath, node.FilePath, StringComparison.OrdinalIgnoreCase));
        if (_fileNavIndex < 0) _fileNavIndex = 0;
        await LoadPreview(node.FilePath!);
    }

    private static List<TreeNode> CollectAllFileNodes(List<TreeNode> roots)
    {
        var list = new List<TreeNode>();
        foreach (var node in roots)
            CollectFilesRec(node, list);
        return list;
    }

    private static void CollectFilesRec(TreeNode node, List<TreeNode> acc)
    {
        if (!node.IsFolder && node.FileData != null)
            acc.Add(node);
        foreach (var c in node.Children ?? Enumerable.Empty<TreeNode>())
            CollectFilesRec(c, acc);
    }

    private async Task OnFolderItemClick(TreeNode item)
    {
        if (item.IsFolder)
        {
            item.IsExpanded = true;
            selectedFile = null;
            selectedFolder = item;
            selectedFolderRowIndex = 0;
        }
        else await SelectFile(item);
    }

    private async Task OnFolderRowClick(TreeNode item)
    {
        if (selectedFolder?.Children == null) return;
        var list = GetSortedAndFilteredItems(selectedFolder.Children).ToList();
        selectedFolderRowIndex = list.IndexOf(item);
        if (selectedFolderRowIndex < 0) selectedFolderRowIndex = 0;
        await OnFolderItemClick(item);
    }

    private async Task OnFolderListKeyDown(KeyboardEventArgs e)
    {
        if (selectedFolder?.Children == null) return;
        var items = GetSortedAndFilteredItems(selectedFolder.Children).ToList();
        if (items.Count == 0) return;
        if (selectedFolderRowIndex >= items.Count) selectedFolderRowIndex = items.Count - 1;
        if (selectedFolderRowIndex < 0) selectedFolderRowIndex = 0;
        if (e.Key == "ArrowDown") { selectedFolderRowIndex = Math.Min(selectedFolderRowIndex + 1, items.Count - 1); StateHasChanged(); }
        else if (e.Key == "ArrowUp") { selectedFolderRowIndex = Math.Max(0, selectedFolderRowIndex - 1); StateHasChanged(); }
        else if (e.Key == "Enter" && selectedFolderRowIndex >= 0 && selectedFolderRowIndex < items.Count)
            await OnFolderItemClick(items[selectedFolderRowIndex]);
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
        previewMode = "text";
        previewHtml = null;
        previewImageDataUrl = null;
        StateHasChanged();
        try
        {
            var result = await PreviewService.GetPreviewAsync(path, searchQuery?.Trim(), token);
            if (token.IsCancellationRequested) return;
            previewMode = result.Mode;
            previewHtml = result.Html;
            previewImageDataUrl = result.ImageDataUrl;
            _previewLines = result.Lines;
            previewLineCount = result.LineCount;
        }
        catch (OperationCanceledException)
        {
            // 別のファイル選択でキャンセルされた場合は何もしない
        }
        catch (Exception ex)
        {
            _previewLines = new List<PreviewLineResult> { new($"[エラー] {ex.Message}", false) };
            previewLineCount = 1;
        }
        finally { isLoadingPreview = false; StateHasChanged(); }
    }

    private bool ShowFileNav => selectedFile != null && _fileNavList != null && _fileNavList.Count > 1 && previewMode != "image";
    private bool ShowHighlightNav => selectedFile != null && !string.IsNullOrWhiteSpace(searchQuery) && previewMode != "image";

    private bool ShowNavButtons => selectedFile != null && previewMode != "image" && (ShowFileNav || ShowHighlightNav);

    private string? NavInfo => !string.IsNullOrEmpty(_highlightNavInfo) ? _highlightNavInfo
        : (_fileNavList != null && _fileNavIndex >= 0 && _fileNavIndex < _fileNavList.Count ? $"{_fileNavIndex + 1}/{_fileNavList.Count}" : null);

    /// <summary>ハイライト位置へスクロールを試みる。スクロールできた場合は結果を返し、できなければ null。</summary>
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
            await SelectNextFile();
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
            await SelectPrevFile();
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

    private async Task SelectNextFile()
    {
        if (_fileNavList == null || _fileNavList.Count < 2) return;
        var next = (_fileNavIndex + 1) % _fileNavList.Count;
        await SelectFile(_fileNavList[next]);
    }

    private async Task SelectPrevFile()
    {
        if (_fileNavList == null || _fileNavList.Count < 2) return;
        var prev = _fileNavIndex <= 0 ? _fileNavList.Count - 1 : _fileNavIndex - 1;
        await SelectFile(_fileNavList[prev]);
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
            indexProgressText = p.ErrorCount > 0
                ? $"{p.ProcessedFiles:N0} / {p.TotalFiles:N0} {countUnit}（スキップ {p.ErrorCount:N0} 件）"
                : $"{p.ProcessedFiles:N0} / {p.TotalFiles:N0} {countUnit}";
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

    /// <summary>インデックス更新の共通実行（差分・全体どちらもこの経路で実行）。</summary>
    private async Task RunIndexUpdateAsync(
        string initialMessage,
        string countUnit,
        Func<IProgress<IndexProgress>, Task> runAsync,
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
        StateHasChanged();
        await Task.Yield();

        var progress = CreateThrottledProgress(countUnit);
        try
        {
            await Task.Run(async () => await runAsync(progress));
            indexCount = IndexService.GetStats().DocumentCount;
            SettingsService.Settings.LastIndexUpdate = DateTime.Now;
            await SettingsService.SaveAsync();
            indexErrorMessage = null;
        }
        catch (Exception ex)
        {
            indexErrorMessage = getErrorMessage(ex);
            Logger.LogError(ex, logContext);
        }
        finally
        {
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
            p => IndexService.UpdateIndexAsync(folders, p, options, CancellationToken.None),
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
            p => IndexService.RebuildIndexAsync(folders, p, options, CancellationToken.None),
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

    /// <summary>選択中の方法でインデックス更新を実行する。</summary>
    private async Task ConfirmIndexUpdateAsync()
    {
        _showRebuildConfirm = false;
        StateHasChanged();
        if (_indexUpdateFullRebuild)
            await RebuildIndex();
        else
            await UpdateIndex();
    }

    private string GetLastUpdateText()
    {
        var lastUpdate = SettingsService.Settings.LastIndexUpdate;
        if (!lastUpdate.HasValue) return "未実行";
        var diff = DateTime.Now - lastUpdate.Value;
        if (diff.TotalMinutes < 1) return "たった今";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}分前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}時間前";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}日前";
        return lastUpdate.Value.ToString("MM/dd HH:mm");
    }

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
        showSettings = true;
    }

    private void CloseSettings() => showSettings = false;

    private void HandleAddFolder()
    {
        var path = (_settingsEdit.NewFolderPath ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !_settingsEdit.TargetFolders.Contains(path))
        {
            _settingsEdit.TargetFolders.Add(path);
            _settingsEdit.NewFolderPath = "";
        }
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

    private async Task SaveSettings()
    {
        SettingsService.Settings.TargetFolders = _settingsEdit.TargetFolders.ToList();
        if (!string.IsNullOrWhiteSpace(_settingsEdit.IndexPath)) SettingsService.Settings.IndexPath = _settingsEdit.IndexPath;
        SettingsService.Settings.TargetExtensions = _settingsEdit.TargetExtensions.ToList();
        SettingsService.Settings.AutoRebuildIntervalMinutes = _settingsEdit.AutoRebuildIntervalMinutes;
        SettingsService.Settings.ThemeMode = _settingsEdit.ThemeMode ?? "System";
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
        _previewLines.Select(p => new PreviewLineDisplay(p.Content, p.HasMatch)).ToList();
}
