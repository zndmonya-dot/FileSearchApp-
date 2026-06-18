// アプリ設定のモデル。検索対象フォルダ・拡張子・インデックスパス・テーマ等を保持する。
using FullTextSearch.Core.Index;

namespace FullTextSearch.Core.Models;

/// <summary>
/// アプリケーション設定。JSON で永続化される。
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 検索対象フォルダのリスト
    /// </summary>
    public List<string> TargetFolders { get; set; } = [];

    /// <summary>
    /// 利用者がオフにした検索対象フォルダ（個人設定。共有設定は変更しない）。
    /// </summary>
    public List<string> DisabledTargetFolders { get; set; } = [];

    /// <summary>有効な検索対象フォルダ（<see cref="DisabledTargetFolders"/> を除く）。</summary>
    public IReadOnlyList<string> GetActiveTargetFolders() =>
        TargetFolderEnablement.GetActiveFolders(TargetFolders, DisabledTargetFolders);

    /// <summary>共有フォルダ一覧変更後に、存在しないパスを <see cref="DisabledTargetFolders"/> から除去する。</summary>
    public void PruneDisabledTargetFolders() =>
        TargetFolderEnablement.PruneDisabled(DisabledTargetFolders, TargetFolders);

    /// <summary>
    /// 対象拡張子（空の場合は抽出器が対応する全拡張子を動的に使用）
    /// </summary>
    public List<string> TargetExtensions { get; set; } = [];

    /// <summary>
    /// インデックス保存先フォルダ
    /// </summary>
    public string IndexPath { get; set; } = DefaultPaths.IndexPath;

    /// <summary>
    /// インデックス最終更新日時
    /// </summary>
    public DateTime? LastIndexUpdate { get; set; }

    /// <summary>
    /// 定期インデックス再構築の時刻（日本時間・0〜23時）。空なら無効。
    /// </summary>
    public List<int> AutoRebuildDailyHours { get; set; } = [];

    /// <summary>
    /// 旧設定（分間隔）。読み込み時に <see cref="AutoRebuildDailyHours"/> へ移行後は 0 にリセットされる。
    /// </summary>
    public int AutoRebuildIntervalMinutes { get; set; }

    /// <summary>
    /// テーマ: "Dark" / "Light" / "System"（システムに従う）
    /// </summary>
    public string ThemeMode { get; set; } = "System";
}


