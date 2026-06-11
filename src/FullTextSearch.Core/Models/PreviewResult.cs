// プレビュー API の戻り値。ハイライト済みの行リストと行数を返す。
namespace FullTextSearch.Core.Models;

/// <summary>
/// プレビュー結果。ハイライト済みの行リストと行数を返す。
/// </summary>
public class PreviewResult
{
    /// <summary>行リスト</summary>
    public IReadOnlyList<PreviewLineResult> Lines { get; init; } = Array.Empty<PreviewLineResult>();

    /// <summary>行数</summary>
    public int LineCount { get; init; }
}

/// <summary>
/// プレビュー1行（HTML 済み Content とハイライト有無）
/// </summary>
public record PreviewLineResult(string Content, bool HasMatch);
