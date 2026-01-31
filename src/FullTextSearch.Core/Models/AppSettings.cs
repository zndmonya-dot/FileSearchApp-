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
    /// 対象拡張子
    /// </summary>
    public List<string> TargetExtensions { get; set; } =
    [
        ".txt", ".csv", ".log", ".md",
        ".docx", ".xlsx", ".pptx",
        ".pdf",
        ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h",
        ".html", ".css", ".xml", ".json", ".yaml", ".yml"
    ];

    /// <summary>
    /// インデックス保存先フォルダ
    /// </summary>
    public string IndexPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FullTextSearch", "Index");


    /// <summary>
    /// プレビュー切り替え遅延（ミリ秒）
    /// </summary>
    public int PreviewDelayMs { get; set; } = 100;

    /// <summary>
    /// インデックス最終更新日時
    /// </summary>
    public DateTime? LastIndexUpdate { get; set; }

    /// <summary>
    /// 検索結果の最大表示件数（ファイルサーバー向けに大きめのデフォルト）
    /// </summary>
    public int MaxResults { get; set; } = 10000;


    /// <summary>
    /// 定期インデックス再構築の間隔（分）。0 の場合は無効。
    /// </summary>
    public int AutoRebuildIntervalMinutes { get; set; } = 0;

    /// <summary>
    /// ファイル切り替え時の確認をスキップする（次回から確認しない）
    /// </summary>
    public bool SkipFileNavConfirm { get; set; } = false;

}


