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
    public void IndexMaxFileBytesForExtract_is_10MiB() =>
        Assert.Equal(10L * 1024 * 1024, ContentLimits.IndexMaxFileBytesForExtract);

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
    public void MaxTextFileBytesToRead_matches_index_file_bytes() =>
        Assert.Equal(ContentLimits.IndexMaxFileBytesForExtract, ContentLimits.MaxTextFileBytesToRead);
}
