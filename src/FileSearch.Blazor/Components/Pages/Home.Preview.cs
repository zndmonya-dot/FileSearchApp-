// =============================================================================
// Home.Preview.cs — partial class Home
// =============================================================================
// 役割: プレビューのデバウンス読み込み、ハイライト行の前後（JS 連携）、既定アプリでファイル/フォルダを開く。
// =============================================================================
using System.Diagnostics;
using FileSearch.Messages;
using FullTextSearch.Core.Models;
using Microsoft.AspNetCore.Components;
namespace FileSearch.Blazor.Components.Pages;

public partial class Home
{
    /// <summary>ファイル切替時にプレビュー読み込みを遅延実行（連続クリック対策）。</summary>
    private void SchedulePreviewLoad(string path)
    {
        _pendingPreviewPath = path;
        _previewCts?.Cancel();
        _previewDebounceTimer?.Dispose();
        _previewDebounceTimer = new Timer(_ =>
        {
            var p = _pendingPreviewPath;
            _pendingPreviewPath = null;
            if (!string.IsNullOrEmpty(p))
                _ = InvokeAsync(async () => { await LoadPreview(p); });
        }, null, PreviewDebounceMs, Timeout.Infinite);
    }

    /// <summary>PreviewService で抽出し、JS 側で WinMerge 風に描画する。</summary>
    private async Task LoadPreview(string path)
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;
        isLoadingPreview = true;
        _previewResult = null;
        StateHasChanged();
        await Task.Yield();
        if (token.IsCancellationRequested) return;
        try
        {
            _previewResult = await PreviewService.GetPreviewAsync(path, searchQuery?.Trim(), token, searchMode);
            if (token.IsCancellationRequested) return;
            _hasTriedInitialHighlightScroll = false;
        }
        catch (OperationCanceledException)
        {
            _previewResult = new PreviewResult
            {
                Content = UserMessages.PreviewLoadCancelled,
                LineStartOffsets = [0],
                IsError = true
            };
        }
        catch (Exception ex)
        {
            _previewResult = new PreviewResult
            {
                Content = UserMessages.PreviewErrorLine(ex.Message),
                LineStartOffsets = [0],
                IsError = true
            };
        }
        finally { isLoadingPreview = false; StateHasChanged(); }
    }

    /// <summary>JS プレビュー描画完了後、最初のハイライト行へスクロールする。</summary>
    private async Task HandlePreviewRendered()
    {
        if (isLoadingPreview || !ShowHighlightNav || _hasTriedInitialHighlightScroll || _previewResult == null)
            return;
        _hasTriedInitialHighlightScroll = true;
        try
        {
            await PreviewJs.InitHighlightNavAsync(_previewResult.MatchLineNumbers);
            var result = await PreviewJs.ScrollToFirstHighlightInstantAsync();
            if (!string.IsNullOrEmpty(result))
            {
                _highlightNavInfo = FormatHighlightNavInfo(result);
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            PreviewJs.LogInteropFailure("HandlePreviewRendered", ex);
        }
    }

    /// <summary>検索ヒットが複数ファイルあるときの前後ファイル移動を出すか。</summary>
    private bool ShowFileNav => selectedFile != null && _fileNavList != null && _fileNavList.Count > 1;
    /// <summary>検索語があり、本文ハイライト間移動を出すか。</summary>
    private bool ShowHighlightNav => selectedFile != null && !string.IsNullOrWhiteSpace(searchQuery);

    private bool ShowNavButtons => selectedFile != null && (ShowFileNav || ShowHighlightNav);

    /// <summary>ツールバー右の位置表示。ハイライト優先、なければファイル n/m。</summary>
    private string? NavInfo => !string.IsNullOrEmpty(_highlightNavInfo) ? _highlightNavInfo
        : (_fileNavList != null && _fileNavIndex >= 0 && _fileNavIndex < _fileNavList.Count ? $"{_fileNavIndex + 1}/{_fileNavList.Count}" : null);

    /// <summary>preview.js の scrollToNext/PrevHighlight。戻り値は「行番号|現在|総数」。</summary>
    private async Task<string?> TryScrollToHighlightAsync(bool next)
    {
        try
        {
            return await PreviewJs.ScrollToHighlightAsync(next, wrap: !ShowFileNav);
        }
        catch (Exception ex)
        {
            PreviewJs.LogInteropFailure(next ? "ScrollToNextHighlight" : "ScrollToPrevHighlight", ex);
            return null;
        }
    }

    /// <summary>ハイライトが尽きたら次のファイルへ（ShowFileNav 時）。</summary>
    private async Task GoNext()
    {
        var result = await TryScrollToHighlightAsync(next: true);
        if (!string.IsNullOrEmpty(result))
        {
            _highlightNavInfo = FormatHighlightNavInfo(result);
            StateHasChanged();
            return;
        }
        if (ShowFileNav)
            SelectNextFile();
    }

    /// <summary>GoNext の逆。</summary>
    private async Task GoPrev()
    {
        var result = await TryScrollToHighlightAsync(next: false);
        if (!string.IsNullOrEmpty(result))
        {
            _highlightNavInfo = FormatHighlightNavInfo(result);
            StateHasChanged();
            return;
        }
        if (ShowFileNav)
            SelectPrevFile();
    }

    /// <summary>JS からのパイプ区切り文字列を表示用に整形。</summary>
    private static string? FormatHighlightNavInfo(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var parts = raw.Split('|');
        if (parts.Length != 3) return null;
        var lineNum = int.TryParse(parts[0], out var ln) ? ln : 0;
        var current = int.TryParse(parts[1], out var c) ? c : 0;
        var total = int.TryParse(parts[2], out var t) ? t : 0;
        if (total <= 0) return null;
        return lineNum > 0
            ? UserMessages.FormatHighlightNavWithLine(lineNum, current, total)
            : UserMessages.FormatHighlightNavCountsOnly(current, total);
    }

    /// <summary>_fileNavList 上で次のファイルを選択（プレビュー読み込みは SelectFile 経由）。</summary>
    private void SelectNextFile()
    {
        if (_fileNavList == null || _fileNavList.Count < 2) return;
        var next = (_fileNavIndex + 1) % _fileNavList.Count;
        SelectFile(_fileNavList[next]);
    }

    /// <summary>SelectNextFile の逆方向。</summary>
    private void SelectPrevFile()
    {
        if (_fileNavList == null || _fileNavList.Count < 2) return;
        var prev = _fileNavIndex <= 0 ? _fileNavList.Count - 1 : _fileNavIndex - 1;
        SelectFile(_fileNavList[prev]);
    }

    /// <summary>既定アプリでファイルを開く。</summary>
    private void OpenFile()
    {
        if (selectedFile != null)
            Process.Start(new ProcessStartInfo { FileName = selectedFile.FilePath, UseShellExecute = true });
    }

    /// <summary>エクスプローラーでファイルを選択状態にする。</summary>
    private void OpenFolder()
    {
        if (selectedFile != null)
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{selectedFile.FilePath}\"", UseShellExecute = true });
    }
}
