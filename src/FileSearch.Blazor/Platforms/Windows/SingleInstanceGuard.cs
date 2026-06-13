using System.Diagnostics;
using System.Runtime.InteropServices;
using FileSearch.Messages;

namespace FileSearch.Blazor.Platforms.Windows;

/// <summary>同一ユーザーセッション内での二重起動を防ぐ。</summary>
internal static class SingleInstanceGuard
{
    private const int SwRestore = 9;
    private static Mutex? _mutex;

    /// <summary>最初のインスタンスなら true。既に起動中なら既存ウィンドウを前面に出して false。</summary>
    public static bool TryAcquire()
    {
        const string mutexName = @"Local\com.fulltext.filesearch.SingleInstance";
        _mutex = new Mutex(true, mutexName, out var createdNew);
        if (createdNew)
            return true;

        _mutex.Dispose();
        _mutex = null;
        TryActivateExistingInstance();
        return false;
    }

    private static void TryActivateExistingInstance()
    {
        var processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "FileSearch.Blazor");
        var other = Process.GetProcessesByName(processName)
            .FirstOrDefault(p => p.Id != Environment.ProcessId);
        if (other == null)
            return;

        var targetPid = (uint)other.Id;
        IntPtr found = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid != targetPid || !IsWindowVisible(hWnd))
                return true;
            found = hWnd;
            return false;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero)
        {
            found = FindWindow(null, UserMessages.AppTitle);
        }

        if (found != IntPtr.Zero)
        {
            ShowWindow(found, SwRestore);
            SetForegroundWindow(found);
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
