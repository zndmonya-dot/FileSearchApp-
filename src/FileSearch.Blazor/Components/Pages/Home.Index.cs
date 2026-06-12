// =============================================================================
// Home.Index.cs — partial class Home
// =============================================================================
// 役割: 差分更新/再構築の起動、進捗コールバック、スキップログを開く、IndexUpdateDialog 確定処理。
// 文言: UserMessages。インデックス内部・ログ行の日本語は FullTextSearch.Core.IndexMessages（Lucene 側）。
// =============================================================================
using System.Diagnostics;
using FileSearch.Messages;
using FullTextSearch.Core;
using FullTextSearch.Core.Index;
using FullTextSearch.Infrastructure.Settings;
using Microsoft.Extensions.Logging;

namespace FileSearch.Blazor.Components.Pages;

public partial class Home
{
    /// <summary>インデックス保存先フォルダをエクスプローラーで開く。</summary>
    private void OpenIndexFolder()
    {
        if (!isAdmin) return;
        var path = SettingsService.Settings.IndexPath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
    }

    /// <summary>skipped_files.log を既定アプリで開く。失敗時は indexErrorMessage を設定。</summary>
    private Task OpenSkippedLog()
    {
        indexErrorMessage = null;

        var indexPath = SettingsService.Settings.IndexPath;
        if (string.IsNullOrWhiteSpace(indexPath))
        {
            indexErrorMessage = UserMessages.IndexPathNotSet;
            StateHasChanged();
            return Task.CompletedTask;
        }

        var logPath = Path.GetFullPath(Path.Combine(indexPath, DefaultPaths.SkippedFilesLogFileName));
        if (!File.Exists(logPath))
        {
            indexErrorMessage = UserMessages.SkipLogNotFound;
            StateHasChanged();
            return Task.CompletedTask;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            indexErrorMessage = UserMessages.SkipLogOpenFailed;
            Logger.LogError(ex, "Failed to open {LogFile}", DefaultPaths.SkippedFilesLogFileName);
        }

        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>設定の拡張子リストから Lucene 再構築オプションを組み立てる。</summary>
    private static IndexRebuildOptions GetIndexRebuildOptions(IAppSettingsService settings)
    {
        var exts = settings.Settings.TargetExtensions;
        return new IndexRebuildOptions { TargetExtensions = exts != null && exts.Count > 0 ? exts : null };
    }

    /// <summary>進捗コールバック。件数・スロットルで UI 更新を間引く。</summary>
    private IProgress<IndexProgress> CreateThrottledProgress(string countUnit)
    {
        _lastReportedProgressCount = -1;
        return new Progress<IndexProgress>(p =>
        {
            indexProgressPercent = p.TotalFiles > 0 ? (int)((double)p.ProcessedFiles / p.TotalFiles * 100) : 0;
            var baseText = UserMessages.FormatIndexProgressCounts(
                p.ProcessedFiles, p.TotalFiles, countUnit, p.ErrorCount);
            indexProgressText = string.IsNullOrEmpty(p.CurrentFile)
                ? baseText
                : Path.GetFileName(p.CurrentFile);
            var shouldUpdate = p.CurrentFile == null
                || (p.ProcessedFiles - _lastReportedProgressCount) >= ProgressReportInterval
                || (DateTime.UtcNow - _lastReportedProgressTime).TotalMilliseconds >= ProgressReportThrottleMs;
            if (shouldUpdate)
            {
                _lastReportedProgressCount = p.ProcessedFiles;
                _lastReportedProgressTime = DateTime.UtcNow;
                InvokeAsync(StateHasChanged);
            }
        });
    }

    /// <summary>差分更新・再構築の共通ラッパー。キャンセル・完了時の件数・スキップ数の反映を含む。</summary>
    private async Task RunIndexUpdateAsync(
        string initialMessage,
        string countUnit,
        Func<IProgress<IndexProgress>, CancellationToken, Task> runAsync,
        Func<Exception, string> getErrorMessage,
        string logContext)
    {
        if (isIndexing) return;
        if (SettingsService.Settings.TargetFolders.Count == 0)
        {
            indexErrorMessage = UserMessages.NoTargetFolders;
            StateHasChanged();
            return;
        }
        indexErrorMessage = null;
        indexSkipCount = 0;
        isIndexing = true;
        indexProgressPercent = 0;
        indexProgressText = initialMessage;
        _indexCts?.Dispose();
        _indexCts = new CancellationTokenSource();
        var token = _indexCts.Token;
        StateHasChanged();
        await Task.Yield();

        var progress = CreateThrottledProgress(countUnit);
        try
        {
            await Task.Run(async () => await runAsync(progress, token), token);
            if (token.IsCancellationRequested) return;
            indexCount = IndexService.GetStats().DocumentCount;
            SettingsService.Settings.LastIndexUpdate = DateTime.Now;
            await SettingsService.SaveAsync();
            indexErrorMessage = null;
            var skipped = IndexService.LastSkippedFiles;
            if (skipped.Count > 0)
                indexSkipCount = skipped.Count;
            else
                indexSkipCount = 0;
        }
        catch (OperationCanceledException)
        {
            indexProgressText = UserMessages.IndexCancelled;
            indexErrorMessage = null;
        }
        catch (Exception ex)
        {
            indexErrorMessage = getErrorMessage(ex);
            Logger.LogError(ex, logContext);
        }
        finally
        {
            _indexCts?.Dispose();
            _indexCts = null;
            isIndexing = false;
            indexProgressPercent = 0;
            StateHasChanged();
        }
    }

    /// <summary>差分更新のみ。</summary>
    private Task UpdateIndex()
    {
        var folders = SettingsService.Settings.TargetFolders;
        var options = GetIndexRebuildOptions(SettingsService);
        return RunIndexUpdateAsync(
            UserMessages.DiffDetecting,
            UserMessages.PieceUnit,
            (p, ct) => IndexService.UpdateIndexAsync(folders, p, options, ct),
            ex => string.IsNullOrEmpty(ex.Message) ? UserMessages.UpdateFailed : $"{UserMessages.UpdateFailed} {ex.Message}",
            "Index update failed");
    }

    /// <summary>全件再構築。</summary>
    private Task RebuildIndex()
    {
        var folders = SettingsService.Settings.TargetFolders;
        var options = GetIndexRebuildOptions(SettingsService);
        return RunIndexUpdateAsync(
            UserMessages.Preparing,
            UserMessages.FileUnit,
            (p, ct) => IndexService.RebuildIndexAsync(folders, p, options, ct),
            _ => UserMessages.RebuildFailed,
            "Index rebuild failed");
    }

    /// <summary>フッター「再構築」から。確認ダイアログを開く（実処理は ConfirmIndexUpdateAsync）。</summary>
    private void RequestRebuildIndex()
    {
        // 非管理者は参照専用のため再構築不可（UI ボタンも非活性だが二重ガード）。
        if (!isAdmin) return;
        if (isIndexing) return;
        if (SettingsService.Settings.TargetFolders.Count == 0)
        {
            indexErrorMessage = UserMessages.NoTargetFolders;
            StateHasChanged();
            return;
        }
        indexErrorMessage = null;
        _indexUpdateFullRebuild = false;
        _showRebuildConfirm = true;
        StateHasChanged();
    }

    /// <summary>インデックス更新ダイアログを閉じる。</summary>
    private void CancelRebuildConfirm()
    {
        _showRebuildConfirm = false;
        StateHasChanged();
    }

    /// <summary>ダイアログで差分か全件再構築かを切り替え。</summary>
    private void OnIndexUpdateModeChanged(bool fullRebuild)
    {
        _indexUpdateFullRebuild = fullRebuild;
        StateHasChanged();
    }

    /// <summary>ダイアログ「実行」。UpdateIndex または RebuildIndex を起動。</summary>
    private async Task ConfirmIndexUpdateAsync()
    {
        _showRebuildConfirm = false;
        StateHasChanged();
        if (_indexUpdateFullRebuild)
            await RebuildIndex();
        else
            await UpdateIndex();
    }

    /// <summary>構築中フッターのキャンセル。インデックス処理の CancellationToken をキャンセル。</summary>
    private void CancelIndexBuild()
    {
        _indexCts?.Cancel();
    }
}
