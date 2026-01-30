using FullTextSearch.Core.Models;

namespace FileSearch.Blazor.Components.Shared;

/// <summary>
/// 検索結果ツリーのノード
/// </summary>
public class TreeNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string? FilePath { get; set; }
    public bool IsFolder { get; set; }
    public bool IsExpanded { get; set; } = true;
    public List<TreeNode>? Children { get; set; }
    public int FileCount { get; set; }
    public SearchResultItem? FileData { get; set; }
    public DateTime? LastModified { get; set; }
    public long FileSize { get; set; }
}
