// =============================================================================
// Home.Resize.cs — partial class Home
// =============================================================================
// 役割: サイドバー幅のドラッグリサイズ、プレビュー行の MarkupString 表示キャッシュ（previewLinesDisplay）。
// =============================================================================
using FullTextSearch.Core.Models;
using FileSearch.Blazor.Components.Shared;
using Microsoft.AspNetCore.Components.Web;

namespace FileSearch.Blazor.Components.Pages;

public partial class Home
{
    /// <summary>サイドバー右端ドラッグ開始。</summary>
    private void StartResize(MouseEventArgs e) { isResizing = true; resizeStartX = e.ClientX; resizeStartWidth = sidebarWidth; }

    /// <summary>幅を 240〜600px にクランプ。</summary>
    private void OnResize(MouseEventArgs e)
    {
        if (!isResizing) return;
        var delta = e.ClientX - resizeStartX;
        sidebarWidth = Math.Max(240, Math.Min(600, resizeStartWidth + (int)delta));
        StateHasChanged();
    }

    /// <summary>ドラッグ終了（オーバーレイの mouseup / leave）。</summary>
    private void StopResize(MouseEventArgs _) => isResizing = false;

    /// <summary>ツリー/プレビューで使う拡張子→アイコン CSS クラス。</summary>
    private static string GetFileIconClass(string name) => DisplayFormatters.GetFileIconClass(name);

    /// <summary>Razor 向け。PreviewLineResult を行ごとにラップし、件数変化時だけキャッシュ再構築。</summary>
    private IReadOnlyList<PreviewLineDisplay> previewLinesDisplay
    {
        get
        {
            var src = _previewLines ?? Array.Empty<PreviewLineResult>();
            if (_previewLinesDisplayCache != null && _previewLinesDisplayCache.Count == src.Count)
                return _previewLinesDisplayCache;
            _previewLinesDisplayCache = src.Select(p => new PreviewLineDisplay(p.Content, p.HasMatch)).ToList();
            return _previewLinesDisplayCache;
        }
    }
}
