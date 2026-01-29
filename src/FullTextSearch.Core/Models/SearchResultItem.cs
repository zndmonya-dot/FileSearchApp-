namespace FullTextSearch.Core.Models;

/// <summary>
/// 検索結果の1件を表すモデル
/// </summary>
public class SearchResultItem
{
    /// <summary>
    /// ファイルのフルパス
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// ファイル名
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// フォルダパス
    /// </summary>
    public required string FolderPath { get; init; }

    /// <summary>
    /// ファイルサイズ（バイト）
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime LastModified { get; init; }

    /// <summary>
    /// ファイルの種類（拡張子から判定）
    /// </summary>
    public required string FileType { get; init; }

    /// <summary>
    /// 検索スコア
    /// </summary>
    public float Score { get; init; }

    /// <summary>
    /// マッチした箇所のリスト
    /// </summary>
    public List<MatchHighlight> Highlights { get; init; } = [];
}

/// <summary>
/// マッチ箇所のハイライト情報
/// </summary>
public class MatchHighlight
{
    /// <summary>
    /// マッチ箇所を含むテキスト（前後の文脈含む）
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// ハイライト開始位置
    /// </summary>
    public int HighlightStart { get; init; }

    /// <summary>
    /// ハイライト終了位置
    /// </summary>
    public int HighlightEnd { get; init; }
}


