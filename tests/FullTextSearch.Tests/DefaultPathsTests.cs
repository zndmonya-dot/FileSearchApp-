using FullTextSearch.Core;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary>UT-CORE-05: デフォルトインデックス場所の形。</summary>
public class DefaultPathsTests
{
    [Fact]
    public void IndexPath_contains_app_folder_and_ends_with_Index()
    {
        var p = DefaultPaths.IndexPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Assert.Contains("FullTextSearch", p, StringComparison.Ordinal);
        Assert.EndsWith("Index", p, StringComparison.OrdinalIgnoreCase);
    }
}
