using FileSearch.Blazor.Services;
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
