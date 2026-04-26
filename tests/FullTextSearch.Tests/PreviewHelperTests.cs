using FullTextSearch.Core.Preview;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary><see cref="PreviewHelper.NormalizeExtension"/> の正規化仕様。</summary>
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
    [InlineData(@"C:\z\a.c", ".c")]
    [InlineData(@"C:\z\y.double.ext", ".ext")]
    [InlineData(@"C:\z\子\file.日本語", ".日本語")]
    public void NormalizeExtension_ReturnsNormalized(string? input, string expected)
    {
        var result = PreviewHelper.NormalizeExtension(input ?? "");
        Assert.Equal(expected, result);
    }
}
