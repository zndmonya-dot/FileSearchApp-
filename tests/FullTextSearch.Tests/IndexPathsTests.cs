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
}
