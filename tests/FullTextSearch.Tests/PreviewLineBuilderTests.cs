using FullTextSearch.Core;
using FullTextSearch.Core.Preview;
using FullTextSearch.Core.Search;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary><see cref="PreviewLineBuilder"/> の行境界・マッチ行検出。</summary>
public class PreviewLineBuilderTests
{
    [Fact]
    public void BuildLineStartOffsets_empty_is_single_zero()
    {
        var starts = PreviewLineBuilder.BuildLineStartOffsets("");
        Assert.Single(starts);
        Assert.Equal(0, starts[0]);
    }

    [Fact]
    public void BuildLineStartOffsets_counts_lines()
    {
        var starts = PreviewLineBuilder.BuildLineStartOffsets("a\nb\nc");
        Assert.Equal(3, starts.Length);
        Assert.Equal(0, starts[0]);
        Assert.Equal(2, starts[1]);
        Assert.Equal(4, starts[2]);
    }

    [Fact]
    public void ExtractLine_trims_cr()
    {
        var content = "hello\r\nworld";
        var starts = PreviewLineBuilder.BuildLineStartOffsets(content);
        Assert.Equal("hello", PreviewLineBuilder.ExtractLine(content, starts, 0));
        Assert.Equal("world", PreviewLineBuilder.ExtractLine(content, starts, 1));
    }

    [Fact]
    public void CollectMatchLineNumbers_finds_keyword_lines()
    {
        const string content = "alpha\nbeta gamma\nalpha again";
        var starts = PreviewLineBuilder.BuildLineStartOffsets(content);
        var matches = PreviewLineBuilder.CollectMatchLineNumbers(content, starts, ["alpha"], SearchMode.Keyword);
        Assert.Equal(2, matches.Length);
        Assert.Equal(1, matches[0]);
        Assert.Equal(3, matches[1]);
    }

    [Fact]
    public void CollectMatchLineNumbers_single_and_keyword_requires_contiguous_substring()
    {
        const string content = "AquesTalkライセンス文書\nライセンス情報の説明";
        var starts = PreviewLineBuilder.BuildLineStartOffsets(content);
        var matches = PreviewLineBuilder.CollectMatchLineNumbers(
            content, starts, ["ライセンス情報"], SearchMode.Keyword);
        Assert.Single(matches);
        Assert.Equal(2, matches[0]);
    }

    [Fact]
    public void CollectMatchLineNumbers_spaced_and_requires_all_terms_on_same_line()
    {
        const string content = "uses import module\nthen sys call\nimport and sys here";
        var starts = PreviewLineBuilder.BuildLineStartOffsets(content);
        var matches = PreviewLineBuilder.CollectMatchLineNumbers(
            content, starts, ["import", "sys"], SearchMode.Keyword);
        Assert.Single(matches);
        Assert.Equal(3, matches[0]);
    }

    [Fact]
    public void SudachiTokenizeChunkChars_is_positive() =>
        Assert.True(ContentLimits.SudachiTokenizeChunkChars >= 10_000);
}

