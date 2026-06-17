using FullTextSearch.Core;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary>UT-CORE-04: 上限定数の回帰防止。</summary>
public class ContentLimitsTests
{
    public ContentLimitsTests()
    {
        ContentLimits.ConfigureIndexMaxFileBytes(null);
    }

    [Fact]
    public void LuceneMaxTermUtf8Bytes_is_below_Lucene_official_32766() =>
        Assert.True(ContentLimits.LuceneMaxTermUtf8Bytes > 0 && ContentLimits.LuceneMaxTermUtf8Bytes <= 32765);

    [Fact]
    public void DefaultIndexMaxFileBytes_is_10MiB() =>
        Assert.Equal(10L * 1024 * 1024, ContentLimits.DefaultIndexMaxFileBytes);

    /// <summary>REQ-2.5: 超過のみスキップ（厳密に 10MB 超）。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(10L * 1024 * 1024, false)]
    [InlineData(10L * 1024 * 1024 + 1, true)]
    [InlineData(long.MaxValue, true)]
    public void ExceedsIndexTextExtractionFileSizeLimit_matches_spec_boundary(long fileSize, bool isExcess) =>
        Assert.Equal(isExcess, ContentLimits.ExceedsIndexTextExtractionFileSizeLimit(fileSize));

    [Fact]
    public void ConfigureIndexMaxFileBytes_zero_means_unlimited()
    {
        ContentLimits.ConfigureIndexMaxFileBytes(0);
        Assert.False(ContentLimits.ExceedsIndexTextExtractionFileSizeLimit(long.MaxValue / 2));
        Assert.Equal("制限なし", ContentLimits.GetIndexMaxFileBytesDisplayLabel());
    }

    [Fact]
    public void MaxTextFileBytesToRead_matches_effective_limit()
    {
        ContentLimits.ConfigureIndexMaxFileBytes(5L * 1024 * 1024);
        Assert.Equal(5L * 1024 * 1024, ContentLimits.MaxTextFileBytesToRead);
    }
}
