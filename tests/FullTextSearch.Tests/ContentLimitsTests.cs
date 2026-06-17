using FullTextSearch.Core;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary>UT-CORE-04: 上限定数の回帰防止。</summary>
public class ContentLimitsTests
{
    [Fact]
    public void LuceneMaxTermUtf8Bytes_is_below_Lucene_official_32766() =>
        Assert.True(ContentLimits.LuceneMaxTermUtf8Bytes > 0 && ContentLimits.LuceneMaxTermUtf8Bytes <= 32765);

    [Fact]
    public void IndexMaxFileBytesForExtract_is_1MiB() =>
        Assert.Equal(1L * 1024 * 1024, ContentLimits.IndexMaxFileBytesForExtract);

    /// <summary>REQ-2.5: 超過のみスキップ（厳密に上限超）。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1L * 1024 * 1024, false)]
    [InlineData(1L * 1024 * 1024 + 1, true)]
    [InlineData(long.MaxValue, true)]
    public void ExceedsIndexTextExtractionFileSizeLimit_matches_spec_boundary(long fileSize, bool isExcess) =>
        Assert.Equal(isExcess, ContentLimits.ExceedsIndexTextExtractionFileSizeLimit(fileSize));

    [Fact]
    public void MaxTextFileBytesToRead_matches_index_limit() =>
        Assert.Equal(ContentLimits.IndexMaxFileBytesForExtract, ContentLimits.MaxTextFileBytesToRead);

    [Fact]
    public void GetIndexMaxFileBytesDisplayLabel_is_1MB() =>
        Assert.Equal("1MB", ContentLimits.GetIndexMaxFileBytesDisplayLabel());
}
