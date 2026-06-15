using FileSearch.Messages;
using Microsoft.Maui.ApplicationModel;

namespace FileSearch.Blazor;

/// <summary>OS ウィンドウタイトル（読み・バージョン付き）。</summary>
internal static class AppWindowTitle
{
    public static string Current =>
        $"{UserMessages.AppTitle}（{UserMessages.AppTitleReading}） v{FormatDisplayVersion(AppInfo.Current.VersionString)}";

    /// <summary>1.0.0.0 等を 2.0 形式に揃える。</summary>
    private static string FormatDisplayVersion(string version)
    {
        if (!Version.TryParse(version, out var parsed))
            return version;
        return $"{parsed.Major}.{parsed.Minor}";
    }
}
