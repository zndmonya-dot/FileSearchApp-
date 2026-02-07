using FullTextSearch.Core.Preview;
using Xunit;

namespace FullTextSearch.Tests;

public class PreviewHelperTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData(".cs", ".cs")]
    [InlineData(".CS", ".cs")]
    [InlineData("txt", ".txt")]
    [InlineData("TXT", ".txt")]
    [InlineData("C:\\path\\to\\file.cs", ".cs")]
    [InlineData("/path/to/file.js", ".js")]
    [InlineData("  .md  ", ".md")]
    public void NormalizeExtension_ReturnsNormalized(string? input, string expected)
    {
        var result = PreviewHelper.NormalizeExtension(input ?? "");
        Assert.Equal(expected, result);
    }
}
