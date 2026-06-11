// 検索結果 1 件。ファイル情報とマッチ箇所のハイライト情報を持つ。
namespace FullTextSearch.Core.Models;

/// <summary>
/// 検索結果の 1 件を表すモデル。一覧表示・プレビュー選択に使用する。
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
}


