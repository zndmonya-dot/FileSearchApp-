using FullTextSearch.Core.UI;
using Xunit;

namespace FullTextSearch.Tests;

public class FolderListDisplayTests
{
    [Fact]
    public void FormatFileDisplay_direct_child_returns_file_name_only()
    {
        var folder = @"C:\Python3\TaskTimer";
        var file = @"C:\Python3\TaskTimer\requirements.txt";
        var parts = FolderListDisplay.FormatFileDisplay(folder, file, "requirements.txt");
        Assert.Null(parts.FolderPrefix);
        Assert.Equal("requirements.txt", parts.FileName);
    }

    [Fact]
    public void FormatFileDisplay_nested_child_splits_folder_prefix_and_file_name()
    {
        var folder = @"C:\Python3";
        var file = @"C:\Python3\TaskTimer\requirements.txt";
        var parts = FolderListDisplay.FormatFileDisplay(folder, file, "requirements.txt");
        Assert.Equal("TaskTimer", parts.FolderPrefix);
        Assert.Equal("requirements.txt", parts.FileName);
    }

    [Fact]
    public void FormatFileName_uses_forward_slash_separator()
    {
        var folder = @"C:\Python3";
        var file = @"C:\Python3\TaskTimer\requirements.txt";
        Assert.Equal("TaskTimer/requirements.txt", FolderListDisplay.FormatFileName(folder, file, "requirements.txt"));
    }

    [Fact]
    public void NormalizeDisplayPath_replaces_backslashes()
    {
        Assert.Equal("C:/Python3/TaskTimer", FolderListDisplay.NormalizeDisplayPath(@"C:\Python3\TaskTimer"));
    }
}
