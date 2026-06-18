// =============================================================================
// Home.Settings.cs — partial class Home
// =============================================================================
// 役割: 設定モーダルの開閉、フォルダ/拡張子の追加・バリデーション、保存と IndexService 初期化。
// 文言: UserMessages。
// =============================================================================
using FullTextSearch.Core;
using FullTextSearch.Core.UI;
using FileSearch.Messages;
using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Preview;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace FileSearch.Blazor.Components.Pages;

public partial class Home
{
    /// <summary>フッター用。最終インデックス更新の相対・短い日時文字列。</summary>
    private string GetLastUpdateText() => DisplayFormatters.FormatLastIndexUpdate(SettingsService.Settings.LastIndexUpdate);

    /// <summary>現在の設定を編集用状態にコピーしてモーダルを開く。</summary>
    private void OpenSettings()
    {
        if (isIndexing) return;
        _settingsEdit.TargetFolders = SettingsService.Settings.TargetFolders.ToList();
        _settingsEdit.DisabledTargetFolders = SettingsService.Settings.DisabledTargetFolders.ToList();
        _settingsEdit.IndexPath = SettingsService.Settings.IndexPath;
        _settingsEdit.TargetExtensions = SettingsService.Settings.TargetExtensions.ToList();
        _settingsEdit.AutoRebuildDailyHours = SettingsService.Settings.AutoRebuildDailyHours.ToList();
        _settingsEdit.ThemeMode = SettingsService.Settings.ThemeMode ?? "System";
        _settingsEdit.IndexPathMessage = null;
        showSettings = true;
    }

    /// <summary>設定モーダルを閉じる。</summary>
    private void CloseSettings() => showSettings = false;

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

    /// <summary>OS のフォルダピッカーで選んだパスをインデックス保存先に設定する。</summary>
    private async Task HandleBrowseIndexPath()
    {
        _settingsEdit.IndexPathMessage = null;
        try
        {
            var path = await PickFolderAsync();
            if (string.IsNullOrEmpty(path)) return;
            _settingsEdit.IndexPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex)
        {
            _settingsEdit.IndexPathMessage = UserMessages.FolderPickerFailed(ex.Message);
        }
    }

    /// <summary>対象拡張子の選択を切り替える。</summary>
    private void HandleToggleTargetExtension(string ext)
    {
        ext = PreviewHelper.NormalizeExtension(ext);
        if (!GetSupportedExtensions().Contains(ext))
            return;

        var existing = _settingsEdit.TargetExtensions
            .FirstOrDefault(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            _settingsEdit.TargetExtensions.Remove(existing);
        else
            _settingsEdit.TargetExtensions.Add(ext);
    }

    /// <summary>設定画面の拡張子ピッカー用。抽出器が対応する拡張子の一覧。</summary>
    private IReadOnlyList<string> BuildAvailableExtensions() =>
        TextExtractors
            .SelectMany(e => e.SupportedExtensions)
            .Select(PreviewHelper.NormalizeExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private HashSet<string> GetSupportedExtensions() =>
        BuildAvailableExtensions().ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>編集内容を永続化し、インデックス再初期化・検索サービス更新・テーマ反映後にモーダルを閉じる。</summary>
    private async Task SaveSettings()
    {
        if (!isAdmin)
        {
            // 非管理者はテーマ・対象拡張子・対象フォルダの有効/無効のみ個人設定として保存。
            SettingsService.Settings.TargetExtensions = _settingsEdit.TargetExtensions.ToList();
            SettingsService.Settings.ThemeMode = _settingsEdit.ThemeMode ?? "System";
            SettingsService.Settings.DisabledTargetFolders = _settingsEdit.DisabledTargetFolders.ToList();
            await SettingsService.SaveAsync();
            await ApplyThemeAfterSettingsSaveAsync();
            showSettings = false;
            await RefreshFolderSkeletonTreeAsync();
            SyncScopedIndexCount();
            await InvokeAsync(StateHasChanged);
            return;
        }

        _settingsEdit.IndexPathMessage = null;
        var indexPath = (_settingsEdit.IndexPath ?? "").Trim();
        if (string.IsNullOrWhiteSpace(indexPath))
        {
            _settingsEdit.IndexPathMessage = UserMessages.IndexPathRequired;
            await InvokeAsync(StateHasChanged);
            return;
        }
        if (!Directory.Exists(indexPath))
        {
            try
            {
                Directory.CreateDirectory(indexPath);
            }
            catch (Exception ex)
            {
                _settingsEdit.IndexPathMessage = UserMessages.IndexPathNotFoundSaveError;
                Logger.LogError(ex, "Failed to create index directory at {IndexPath}", indexPath);
                await InvokeAsync(StateHasChanged);
                return;
            }
        }

        SettingsService.Settings.TargetFolders = _settingsEdit.TargetFolders
            .Select(IndexPaths.NormalizeFolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SettingsService.Settings.IndexPath = indexPath;
        SettingsService.Settings.TargetExtensions = _settingsEdit.TargetExtensions.ToList();
        SettingsService.Settings.AutoRebuildDailyHours =
            AutoRebuildSchedule.NormalizeDailyHours(_settingsEdit.AutoRebuildDailyHours);
        SettingsService.Settings.ThemeMode = _settingsEdit.ThemeMode ?? "System";
        await SettingsService.SaveAsync();

        // 共有設定ファイルへ書き込み（利用者は起動時にここから読む）。
        if (!AppMode.TrySaveSharedConfig(
                indexPath,
                SettingsService.Settings.TargetFolders,
                SettingsService.Settings.AutoRebuildDailyHours))
        {
            var sharedPath = AppMode.ResolveSharedConfigPath(indexPath);
            _settingsEdit.IndexPathMessage = UserMessages.SharedConfigSaveFailed(sharedPath ?? indexPath);
            await InvokeAsync(StateHasChanged);
            return;
        }

        try
        {
            await IndexService.InitializeAsync(SettingsService.Settings.IndexPath);
            if (IndexService.LastInitializeFailed)
            {
                indexErrorMessage = UserMessages.IndexLoadFailed;
                _settingsEdit.IndexPathMessage = UserMessages.IndexLoadFailed;
            }
            else
            {
                SearchService.RefreshIndex();
                indexErrorMessage = null;
                _settingsEdit.IndexPathMessage = null;
            }
        }
        catch (Exception ex)
        {
            indexErrorMessage = UserMessages.IndexLoadFailed;
            _settingsEdit.IndexPathMessage = UserMessages.IndexLoadFailed;
            Logger.LogError(ex, "Failed to re-initialize index at {IndexPath}", SettingsService.Settings.IndexPath);
        }

        await ApplyThemeAfterSettingsSaveAsync();
        showSettings = false;
        await RefreshFolderSkeletonTreeAsync();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>設定保存後のテーマ反映。</summary>
    private async Task ApplyThemeAfterSettingsSaveAsync()
    {
        if (string.Equals(SettingsService.Settings.ThemeMode, "System", StringComparison.OrdinalIgnoreCase))
        {
            try { isDarkMode = await GetPreferredColorSchemeFromSystemAsync(); } catch { /* keep current */ }
        }
        else
        {
            ApplyThemeFromSettings();
        }
        try
        {
            BootThemeSync.WriteTheme(isDarkMode);
            await JSRuntime.InvokeVoidAsync("setBootSplashTheme", isDarkMode ? "dark" : "light");
        }
        catch { /* WebView 未準備 */ }
    }
}
