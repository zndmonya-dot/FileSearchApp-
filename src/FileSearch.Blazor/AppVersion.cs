using Microsoft.Maui.ApplicationModel;

namespace FileSearch.Blazor;

/// <summary>
/// アプリ版表示の一元取得。<c>FileSearch.Blazor.csproj</c> の
/// <c>ApplicationDisplayVersion</c> → MAUI <see cref="AppInfo"/> の版文字列を参照する。
/// </summary>
internal static class AppVersion
{
    /// <summary>major.minor（例: 2.0）。</summary>
    public static string Display => FormatMajorMinor(AppInfo.Current.VersionString);

    /// <summary>起動スプラッシュ等向け（例: v2.0）。</summary>
    public static string Label => $"v{Display}";

    /// <summary>1.0.0.0 等を major.minor に揃える。</summary>
    public static string FormatMajorMinor(string version)
    {
        if (!Version.TryParse(version, out var parsed))
            return version;
        return $"{parsed.Major}.{parsed.Minor}";
    }
}
