namespace FileSearch.Blazor.Components.Shared;

/// <summary>
/// 設定モーダル用の編集中状態
/// </summary>
public class SettingsEditState
{
    public List<string> TargetFolders { get; set; } = new();
    public string NewFolderPath { get; set; } = "";
    /// <summary>フォルダ追加時のエラー・注意メッセージ</summary>
    public string? FolderMessage { get; set; }
    public string IndexPath { get; set; } = "";
    public List<string> TargetExtensions { get; set; } = new();
    public string NewTargetExtension { get; set; } = "";
    public string? ExtensionMessage { get; set; }
    public int AutoRebuildIntervalMinutes { get; set; }
    /// <summary>テーマ: "Dark" / "Light" / "System"</summary>
    public string ThemeMode { get; set; } = "System";
}
