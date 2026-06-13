using FileSearch.Messages;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace FileSearch.Blazor.Services;

/// <summary>プレビュー DOM 描画とハイライトナビの JS 連携。本文はチャンク転送で WebView 回路クラッシュを防ぐ。</summary>
public sealed class PreviewJsInterop(IJSRuntime js, ILogger<PreviewJsInterop>? logger = null)
{
    private const int ChunkChars = 64 * 1024;

    /// <summary>プレビュー本文を JS 側で描画する（チャンク転送）。描画完了時のハイライト位置文字列を返す。</summary>
    public async Task<string?> RenderAsync(PreviewResult result, CancellationToken cancellationToken = default)
    {
        string? tooManyLinesMessage = null;
        if (!result.IsError && result.LineCount > PreviewLineBuilder.PreviewMaxRenderLines)
        {
            tooManyLinesMessage = UserMessages.PreviewTooManyLinesLine(
                result.LineCount, PreviewLineBuilder.PreviewMaxRenderLines);
        }

        await js.InvokeVoidAsync("previewClear").ConfigureAwait(false);

        var meta = new
        {
            isError = result.IsError,
            searchTerms = result.SearchTerms,
            matchLineNumbers = result.MatchLineNumbers.ToArray(),
            maxLines = PreviewLineBuilder.PreviewMaxRenderLines,
            tooManyLinesMessage,
            scrollToFirstMatch = result.MatchLineNumbers.Count > 0
        };
        await js.InvokeVoidAsync("previewBegin", meta).ConfigureAwait(false);

        var content = result.Content;
        for (var offset = 0; offset < content.Length; offset += ChunkChars)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(ChunkChars, content.Length - offset);
            await js.InvokeVoidAsync("previewAppend", content.Substring(offset, length)).ConfigureAwait(false);
        }

        return await js.InvokeAsync<string?>("previewFinish").ConfigureAwait(false);
    }

    /// <summary>ハイライト行ナビを初期化する。</summary>
    public Task InitHighlightNavAsync(IReadOnlyList<int> lineNumbers) =>
        js.InvokeVoidAsync("initHighlightNav", lineNumbers.ToArray()).AsTask();

    /// <summary>最初のハイライト行へ即スクロール。戻り値は「行|現在|総数」。</summary>
    public Task<string?> ScrollToFirstHighlightInstantAsync() =>
        js.InvokeAsync<string?>("scrollToFirstHighlightInstant").AsTask();

    /// <summary>次/前のハイライト行へスクロール。</summary>
    public Task<string?> ScrollToHighlightAsync(bool next, bool wrap) =>
        js.InvokeAsync<string?>(next ? "scrollToNextHighlight" : "scrollToPrevHighlight", wrap).AsTask();

    /// <summary>ハイライトナビ状態をリセットする。</summary>
    public Task ResetHighlightNavAsync() =>
        js.InvokeVoidAsync("resetHighlightNav").AsTask();

    /// <summary>JS 呼び出し失敗をログに残す。</summary>
    public void LogInteropFailure(string operation, Exception ex) =>
        logger?.LogWarning(ex, "Preview JS interop failed: {Operation}", operation);
}
