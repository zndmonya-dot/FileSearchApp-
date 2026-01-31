using System.Text;
using System.Text.RegularExpressions;
using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Extractors;
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
    private List<PreviewLine> previewLines = new();
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
    private Timer? _searchDebounceTimer;
    private const int SearchDebounceMs = 400;
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
    private bool resultLimitReached = false;
    private bool isSourceCode = false;
    private string currentLanguage = "";
    /// <summary>text=行テキスト, html=Excel等HTML, image=画像DataURL</summary>
    private string previewMode = "text";
    private string? previewHtml;
    private string? previewImageDataUrl;
    private string? _lastHighlightNavFilePath;
    private string? _highlightNavInfo;
    private bool _hasTriedInitialHighlightScroll;
    private List<TreeNode>? _fileNavList;
    private int _fileNavIndex = -1;
    private bool _showFileNavConfirm;
    private bool _pendingFileNavNext; // true=次へ, false=前へ

    internal class PreviewLine
    {
        public string Content { get; set; } = "";
        public string HighlightedContent { get; set; } = "";
        public bool HasMatch { get; set; }
        public bool IsSyntaxHighlighted { get; set; }
    }

    private const int PreviewMaxChars = 50_000;
    private const int PreviewMaxLinesForHighlight = 500;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
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
        var indexPath = SettingsService.Settings.IndexPath;
        if (!string.IsNullOrWhiteSpace(indexPath))
        {
            await IndexService.InitializeAsync(indexPath);
            indexCount = IndexService.GetStats().DocumentCount;
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
        _searchDebounceTimer?.Dispose();
        _searchDebounceTimer = null;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
    }

    private void ToggleTheme() => isDarkMode = !isDarkMode;

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !isIndexing)
        {
            _searchDebounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            await ExecuteSearch();
        }
        if (e.Key == "Escape") { searchQuery = string.Empty; searchErrorMessage = null; _searchDebounceTimer?.Change(Timeout.Infinite, Timeout.Infinite); await InvokeAsync(StateHasChanged); }
    }

    private void OnSearchInputChanged()
    {
        _searchDebounceTimer?.Dispose();
        if (string.IsNullOrWhiteSpace(searchQuery)) return;
        _searchDebounceTimer = new Timer(_ =>
        {
            _searchDebounceTimer?.Dispose();
            _searchDebounceTimer = null;
            InvokeAsync(ExecuteSearch);
        }, null, SearchDebounceMs, Timeout.Infinite);
    }

    private async Task ExecuteSearch()
    {
        var query = searchQuery?.Trim() ?? "";
        if (isIndexing || string.IsNullOrWhiteSpace(query)) return;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        searchErrorMessage = null;
        resultLimitReached = false;
        isSearching = true; treeNodes.Clear(); selectedFile = null; totalFileCount = 0;
        StateHasChanged();
        try
        {
            var maxResults = SettingsService.Settings.MaxResults;
            var result = await SearchService.SearchAsync(query, new SearchOptions
            {
                MaxResults = maxResults
            }, token);
            if (token.IsCancellationRequested) return;
            treeNodes = BuildTree(result.Items.ToList());
            totalFileCount = result.Items.Count();
            resultLimitReached = totalFileCount >= maxResults;
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
        previewLines.Clear();
        previewLineCount = 0;
        previewMode = "text";
        previewHtml = null;
        previewImageDataUrl = null;
        isSourceCode = false;
        currentLanguage = "";
        StateHasChanged();
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();

            // 画像: Data URL で img 表示
            if (PreviewHelper.IsImageFile(ext))
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(path, token);
                    if (token.IsCancellationRequested) return;
                    var base64 = Convert.ToBase64String(bytes);
                    var mime = PreviewHelper.GetImageMimeType(ext);
                    previewImageDataUrl = $"data:{mime};base64,{base64}";
                    previewMode = "image";
                }
                catch
                {
                    previewMode = "text";
                    previewLines.Add(new PreviewLine { Content = "[画像の読み込みに失敗しました]", HasMatch = false });
                    previewLineCount = 1;
                }
                finally { isLoadingPreview = false; StateHasChanged(); }
                return;
            }

            // Excel: HTML テーブルで直感的に表示
            if (ext == ".xlsx")
            {
                try
                {
                    var query = searchQuery?.Trim() ?? "";
                    previewHtml = OfficeExtractor.ExtractExcelAsHtml(path, string.IsNullOrWhiteSpace(query) ? null : query);
                    previewMode = "html";
                }
                catch (Exception ex)
                {
                    previewMode = "text";
                    previewLines.Add(new PreviewLine { Content = $"[Excelプレビューエラー] {ex.Message}", HasMatch = false });
                    previewLineCount = 1;
                }
                finally { isLoadingPreview = false; StateHasChanged(); }
                return;
            }

            string content;
            var extractor = ExtractorFactory.GetExtractor(ext);
            if (extractor != null)
                content = await extractor.ExtractTextAsync(path, token);
            else if (PreviewHelper.IsTextFile(ext) || IsTextFile(path))
                content = await File.ReadAllTextAsync(path, token);
            else
                content = "[プレビュー不可]";
            if (token.IsCancellationRequested) return;

            if (content.Length > PreviewMaxChars) content = content.Substring(0, PreviewMaxChars) + "\n... (省略)";

            isSourceCode = PreviewHelper.IsCodeFile(ext);
            currentLanguage = PreviewHelper.GetLanguage(ext);
            var searchTerms = string.IsNullOrWhiteSpace(searchQuery) ? Array.Empty<string>() : searchQuery.Split(' ', '　').Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();

            var lines = content.Split('\n');
            string[] highlightedLines;
            if (isSourceCode)
            {
                var toHighlight = lines.Length <= PreviewMaxLinesForHighlight
                    ? content
                    : string.Join("\n", lines.Take(PreviewMaxLinesForHighlight)) + "\n";
                try
                {
                    var highlighted = await JSRuntime.InvokeAsync<string>("highlightCode", token, new object[] { toHighlight, currentLanguage });
                    highlightedLines = highlighted.Split('\n');
                    if (lines.Length > PreviewMaxLinesForHighlight)
                    {
                        var rest = lines.Skip(PreviewMaxLinesForHighlight).Select(l => System.Net.WebUtility.HtmlEncode(l.TrimEnd('\r')));
                        highlightedLines = highlightedLines.Concat(rest).ToArray();
                    }
                }
                catch
                {
                    highlightedLines = lines.Select(l => System.Net.WebUtility.HtmlEncode(l.TrimEnd('\r'))).ToArray();
                    isSourceCode = false;
                }
            }
            else
            {
                highlightedLines = lines.Select(l => System.Net.WebUtility.HtmlEncode(l.TrimEnd('\r'))).ToArray();
            }
            previewLineCount = lines.Length;

            for (int i = 0; i < lines.Length; i++)
            {
                var originalLine = lines[i].TrimEnd('\r');
                var displayLine = isSourceCode && i < highlightedLines.Length ? highlightedLines[i].TrimEnd('\r') : System.Net.WebUtility.HtmlEncode(originalLine);
                var hasMatch = false;
                foreach (var term in searchTerms)
                {
                    var pattern = Regex.Escape(term);
                    if (Regex.IsMatch(originalLine, pattern, RegexOptions.IgnoreCase))
                    {
                        hasMatch = true;
                        if (isSourceCode) displayLine = HighlightSearchInSyntax(displayLine, term);
                        else displayLine = Regex.Replace(displayLine, Regex.Escape(System.Net.WebUtility.HtmlEncode(term)), m => $"<mark>{m.Value}</mark>", RegexOptions.IgnoreCase);
                    }
                }
                previewLines.Add(new PreviewLine { Content = displayLine, HasMatch = hasMatch, IsSyntaxHighlighted = isSourceCode });
            }
        }
        catch (OperationCanceledException)
        {
            // 別のファイル選択でキャンセルされた場合は何もしない
        }
        catch (Exception ex)
        {
            previewLines.Add(new PreviewLine { Content = $"[エラー] {ex.Message}", HasMatch = false });
            previewLineCount = 1;
        }
        finally { isLoadingPreview = false; StateHasChanged(); }
    }

    private string HighlightSearchInSyntax(string htmlLine, string term)
    {
        try
        {
            var pattern = $"(?<=>|^)([^<]*?)({Regex.Escape(term)})([^<]*?)(?=<|$)";
            return Regex.Replace(htmlLine, pattern, m => $"{m.Groups[1].Value}<mark>{m.Groups[2].Value}</mark>{m.Groups[3].Value}", RegexOptions.IgnoreCase);
        }
        catch { return htmlLine; }
    }

    private bool IsTextFile(string path)
    {
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var b = new byte[8192];
            return !b.Take(s.Read(b, 0, b.Length)).Any(x => x == 0);
        }
        catch { return false; }
    }

    private bool ShowFileNav => selectedFile != null && _fileNavList != null && _fileNavList.Count > 1 && previewMode != "image";
    private bool ShowHighlightNav => selectedFile != null && !string.IsNullOrWhiteSpace(searchQuery) && previewMode != "image";

    private bool ShowNavButtons => selectedFile != null && previewMode != "image" && (ShowFileNav || ShowHighlightNav);

    private string? NavInfo => !string.IsNullOrEmpty(_highlightNavInfo) ? _highlightNavInfo
        : (_fileNavList != null && _fileNavIndex >= 0 && _fileNavIndex < _fileNavList.Count ? $"{_fileNavIndex + 1}/{_fileNavList.Count}" : null);

    private async Task GoNext()
    {
        var canGoNextFile = ShowFileNav;
        var wrap = !canGoNextFile;
        try
        {
            var result = await JSRuntime.InvokeAsync<string?>("scrollToNextHighlight", wrap);
            if (!string.IsNullOrEmpty(result))
            {
                _highlightNavInfo = FormatHighlightNavInfo(result);
                StateHasChanged();
                return;
            }
        }
        catch { /* JS not ready */ }
        if (canGoNextFile)
        {
            if (SettingsService.Settings.SkipFileNavConfirm)
                await SelectNextFile();
            else
            {
                _pendingFileNavNext = true;
                _showFileNavConfirm = true;
                StateHasChanged();
            }
        }
    }

    private async Task GoPrev()
    {
        var canGoPrevFile = ShowFileNav;
        var wrap = !canGoPrevFile;
        try
        {
            var result = await JSRuntime.InvokeAsync<string?>("scrollToPrevHighlight", wrap);
            if (!string.IsNullOrEmpty(result))
            {
                _highlightNavInfo = FormatHighlightNavInfo(result);
                StateHasChanged();
                return;
            }
        }
        catch { /* JS not ready */ }
        if (canGoPrevFile)
        {
            if (SettingsService.Settings.SkipFileNavConfirm)
                await SelectPrevFile();
            else
            {
                _pendingFileNavNext = false;
                _showFileNavConfirm = true;
                StateHasChanged();
            }
        }
    }

    private async Task ConfirmFileNavAsync()
    {
        _showFileNavConfirm = false;
        if (_pendingFileNavNext)
            await SelectNextFile();
        else
            await SelectPrevFile();
        StateHasChanged();
    }

    private void CancelFileNavConfirm()
    {
        _showFileNavConfirm = false;
        StateHasChanged();
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

    private async Task OptimizeIndex()
    {
        if (isIndexing) return;
        var path = SettingsService.Settings.IndexPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            await IndexService.InitializeAsync(path);
            await IndexService.OptimizeAsync(CancellationToken.None);
            indexCount = IndexService.GetStats().DocumentCount;
            indexErrorMessage = null;
        }
        catch (Exception ex)
        {
            indexErrorMessage = "最適化に失敗しました。";
            Logger.LogError(ex, "Index optimize failed");
        }
        StateHasChanged();
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
            indexProgressText = $"{p.ProcessedFiles:N0} / {p.TotalFiles:N0} {countUnit}";
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

    private async Task UpdateIndex()
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
        indexProgressText = "差分を検出中...";
        StateHasChanged();

        var progress = CreateThrottledProgress("件");
        try
        {
            await IndexService.UpdateIndexAsync(SettingsService.Settings.TargetFolders, progress, GetIndexRebuildOptions(SettingsService), CancellationToken.None);
            indexCount = IndexService.GetStats().DocumentCount;
            SettingsService.Settings.LastIndexUpdate = DateTime.Now;
            await SettingsService.SaveAsync();
            indexErrorMessage = null;
        }
        catch (Exception ex)
        {
            indexErrorMessage = MsgUpdateFailed;
            Logger.LogError(ex, "Index update failed");
        }
        finally
        {
            isIndexing = false;
            indexProgressPercent = 0;
            StateHasChanged();
        }
    }

    private async Task RebuildIndex()
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
        indexProgressText = "準備中...";
        StateHasChanged();

        var progress = CreateThrottledProgress("ファイル");
        try
        {
            await IndexService.RebuildIndexAsync(SettingsService.Settings.TargetFolders, progress, GetIndexRebuildOptions(SettingsService), CancellationToken.None);
            indexCount = IndexService.GetStats().DocumentCount;
            SettingsService.Settings.LastIndexUpdate = DateTime.Now;
            await SettingsService.SaveAsync();
            indexErrorMessage = null;
        }
        catch (Exception ex)
        {
            indexErrorMessage = MsgRebuildFailed;
            Logger.LogError(ex, "Index rebuild failed");
        }
        finally
        {
            isIndexing = false;
            indexProgressPercent = 0;
            StateHasChanged();
        }
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
        _settingsEdit.MaxResults = SettingsService.Settings.MaxResults;
        _settingsEdit.TargetExtensions = SettingsService.Settings.TargetExtensions.ToList();
        _settingsEdit.AutoRebuildIntervalMinutes = SettingsService.Settings.AutoRebuildIntervalMinutes;
        _settingsEdit.ConfirmFileNav = !SettingsService.Settings.SkipFileNavConfirm;
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
        SettingsService.Settings.MaxResults = _settingsEdit.MaxResults;
        SettingsService.Settings.TargetExtensions = _settingsEdit.TargetExtensions.ToList();
        SettingsService.Settings.AutoRebuildIntervalMinutes = _settingsEdit.AutoRebuildIntervalMinutes;
        SettingsService.Settings.SkipFileNavConfirm = !_settingsEdit.ConfirmFileNav;
        await SettingsService.SaveAsync();
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

    private string GetFileIconClass(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".doc" or ".docx" => "word", ".xls" or ".xlsx" => "excel", ".ppt" or ".pptx" => "ppt", ".pdf" => "pdf",
        ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".c" or ".h" or ".go" or ".rs" or ".rb" or ".php" or ".swift" or ".kt" or ".scala" or ".vb" or ".fs" => "code",
        _ => "text"
    };

    private string GetFileExt(string name) => Path.GetExtension(name).TrimStart('.').ToUpperInvariant();

    private string GetFileType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".doc" or ".docx" => "Word文書", ".xls" or ".xlsx" => "Excelブック", ".ppt" or ".pptx" => "PowerPoint",
        ".pdf" => "PDF", ".txt" => "テキスト", ".csv" => "CSV", ".md" => "Markdown", ".json" => "JSON", ".xml" => "XML",
        ".html" or ".htm" => "HTML", ".css" => "CSS", ".js" => "JavaScript", ".ts" => "TypeScript", ".cs" => "C#",
        ".py" => "Python", ".java" => "Java", ".cpp" or ".c" or ".h" => "C/C++", ".go" => "Go", ".rs" => "Rust",
        ".rb" => "Ruby", ".php" => "PHP", ".swift" => "Swift", ".kt" => "Kotlin", ".sql" => "SQL", ".sh" => "Shell",
        ".ps1" => "PowerShell", ".yaml" or ".yml" => "YAML", ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => "画像",
        ".mp3" or ".wav" or ".flac" => "音声", ".mp4" or ".avi" or ".mov" => "動画", ".zip" or ".rar" or ".7z" => "圧縮",
        _ => Path.GetExtension(name).TrimStart('.').ToUpperInvariant() + "ファイル"
    };

    private string FormatSize(long b) => b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024} KB" : $"{b / 1048576.0:F1} MB";
    private string FormatDate(DateTime d) => d.ToLocalTime().ToString("yyyy/MM/dd HH:mm");

    private string FormatRelativeDate(DateTime d)
    {
        var local = d.ToLocalTime();
        var diff = DateTime.Now - local;
        if (diff.TotalMinutes < 1) return "たった今";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}分前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}時間前";
        if (diff.TotalDays < 2) return "昨日";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}日前";
        if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)}週間前";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}ヶ月前";
        return local.ToString("yyyy/MM/dd");
    }
}
