using FullTextSearch.Core.UI;
using FileSearch.Messages;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary>UT-UI-01, UT-UI-02, UT-UI-03, UT-UI-04</summary>
public class DisplayFormattersTests
{
    [Fact]
    public void FormatDate_converts_to_local_display()
    {
        var utc = new DateTime(2024, 6, 15, 10, 30, 45, DateTimeKind.Utc);
        var expected = utc.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
        Assert.Equal(expected, DisplayFormatters.FormatDate(utc));
    }

    [Fact]
    public void FormatLastIndexUpdate_null_uses_never_run() =>
        Assert.Equal(UserMessages.LastIndexNeverRun, DisplayFormatters.FormatLastIndexUpdate(null));

    [Fact]
    public void FormatLastIndexUpdate_ancient_uses_date_time_string()
    {
        var t = DateTime.Now.AddDays(-20);
        var s = DisplayFormatters.FormatLastIndexUpdate(t);
        Assert.Matches(@"\A\d{2}/\d{2} \d{2}:\d{2}\z", s);
    }

    [Theory]
    [InlineData("x.docx", "word")]
    [InlineData("D.PDF", "pdf")]
    [InlineData("a.cs", "code")]
    [InlineData("App.java", "code")]
    [InlineData("n.unknown", "text")]
    public void GetFileIconClass_maps(string name, string cls) =>
        Assert.Equal(cls, DisplayFormatters.GetFileIconClass(name));
}
