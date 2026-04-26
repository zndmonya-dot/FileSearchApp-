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

    /// <summary>REQ-2.5: 超過のみスキップ（厳密に 10MB 超）。<see cref="ContentLimits.ExceedsIndexTextExtractionFileSizeLimit"/> は Lucene の <c>TryGetIndexedDocumentAsync</c> と同じ式。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(10L * 1024 * 1024, false)]
    [InlineData(10L * 1024 * 1024 + 1, true)]
    [InlineData(long.MaxValue, true)]
    public void ExceedsIndexTextExtractionFileSizeLimit_matches_spec_boundary(long fileSize, bool isExcess) =>
        Assert.Equal(isExcess, ContentLimits.ExceedsIndexTextExtractionFileSizeLimit(fileSize));

    [Fact]
    public void PreviewAndTextFile_limits_are_10MiB()
    {
        const long expected = 10L * 1024 * 1024;
        Assert.Equal(expected, ContentLimits.PreviewMaxFileBytes);
        Assert.Equal(expected, ContentLimits.MaxTextFileBytesToRead);
    }

    /// <summary>要件 REQ-2.7（1 文書のインデックス格納文字数上限）および ExtractMaxChars との一致。</summary>
    [Fact]
    public void IndexMaxContentChars_and_ExtractMaxChars_match_requirement_100_000()
    {
        const int cap = 100_000;
        Assert.Equal(cap, ContentLimits.IndexMaxContentChars);
        Assert.Equal(cap, ContentLimits.ExtractMaxChars);
    }
}
