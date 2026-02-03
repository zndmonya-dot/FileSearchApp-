// プレビュー API の戻り値。行テキスト（Mode=text）または HTML を返す。
namespace FullTextSearch.Core.Models;

/// <summary>
/// プレビュー結果。Mode（text / html）、行リスト、行数、HTML 本文を返す。
/// </summary>
public class PreviewResult
{
    /// <summary>text=行テキスト, html=HTML</summary>
    public required string Mode { get; init; }

    /// <summary>行リスト（Mode=text 時）</summary>
    public IReadOnlyList<PreviewLineResult> Lines { get; init; } = Array.Empty<PreviewLineResult>();

    /// <summary>行数</summary>
    public int LineCount { get; init; }

    /// <summary>HTML 本文（Mode=html 時）</summary>
    public string? Html { get; init; }
}

/// <summary>
/// プレビュー1行（HTML 済み Content とハイライト有無）
/// </summary>
public record PreviewLineResult(string Content, bool HasMatch);
