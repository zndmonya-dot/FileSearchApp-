using FullTextSearch.Core.Models;

namespace FullTextSearch.Core.Index;

/// <summary>
/// インデックスサービスのインターフェース
/// </summary>
public interface IIndexService
{
    /// <summary>
    /// インデックスの初期化
    /// </summary>
    Task InitializeAsync(string indexPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// ドキュメントをインデックスに追加/更新
    /// </summary>
    Task IndexDocumentAsync(IndexedDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// ドキュメントをインデックスから削除
    /// </summary>
    Task DeleteDocumentAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// フォルダ全体をインデックス。
    /// 再構築時は progressOffset / progressTotalOverride で進捗を全フォルダ合計で表示する。
    /// </summary>
    Task IndexFolderAsync(string folderPath, IProgress<IndexProgress>? progress = null, CancellationToken cancellationToken = default, int progressOffset = 0, int? progressTotalOverride = null);

    /// <summary>
    /// インデックスを再構築（全削除のうえ全件追加）
    /// </summary>
    Task RebuildIndexAsync(IEnumerable<string> folders, IProgress<IndexProgress>? progress = null, IndexRebuildOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 差分のみ更新（追加・更新・削除されたファイルだけ反映）。大量のファイルで高速。
    /// </summary>
    Task UpdateIndexAsync(IEnumerable<string> folders, IProgress<IndexProgress>? progress = null, IndexRebuildOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// インデックスの統計情報を取得
    /// </summary>
    IndexStats GetStats();

    /// <summary>
    /// インデックスを最適化
    /// </summary>
    Task OptimizeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// インデックス作成の進捗情報
/// </summary>
public class IndexProgress
{
    /// <summary>
    /// 処理済みファイル数
    /// </summary>
    public int ProcessedFiles { get; init; }

    /// <summary>
    /// 総ファイル数
    /// </summary>
    public int TotalFiles { get; init; }

    /// <summary>
    /// 現在処理中のファイル
    /// </summary>
    public string? CurrentFile { get; init; }

    /// <summary>
    /// 進捗率（0-100）
    /// </summary>
    public double ProgressPercent => TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100 : 0;

    /// <summary>
    /// エラーが発生したファイル数
    /// </summary>
    public int ErrorCount { get; init; }
}

/// <summary>
/// インデックス再構築時のオプション（対象拡張子）
/// </summary>
public class IndexRebuildOptions
{
    /// <summary>対象拡張子（例: .txt, .docx）。未指定時は抽出器の対応拡張子を使用</summary>
    public IReadOnlyList<string>? TargetExtensions { get; init; }
}

/// <summary>
/// インデックスの統計情報
/// </summary>
public class IndexStats
{
    /// <summary>
    /// インデックス済みドキュメント数
    /// </summary>
    public int DocumentCount { get; init; }

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime? LastUpdated { get; init; }

    /// <summary>
    /// インデックスサイズ（バイト）
    /// </summary>
    public long IndexSizeBytes { get; init; }
}


