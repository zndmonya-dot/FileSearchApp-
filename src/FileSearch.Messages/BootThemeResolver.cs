namespace FileSearch.Messages;

/// <summary>起動画面・アプリ UI のテーマ解決（settings.json の ThemeMode と OS 設定）。</summary>
public static class BootThemeResolver
{
    /// <summary>起動画面用のテーマ名（<c>dark</c> / <c>light</c>）。</summary>
    public static string ResolveBootTheme(string? themeMode, bool systemPrefersLight) =>
        IsDarkTheme(themeMode, systemPrefersLight) ? "dark" : "light";

    /// <summary>ダークテーマか。</summary>
    public static bool IsDarkTheme(string? themeMode, bool systemPrefersLight)
    {
        var mode = themeMode?.Trim();
        if (string.IsNullOrEmpty(mode) || mode.Equals("System", StringComparison.OrdinalIgnoreCase))
            return !systemPrefersLight;

        if (mode.Equals("Light", StringComparison.OrdinalIgnoreCase))
            return false;
        if (mode.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("Chameleon", StringComparison.OrdinalIgnoreCase))
            return true;

        return !systemPrefersLight;
    }
}
