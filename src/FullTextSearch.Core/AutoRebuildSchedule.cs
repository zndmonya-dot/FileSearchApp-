namespace FullTextSearch.Core;

/// <summary>
/// 定期再構築の実行タイミング判定。
/// 短い間隔は経過時間、24時間は日本時間の日次0時、1週間は月曜0時（日本時間）に揃える。
/// </summary>
public static class AutoRebuildSchedule
{
    /// <summary>定期再構築: 毎日0時（日本時間）相当の間隔（分）。</summary>
    public const int DailyAtMidnightJstMinutes = 1440;

    /// <summary>定期再構築: 毎週月曜0時（日本時間）相当の間隔（分）。</summary>
    public const int WeeklyMondayJstMinutes = 10080;

    private static readonly TimeZoneInfo JapanTimeZone = ResolveJapanTimeZone();

    /// <summary>
    /// 指定間隔で再構築が実行可能か。intervalMinutes が 0 以下なら常に false。
    /// </summary>
    /// <param name="intervalMinutes">設定の間隔（分）。</param>
    /// <param name="lastUpdate">前回のインデックス更新（未実行なら null）。</param>
    /// <param name="utcNow">判定基準の UTC 時刻（テスト用に注入可能）。</param>
    public static bool IsDue(int intervalMinutes, DateTime? lastUpdate, DateTime utcNow)
    {
        if (intervalMinutes <= 0) return false;
        if (!lastUpdate.HasValue) return true;

        return intervalMinutes switch
        {
            DailyAtMidnightJstMinutes => IsCalendarDailyDue(lastUpdate.Value, utcNow),
            WeeklyMondayJstMinutes => IsCalendarWeeklyDue(lastUpdate.Value, utcNow),
            _ => IsElapsedDue(intervalMinutes, lastUpdate.Value, utcNow)
        };
    }

    /// <summary>前回更新から interval 分以上経過しているか（ローカル時計の経過時間）。</summary>
    private static bool IsElapsedDue(int intervalMinutes, DateTime lastUpdate, DateTime utcNow)
    {
        var nowLocal = utcNow.ToLocalTime();
        var lastLocal = ToLocalWallClock(lastUpdate);
        return (nowLocal - lastLocal).TotalMinutes >= intervalMinutes;
    }

    /// <summary>日本時間で日付が変わったあと、まだ今日の再構築をしていないか。</summary>
    private static bool IsCalendarDailyDue(DateTime lastUpdate, DateTime utcNow)
    {
        var nowJst = ToJapanTime(utcNow);
        var todayStartJst = nowJst.Date;
        return ToJapanTime(lastUpdate) < todayStartJst;
    }

    /// <summary>日本時間で今週の月曜0時を過ぎ、まだ今週の再構築をしていないか。</summary>
    private static bool IsCalendarWeeklyDue(DateTime lastUpdate, DateTime utcNow)
    {
        var nowJst = ToJapanTime(utcNow);
        var weekStartJst = GetWeekStartMonday(nowJst);
        return ToJapanTime(lastUpdate) < weekStartJst;
    }

    /// <summary>月曜 0:00（date が属する週の開始、日本時間の日付部分）。</summary>
    private static DateTime GetWeekStartMonday(DateTime dateJst)
    {
        var d = dateJst.Date;
        var daysFromMonday = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return d.AddDays(-daysFromMonday);
    }

    private static DateTime ToJapanTime(DateTime dateTime)
    {
        var utc = dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Local).ToUniversalTime()
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, JapanTimeZone);
    }

    private static DateTime ToLocalWallClock(DateTime dateTime) =>
        dateTime.Kind switch
        {
            DateTimeKind.Local => dateTime,
            DateTimeKind.Utc => dateTime.ToLocalTime(),
            _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Local)
        };

    private static TimeZoneInfo ResolveJapanTimeZone()
    {
        foreach (var id in new[] { "Tokyo Standard Time", "Asia/Tokyo" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        throw new InvalidOperationException("Japan time zone not found on this system.");
    }
}
