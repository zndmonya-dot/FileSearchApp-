using FullTextSearch.Core;
using Xunit;

namespace FullTextSearch.Tests;

public class AutoRebuildScheduleTests
{
    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");

    private static DateTime JstToUtc(int year, int month, int day, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, hour, minute, 0), Jst);

    [Fact]
    public void IsDue_zero_interval_returns_false()
    {
        Assert.False(AutoRebuildSchedule.IsDue(0, DateTime.UtcNow, DateTime.UtcNow));
    }

    [Fact]
    public void IsDue_no_last_update_returns_true()
    {
        Assert.True(AutoRebuildSchedule.IsDue(60, null, DateTime.UtcNow));
    }

    [Fact]
    public void IsDue_elapsed_not_yet_due()
    {
        var now = DateTime.UtcNow;
        var last = now.AddMinutes(-30).ToLocalTime();
        Assert.False(AutoRebuildSchedule.IsDue(60, last, now));
    }

    [Fact]
    public void IsDue_elapsed_due_after_interval()
    {
        var now = DateTime.UtcNow;
        var last = now.AddMinutes(-61).ToLocalTime();
        Assert.True(AutoRebuildSchedule.IsDue(60, last, now));
    }

    [Fact]
    public void IsDue_daily_not_due_same_jst_day()
    {
        // 2026-06-12 10:00 JST に更新、同日 15:00 JST → 未実行
        var last = JstToUtc(2026, 6, 12, 10);
        var now = JstToUtc(2026, 6, 12, 15);
        Assert.False(AutoRebuildSchedule.IsDue(AutoRebuildSchedule.DailyAtMidnightJstMinutes, last, now));
    }

    [Fact]
    public void IsDue_daily_due_after_jst_midnight()
    {
        // 2026-06-12 23:00 JST に更新、翌日 0:30 JST → 実行
        var last = JstToUtc(2026, 6, 12, 23);
        var now = JstToUtc(2026, 6, 13, 0, 30);
        Assert.True(AutoRebuildSchedule.IsDue(AutoRebuildSchedule.DailyAtMidnightJstMinutes, last, now));
    }

    [Fact]
    public void IsDue_weekly_not_due_same_jst_week()
    {
        // 月曜 1:00 JST に更新、同週水曜 → 未実行
        var last = JstToUtc(2026, 6, 8, 1); // Monday
        var now = JstToUtc(2026, 6, 10, 12); // Wednesday
        Assert.False(AutoRebuildSchedule.IsDue(AutoRebuildSchedule.WeeklyMondayJstMinutes, last, now));
    }

    [Fact]
    public void IsDue_weekly_due_after_monday_midnight()
    {
        // 先週火曜に更新、今週月曜 0:30 JST → 実行
        var last = JstToUtc(2026, 6, 9, 10); // Tuesday
        var now = JstToUtc(2026, 6, 15, 0, 30); // Monday
        Assert.True(AutoRebuildSchedule.IsDue(AutoRebuildSchedule.WeeklyMondayJstMinutes, last, now));
    }

}
