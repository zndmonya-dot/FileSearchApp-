using FileSearch.Messages;

namespace FileSearch.Blazor;

/// <summary>OS ウィンドウタイトル（読み・バージョン付き）。</summary>
internal static class AppWindowTitle
{
    public static string Current =>
        $"{UserMessages.AppTitle} v{AppVersion.Display}";
}
