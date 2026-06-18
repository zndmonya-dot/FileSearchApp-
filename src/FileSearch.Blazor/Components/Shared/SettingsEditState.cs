namespace FileSearch.Blazor.Components.Shared;

/// <summary>
/// 設定モーダル用の編集中状態。保存前に編集内容を保持する。
/// </summary>
public class SettingsEditState
{
    /// <summary>検索対象フォルダの一覧（編集中）</summary>
    public List<string> TargetFolders { get; set; } = new();
    /// <summary>オフにした検索対象フォルダ（利用者の個人設定・編集中）</summary>
    public List<string> DisabledTargetFolders { get; set; } = new();
    /// <summary>フォルダ追加時のエラー・注意メッセージ</summary>
    public string? FolderMessage { get; set; }
    /// <summary>インデックス保存先パス</summary>
    public string IndexPath { get; set; } = "";
    /// <summary>インデックス保存先のエラー・注意メッセージ（W-07/W-08）</summary>
    public string? IndexPathMessage { get; set; }
    /// <summary>対象拡張子の一覧</summary>
    public List<string> TargetExtensions { get; set; } = new();
    /// <summary>定期再構築の時刻（日本標準時 0〜23時）。空なら無効。</summary>
    public List<int> AutoRebuildDailyHours { get; set; } = new();
    /// <summary>テーマ: "Dark" / "Light" / "System"</summary>
    public string ThemeMode { get; set; } = "System";
}
