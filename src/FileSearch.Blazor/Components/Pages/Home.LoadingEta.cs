// インデックス構築中の残り時間（予測）表示。
using FileSearch.Messages;
using FullTextSearch.Core.UI;

namespace FileSearch.Blazor.Components.Pages;

public partial class Home
{
    private Timer? _loadingEtaTimer;
    private DateTime _indexStartedUtc;
    private int _indexEtaProcessed;
    private int _indexEtaTotal;

    private string indexProgressEtaHint = "";

    private void MarkIndexBuildStarted()
    {
        _indexStartedUtc = DateTime.UtcNow;
        _indexEtaProcessed = 0;
        _indexEtaTotal = 0;
        indexProgressEtaHint = "";
        EnsureLoadingEtaTimer();
    }

    private void MarkIndexBuildEnded()
    {
        _indexEtaProcessed = 0;
        _indexEtaTotal = 0;
        indexProgressEtaHint = "";
        TryStopLoadingEtaTimer();
    }

    private void UpdateIndexProgressEta(int processed, int total)
    {
        _indexEtaProcessed = processed;
        _indexEtaTotal = total;
        indexProgressEtaHint = UserMessages.FormatLoadingEtaHint(
            LoadingEta.TryEstimateRemaining(_indexStartedUtc, processed, total));
    }

    private void EnsureLoadingEtaTimer()
    {
        if (_loadingEtaTimer != null)
            return;

        _loadingEtaTimer = new Timer(OnLoadingEtaTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void TryStopLoadingEtaTimer()
    {
        if (IsAnyLoadingTracked())
            return;

        _loadingEtaTimer?.Dispose();
        _loadingEtaTimer = null;
    }

    private bool IsAnyLoadingTracked() => isIndexing;

    private void OnLoadingEtaTick(object? _)
    {
        try
        {
            _ = InvokeAsync(() =>
            {
                if (!IsAnyLoadingTracked())
                {
                    TryStopLoadingEtaTimer();
                    return;
                }

                UpdateLoadingEtaHints();
                StateHasChanged();
            });
        }
        catch
        {
            // 画面破棄後の tick は無視
        }
    }

    private void UpdateLoadingEtaHints()
    {
        if (isIndexing)
        {
            indexProgressEtaHint = UserMessages.FormatLoadingEtaHint(
                LoadingEta.TryEstimateRemaining(_indexStartedUtc, _indexEtaProcessed, _indexEtaTotal));
        }
    }

    private void DisposeLoadingEtaTimer()
    {
        _loadingEtaTimer?.Dispose();
        _loadingEtaTimer = null;
    }
}
