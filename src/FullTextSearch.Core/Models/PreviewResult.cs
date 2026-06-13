// プレビュー API の戻り値。全文・行境界・マッチ行番号を返す（描画は JS が担当）。

namespace FullTextSearch.Core.Models;

/// <summary>
/// プレビュー結果。全文と行境界を保持し、UI は JS（preview.js）で WinMerge 風に描画する。
/// </summary>
public class PreviewResult
{
    /// <summary>正規化済み全文（エラー時は 1 行メッセージ）。</summary>
    public string Content { get; init; } = "";

    /// <summary>各行の先頭インデックス。行数 = 配列長。</summary>
    public int[] LineStartOffsets { get; init; } = [0];

    /// <summary>行数（<see cref="LineStartOffsets"/> の長さと同値）。</summary>
    public int LineCount => LineStartOffsets.Length;

    /// <summary>検索語にマッチした行番号（1-based）。</summary>
    public IReadOnlyList<int> MatchLineNumbers { get; init; } = Array.Empty<int>();

    /// <summary>エラー・キャンセル等の単行メッセージ表示。</summary>
    public bool IsError { get; init; }

    /// <summary>ハイライト用検索語（NFC 正規化済み）。</summary>
    public string[] SearchTerms { get; init; } = [];
}
