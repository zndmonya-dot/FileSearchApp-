using FullTextSearch.Core.Index;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary><see cref="IndexPaths"/> のパス正規化。</summary>
public class IndexPathsTests
{
    [Fact]
    public void NormalizeFilePath_uses_full_path()
    {
        var temp = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "fts-diff-test"));
        var file = Path.Combine(temp, "sample.txt");
        var normalized = IndexPaths.NormalizeFilePath(file);
        Assert.Equal(Path.GetFullPath(file), normalized);
    }

    [Fact]
    public void NormalizeFolderPath_drive_letter_is_root()
    {
        Assert.Equal("C:\\", IndexPaths.NormalizeFolderPath("C:"));
        Assert.Equal("D:\\", IndexPaths.NormalizeFolderPath("D:\\"));
    }

    [Fact]
    public void NormalizeFilePath_strips_long_path_prefix()
    {
        var path = @"\\?\C:\Windows\System32\drivers\etc\hosts";
        var normalized = IndexPaths.NormalizeFilePath(path);
        Assert.Equal(Path.GetFullPath(@"C:\Windows\System32\drivers\etc\hosts"), normalized);
    }

    [Fact]
    public void IsPathUnderAnyFolder_drive_root_includes_files_on_drive()
    {
        var roots = new[] { IndexPaths.NormalizeFolderPath("C:") };
        Assert.True(IndexPaths.IsPathUnderAnyFolder(@"C:\Users\test.txt", roots));
        Assert.True(IndexPaths.IsPathUnderAnyFolder(@"C:\全文検索システム\README.md", roots));
        Assert.False(IndexPaths.IsPathUnderAnyFolder(@"D:\other.txt", roots));
    }

    [Fact]
    public void IsPathUnderFolder_subfolder_does_not_match_sibling_prefix()
    {
        var root = IndexPaths.NormalizeFolderPath(@"C:\data");
        Assert.True(IndexPaths.IsPathUnderFolder(@"C:\data\a.txt", root));
        Assert.False(IndexPaths.IsPathUnderFolder(@"C:\datafile.txt", root));
    }

    [Fact]
    public void IsPathUnderFolderRoot_normalizes_before_compare()
    {
        var root = @"C:\data";
        Assert.True(IndexPaths.IsPathUnderFolderRoot(@"C:\data\sub\file.txt", root));
        Assert.False(IndexPaths.IsPathUnderFolderRoot(@"C:\other\file.txt", root));
    }
}
