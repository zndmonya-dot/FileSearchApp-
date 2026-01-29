using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using FullTextSearch.Infrastructure.Preview;
using Microsoft.Extensions.DependencyInjection;

namespace FullTextSearch.App.Controls;

/// <summary>
/// プレビューをホストするコントロール
/// </summary>
public partial class PreviewHost : UserControl
{
    private readonly IPreviewService _previewService;
    private HwndHost? _hwndHost;
    private IntPtr _previewHwnd = IntPtr.Zero;
    private string? _currentFilePath;

    // テキストファイルとして扱う拡張子
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".csv", ".log", ".md",
        ".cs", ".js", ".ts", ".jsx", ".tsx",
        ".py", ".java", ".cpp", ".c", ".h", ".hpp",
        ".html", ".htm", ".css", ".scss", ".sass", ".less",
        ".xml", ".json", ".yaml", ".yml",
        ".sql", ".sh", ".bat", ".ps1",
        ".ini", ".cfg", ".conf", ".config",
        ".gitignore", ".env", ".editorconfig",
        ".rs", ".go", ".rb", ".php", ".swift", ".kt"
    };

    public static readonly DependencyProperty FilePathProperty =
        DependencyProperty.Register(
            nameof(FilePath),
            typeof(string),
            typeof(PreviewHost),
            new PropertyMetadata(null, OnFilePathChanged));

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public PreviewHost()
    {
        InitializeComponent();

        _previewService = App.Services.GetRequiredService<IPreviewService>();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    private static void OnFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PreviewHost host)
        {
            host.UpdatePreview(e.NewValue as string);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CreatePreviewWindow();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DestroyPreviewWindow();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewBounds();
    }

    private void CreatePreviewWindow()
    {
        if (_previewHwnd != IntPtr.Zero)
        {
            return;
        }

        var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (hwndSource == null)
        {
            return;
        }

        _previewHwnd = CreateNativeWindow(hwndSource.Handle);

        if (_previewHwnd != IntPtr.Zero)
        {
            _hwndHost = new PreviewHwndHost(_previewHwnd);
            PreviewContainer.Child = _hwndHost;
        }
    }

    private void DestroyPreviewWindow()
    {
        _previewService.ClearPreview();

        if (_hwndHost != null)
        {
            PreviewContainer.Child = null;
            _hwndHost.Dispose();
            _hwndHost = null;
        }

        if (_previewHwnd != IntPtr.Zero)
        {
            DestroyWindow(_previewHwnd);
            _previewHwnd = IntPtr.Zero;
        }
    }

    private async void UpdatePreview(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            ShowNoFilePanel();
            return;
        }

        if (filePath == _currentFilePath)
        {
            return;
        }

        _currentFilePath = filePath;

        if (!File.Exists(filePath))
        {
            ShowNoPreviewPanel("ファイルが見つかりません");
            return;
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // テキストファイルの場合は自前でプレビュー
        if (IsTextFile(extension))
        {
            await ShowTextPreview(filePath);
            return;
        }

        // Office/PDFはWindowsプレビューハンドラを使用
        if (_previewService.CanPreview(filePath))
        {
            ShowLoading();

            try
            {
                var bounds = GetPreviewBounds();
                var result = await _previewService.ShowPreviewAsync(filePath, _previewHwnd, bounds);

                if (result)
                {
                    ShowNativePreview();
                }
                else
                {
                    // プレビューハンドラが失敗した場合はテキストとして試す
                    await ShowTextPreview(filePath);
                }
            }
            catch (Exception ex)
            {
                ShowNoPreviewPanel($"エラー: {ex.Message}");
            }
        }
        else
        {
            // プレビューハンドラがない場合はテキストとして試す
            await ShowTextPreview(filePath);
        }
    }

    private static bool IsTextFile(string extension)
    {
        return TextExtensions.Contains(extension);
    }

    private async Task ShowTextPreview(string filePath)
    {
        try
        {
            ShowLoading();

            var content = await Task.Run(() =>
            {
                var fileInfo = new FileInfo(filePath);
                
                // 10MB以上は読み込まない
                if (fileInfo.Length > 10 * 1024 * 1024)
                {
                    return "[ファイルサイズが大きすぎます (10MB以上)]";
                }

                // バイナリファイルかチェック
                if (IsBinaryFile(filePath))
                {
                    return "[バイナリファイルです]";
                }

                using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            });

            TextPreviewBox.Text = content;
            ShowTextPreviewPanel();
        }
        catch (Exception ex)
        {
            ShowNoPreviewPanel($"読み込みエラー: {ex.Message}");
        }
    }

    private static bool IsBinaryFile(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[8192];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);

            // NULL文字が含まれていたらバイナリ
            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private void UpdatePreviewBounds()
    {
        if (_currentFilePath != null && _previewHwnd != IntPtr.Zero && PreviewContainer.Visibility == Visibility.Visible)
        {
            var bounds = GetPreviewBounds();
            _ = _previewService.ShowPreviewAsync(_currentFilePath, _previewHwnd, bounds);
        }
    }

    private PreviewBounds GetPreviewBounds()
    {
        var size = PreviewContainer.RenderSize;
        return new PreviewBounds
        {
            Left = 0,
            Top = 0,
            Right = (int)size.Width,
            Bottom = (int)size.Height
        };
    }

    private void ShowNativePreview()
    {
        _previewService.ClearPreview();
        PreviewContainer.Visibility = Visibility.Visible;
        TextPreviewContainer.Visibility = Visibility.Collapsed;
        NoPreviewPanel.Visibility = Visibility.Collapsed;
        NoFilePanel.Visibility = Visibility.Collapsed;
        LoadingIndicator.Visibility = Visibility.Collapsed;
    }

    private void ShowTextPreviewPanel()
    {
        _previewService.ClearPreview();
        PreviewContainer.Visibility = Visibility.Collapsed;
        TextPreviewContainer.Visibility = Visibility.Visible;
        NoPreviewPanel.Visibility = Visibility.Collapsed;
        NoFilePanel.Visibility = Visibility.Collapsed;
        LoadingIndicator.Visibility = Visibility.Collapsed;
    }

    private void ShowNoPreviewPanel(string message)
    {
        _previewService.ClearPreview();
        NoPreviewMessage.Text = message;
        PreviewContainer.Visibility = Visibility.Collapsed;
        TextPreviewContainer.Visibility = Visibility.Collapsed;
        NoPreviewPanel.Visibility = Visibility.Visible;
        NoFilePanel.Visibility = Visibility.Collapsed;
        LoadingIndicator.Visibility = Visibility.Collapsed;
    }

    private void ShowNoFilePanel()
    {
        _previewService.ClearPreview();
        _currentFilePath = null;
        PreviewContainer.Visibility = Visibility.Collapsed;
        TextPreviewContainer.Visibility = Visibility.Collapsed;
        NoPreviewPanel.Visibility = Visibility.Collapsed;
        NoFilePanel.Visibility = Visibility.Visible;
        LoadingIndicator.Visibility = Visibility.Collapsed;
    }

    private void ShowLoading()
    {
        PreviewContainer.Visibility = Visibility.Collapsed;
        TextPreviewContainer.Visibility = Visibility.Collapsed;
        NoPreviewPanel.Visibility = Visibility.Collapsed;
        NoFilePanel.Visibility = Visibility.Collapsed;
        LoadingIndicator.Visibility = Visibility.Visible;
    }

    #region Native Window

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPCHILDREN = 0x02000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    private static IntPtr CreateNativeWindow(IntPtr parent)
    {
        return CreateWindowEx(
            0,
            "Static",
            "",
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            0, 0, 1, 1,
            parent,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    #endregion
}

/// <summary>
/// プレビューウィンドウをホストするHwndHost
/// </summary>
internal class PreviewHwndHost : HwndHost
{
    private readonly IntPtr _hwnd;

    public PreviewHwndHost(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        // ウィンドウは親コントロールで管理するため、ここでは何もしない
    }
}
