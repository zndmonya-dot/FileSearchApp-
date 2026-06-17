using System.Text.Json;
using FileSearch.Messages;

namespace FileSearch.Blazor;

/// <summary>
/// 起動画面用テーマを settings.json から解決し、WebView 表示前に wwwroot/ui-theme.json へ書き出す。
/// </summary>
internal static class BootThemeSync
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FullTextSearch",
        "settings.json");

    /// <summary>設定ファイルを読み、起動画面用 JSON を同期する（MainPage 初期化前に呼ぶ）。</summary>
    public static void SyncFromSettingsFile() => WriteThemeFile(ResolveTheme());

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
        var directory = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ui-theme.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { theme = resolved }));
    }
}
