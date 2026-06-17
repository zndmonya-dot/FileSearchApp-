#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace FileSearch.Blazor.Platforms.Windows;

/// <summary>非パッケージ WinUI ウィンドウのタイトルバー／タスクバー用アイコン設定。</summary>
internal static class Win32WindowIcon
{
    private const int WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    /// <summary>WinUI 推奨の AppWindow.SetIcon(IconId) でタイトルバーアイコンを設定する。</summary>
    public static bool TrySetAppWindowIcon(AppWindow appWindow, string? iconPath)
    {
        var hIcon = LoadTitleBarIconHandle(iconPath);
        if (hIcon == IntPtr.Zero)
            return false;

        try
        {
            var iconId = Win32Interop.GetIconIdFromIcon(hIcon);
            appWindow.SetIcon(iconId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>WM_SETICON によるフォールバック。</summary>
    public static void ApplyTitleBarIcon(IntPtr hwnd, string? iconPath)
    {
        if (hwnd == IntPtr.Zero)
            return;

        if (TryApplyFromExe(hwnd))
            return;

        if (string.IsNullOrEmpty(iconPath) || !iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            return;

        var fullPath = Path.GetFullPath(iconPath);
        var small = LoadImage(IntPtr.Zero, fullPath, ImageIcon, 16, 16, LrLoadFromFile);
        var big = LoadImage(IntPtr.Zero, fullPath, ImageIcon, 32, 32, LrLoadFromFile);
        if (small != IntPtr.Zero)
            SendMessage(hwnd, WmSetIcon, (IntPtr)IconSmall, small);
        if (big != IntPtr.Zero)
            SendMessage(hwnd, WmSetIcon, (IntPtr)IconBig, big);
    }

    private static IntPtr LoadTitleBarIconHandle(string? iconPath)
    {
        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            var fullPath = Path.GetFullPath(iconPath);
            var fromFile = LoadImage(IntPtr.Zero, fullPath, ImageIcon, 16, 16, LrLoadFromFile);
            if (fromFile != IntPtr.Zero)
                return fromFile;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return IntPtr.Zero;

        ExtractIconEx(exe, 0, out _, out var small, 1);
        return small;
    }

    private static bool TryApplyFromExe(IntPtr hwnd)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return false;

        ExtractIconEx(exe, 0, out var large, out var small, 1);
        if (small != IntPtr.Zero)
            SendMessage(hwnd, WmSetIcon, (IntPtr)IconSmall, small);
        if (large != IntPtr.Zero)
            SendMessage(hwnd, WmSetIcon, (IntPtr)IconBig, large);
        return small != IntPtr.Zero || large != IntPtr.Zero;
    }
}
#endif
