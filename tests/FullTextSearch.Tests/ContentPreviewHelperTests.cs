using FullTextSearch.Core;
using FullTextSearch.Core.Search;
using Xunit;

namespace FullTextSearch.Tests;

public class ContentPreviewHelperTests
{
    [Fact]
    public void ExtractFirstLine_skips_blank_lines_and_trims()
    {
        Assert.Equal("hello", ContentPreviewHelper.ExtractFirstLine("\r\n\r\n  hello  \r\nworld"));
    }

    [Fact]
    public void ExtractFirstLine_truncates_with_ellipsis()
    {
        var line = new string('a', 100);
        var preview = ContentPreviewHelper.ExtractFirstLine(line, maxChars: 10);
        Assert.Equal("aaaaaaaaaa…", preview);
    }

    [Fact]
    public void ExtractFirstLine_empty_content_returns_empty()
    {
        Assert.Equal("", ContentPreviewHelper.ExtractFirstLine(null));
        Assert.Equal("", ContentPreviewHelper.ExtractFirstLine("\n\r\n"));
    }

    [Fact]
    public void ExtractSearchMatchLine_returns_first_matching_line()
    {
        const string content = "header\nlicense text here\nfooter";
        var preview = ContentPreviewHelper.ExtractSearchMatchLine(
            content,
            ["license"],
            SearchMode.Keyword);
        Assert.Equal("license text here", preview);
    }

    [Fact]
    public void ExtractSearchMatchLine_and_mode_requires_all_terms_on_same_line()
    {
        const string content = "alpha only\nbeta and gamma together";
        Assert.Equal(
            "beta and gamma together",
            ContentPreviewHelper.ExtractSearchMatchLine(content, ["beta", "gamma"], SearchMode.Keyword));
    }

    [Fact]
    public void TryReadFirstLineFromDisk_reads_txt_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"preview-test-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "first line\nsecond line", Encoding.UTF8);
            Assert.Equal("first line", ContentPreviewHelper.TryReadFirstLineFromDisk(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
