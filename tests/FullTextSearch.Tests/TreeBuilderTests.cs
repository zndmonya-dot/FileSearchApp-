using FullTextSearch.Core.UI;
using FullTextSearch.Core.Models;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary>UT-UI-05〜UT-UI-08</summary>
public class TreeBuilderTests
{
    private static string Root(params string[] parts) =>
        Path.GetFullPath(Path.Combine(new[] { Path.GetTempPath(), "fts-test-tree" }.Concat(parts).ToArray()));

    [Fact]
    public void BuildTree_empty_target_folders_derives_roots_from_items()
    {
        var root = Root("h0", "shared");
        var sub = Path.Combine(root, "docs");
        var item = new SearchResultItem
        {
            FilePath = Path.Combine(sub, "note.txt"),
            FileName = "note.txt",
            FolderPath = sub,
            FileSize = 1,
            LastModified = default
        };
        var tree = TreeBuilder.BuildTree(Array.Empty<string>(), new[] { item });
        Assert.Single(tree);
        Assert.Equal(sub, tree[0].FullPath);
    }

    [Fact]
    public void BuildFolderSkeleton_returns_roots_only()
    {
        var root = Root("skel0");
        Directory.CreateDirectory(root);
        var sub = Path.Combine(root, "docs");
        Directory.CreateDirectory(sub);

        try
        {
            var tree = TreeBuilder.BuildFolderSkeleton(new[] { root });
            Assert.Single(tree);
            Assert.False(tree[0].FolderChildrenLoaded);
            Assert.Empty(tree[0].Children!);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void LoadDirectFolderChildren_loads_one_level()
    {
        var root = Root("skel1");
        Directory.CreateDirectory(root);
        var sub = Path.Combine(root, "docs");
        var nested = Path.Combine(sub, "2024");
        Directory.CreateDirectory(nested);

        try
        {
            var node = TreeBuilder.BuildFolderSkeleton(new[] { root })[0];
            TreeBuilder.LoadDirectFolderChildren(node);
            Assert.True(node.FolderChildrenLoaded);
            var docs = Assert.Single(node.Children!.Where(c => c.Name == "docs"));
            Assert.False(docs.FolderChildrenLoaded);
            Assert.Empty(docs.Children!);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void LoadDirectFolderChildren_loads_files_in_folder()
    {
        var root = Root("skel2");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "readme.txt");
        File.WriteAllText(file, "hello");

        try
        {
            var node = TreeBuilder.BuildFolderSkeleton(new[] { root })[0];
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" };
            TreeBuilder.LoadDirectFolderChildren(node, exts);
            var fileNode = Assert.Single(node.Children!.Where(c => !c.IsFolder));
            Assert.Equal("readme.txt", fileNode.Name);
            Assert.NotNull(fileNode.FileData);
            Assert.Equal(file, fileNode.FilePath);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CreateSearchResultItem_reads_file_metadata()
    {
        var root = Root("skel3");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "a.txt");
        File.WriteAllText(file, "x");

        try
        {
            var item = TreeBuilder.CreateSearchResultItem(file);
            Assert.Equal("a.txt", item.FileName);
            Assert.Equal(root, item.FolderPath);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void BuildFullFolderTree_loads_nested_folders_and_files()
    {
        var root = Root("skel4");
        Directory.CreateDirectory(root);
        var sub = Path.Combine(root, "docs");
        Directory.CreateDirectory(sub);
        var file = Path.Combine(sub, "note.txt");
        File.WriteAllText(file, "hello");

        try
        {
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" };
            var tree = TreeBuilder.BuildFullFolderTree(new[] { root }, exts);
            var rootNode = Assert.Single(tree);
            Assert.True(rootNode.FolderChildrenLoaded);
            var docs = Assert.Single(rootNode.Children!.Where(c => c.Name == "docs"));
            Assert.True(docs.FolderChildrenLoaded);
            var fileNode = Assert.Single(docs.Children!.Where(c => !c.IsFolder));
            Assert.Equal("note.txt", fileNode.Name);
            Assert.Equal(1, rootNode.FileCount);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void BuildFullFolderTree_omits_folders_without_matching_files()
    {
        var root = Root("skel5");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        var docs = Path.Combine(root, "docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "note.txt"), "hello");

        try
        {
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" };
            var tree = TreeBuilder.BuildFullFolderTree(new[] { root }, exts);
            var rootNode = Assert.Single(tree);
            Assert.DoesNotContain(rootNode.Children!, c => c.IsFolder && c.Name == "empty");
            Assert.Contains(rootNode.Children!, c => c.IsFolder && c.Name == "docs");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void BuildFullFolderTree_omits_root_when_no_matching_files()
    {
        var root = Root("skel6");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "app.exe"), "bin");

        try
        {
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" };
            Assert.Empty(TreeBuilder.BuildFullFolderTree(new[] { root }, exts));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void GetFolderDisplayName_handles_drive_root_and_trailing_slash()
    {
        Assert.Equal("yamamoro", TreeBuilder.GetFolderDisplayName(@"C:\yamamoro\"));
        Assert.Equal("C", TreeBuilder.GetFolderDisplayName(@"C:\"));
        Assert.Equal("C", TreeBuilder.GetFolderDisplayName(@"C:"));
        Assert.Equal("D", TreeBuilder.GetFolderDisplayName(@"D:\"));
    }

    [Fact]
    public void BuildFolderSkeleton_empty_folders_returns_empty()
    {
        Assert.Empty(TreeBuilder.BuildFolderSkeleton(Array.Empty<string>()));
    }

    [Fact]
    public void BuildTree_empty_items_returns_empty()
    {
        var t = Root("a0");
        var r = TreeBuilder.BuildTree(new[] { t }, new List<SearchResultItem>());
        Assert.Empty(r);
    }

    [Fact]
    public void BuildTree_one_file_under_target()
    {
        var root = Root("b0");
        var item = new SearchResultItem
        {
            FilePath = Path.Combine(root, "a.txt"),
            FileName = "a.txt",
            FolderPath = root,
            FileSize = 1,
            LastModified = default
        };
        var tree = TreeBuilder.BuildTree(new[] { root }, new[] { item });
        Assert.Single(tree);
        var rootNode = tree[0];
        Assert.True(rootNode.IsFolder);
        var files = rootNode.Children!.Where(c => !c.IsFolder).ToList();
        Assert.Single(files);
        Assert.Equal("a.txt", files[0].Name);
    }

    [Fact]
    public void BuildTree_first_target_wins_for_nested_path_prefixes()
    {
        var t1 = Root("c0", "d0");
        var t2 = Root("c0", "d0", "inner0");
        var filePath = Path.Combine(t2, "x.txt");
        var item = new SearchResultItem
        {
            FilePath = filePath,
            FileName = "x.txt",
            FolderPath = t2,
            FileSize = 1,
            LastModified = default
        };
        var tree = TreeBuilder.BuildTree(new[] { t1, t2 }, new[] { item });
        Assert.Single(tree);
    }

    [Fact]
    public void ExpandPathToFile_expands_ancestors()
    {
        var d = Root("e0", "n0", "d0", "d1");
        var f = Path.Combine(d, "f.txt");
        var item = new SearchResultItem
        {
            FilePath = f,
            FileName = "f.txt",
            FolderPath = d,
            FileSize = 1,
            LastModified = default
        };
        var rootT = Root("e0", "n0");
        var tree = TreeBuilder.BuildTree(new[] { rootT }, new[] { item });
        var ok = TreeBuilder.ExpandPathToFile(tree, f);
        Assert.True(ok);
    }

    [Fact]
    public void CollectAllFileNodes_two_files_same_folder()
    {
        var rootT = Root("f0", "g0");
        var d1 = Path.Combine(rootT, "u0");
        var f1 = Path.Combine(d1, "1.txt");
        var f2 = Path.Combine(d1, "2.txt");
        var items = new[] { M(f1, "1.txt", d1), M(f2, "2.txt", d1) };
        var tree = TreeBuilder.BuildTree(new[] { rootT }, items);
        var all = TreeBuilder.CollectAllFileNodes(tree);
        Assert.Equal(2, all.Count);
    }

    private static SearchResultItem M(string filePath, string name, string folder) => new()
    {
        FilePath = filePath,
        FileName = name,
        FolderPath = folder,
        FileSize = 0,
        LastModified = default
    };
}
