// 検索結果一覧を「対象フォルダ → サブフォルダ → ファイル」のツリーに変換する。
using FullTextSearch.Core.Models;
using FileSearch.Blazor.Components.Shared;

namespace FileSearch.Blazor.Services;

/// <summary>
/// 検索結果からツリー構造を構築する静的ヘルパー。フォルダ別グルーピング・ソート・ファイル件数集計を行う。
/// </summary>
public static class TreeBuilder
{
    /// <summary>検索結果一覧と対象フォルダ一覧からツリーを構築する</summary>
    public static List<TreeNode> BuildTree(IReadOnlyList<string> targetFolders, IReadOnlyList<SearchResultItem> items)
    {
        if (items == null || items.Count == 0) return [];
        try
        {
            // 1 回の走査で「対象フォルダ → 該当アイテム一覧」にグループ化（フォルダ数×件数ループを避ける）
            var normalizedTargets = new List<(string original, string normalized)>(targetFolders.Count);
            foreach (var f in targetFolders)
                normalizedTargets.Add((f, f.TrimEnd('\\', '/').ToLowerInvariant()));
            var bucket = new List<SearchResultItem>[targetFolders.Count];
            for (var t = 0; t < targetFolders.Count; t++)
                bucket[t] = new List<SearchResultItem>();
            foreach (var item in items)
            {
                var folderLower = item.FolderPath.ToLowerInvariant();
                for (var t = 0; t < normalizedTargets.Count; t++)
                {
                    if (folderLower.StartsWith(normalizedTargets[t].normalized))
                    {
                        bucket[t].Add(item);
                        break;
                    }
                }
            }

            var result = new List<TreeNode>(targetFolders.Count);
            for (var t = 0; t < targetFolders.Count; t++)
            {
                var matchingItems = bucket[t];
                if (matchingItems.Count == 0) continue;
                var targetFolder = normalizedTargets[t].original;

                var rootNode = new TreeNode
                {
                    Name = Path.GetFileName(targetFolder) ?? targetFolder,
                    FullPath = targetFolder,
                    IsFolder = true,
                    IsExpanded = true,
                    Children = new List<TreeNode>()
                };
                foreach (var item in matchingItems)
                {
                    var relativePath = item.FolderPath.Length > targetFolder.Length
                        ? item.FolderPath.Substring(targetFolder.Length).TrimStart('\\', '/')
                        : "";
                    var parts = string.IsNullOrEmpty(relativePath)
                        ? Array.Empty<string>()
                        : relativePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    var current = rootNode;
                    foreach (var part in parts)
                    {
                        current.Children ??= new List<TreeNode>();
                        var child = current.Children.FirstOrDefault(c => c.IsFolder && c.Name == part);
                        if (child == null)
                        {
                            var childFullPath = Path.Combine(current.FullPath, part);
                            child = new TreeNode
                            {
                                Name = part,
                                FullPath = childFullPath,
                                IsFolder = true,
                                IsExpanded = false,
                                Children = new List<TreeNode>(),
                                Parent = current
                            };
                            current.Children.Add(child);
                        }
                        current = child;
                    }
                    current.Children ??= new List<TreeNode>();
                    current.Children.Add(new TreeNode
                    {
                        Name = item.FileName,
                        FilePath = item.FilePath,
                        IsFolder = false,
                        FileData = item,
                        LastModified = item.LastModified,
                        FileSize = item.FileSize,
                        Parent = current
                    });
                }
                SortTreeInPlace(rootNode);
                UpdateFileCount(rootNode);
                result.Add(rootNode);
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>指定フォルダへ至るパス上のフォルダをすべて展開する（右パネルで選択中のフォルダがツリーで見えるように連動）。1つでも展開したら true。</summary>
    public static bool ExpandPathToFolder(List<TreeNode> roots, string folderPath)
    {
        if (roots == null || string.IsNullOrEmpty(folderPath)) return false;
        var target = (folderPath ?? "").Replace('/', '\\').TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(target)) return false;
        foreach (var node in roots)
        {
            if (!node.IsFolder) continue;
            var nodePath = (node.FullPath ?? "").Replace('/', '\\').TrimEnd('\\', '/');
            if (string.IsNullOrEmpty(nodePath)) continue;
            if (!string.Equals(target, nodePath, StringComparison.OrdinalIgnoreCase) && !target.StartsWith(nodePath + "\\", StringComparison.OrdinalIgnoreCase))
                continue;
            var changed = !node.IsExpanded;
            node.IsExpanded = true;
            if (node.Children != null && !string.Equals(target, nodePath, StringComparison.OrdinalIgnoreCase))
                changed |= ExpandPathToFolder(node.Children, folderPath!);
            return changed;
        }
        return false;
    }

    /// <summary>指定ファイルへ至るフォルダをすべて展開する（プレビュー中ファイルが閉じたフォルダ内にあっても行が表示されるように）。1つでも展開したら true。</summary>
    public static bool ExpandPathToFile(List<TreeNode> roots, string filePath)
    {
        if (roots == null || string.IsNullOrEmpty(filePath)) return false;
        var fileDir = (Path.GetDirectoryName(filePath) ?? "").Replace('/', '\\').TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(fileDir)) return false;
        foreach (var node in roots)
        {
            if (!node.IsFolder) continue;
            var folderPath = (node.FullPath ?? "").Replace('/', '\\').TrimEnd('\\', '/');
            if (string.IsNullOrEmpty(folderPath)) continue;
            if (!string.Equals(fileDir, folderPath, StringComparison.OrdinalIgnoreCase) && !fileDir.StartsWith(folderPath + "\\", StringComparison.OrdinalIgnoreCase))
                continue;
            var changed = !node.IsExpanded;
            node.IsExpanded = true;
            if (node.Children != null)
                changed |= ExpandPathToFile(node.Children, filePath);
            return changed;
        }
        return false;
    }

    /// <summary>ツリー全体からファイルノードのみをフラットに収集する</summary>
    public static List<TreeNode> CollectAllFileNodes(List<TreeNode> roots)
    {
        var list = new List<TreeNode>();
        foreach (var node in roots)
            CollectFilesRec(node, list);
        return list;
    }

    /// <summary>フォルダを上・名前順にソートし、子ノードも再帰的にソートする。</summary>
    private static void SortTreeInPlace(TreeNode node)
    {
        if (node.Children == null || node.Children.Count == 0) return;
        node.Children.Sort((a, b) =>
        {
            if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var child in node.Children)
        {
            if (child.IsFolder) SortTreeInPlace(child);
        }
    }

    /// <summary>フォルダノードの FileCount を配下のファイル数で更新する。</summary>
    private static int UpdateFileCount(TreeNode node)
    {
        if (!node.IsFolder) return 0;
        var count = node.Children?.Count(c => !c.IsFolder) ?? 0;
        foreach (var child in node.Children?.Where(c => c.IsFolder) ?? Enumerable.Empty<TreeNode>())
            count += UpdateFileCount(child);
        node.FileCount = count;
        return count;
    }

    private static void CollectFilesRec(TreeNode node, List<TreeNode> acc)
    {
        if (!node.IsFolder && node.FileData != null)
            acc.Add(node);
        foreach (var c in node.Children ?? Enumerable.Empty<TreeNode>())
            CollectFilesRec(c, acc);
    }
}
