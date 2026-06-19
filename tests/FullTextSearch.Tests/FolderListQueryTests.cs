using FullTextSearch.Core.UI;
using Xunit;

namespace FullTextSearch.Tests;

public class FolderListQueryTests
{
    private static TreeNode File(string name, string path, DateTime modified) => new()
    {
        Name = name,
        FilePath = path,
        LastModified = modified,
        IsFolder = false
    };

    [Fact]
    public void Apply_filters_by_extension()
    {
        var items = new List<TreeNode>
        {
            File("a.txt", @"C:\a.txt", DateTime.MinValue),
            File("b.pdf", @"C:\b.pdf", DateTime.MinValue)
        };

        var result = FolderListQuery.Apply(items, ".txt", "", "name", true).ToList();
        Assert.Single(result);
        Assert.Equal("a.txt", result[0].Name);
    }

    [Fact]
    public void Apply_filters_by_file_name_query()
    {
        var items = new List<TreeNode>
        {
            File("readme.txt", @"C:\docs\readme.txt", DateTime.MinValue),
            File("notes.txt", @"C:\other\notes.txt", DateTime.MinValue)
        };

        var result = FolderListQuery.Apply(items, null, "readme", "name", true).ToList();
        Assert.Single(result);
        Assert.Equal("readme.txt", result[0].Name);
    }

    [Fact]
    public void Apply_sorts_by_date_descending()
    {
        var older = File("old.txt", @"C:\old.txt", new DateTime(2020, 1, 1));
        var newer = File("new.txt", @"C:\new.txt", new DateTime(2024, 1, 1));
        var items = new List<TreeNode> { older, newer };

        var result = FolderListQuery.Apply(items, null, "", "date", false).ToList();
        Assert.Equal("new.txt", result[0].Name);
        Assert.Equal("old.txt", result[1].Name);
    }
}
