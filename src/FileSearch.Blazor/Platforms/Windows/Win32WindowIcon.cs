#if WINDOWS
using System.Runtime.InteropServices;

namespace FileSearch.Blazor.Platforms.Windows;

/// <summary>非パッケージ WinUI ウィンドウのタイトルバー／タスクバー用アイコン設定。</summary>
internal static class Win32WindowIcon
{
    private const int WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static void ApplyTitleBarIcon(IntPtr hwnd, string iconPath)
    {
        if (hwnd == IntPtr.Zero || !iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            return;

        var hIcon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
        if (hIcon == IntPtr.Zero)
            return;

        SendMessage(hwnd, WmSetIcon, (IntPtr)IconSmall, hIcon);
        SendMessage(hwnd, WmSetIcon, (IntPtr)IconBig, hIcon);
    }
}
#endif
