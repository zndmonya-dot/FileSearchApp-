using FullTextSearch.Infrastructure.Extractors;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary><see cref="MsgExtractor"/> の .msg 本文抽出。</summary>
public class MsgExtractorTests
{
    private static string SamplePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "msg", fileName);

    [Theory]
    [InlineData("RtfSampleEmail.msg", "Heavens! what a virulent attack!")]
    [InlineData("HtmlSampleEmail.msg", "Heavens! what a virulent attack!")]
    [InlineData("TxtSampleEmail.msg", "Heavens! what a virulent attack!")]
    [InlineData("RtfWithShortRussianString.msg", "Имя")]
    public async Task ExtractTextAsync_reads_body_from_outlook_msg(string fileName, string expectedSnippet)
    {
        var path = SamplePath(fileName);
        Assert.True(File.Exists(path), $"Missing test fixture: {path}");

        var extractor = new MsgExtractor();
        var text = await extractor.ExtractTextAsync(path);

        Assert.Contains(expectedSnippet, text, StringComparison.Ordinal);
    }
}
