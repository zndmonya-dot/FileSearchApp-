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
    public void IsDueAtDailyHours_empty_returns_false()
    {
        Assert.False(AutoRebuildSchedule.IsDueAtDailyHours([], DateTime.UtcNow, DateTime.UtcNow));
    }

    [Fact]
    public void IsDueAtDailyHours_no_last_update_returns_true()
    {
        Assert.True(AutoRebuildSchedule.IsDueAtDailyHours([0], null, DateTime.UtcNow));
    }

    [Fact]
    public void IsDueAtDailyHours_not_due_before_checked_hour()
    {
        var last = JstToUtc(2026, 6, 12, 8);
        var now = JstToUtc(2026, 6, 12, 11, 30);
        Assert.False(AutoRebuildSchedule.IsDueAtDailyHours([12, 18], last, now));
    }

    [Fact]
    public void IsDueAtDailyHours_due_after_checked_hour_same_day()
    {
        var last = JstToUtc(2026, 6, 12, 8);
        var now = JstToUtc(2026, 6, 12, 12, 5);
        Assert.True(AutoRebuildSchedule.IsDueAtDailyHours([12, 18], last, now));
    }

    [Fact]
    public void IsDueAtDailyHours_not_due_again_after_slot_ran()
    {
        var last = JstToUtc(2026, 6, 12, 12, 10);
        var now = JstToUtc(2026, 6, 12, 15);
        Assert.False(AutoRebuildSchedule.IsDueAtDailyHours([12, 18], last, now));
    }

    [Fact]
    public void IsDueAtDailyHours_due_for_second_slot_same_day()
    {
        var last = JstToUtc(2026, 6, 12, 12, 10);
        var now = JstToUtc(2026, 6, 12, 18, 5);
        Assert.True(AutoRebuildSchedule.IsDueAtDailyHours([12, 18], last, now));
    }

    [Fact]
    public void IsDueAtDailyHours_midnight_slot_after_previous_day()
    {
        var last = JstToUtc(2026, 6, 12, 23);
        var now = JstToUtc(2026, 6, 13, 0, 20);
        Assert.True(AutoRebuildSchedule.IsDueAtDailyHours([0], last, now));
    }

    [Fact]
    public void MigrateFromIntervalMinutes_maps_daily_midnight()
    {
        Assert.Equal([0], AutoRebuildSchedule.MigrateFromIntervalMinutes(1440));
    }

    [Fact]
    public void NormalizeDailyHours_sorts_and_deduplicates()
    {
        Assert.Equal([0, 6, 12], AutoRebuildSchedule.NormalizeDailyHours([12, 0, 6, 0, 99, -1]));
    }
}
