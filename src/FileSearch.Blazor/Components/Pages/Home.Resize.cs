// =============================================================================
// Home.Resize.cs — partial class Home
// =============================================================================
// 役割: サイドバー幅のドラッグリサイズ。
// =============================================================================
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
}
