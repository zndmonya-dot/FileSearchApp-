namespace FileSearch.Blazor;

/// <summary>
/// 起動時ウィンドウのサイズ・位置（Windows 11 メモ帳: 作業領域の約 2/3 幅）。
/// </summary>
internal static class AppWindowLayout
{
    public const double MinimumWidth = 800;
    public const double MinimumHeight = 560;

    /// <summary>WorkArea 取得前のフォールバック（FHD 想定）。</summary>
    public const double FallbackWidth = 1270;
    public const double FallbackHeight = 720;

    /// <summary>Win11 メモ帳相当: 作業領域幅の約 2/3。</summary>
    private const double WidthRatio = 0.66;
    private const double HeightRatio = 0.70;
    private const int EdgeMarginPx = 40;
    private const int MinWidthPx = 800;
    private const int MinHeightPx = 560;

    /// <summary>作業領域（px）から Win11 メモ帳相当サイズを算出する。</summary>
    private static (int Width, int Height) ComputeInitialSize(int workAreaWidth, int workAreaHeight)
    {
        var w = Math.Clamp((int)(workAreaWidth * WidthRatio), MinWidthPx, workAreaWidth - EdgeMarginPx);
        var h = Math.Clamp((int)(workAreaHeight * HeightRatio), MinHeightPx, workAreaHeight - EdgeMarginPx);
        return (w, h);
    }

#if WINDOWS
    private static bool _initialLayoutDone;
    private static bool _iconApplied;

    /// <summary>WinUI ウィンドウ作成直後に呼ぶ。</summary>
    public static void ApplyToWinUiWindow(Microsoft.UI.Xaml.Window nativeWindow)
    {
        ScheduleWindowIcon(nativeWindow);

        if (_initialLayoutDone)
            return;

        var appWindow = nativeWindow.AppWindow;
        var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
            appWindow.Id,
            Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        ApplyToAppWindow(appWindow, displayArea.WorkArea);
    }

    /// <summary>タイトルバー左のアイコンを設定する（Activated 後に適用）。</summary>
    public static void ApplyWindowIcon(Microsoft.UI.Xaml.Window nativeWindow)
    {
        var iconPath = ResolveIconPath();
        if (iconPath == null)
            return;

        var fullPath = Path.GetFullPath(iconPath);

        if (Platforms.Windows.Win32WindowIcon.TrySetAppWindowIcon(nativeWindow.AppWindow, fullPath))
        {
            _iconApplied = true;
            return;
        }

        try
        {
            nativeWindow.AppWindow.SetIcon(fullPath);
            _iconApplied = true;
        }
        catch
        {
            /* SetIcon(string) 失敗時は Win32 へ */
        }

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
            Platforms.Windows.Win32WindowIcon.ApplyTitleBarIcon(hwnd, fullPath);
            _iconApplied = true;
        }
        catch
        {
            /* HWND 未準備 */
        }
    }

    private static void ScheduleWindowIcon(Microsoft.UI.Xaml.Window nativeWindow)
    {
        void ApplyWhenReady()
        {
            if (_iconApplied)
                return;
            ApplyWindowIcon(nativeWindow);
        }

        nativeWindow.Activated += (_, args) =>
        {
            if (args.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated)
                return;

            var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (queue != null)
                queue.TryEnqueue(ApplyWhenReady);
            else
                ApplyWhenReady();
        };

        ApplyWhenReady();
    }

    private static string? ResolveIconPath()
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var path in new[]
        {
            Path.Combine(baseDir, "appicon.ico"),
            Path.Combine(baseDir, "Resources", "AppIcon", "appicon.ico"),
        })
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static void ApplyToAppWindow(
        Microsoft.UI.Windowing.AppWindow appWindow,
        Windows.Graphics.RectInt32 work)
    {
        var (width, height) = ComputeInitialSize(work.Width, work.Height);
        appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

        var x = work.X + (work.Width - width) / 2;
        var y = work.Y + (work.Height - height) / 2;
        x = Math.Max(work.X, Math.Min(x, work.X + work.Width - width));
        y = Math.Max(work.Y, Math.Min(y, work.Y + work.Height - height));
        appWindow.Move(new Windows.Graphics.PointInt32(x, y));
        _initialLayoutDone = true;
    }
#endif
}
