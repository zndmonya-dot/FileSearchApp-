// 検索結果一覧を「対象フォルダ → サブフォルダ → ファイル」のツリーに変換する。
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;

namespace FullTextSearch.Core.UI;

/// <summary>
/// 検索結果からツリー構造を構築する静的ヘルパー。フォルダ別グルーピング・ソート・ファイル件数集計を行う。
/// </summary>
public static class TreeBuilder
{
    /// <summary>ツリー表示用のフォルダ名（ドライブルート・UNC・末尾スラッシュに対応）。</summary>
    public static string GetFolderDisplayName(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return "";

        var normalized = IndexPaths.NormalizeFolderPath(folderPath);
        var trimmed = normalized.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        if (!string.IsNullOrEmpty(name))
            return name;

        if (trimmed.Length >= 2 && trimmed[1] == ':')
            return trimmed[..1];

        return normalized;
    }

    /// <summary>対象フォルダのルートのみをツリー化する（検索前の初期表示用・即時）。</summary>
    public static List<TreeNode> BuildFolderSkeleton(IReadOnlyList<string> targetFolders)
    {
        if (targetFolders == null || targetFolders.Count == 0)
            return [];

        var result = new List<TreeNode>(targetFolders.Count);
        foreach (var folder in targetFolders)
        {
            var rootPath = IndexPaths.NormalizeFolderPath(folder);
            if (!Directory.Exists(rootPath))
                continue;

            result.Add(new TreeNode
            {
                Name = GetFolderDisplayName(folder),
                FullPath = rootPath,
                IsFolder = true,
                IsExpanded = false,
                Children = new List<TreeNode>(),
                FolderChildrenLoaded = false
            });
        }

        return result;
    }

    /// <summary>対象フォルダ配下を再帰的に走査し、検索前表示用の完全なフォルダツリーを構築する。</summary>
    public static List<TreeNode> BuildFullFolderTree(IReadOnlyList<string> targetFolders, IReadOnlySet<string>? supportedExtensions = null)
    {
        var roots = BuildFolderSkeleton(targetFolders);
        foreach (var root in roots)
            LoadFolderTreeRecursive(root, supportedExtensions);
        foreach (var root in roots)
        {
            UpdateFileCount(root);
            PruneEmptyFolderBranches(root);
        }
        roots.RemoveAll(r => r.FileCount == 0);
        return roots;
    }

    /// <summary>配下に該当ファイルが 1 件もないフォルダノードを除去する。</summary>
    private static void PruneEmptyFolderBranches(TreeNode node)
    {
        if (!node.IsFolder || node.Children == null)
            return;

        foreach (var folderChild in node.Children.Where(c => c.IsFolder).ToList())
            PruneEmptyFolderBranches(folderChild);

        node.Children.RemoveAll(c => c.IsFolder && c.FileCount == 0);
        UpdateFileCount(node);
    }

    /// <summary>フォルダノード配下を再帰的に読み込む（<see cref="BuildFullFolderTree"/> 用）。</summary>
    private static void LoadFolderTreeRecursive(TreeNode parent, IReadOnlySet<string>? supportedExtensions)
    {
        LoadDirectFolderChildren(parent, supportedExtensions);
        foreach (var child in parent.Children?.Where(c => c.IsFolder) ?? Enumerable.Empty<TreeNode>())
            LoadFolderTreeRecursive(child, supportedExtensions);
    }

    /// <summary>フォルダ直下のサブフォルダとファイルを 1 階層だけ読み込む（展開時の遅延読み込み）。</summary>
    public static void LoadDirectFolderChildren(TreeNode parent, IReadOnlySet<string>? supportedExtensions = null)
    {
        if (!parent.IsFolder || parent.FolderChildrenLoaded)
            return;

        var children = new List<TreeNode>();

        try
        {
            foreach (var subdir in Directory.EnumerateDirectories(parent.FullPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var dirName = Path.GetFileName(subdir);
                if (string.IsNullOrEmpty(dirName) || ShouldSkipDirectory(dirName))
                    continue;

                children.Add(new TreeNode
                {
                    Name = dirName,
                    FullPath = subdir,
                    IsFolder = true,
                    IsExpanded = false,
                    Children = new List<TreeNode>(),
                    Parent = parent,
                    FolderChildrenLoaded = false
                });
            }

            foreach (var file in Directory.EnumerateFiles(parent.FullPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith("~$", StringComparison.Ordinal))
                    continue;

                var ext = PreviewHelper.NormalizeExtension(Path.GetExtension(file));
                if (supportedExtensions != null && (string.IsNullOrEmpty(ext) || !supportedExtensions.Contains(ext)))
                    continue;

                var item = CreateSearchResultItem(file);
                children.Add(new TreeNode
                {
                    Name = item.FileName,
                    FilePath = item.FilePath,
                    IsFolder = false,
                    FileData = item,
                    LastModified = item.LastModified,
                    Parent = parent
                });
            }
        }
        catch
        {
            parent.Children = children;
            parent.FolderChildrenLoaded = true;
            return;
        }

        children.Sort((a, b) =>
        {
            if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        parent.Children = children;
        parent.FolderChildrenLoaded = true;
    }

    /// <summary>ディスク上のファイルから検索結果 1 件分のメタデータを生成する。</summary>
    public static SearchResultItem CreateSearchResultItem(string filePath)
    {
        var info = new FileInfo(filePath);
        return new SearchResultItem
        {
            FilePath = info.FullName,
            FileName = info.Name,
            FolderPath = info.DirectoryName ?? "",
            FileSize = info.Length,
            LastModified = info.LastWriteTimeUtc
        };
    }

    /// <summary>検索結果一覧と対象フォルダ一覧からツリーを構築する</summary>
    public static List<TreeNode> BuildTree(IReadOnlyList<string> targetFolders, IReadOnlyList<SearchResultItem> items)
    {
        if (items == null || items.Count == 0) return [];
        try
        {
            var folders = targetFolders;
            if (folders == null || folders.Count == 0)
                folders = DeriveTargetFoldersFromItems(items);

            // 1 回の走査で「対象フォルダ → 該当アイテム一覧」にグループ化（フォルダ数×件数ループを避ける）
            var normalizedTargets = new List<(string original, string normalized)>(folders.Count);
            foreach (var f in folders)
                normalizedTargets.Add((f, f.TrimEnd('\\', '/').ToLowerInvariant()));
            var bucket = new List<SearchResultItem>[folders.Count];
            for (var t = 0; t < folders.Count; t++)
                bucket[t] = new List<SearchResultItem>();
            foreach (var item in items)
            {
                var folderLower = item.FolderPath.ToLowerInvariant();
                for (var t = 0; t < normalizedTargets.Count; t++)
                {
                    if (IsUnderOrEqual(normalizedTargets[t].normalized, folderLower))
                    {
                        bucket[t].Add(item);
                        break;
                    }
                }
            }

            var result = new List<TreeNode>(folders.Count);
            for (var t = 0; t < folders.Count; t++)
            {
                var matchingItems = bucket[t];
                if (matchingItems.Count == 0) continue;
                var targetFolder = normalizedTargets[t].original;

                var rootNode = new TreeNode
                {
                    Name = GetFolderDisplayName(targetFolder),
                    FullPath = IndexPaths.NormalizeFolderPath(targetFolder),
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

    /// <summary>パンくず表示用にフォルダパスをルートからのセグメント列に分解する。anchorFolders があれば最も近い対象ルートから表示する。</summary>
    public static IReadOnlyList<(string FullPath, string DisplayName)> GetFolderPathSegments(
        string folderPath,
        IReadOnlyList<string>? anchorFolders = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return Array.Empty<(string, string)>();

        try
        {
            var stack = new Stack<(string FullPath, string DisplayName)>();
            for (var dir = new DirectoryInfo(IndexPaths.NormalizeFolderPath(folderPath)); dir != null; dir = dir.Parent)
            {
                var full = IndexPaths.NormalizeFolderPath(dir.FullName).TrimEnd('\\', '/');
                stack.Push((full, GetFolderDisplayName(dir.FullName)));
            }

            var segments = stack.ToList();
            if (anchorFolders is { Count: > 0 })
            {
                var folderNorm = IndexPaths.NormalizeFolderPath(folderPath).TrimEnd('\\', '/');
                var anchor = anchorFolders
                    .Select(f => IndexPaths.NormalizeFolderPath(f).TrimEnd('\\', '/'))
                    .Where(a => folderNorm.Equals(a, StringComparison.OrdinalIgnoreCase)
                        || folderNorm.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(a => a.Length)
                    .FirstOrDefault();
                if (anchor != null)
                {
                    var idx = segments.FindIndex(s => s.FullPath.Equals(anchor, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                        segments = segments.Skip(idx).ToList();
                }
            }

            return segments;
        }
        catch
        {
            return Array.Empty<(string, string)>();
        }
    }

    /// <summary>ツリーから指定フォルダパスに一致するフォルダノードを探す。</summary>
    public static TreeNode? FindFolderNode(IReadOnlyList<TreeNode> roots, string folderPath)
    {
        if (roots == null || string.IsNullOrWhiteSpace(folderPath))
            return null;

        var target = IndexPaths.NormalizeFolderPath(folderPath).TrimEnd('\\', '/');
        foreach (var root in roots)
        {
            var found = FindFolderNodeRec(root, target);
            if (found != null)
                return found;
        }

        return null;
    }

    private static TreeNode? FindFolderNodeRec(TreeNode node, string target)
    {
        if (!node.IsFolder)
            return null;

        var nodePath = IndexPaths.NormalizeFolderPath(node.FullPath).TrimEnd('\\', '/');
        if (string.Equals(nodePath, target, StringComparison.OrdinalIgnoreCase))
            return node;

        foreach (var child in node.Children ?? Enumerable.Empty<TreeNode>())
        {
            if (!child.IsFolder)
                continue;

            var childPath = IndexPaths.NormalizeFolderPath(child.FullPath).TrimEnd('\\', '/');
            if (target.Equals(childPath, StringComparison.OrdinalIgnoreCase)
                || target.StartsWith(childPath + "\\", StringComparison.OrdinalIgnoreCase))
            {
                var found = FindFolderNodeRec(child, target);
                if (found != null)
                    return found;
            }
        }

        return null;
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

    /// <summary>指定フォルダ配下のファイルノードを再帰的に収集する。</summary>
    public static List<TreeNode> CollectFileNodesUnderFolder(TreeNode folder)
    {
        var list = new List<TreeNode>();
        if (!folder.IsFolder)
            return list;

        foreach (var child in folder.Children ?? Enumerable.Empty<TreeNode>())
            CollectFilesRec(child, list);
        return list;
    }

    /// <summary>ツリー構築済みフォルダに遅延読み込みフラグを立て、ディスク走査での上書きを防ぐ。</summary>
    public static void MarkFolderTreeLoaded(IEnumerable<TreeNode> roots)
    {
        foreach (var root in roots)
            MarkFolderTreeLoadedRec(root);
    }

    private static void MarkFolderTreeLoadedRec(TreeNode node)
    {
        if (!node.IsFolder)
            return;

        node.FolderChildrenLoaded = true;
        foreach (var child in node.Children ?? Enumerable.Empty<TreeNode>())
        {
            if (child.IsFolder)
                MarkFolderTreeLoadedRec(child);
        }
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

    /// <summary>対象フォルダ未設定時、検索結果のフォルダパスから最小のルート集合を推定する。</summary>
    internal static List<string> DeriveTargetFoldersFromItems(IReadOnlyList<SearchResultItem> items)
    {
        var paths = items
            .Select(i => i.FolderPath.TrimEnd('\\', '/'))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Length)
            .ToList();
        var roots = new List<string>();
        foreach (var path in paths)
        {
            if (roots.Any(r => IsUnderOrEqual(r, path)))
                continue;
            roots.RemoveAll(r => IsUnderOrEqual(path, r));
            roots.Add(path);
        }
        return roots;
    }

    private static bool IsUnderOrEqual(string root, string path)
    {
        var r = root.TrimEnd('\\', '/');
        var p = path.TrimEnd('\\', '/');
        if (p.Length < r.Length) return false;
        if (!p.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return false;
        return p.Length == r.Length || p[r.Length] is '\\' or '/';
    }

    private static bool ShouldSkipDirectory(string dirName) =>
        dirName.StartsWith('$') ||
        dirName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("ProgramData", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals("__pycache__", StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals(".venv", StringComparison.OrdinalIgnoreCase);

    /// <summary>ツリーを再帰走査し、ファイルノードのみ <paramref name="acc"/> に追加する。</summary>
    private static void CollectFilesRec(TreeNode node, List<TreeNode> acc)
    {
        if (!node.IsFolder && !string.IsNullOrEmpty(node.FilePath))
            acc.Add(node);
        foreach (var c in node.Children ?? Enumerable.Empty<TreeNode>())
            CollectFilesRec(c, acc);
    }
}
