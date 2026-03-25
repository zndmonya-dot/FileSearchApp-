// =============================================================================
// Home.Settings.cs — partial class Home
// =============================================================================
// 役割: 設定モーダルの開閉、フォルダ/拡張子の追加・バリデーション、保存と IndexService 初期化。
// 文言: UserMessages（設定エラーは FolderPathRequired 等）。
// =============================================================================
using FileSearch.Messages;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Settings;
using Microsoft.AspNetCore.Components;

namespace FileSearch.Blazor.Components.Pages;

public partial class Home
{
    /// <summary>フッター用。最終インデックス更新の相対・短い日時文字列。</summary>
    private string GetLastUpdateText() => DisplayFormatters.FormatLastIndexUpdate(SettingsService.Settings.LastIndexUpdate);

    /// <summary>現在の設定を編集用状態にコピーしてモーダルを開く。</summary>
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

    /// <summary>設定モーダルを閉じる。</summary>
    private void CloseSettings() => showSettings = false;

    /// <summary>手入力パスを正規化し、重複・存在チェックのうえ TargetFolders に追加。</summary>
    private void HandleAddFolder()
    {
        _settingsEdit.FolderMessage = null;
        var path = (_settingsEdit.NewFolderPath ?? "").Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(path))
        {
            _settingsEdit.FolderMessage = UserMessages.FolderPathRequired;
            return;
        }
        if (!Directory.Exists(path))
        {
            _settingsEdit.FolderMessage = UserMessages.FolderNotFound;
            return;
        }
        var normalizedExisting = _settingsEdit.TargetFolders
            .Select(f => f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).ToList();
        if (normalizedExisting.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            _settingsEdit.FolderMessage = UserMessages.AlreadyAdded;
            return;
        }
        _settingsEdit.TargetFolders.Add(path);
        _settingsEdit.NewFolderPath = "";
    }

    /// <summary>OS のフォルダピッカーで選んだパスを追加。</summary>
    private async Task HandleBrowseFolder()
    {
        _settingsEdit.FolderMessage = null;
        try
        {
            var path = await PickFolderAsync();
            if (string.IsNullOrEmpty(path)) return;

            path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedExisting = _settingsEdit.TargetFolders
                .Select(f => f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).ToList();
            if (normalizedExisting.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _settingsEdit.FolderMessage = UserMessages.AlreadyAdded;
                return;
            }
            _settingsEdit.TargetFolders.Add(path);
        }
        catch (Exception ex)
        {
            _settingsEdit.FolderMessage = UserMessages.FolderPickerFailed(ex.Message);
        }
    }

    /// <summary>Windows の FolderPicker。非 Windows では null。</summary>
    private static async Task<string?> PickFolderAsync()
    {
#if WINDOWS
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
        picker.FileTypeFilter.Add("*");
        var hwnd = GetWindowHandle();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
#else
        await Task.CompletedTask;
        return null;
#endif
    }

#if WINDOWS
    /// <summary>FolderPicker を前面に出すための WinUI ウィンドウハンドル。</summary>
    private static nint GetWindowHandle()
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window winuiWindow)
            return WinRT.Interop.WindowNative.GetWindowHandle(winuiWindow);
        return nint.Zero;
    }
#endif

    /// <summary>検索対象フォルダ一覧から 1 件削除。</summary>
    private void RemoveFolder(string f)
    {
        _settingsEdit.TargetFolders.Remove(f);
    }

    /// <summary>拡張子を「.」付きに正規化して追加。</summary>
    private void HandleAddTargetExtension()
    {
        var ext = (_settingsEdit.NewTargetExtension ?? "").Trim();
        if (!string.IsNullOrEmpty(ext) && !ext.StartsWith(".")) ext = "." + ext;
        if (string.IsNullOrEmpty(ext)) { _settingsEdit.ExtensionMessage = null; return; }
        if (_settingsEdit.TargetExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) { _settingsEdit.ExtensionMessage = UserMessages.AlreadyAdded; return; }
        _settingsEdit.TargetExtensions.Add(ext);
        _settingsEdit.NewTargetExtension = "";
        _settingsEdit.ExtensionMessage = null;
    }

    /// <summary>対象拡張子一覧から 1 件削除。</summary>
    private void RemoveTargetExtension(string ext)
    {
        _settingsEdit.TargetExtensions.Remove(ext);
    }

    /// <summary>編集内容を永続化し、インデックス再初期化・検索サービス更新・テーマ反映後にモーダルを閉じる。</summary>
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
}
