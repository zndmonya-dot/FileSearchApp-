using System.Windows;
using FullTextSearch.Core.Index;

namespace FullTextSearch.App.Views;

/// <summary>
/// IndexProgressWindow.xaml の相互作用ロジック
/// </summary>
public partial class IndexProgressWindow : Window
{
    private CancellationTokenSource? _cts;

    public IndexProgressWindow()
    {
        InitializeComponent();
    }

    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    public void Start()
    {
        _cts = new CancellationTokenSource();
    }

    public void UpdateProgress(IndexProgress progress)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBar.Value = progress.ProgressPercent;
            ProgressText.Text = $"{progress.ProcessedFiles:N0} / {progress.TotalFiles:N0} ({progress.ProgressPercent:F1}%)";
            CurrentFileText.Text = progress.CurrentFile ?? "";

            if (progress.ErrorCount > 0)
            {
                CurrentFileText.Text += $" (エラー: {progress.ErrorCount}件)";
            }
        });
    }

    public void Complete()
    {
        Dispatcher.Invoke(() =>
        {
            DialogResult = true;
            Close();
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Dispose();
        base.OnClosed(e);
    }
}

