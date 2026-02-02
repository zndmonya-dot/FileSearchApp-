using FullTextSearch.Core;

namespace FullTextSearch.Core.Models;

/// <summary>
/// アプリケーション設定
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 検索対象フォルダのリスト
    /// </summary>
    public List<string> TargetFolders { get; set; } = [];

    /// <summary>
    /// 対象拡張子（空の場合は抽出器が対応する全拡張子を動的に使用）
    /// </summary>
    public List<string> TargetExtensions { get; set; } = [];

    /// <summary>
    /// インデックス保存先フォルダ
    /// </summary>
    public string IndexPath { get; set; } = DefaultPaths.IndexPath;


    /// <summary>
    /// プレビュー切り替え遅延（ミリ秒）
    /// </summary>
    public int PreviewDelayMs { get; set; } = 100;

    /// <summary>
    /// インデックス最終更新日時
    /// </summary>
    public DateTime? LastIndexUpdate { get; set; }

    /// <summary>
    /// 定期インデックス再構築の間隔（分）。0 の場合は無効。
    /// </summary>
    public int AutoRebuildIntervalMinutes { get; set; } = 0;

    /// <summary>
    /// テーマ: "Dark" / "Light" / "System"（システムに従う）
    /// </summary>
    public string ThemeMode { get; set; } = "System";

}


