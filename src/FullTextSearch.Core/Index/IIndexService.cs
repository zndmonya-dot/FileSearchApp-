// インデックス操作のインターフェースと進捗・オプション・統計の型定義。
using FullTextSearch.Core.Models;

namespace FullTextSearch.Core.Index;

/// <summary>
/// インデックスサービスのインターフェース。初期化・再構築・差分更新・一覧取得を提供する。
/// </summary>
public interface IIndexService
{
    /// <summary>
    /// インデックスの初期化。<paramref name="readOnly"/> が true のときは参照専用（IndexWriter を開かない）。
    /// </summary>
    Task InitializeAsync(string indexPath, bool readOnly = false, CancellationToken cancellationToken = default);

    /// <summary>直近の <see cref="InitializeAsync"/> が失敗したか（パス未到達・破損・ロック等）。</summary>
    bool LastInitializeFailed { get; }

    /// <summary>
    /// インデックスを再構築（全削除のうえ全件追加）
    /// </summary>
    Task RebuildIndexAsync(IEnumerable<string> folders, IProgress<IndexProgress>? progress = null, IndexRebuildOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 差分のみ更新（追加・更新・削除されたファイルだけ反映）。大量のファイルで高速。
    /// </summary>
    Task UpdateIndexAsync(IEnumerable<string> folders, IProgress<IndexProgress>? progress = null, IndexRebuildOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>直近のインデックス操作でスキップされたファイル（パスと理由）。</summary>
    IReadOnlyList<SkippedFileEntry> LastSkippedFiles { get; }

    /// <summary>
    /// 対象フォルダ・拡張子に含まれるインデックス済みファイル一覧（閲覧ツリー・登録件数表示用）。
    /// </summary>
    IReadOnlyList<SearchResultItem> ListIndexedItems(
        IReadOnlyList<string> targetFolders,
        IReadOnlySet<string>? targetExtensions = null);
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
    /// エラーが発生したファイル数
    /// </summary>
    public int ErrorCount { get; init; }

    /// <summary>差分更新で追加・更新・削除対象が 0 件だった場合に true。</summary>
    public bool NoChanges { get; init; }
}

/// <summary>
/// インデックス再構築時のオプション（対象拡張子）
/// </summary>
public class IndexRebuildOptions
{
    /// <summary>対象拡張子（例: .txt, .docx）。未指定時は抽出器の対応拡張子を使用</summary>
    public IReadOnlyList<string>? TargetExtensions { get; init; }
}

