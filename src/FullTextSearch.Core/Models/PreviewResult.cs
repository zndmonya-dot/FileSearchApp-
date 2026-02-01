namespace FullTextSearch.Core.Models;

/// <summary>
/// プレビュー結果（モード・行リスト・HTML・DataURL）
/// </summary>
public class PreviewResult
{
    /// <summary>text=行テキスト, html=Excel等HTML, image=画像DataURL</summary>
    public required string Mode { get; init; }

    /// <summary>行リスト（Mode=text 時）</summary>
    public IReadOnlyList<PreviewLineResult> Lines { get; init; } = Array.Empty<PreviewLineResult>();

    /// <summary>行数</summary>
    public int LineCount { get; init; }

    /// <summary>HTML 本文（Mode=html 時、Excel 等）</summary>
    public string? Html { get; init; }

    /// <summary>画像 Data URL（Mode=image 時）</summary>
    public string? ImageDataUrl { get; init; }
}

/// <summary>
/// プレビュー1行（HTML 済み Content とハイライト有無）
/// </summary>
public record PreviewLineResult(string Content, bool HasMatch);
