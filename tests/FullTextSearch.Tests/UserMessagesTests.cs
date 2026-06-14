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

    [Fact]
    public void FormatIndexProgressCounts_zero_skips_omits_skip_phrase()
    {
        var s = UserMessages.FormatIndexProgressCounts(5, 5, UserMessages.PieceUnit, 0);
        Assert.DoesNotContain("スキップ", s);
    }

    [Fact]
    public void FormatMinutesAgo_includes_count_and_label() =>
        Assert.Equal("7分前", UserMessages.FormatMinutesAgo(7));

    [Fact]
    public void FormatHoursAgo_includes_count_and_label() =>
        Assert.Equal("3時間前", UserMessages.FormatHoursAgo(3));

    [Fact]
    public void FormatDaysAgo_includes_count_and_label() =>
        Assert.Equal("2日前", UserMessages.FormatDaysAgo(2));

    [Fact]
    public void FormatBuildingPercent_includes_value() =>
        Assert.Equal("構築中 40%", UserMessages.FormatBuildingPercent(40));

    [Fact]
    public void FormatRegisteredCount_uses_N0() =>
        Assert.Equal("1,000 件登録済み", UserMessages.FormatRegisteredCount(1000));

    [Theory]
    [InlineData(nameof(UserMessages.SearchFailed), UserMessages.SearchFailed)]
    [InlineData(nameof(UserMessages.NoTargetFolders), UserMessages.NoTargetFolders)]
    [InlineData(nameof(UserMessages.CannotSearchWhileIndexing), UserMessages.CannotSearchWhileIndexing)]
    [InlineData(nameof(UserMessages.SettingsTitle), UserMessages.SettingsTitle)]
    public void Key_user_strings_are_non_empty(string _, string value) =>
        Assert.False(string.IsNullOrWhiteSpace(value));
}
