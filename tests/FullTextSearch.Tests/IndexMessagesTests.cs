using FullTextSearch.Core;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary>スキップログ文言と拡張子表示名のフォーマット。</summary>
public class IndexMessagesTests
{
    [Fact]
    public void SkippedLogHeaderLine_contains_label_and_timestamp_pattern()
    {
        var t = new DateTime(2025, 3, 25, 14, 30, 0);
        var line = IndexMessages.SkippedLogHeaderLine(t);
        Assert.Contains("スキップファイル一覧", line);
        Assert.Contains("2025-03-25", line);
    }

    [Fact]
    public void SkippedLogTotalLine_formats_count()
    {
        Assert.Equal("合計: 3 件", IndexMessages.SkippedLogTotalLine(3));
    }

    [Theory]
    [InlineData(".docx", "Word文書")]
    [InlineData(".PDF", "PDFファイル")]
    [InlineData(".unknown", "ファイル")]
    [InlineData(".xlsx", "Excelブック")]
    public void GetFileTypeDisplayName_maps_extension(string ext, string expected) =>
        Assert.Equal(expected, IndexMessages.GetFileTypeDisplayName(ext));

    [Fact]
    public void GetFileTypeDisplayName_mixed_case_with_dot_uses_type_map() =>
        Assert.Equal("PDFファイル", IndexMessages.GetFileTypeDisplayName(".Pdf"));
}
