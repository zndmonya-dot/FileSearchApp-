using System.Text.Json;
using FileSearch.Messages;

namespace FileSearch.Blazor;

/// <summary>
/// 起動画面用テーマを settings.json から解決し、LocalAppData へ書き出す（MSIX ではインストール先が読み取り専用のため）。
/// </summary>
internal static class BootThemeSync
{
    /// <summary>WebView 初期化前に解決した起動テーマ（documentCreated スクリプト注入用）。</summary>
    internal static string? PendingBootTheme { get; private set; }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FullTextSearch",
        "settings.json");

    private static readonly string BootThemePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FullTextSearch",
        "ui-theme.json");

    /// <summary>設定ファイルを読み、起動画面用 JSON を同期する（MainPage 初期化前に呼ぶ）。解決済みテーマ名を返す。</summary>
    public static string SyncFromSettingsFile()
    {
        var theme = ResolveTheme();
        PendingBootTheme = theme;
        WriteThemeFile(theme);
        return theme;
    }

    /// <summary>解決済みテーマを起動画面用 JSON と次回起動の参照用に書き出す。</summary>
    public static void WriteTheme(bool isDarkMode) => WriteThemeFile(isDarkMode ? "dark" : "light");

    private static string ResolveTheme() =>
        BootThemeResolver.ResolveBootTheme(
            ReadThemeModeFromSettings(),
            Application.Current?.RequestedTheme == AppTheme.Light);

    private static string? ReadThemeModeFromSettings()
    {
        if (!File.Exists(SettingsPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (!doc.RootElement.TryGetProperty("themeMode", out var prop))
                return null;
            return prop.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static void WriteThemeFile(string theme)
    {
        var resolved = theme == "light" ? "light" : "dark";
        try
        {
            var directory = Path.GetDirectoryName(BootThemePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(BootThemePath, JsonSerializer.Serialize(new { theme = resolved }));
        }
        catch
        {
            // LocalAppData への書き込み失敗時も起動継続（WebView 注入スクリプトでテーマを渡す）。
        }
    }
}
