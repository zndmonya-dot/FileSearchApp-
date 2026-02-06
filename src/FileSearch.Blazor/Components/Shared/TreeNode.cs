using FullTextSearch.Core.Models;

namespace FileSearch.Blazor.Components.Shared;

/// <summary>
/// 検索結果ツリーの 1 ノード。フォルダまたはファイルを表し、子ノード・ファイル件数・メタ情報を持つ。
/// </summary>
public class TreeNode
{
    /// <summary>表示名（フォルダ名またはファイル名）</summary>
    public string Name { get; set; } = "";
    /// <summary>親フォルダノード（ルートの場合は null）</summary>
    public TreeNode? Parent { get; set; }
    /// <summary>フォルダのフルパス（フォルダノード時）</summary>
    public string FullPath { get; set; } = "";
    /// <summary>ファイルのフルパス（ファイルノード時）</summary>
    public string? FilePath { get; set; }
    /// <summary>フォルダノードかどうか</summary>
    public bool IsFolder { get; set; }
    /// <summary>子を展開表示しているか</summary>
    public bool IsExpanded { get; set; } = true;
    /// <summary>子ノード（フォルダまたはファイル）</summary>
    public List<TreeNode>? Children { get; set; }
    /// <summary>配下のファイル数（フォルダノード時）</summary>
    public int FileCount { get; set; }
    /// <summary>検索結果 1 件（ファイルノード時。プレビュー用）</summary>
    public SearchResultItem? FileData { get; set; }
    /// <summary>最終更新日時（表示・ソート用）</summary>
    public DateTime? LastModified { get; set; }
    /// <summary>ファイルサイズ（バイト）。フォルダ時は 0</summary>
    public long FileSize { get; set; }
}
