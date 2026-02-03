// インデックス 1 件分のデータ。ファイルパス・抽出テキスト・メタ情報を保持する。
namespace FullTextSearch.Core.Models;

/// <summary>
/// インデックスに登録されるドキュメント。Lucene に書き込む際の元データ。
/// </summary>
public class IndexedDocument
{
    /// <summary>
    /// ファイルのフルパス（一意識別子）
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
    /// 抽出されたテキスト内容
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// ファイルサイズ（バイト）
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime LastModified { get; init; }

    /// <summary>
    /// ファイルの種類
    /// </summary>
    public required string FileType { get; init; }

    /// <summary>
    /// インデックス登録日時
    /// </summary>
    public DateTime IndexedAt { get; init; } = DateTime.UtcNow;
}


