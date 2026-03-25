using FileSearch.Messages;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary>ユーザー向け文言の代表的な不変条件。</summary>
public class UserMessagesTests
{
    [Fact]
    public void AppTitle_is_non_empty() => Assert.False(string.IsNullOrWhiteSpace(UserMessages.AppTitle));

    [Fact]
    public void FormatSkippedCountLine_zero_returns_empty() =>
        Assert.Equal("", UserMessages.FormatSkippedCountLine(0));

    [Fact]
    public void FormatSkippedCountLine_positive_contains_count_and_label()
    {
        var s = UserMessages.FormatSkippedCountLine(12);
        Assert.Contains("12", s);
        Assert.Contains("スキップ", s);
    }

    [Fact]
    public void PreviewErrorLine_wraps_message() =>
        Assert.Equal("[エラー] x", UserMessages.PreviewErrorLine("x"));

    [Fact]
    public void FormatIndexProgressCounts_with_skips_includes_skip_word() =>
        Assert.Contains("スキップ", UserMessages.FormatIndexProgressCounts(1, 10, UserMessages.FileUnit, 2));
}
