using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Preview;
using FullTextSearch.Infrastructure.Settings;

namespace FullTextSearch.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISearchService _searchService;
    private readonly IIndexService _indexService;
    private readonly IPreviewService _previewService;
    private readonly IAppSettingsService _settingsService;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FileTreeNode> _fileTree = [];

    [ObservableProperty]
    private FileTreeNode? _selectedFile;

    [ObservableProperty]
    private int _fileTreeItemCount;

    [ObservableProperty]
    private bool _isLoadingTree;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isIndexing;

    [ObservableProperty]
    private int _indexedDocumentCount;

    [ObservableProperty]
    private string _statusMessage = "準備完了";

    [ObservableProperty]
    private double _indexProgress;

    [ObservableProperty]
    private string _selectedFileTypeFilter = "すべて";

    [ObservableProperty]
    private string _selectedDateFilter = "すべて";

    public ObservableCollection<string> FileTypeFilters { get; } = ["すべて", "Word", "Excel", "PDF", "テキスト"];
    public ObservableCollection<string> DateFilters { get; } = ["すべて", "今日", "今週", "今月"];

    public MainViewModel(
        ISearchService searchService,
        IIndexService indexService,
        IPreviewService previewService,
        IAppSettingsService settingsService)
    {
        _searchService = searchService;
        _indexService = indexService;
        _previewService = previewService;
        _settingsService = settingsService;
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await _settingsService.LoadAsync();
        var settings = _settingsService.Settings;
        await _indexService.InitializeAsync(settings.IndexPath);

        var stats = _indexService.GetStats();
        IndexedDocumentCount = stats.DocumentCount;

        // ファイルツリーを構築
        await LoadFileTreeAsync(settings.TargetFolders);

        StatusMessage = $"準備完了 - {IndexedDocumentCount:N0} ファイル";
    }

    private async Task LoadFileTreeAsync(IEnumerable<string> targetFolders)
    {
        IsLoadingTree = true;
        StatusMessage = "ファイルツリーを読み込み中...";

        try
        {
            var nodes = await Task.Run(() =>
            {
                var result = new List<FileTreeNode>();
                foreach (var folder in targetFolders)
                {
                    if (Directory.Exists(folder))
                    {
                        var node = CreateFolderNode(folder, 0);
                        if (node != null)
                        {
                            result.Add(node);
                        }
                    }
                }
                return result;
            });

            FileTree.Clear();
            int count = 0;
            foreach (var node in nodes)
            {
                FileTree.Add(node);
                count += CountNodes(node);
            }
            FileTreeItemCount = count;
        }
        finally
        {
            IsLoadingTree = false;
            StatusMessage = "準備完了";
        }
    }

    private int CountNodes(FileTreeNode node)
    {
        int count = 1;
        foreach (var child in node.Children)
        {
            count += CountNodes(child);
        }
        return count;
    }

    private FileTreeNode? CreateFolderNode(string path, int depth)
    {
        if (depth > 5) return null;

        try
        {
            var dirInfo = new DirectoryInfo(path);
            var node = new FileTreeNode
            {
                Name = dirInfo.Name,
                FullPath = path,
                IsFolder = true,
                IsExpanded = depth < 2
            };

            // ファイルを追加
            try
            {
                var files = dirInfo.GetFiles()
                    .Where(f => !f.Name.StartsWith("."))
                    .OrderBy(f => f.Name)
                    .Take(100);

                foreach (var file in files)
                {
                    node.Children.Add(new FileTreeNode
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsFile = true,
                        Size = file.Length,
                        Modified = file.LastWriteTime
                    });
                }
            }
            catch { }

            // サブフォルダを追加
            try
            {
                var dirs = dirInfo.GetDirectories()
                    .Where(d => !d.Name.StartsWith(".") && !d.Name.StartsWith("$"))
                    .Where(d => !IsSystemFolder(d.Name))
                    .OrderBy(d => d.Name)
                    .Take(50);

                foreach (var dir in dirs)
                {
                    var childNode = CreateFolderNode(dir.FullName, depth + 1);
                    if (childNode != null && (childNode.Children.Count > 0 || HasFiles(dir.FullName)))
                    {
                        node.Children.Insert(0, childNode); // フォルダを先に
                    }
                }
            }
            catch { }

            // 子がなければnull
            if (node.Children.Count == 0 && depth > 0)
            {
                return null;
            }

            return node;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSystemFolder(string name)
    {
        var systemFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "__pycache__", ".git", ".vs", "bin", "obj",
            "packages", ".idea", ".vscode", "target", "dist", "build"
        };
        return systemFolders.Contains(name);
    }

    private static bool HasFiles(string path)
    {
        try
        {
            return Directory.GetFiles(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public void SelectFileNode(FileTreeNode? node)
    {
        SelectedFile = node;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        IsSearching = true;
        StatusMessage = "検索中...";

        try
        {
            var options = new SearchOptions { MaxResults = 500 };
            var result = await _searchService.SearchAsync(SearchQuery, options, CancellationToken.None);

            // 検索結果をツリーに変換
            var searchResults = new List<FileTreeNode>();
            foreach (var item in result.Items)
            {
                searchResults.Add(new FileTreeNode
                {
                    Name = item.FileName,
                    FullPath = item.FilePath,
                    IsFile = true,
                    Size = item.FileSize,
                    Modified = item.LastModified.ToLocalTime()
                });
            }

            // 検索結果をフォルダ別にグループ化
            var grouped = searchResults
                .GroupBy(f => Path.GetDirectoryName(f.FullPath) ?? "")
                .OrderBy(g => g.Key);

            FileTree.Clear();
            int count = 0;
            foreach (var group in grouped)
            {
                var folderNode = new FileTreeNode
                {
                    Name = group.Key,
                    FullPath = group.Key,
                    IsFolder = true,
                    IsExpanded = true
                };

                foreach (var file in group)
                {
                    folderNode.Children.Add(file);
                    count++;
                }

                FileTree.Add(folderNode);
            }

            FileTreeItemCount = count;
            StatusMessage = $"{result.TotalHits:N0} 件見つかりました ({result.ElapsedMilliseconds}ms)";

            await _settingsService.AddSearchHistoryAsync(SearchQuery);
        }
        catch (Exception ex)
        {
            StatusMessage = $"検索エラー: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task RebuildIndexAsync()
    {
        if (IsIndexing) return;

        var settings = _settingsService.Settings;
        if (settings.TargetFolders.Count == 0)
        {
            StatusMessage = "対象フォルダが設定されていません";
            return;
        }

        IsIndexing = true;
        IndexProgress = 0;

        try
        {
            var progress = new Progress<IndexProgress>(p =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    IndexProgress = p.ProgressPercent;
                    StatusMessage = $"インデックス作成中... {p.ProcessedFiles:N0}/{p.TotalFiles:N0}";
                });
            });

            await _indexService.RebuildIndexAsync(settings.TargetFolders, progress);

            var stats = _indexService.GetStats();
            IndexedDocumentCount = stats.DocumentCount;
            StatusMessage = $"完了 - {IndexedDocumentCount:N0} ファイル";

            // ツリーを再読み込み
            await LoadFileTreeAsync(settings.TargetFolders);
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
        finally
        {
            IsIndexing = false;
            IndexProgress = 0;
        }
    }

    [RelayCommand]
    private void OpenSelectedFile()
    {
        if (SelectedFile?.IsFile != true) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SelectedFile.FullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"開けません: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenSelectedFolder()
    {
        if (SelectedFile == null) return;

        try
        {
            var path = SelectedFile.IsFile 
                ? Path.GetDirectoryName(SelectedFile.FullPath) 
                : SelectedFile.FullPath;

            if (!string.IsNullOrEmpty(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = SelectedFile.IsFile 
                        ? $"/select,\"{SelectedFile.FullPath}\""
                        : $"\"{path}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"開けません: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        _ = LoadFileTreeAsync(_settingsService.Settings.TargetFolders);
    }
}

/// <summary>
/// ファイルツリーのノード
/// </summary>
public partial class FileTreeNode : ObservableObject
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsFolder { get; set; }
    public bool IsFile { get; set; }
    public long Size { get; set; }
    public DateTime Modified { get; set; }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public ObservableCollection<FileTreeNode> Children { get; } = [];

    public string FolderIcon => IsExpanded ? "FolderOpen" : "Folder";

    public string SizeText => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{Size / (1024.0 * 1024):F1} MB",
        _ => $"{Size / (1024.0 * 1024 * 1024):F2} GB"
    };

    public string ModifiedText => Modified.ToString("yyyy/MM/dd HH:mm");

    public ImageSource? IconSource
    {
        get
        {
            if (!IsFile) return null;
            try
            {
                return FileIconHelper.GetIcon(FullPath);
            }
            catch
            {
                return null;
            }
        }
    }
}

/// <summary>
/// ファイルアイコン取得ヘルパー
/// </summary>
public static class FileIconHelper
{
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return null;

        if (Cache.TryGetValue(ext, out var cached))
        {
            return cached;
        }

        try
        {
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
            if (icon != null)
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                Cache[ext] = source;
                icon.Dispose();
                return source;
            }
        }
        catch { }

        return null;
    }
}
