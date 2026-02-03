namespace FileSearch.Blazor.Components.Shared;

/// <summary>
/// 設定モーダル用の編集中状態。保存前に編集内容を保持する。
/// </summary>
public class SettingsEditState
{
    /// <summary>検索対象フォルダの一覧（編集中）</summary>
    public List<string> TargetFolders { get; set; } = new();
    /// <summary>追加入力欄のフォルダパス</summary>
    public string NewFolderPath { get; set; } = "";
    /// <summary>フォルダ追加時のエラー・注意メッセージ</summary>
    public string? FolderMessage { get; set; }
    /// <summary>インデックス保存先パス</summary>
    public string IndexPath { get; set; } = "";
    /// <summary>対象拡張子の一覧</summary>
    public List<string> TargetExtensions { get; set; } = new();
    /// <summary>追加入力欄の拡張子</summary>
    public string NewTargetExtension { get; set; } = "";
    /// <summary>拡張子追加時のメッセージ</summary>
    public string? ExtensionMessage { get; set; }
    /// <summary>定期再構築間隔（分）。0 で無効</summary>
    public int AutoRebuildIntervalMinutes { get; set; }
    /// <summary>テーマ: "Dark" / "Light" / "System"</summary>
    public string ThemeMode { get; set; } = "System";

    /// <summary>シンタックスハイライト用 拡張子→言語名（上書き・追加用）。null のときは未使用。</summary>
    public Dictionary<string, string>? ExtensionLanguageMap { get; set; }

    /// <summary>拡張子→言語の追加入力：拡張子</summary>
    public string NewExtensionLanguageExt { get; set; } = "";

    /// <summary>拡張子→言語の追加入力：言語名（Highlight.js 用）</summary>
    public string NewExtensionLanguageLang { get; set; } = "";

    /// <summary>拡張子→言語追加時のメッセージ</summary>
    public string? ExtensionLanguageMessage { get; set; }
}
