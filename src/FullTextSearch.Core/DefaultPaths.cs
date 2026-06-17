// アプリのデフォルトパスを一元定義。インデックス保存先など。
namespace FullTextSearch.Core;

/// <summary>
/// アプリケーションのデフォルトパス（一元定義）。LocalApplicationData 配下を使用する。
/// </summary>
public static class DefaultPaths
{
    /// <summary>
    /// インデックス保存先のデフォルトフォルダ
    /// </summary>
    public static string IndexPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FullTextSearch", "Index");

    /// <summary>インデックスフォルダ内に出力するスキップ一覧ログのファイル名（LuceneIndexService / UI で共通）</summary>
    public const string SkippedFilesLogFileName = "skipped_files.log";

    /// <summary>インデックスフォルダ内の共有設定ファイル名（管理者が保存時に出力、利用者が起動時に読む）。</summary>
    public const string SharedConfigFileName = "shared.json";
}
