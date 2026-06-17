using FullTextSearch.Core;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary>スキップログ文言のフォーマット。</summary>
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

    [Fact]
    public void SkippedLogLine_includes_path_and_reason_tab_separated()
    {
        var line = IndexMessages.SkippedLogLine(@"C:\docs\a.docx", "テキスト抽出に失敗");
        Assert.Equal("C:\\docs\\a.docx" + '\t' + "テキスト抽出に失敗", line);
    }

    [Fact]
    public void SkippedReasonFileTooLarge_includes_byte_count()
    {
        var reason = IndexMessages.SkippedReasonFileTooLarge(12_345_678);
        Assert.Contains(ContentLimits.GetIndexMaxFileBytesDisplayLabel(), reason);
        Assert.Contains("12,345,678", reason);
    }
}
