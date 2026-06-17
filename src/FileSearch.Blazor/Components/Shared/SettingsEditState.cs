namespace FileSearch.Blazor.Components.Shared;

/// <summary>
/// 設定モーダル用の編集中状態。保存前に編集内容を保持する。
/// </summary>
public class SettingsEditState
{
    /// <summary>検索対象フォルダの一覧（編集中）</summary>
    public List<string> TargetFolders { get; set; } = new();
    /// <summary>フォルダ追加時のエラー・注意メッセージ</summary>
    public string? FolderMessage { get; set; }
    /// <summary>インデックス保存先パス</summary>
    public string IndexPath { get; set; } = "";
    /// <summary>インデックス保存先のエラー・注意メッセージ（W-07/W-08）</summary>
    public string? IndexPathMessage { get; set; }
    /// <summary>対象拡張子の一覧</summary>
    public List<string> TargetExtensions { get; set; } = new();
    /// <summary>定期再構築間隔（分）。0 で無効</summary>
    public int AutoRebuildIntervalMinutes { get; set; }
    /// <summary>インデックス対象の最大ファイルサイズ（MB）。0=無制限。</summary>
    public int IndexMaxFileMegabytes { get; set; } = 10;
    /// <summary>テーマ: "Dark" / "Light" / "System"</summary>
    public string ThemeMode { get; set; } = "System";
}
