using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FullTextSearch.Infrastructure.Settings;
using Microsoft.Win32;

namespace FullTextSearch.App.ViewModels;

/// <summary>
/// 設定画面のViewModel
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _settingsService;

    [ObservableProperty]
    private ObservableCollection<string> _targetFolders = [];

    [ObservableProperty]
    private string? _selectedFolder;

    [ObservableProperty]
    private ObservableCollection<string> _targetExtensions = [];

    [ObservableProperty]
    private string? _selectedExtension;

    [ObservableProperty]
    private string _newExtension = string.Empty;

    [ObservableProperty]
    private string _indexPath = string.Empty;

    [ObservableProperty]
    private int _previewDelayMs = 100;

    [ObservableProperty]
    private int _maxSearchHistory = 50;

    [ObservableProperty]
    private bool _hasChanges;

    public SettingsViewModel(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Settings;

        TargetFolders = new ObservableCollection<string>(settings.TargetFolders);
        TargetExtensions = new ObservableCollection<string>(settings.TargetExtensions);
        IndexPath = settings.IndexPath;
        PreviewDelayMs = settings.PreviewDelayMs;
        MaxSearchHistory = settings.MaxSearchHistory;

        HasChanges = false;
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "検索対象フォルダを選択"
        };

        if (dialog.ShowDialog() == true)
        {
            var folderPath = dialog.FolderName;
            if (!TargetFolders.Contains(folderPath))
            {
                TargetFolders.Add(folderPath);
                HasChanges = true;
            }
        }
    }

    [RelayCommand]
    private void RemoveFolder()
    {
        if (SelectedFolder != null)
        {
            TargetFolders.Remove(SelectedFolder);
            HasChanges = true;
        }
    }

    [RelayCommand]
    private void AddExtension()
    {
        if (string.IsNullOrWhiteSpace(NewExtension))
        {
            return;
        }

        var ext = NewExtension.Trim();
        if (!ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        if (!TargetExtensions.Contains(ext))
        {
            TargetExtensions.Add(ext);
            NewExtension = string.Empty;
            HasChanges = true;
        }
    }

    [RelayCommand]
    private void RemoveExtension()
    {
        if (SelectedExtension != null)
        {
            TargetExtensions.Remove(SelectedExtension);
            HasChanges = true;
        }
    }

    [RelayCommand]
    private void BrowseIndexPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "インデックス保存先を選択"
        };

        if (dialog.ShowDialog() == true)
        {
            IndexPath = dialog.FolderName;
            HasChanges = true;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = _settingsService.Settings;

        settings.TargetFolders = [.. TargetFolders];
        settings.TargetExtensions = [.. TargetExtensions];
        settings.IndexPath = IndexPath;
        settings.PreviewDelayMs = PreviewDelayMs;
        settings.MaxSearchHistory = MaxSearchHistory;

        await _settingsService.SaveAsync();
        HasChanges = false;
    }

    [RelayCommand]
    private void Reset()
    {
        LoadSettings();
    }

    [RelayCommand]
    private void ClearSearchHistory()
    {
        _settingsService.Settings.SearchHistory.Clear();
        _ = _settingsService.SaveAsync();
    }
}

