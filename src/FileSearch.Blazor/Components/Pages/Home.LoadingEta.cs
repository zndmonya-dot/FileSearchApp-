// 読み込み・検索・構築中の残り時間／経過時間表示。
using FileSearch.Messages;
using FullTextSearch.Core.UI;

namespace FileSearch.Blazor.Components.Pages;

public partial class Home
{
    private Timer? _loadingEtaTimer;
    private DateTime? _folderTreeStartedUtc;
    private DateTime _indexStartedUtc;
    private int _indexEtaProcessed;
    private int _indexEtaTotal;

    private string folderTreeLoadingEtaHint = "";
    private string indexProgressEtaHint = "";

    private void MarkFolderTreeLoadStarted()
    {
        _folderTreeStartedUtc = DateTime.UtcNow;
        EnsureLoadingEtaTimer();
    }

    private void MarkFolderTreeLoadEnded()
    {
        _folderTreeStartedUtc = null;
        folderTreeLoadingEtaHint = "";
        TryStopLoadingEtaTimer();
    }

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
            LoadingEta.TryEstimateRemaining(_indexStartedUtc, processed, total),
            DateTime.UtcNow - _indexStartedUtc);
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

    private bool IsAnyLoadingTracked() =>
        isSearching || isLoadingFolderTree || isLoadingPreview || isIndexing;

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
        if (isLoadingFolderTree && _folderTreeStartedUtc is { } treeStart)
            folderTreeLoadingEtaHint = UserMessages.FormatLoadingEtaHint(null, DateTime.UtcNow - treeStart);

        if (isIndexing)
        {
            indexProgressEtaHint = UserMessages.FormatLoadingEtaHint(
                LoadingEta.TryEstimateRemaining(_indexStartedUtc, _indexEtaProcessed, _indexEtaTotal),
                DateTime.UtcNow - _indexStartedUtc);
        }
    }

    private void DisposeLoadingEtaTimer()
    {
        _loadingEtaTimer?.Dispose();
        _loadingEtaTimer = null;
    }
}
