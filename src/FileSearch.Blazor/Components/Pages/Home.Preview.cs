// =============================================================================
// Home.Preview.cs — partial class Home
// =============================================================================
// 役割: プレビューのデバウンス読み込み、ハイライト行の前後（JS 連携）、既定アプリでファイル/フォルダを開く。
// =============================================================================
using System.Diagnostics;
using FileSearch.Blazor.Components.Shared;
using FileSearch.Blazor.Services;
using FileSearch.Messages;
using FullTextSearch.Core.Index;
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

        if (token.IsCancellationRequested)
        {
            isLoadingPreview = false;
            StateHasChanged();
            return;
        }

        StateHasChanged();
    }

    /// <summary>JS 描画完了後に読み込み状態を解除し、最初のハイライト行へスクロールする。</summary>
    private async Task HandlePreviewRendered(string? scrollResultFromJs)
    {
        try
        {
            if (_previewResult != null
                && selectedFile != null
                && !string.IsNullOrWhiteSpace(searchQuery)
                && !_hasTriedInitialHighlightScroll)
            {
                _hasTriedInitialHighlightScroll = true;
                if (string.IsNullOrEmpty(scrollResultFromJs))
                {
                    await PreviewJs.InitHighlightNavAsync(_previewResult.MatchLineNumbers);
                    await PreviewJs.ScrollToFirstHighlightInstantAsync();
                }
            }
        }
        catch (Exception ex)
        {
            PreviewJs.LogInteropFailure("HandlePreviewRendered", ex);
        }
        finally
        {
            if (isLoadingPreview)
            {
                isLoadingPreview = false;
                StateHasChanged();
            }
        }
    }

    /// <summary>検索ヒットが複数ファイルあるときの前後ファイル移動を出すか。</summary>
    private bool ShowFileNav => selectedFile != null && _fileNavList != null && _fileNavList.Count > 1;
    /// <summary>検索語があり、本文ハイライト間移動を出すか。</summary>
    private bool ShowHighlightNav => selectedFile != null && !string.IsNullOrWhiteSpace(searchQuery);

    private bool ShowNavButtons => selectedFile != null && (ShowFileNav || ShowHighlightNav);

    /// <summary>preview.js の scrollToNext/PrevHighlight。</summary>
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
        if (await TryScrollToHighlightAsync(next: true) != null)
            return;
        if (ShowFileNav)
            SelectNextFile();
    }

    /// <summary>GoNext の逆。</summary>
    private async Task GoPrev()
    {
        if (await TryScrollToHighlightAsync(next: false) != null)
            return;
        if (ShowFileNav)
            SelectPrevFile();
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

    /// <summary>プレビューヘッダーのパンくず用セグメント。</summary>
    private IReadOnlyList<(string FullPath, string DisplayName)> GetPreviewFolderPathSegments()
    {
        if (selectedFile == null || string.IsNullOrWhiteSpace(selectedFile.FolderPath))
            return Array.Empty<(string, string)>();
        return TreeBuilder.GetFolderPathSegments(selectedFile.FolderPath, SettingsService.Settings.TargetFolders);
    }

    /// <summary>プレビュー中のパスクリックでフォルダ一覧へ戻る。</summary>
    private async Task NavigateToFolderFromPreview(string folderPath)
    {
        if (isIndexing || string.IsNullOrWhiteSpace(folderPath))
            return;

        var normalized = IndexPaths.NormalizeFolderPath(folderPath).TrimEnd('\\', '/');
        var currentFile = selectedFile;
        var folderNode = await ResolveFolderNodeAsync(normalized);
        if (folderNode == null)
            return;

        Interlocked.Increment(ref _folderNavigationGeneration);
        folderNode.IsExpanded = true;
        TreeBuilder.ExpandPathToFolder(treeNodes, normalized);

        selectedFile = null;
        _previewCts?.Cancel();
        selectedFolder = folderNode;

        var list = GetSortedAndFilteredItems(folderNode.Children ?? new List<TreeNode>()).ToList();
        if (currentFile != null
            && string.Equals(
                normalized,
                IndexPaths.NormalizeFolderPath(currentFile.FolderPath).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
        {
            var idx = list.FindIndex(n => !n.IsFolder
                && string.Equals(n.FilePath, currentFile.FilePath, StringComparison.OrdinalIgnoreCase));
            selectedFolderRowIndex = idx >= 0 ? idx : 0;
        }
        else
        {
            selectedFolderRowIndex = 0;
        }

        ScheduleFolderContentPreviewsLoad(folderNode);
        StateHasChanged();
    }
}

