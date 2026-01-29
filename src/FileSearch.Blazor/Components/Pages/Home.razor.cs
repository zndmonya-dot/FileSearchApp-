using System.Text.RegularExpressions;
using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
    private List<string> editTargetFolders = new();
    private string newFolderPath = "";
    private string editIndexPath = "";
    private int editMaxResults = 10000;
    private List<string> editTargetExtensions = new();
    private string newTargetExtension = "";
    private string? settingsExtensionMessage = null;
    private int editAutoRebuildIntervalMinutes = 0;
    private int sidebarWidth = 300;
    private Timer? _autoRebuildTimer;
    private bool isResizing = false;
    private double resizeStartX = 0;
    private int resizeStartWidth = 0;

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

    internal class PreviewLine
    {
        public string Content { get; set; } = "";
        public string HighlightedContent { get; set; } = "";
        public bool HasMatch { get; set; }
        public bool IsSyntaxHighlighted { get; set; }
    }

    internal class TreeNode
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string? FilePath { get; set; }
        public bool IsFolder { get; set; }
        public bool IsExpanded { get; set; } = true;
        public List<TreeNode>? Children { get; set; }
        public int FileCount { get; set; }
        public SearchResultItem? FileData { get; set; }
        public DateTime? LastModified { get; set; }
        public long FileSize { get; set; }
    }

    private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf" };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".csv", ".log", ".md", ".json", ".xml", ".yaml", ".yml", ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".html", ".css", ".sql", ".sh", ".bat", ".ps1", ".ini", ".cfg", ".config" };

    private static readonly Dictionary<string, string> LanguageMap = new()
    {
        { ".cs", "csharp" }, { ".js", "javascript" }, { ".ts", "typescript" }, { ".py", "python" },
        { ".java", "java" }, { ".cpp", "cpp" }, { ".c", "c" }, { ".h", "cpp" }, { ".go", "go" },
        { ".rs", "rust" }, { ".rb", "ruby" }, { ".php", "php" }, { ".swift", "swift" }, { ".kt", "kotlin" },
        { ".scala", "scala" }, { ".vb", "vb" }, { ".fs", "fsharp" }, { ".html", "html" }, { ".htm", "html" },
        { ".css", "css" }, { ".scss", "scss" }, { ".less", "less" }, { ".xml", "xml" }, { ".json", "json" },
        { ".yaml", "yaml" }, { ".yml", "yaml" }, { ".sql", "sql" }, { ".sh", "bash" }, { ".bat", "batch" },
        { ".ps1", "powershell" }, { ".md", "markdown" }, { ".jsx", "jsx" }, { ".tsx", "tsx" },
        { ".vue", "xml" }, { ".r", "r" }, { ".m", "objectivec" }, { ".lua", "lua" }, { ".pl", "perl" }
    };

    private static readonly HashSet<string> CodeExtensions = new(LanguageMap.Keys);

    protected override async Task OnInitializedAsync()
    {
        await SettingsService.LoadAsync();
        await IndexService.InitializeAsync(SettingsService.Settings.IndexPath);
        indexCount = IndexService.GetStats().DocumentCount;
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
                _ = InvokeAsync(RebuildIndex);
        }
        catch { /* timer thread: ignore */ }
    }

    public void Dispose()
    {
        _autoRebuildTimer?.Dispose();
        _autoRebuildTimer = null;
    }

    private void ToggleTheme() => isDarkMode = !isDarkMode;

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !isIndexing) await ExecuteSearch();
        if (e.Key == "Escape") { searchQuery = string.Empty; searchErrorMessage = null; await InvokeAsync(StateHasChanged); }
    }

    private async Task ExecuteSearch()
    {
        if (isIndexing || string.IsNullOrWhiteSpace(searchQuery)) return;
        searchErrorMessage = null;
        resultLimitReached = false;
        isSearching = true; treeNodes.Clear(); selectedFile = null; totalFileCount = 0;
        StateHasChanged();
        try
        {
            var maxResults = SettingsService.Settings.MaxResults;
            var result = await SearchService.SearchAsync(searchQuery, new SearchOptions { MaxResults = maxResults }, CancellationToken.None);
            treeNodes = BuildTree(result.Items.ToList());
            totalFileCount = result.Items.Count();
            resultLimitReached = totalFileCount >= maxResults;
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
                        if (child == null) { child = new TreeNode { Name = part, IsFolder = true, IsExpanded = false, Children = new List<TreeNode>() }; current.Children.Add(child); }
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
        await LoadPreview(node.FilePath!);
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
        isLoadingPreview = true;
        previewLines.Clear();
        previewLineCount = 0;
        isSourceCode = false;
        currentLanguage = "";
        StateHasChanged();
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            string content;
            if (OfficeExtensions.Contains(ext))
            {
                var extractor = ExtractorFactory.GetExtractor(ext);
                content = extractor != null ? await extractor.ExtractTextAsync(path) : "[未対応]";
            }
            else if (TextExtensions.Contains(ext) || IsTextFile(path))
                content = await File.ReadAllTextAsync(path);
            else
                content = "[プレビュー不可]";

            if (content.Length > 100000) content = content.Substring(0, 100000) + "\n... (省略)";

            isSourceCode = IsCodeFile(ext);
            currentLanguage = GetLanguage(ext);
            var searchTerms = string.IsNullOrWhiteSpace(searchQuery) ? Array.Empty<string>() : searchQuery.Split(' ', '　').Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();

            string highlightedContent = content;
            if (isSourceCode)
            {
                try { highlightedContent = await JSRuntime.InvokeAsync<string>("highlightCode", new object[] { content, currentLanguage }); }
                catch { highlightedContent = System.Net.WebUtility.HtmlEncode(content); isSourceCode = false; }
            }

            var lines = content.Split('\n');
            var highlightedLines = highlightedContent.Split('\n');
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

    private async Task RebuildIndex()
    {
        if (isIndexing) return;
        indexErrorMessage = null;
        isIndexing = true;
        indexProgressPercent = 0;
        indexProgressText = "準備中...";
        StateHasChanged();

        var progress = new Progress<IndexProgress>(p =>
        {
            indexProgressPercent = p.TotalFiles > 0 ? (int)((double)p.ProcessedFiles / p.TotalFiles * 100) : 0;
            indexProgressText = $"{p.ProcessedFiles:N0} / {p.TotalFiles:N0} ファイル";
            InvokeAsync(StateHasChanged);
        });

        try
        {
            var options = new IndexRebuildOptions { TargetExtensions = SettingsService.Settings.TargetExtensions.Count > 0 ? SettingsService.Settings.TargetExtensions : null };
            await IndexService.RebuildIndexAsync(SettingsService.Settings.TargetFolders, progress, options, CancellationToken.None);
            indexCount = IndexService.GetStats().DocumentCount;
            SettingsService.Settings.LastIndexUpdate = DateTime.Now;
            await SettingsService.SaveAsync();
            indexErrorMessage = null;
        }
        catch (Exception ex)
        {
            indexErrorMessage = "インデックス構築に失敗しました。";
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
        editTargetFolders = SettingsService.Settings.TargetFolders.ToList();
        editIndexPath = SettingsService.Settings.IndexPath;
        editMaxResults = SettingsService.Settings.MaxResults;
        editTargetExtensions = SettingsService.Settings.TargetExtensions.ToList();
        editAutoRebuildIntervalMinutes = SettingsService.Settings.AutoRebuildIntervalMinutes;
        settingsExtensionMessage = null;
        showSettings = true;
    }

    private void CloseSettings() => showSettings = false;

    private void AddFolder()
    {
        if (!string.IsNullOrWhiteSpace(newFolderPath) && Directory.Exists(newFolderPath) && !editTargetFolders.Contains(newFolderPath))
        { editTargetFolders.Add(newFolderPath); newFolderPath = ""; }
    }

    private void RemoveFolder(string f) => editTargetFolders.Remove(f);

    private void AddTargetExtension()
    {
        var ext = (newTargetExtension ?? "").Trim();
        if (!string.IsNullOrEmpty(ext) && !ext.StartsWith(".")) ext = "." + ext;
        if (string.IsNullOrEmpty(ext)) { settingsExtensionMessage = null; return; }
        if (editTargetExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) { settingsExtensionMessage = "既に追加されています"; return; }
        editTargetExtensions.Add(ext);
        newTargetExtension = "";
        settingsExtensionMessage = null;
    }

    private void RemoveTargetExtension(string ext) => editTargetExtensions.Remove(ext);

    private async Task SaveSettings()
    {
        SettingsService.Settings.TargetFolders = editTargetFolders.ToList();
        if (!string.IsNullOrWhiteSpace(editIndexPath)) SettingsService.Settings.IndexPath = editIndexPath;
        SettingsService.Settings.MaxResults = editMaxResults;
        SettingsService.Settings.TargetExtensions = editTargetExtensions.ToList();
        SettingsService.Settings.AutoRebuildIntervalMinutes = editAutoRebuildIntervalMinutes;
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

    private string GetLanguage(string ext) => LanguageMap.TryGetValue(ext, out var lang) ? lang : "plaintext";
    private bool IsCodeFile(string ext) => CodeExtensions.Contains(ext);
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
