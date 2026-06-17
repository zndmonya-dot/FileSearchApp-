namespace FullTextSearch.Core;

/// <summary>
/// 定期再構築の実行タイミング判定。
/// 毎日、日本時間でチェックした時刻（0〜23時）を過ぎたあと、まだその枠で更新していなければ実行可能。
/// </summary>
public static class AutoRebuildSchedule
{
    private static readonly TimeZoneInfo JapanTimeZone = ResolveJapanTimeZone();

    /// <summary>
    /// 指定した毎日の時刻（日本時間・0〜23時）で再構築が実行可能か。
    /// <paramref name="dailyHoursJst"/> が空なら常に false。
    /// </summary>
    public static bool IsDueAtDailyHours(IReadOnlyList<int> dailyHoursJst, DateTime? lastUpdate, DateTime utcNow)
    {
        var hours = NormalizeDailyHours(dailyHoursJst);
        if (hours.Count == 0) return false;
        if (!lastUpdate.HasValue) return true;

        var nowJst = ToJapanTime(utcNow);
        var lastJst = ToJapanTime(lastUpdate.Value);
        var today = nowJst.Date;

        for (var i = hours.Count - 1; i >= 0; i--)
        {
            var slot = today.AddHours(hours[i]);
            if (nowJst < slot) continue;
            return lastJst < slot;
        }

        return false;
    }

    /// <summary>旧設定（分間隔）を毎日の時刻リストへ変換する。</summary>
    public static List<int> MigrateFromIntervalMinutes(int intervalMinutes) => intervalMinutes switch
    {
        <= 0 => [],
        30 => [0, 6, 12, 18],
        60 => Enumerable.Range(0, 24).ToList(),
        120 => Enumerable.Range(0, 24).Where(h => h % 2 == 0).ToList(),
        360 => [0, 6, 12, 18],
        720 => [0, 12],
        1440 => [0],
        10080 => [0],
        _ => [0]
    };

    /// <summary>0〜23 の整数だけを昇順・重複なしで返す。</summary>
    public static List<int> NormalizeDailyHours(IReadOnlyList<int>? dailyHoursJst)
    {
        if (dailyHoursJst is not { Count: > 0 }) return [];
        return dailyHoursJst
            .Where(h => h is >= 0 and <= 23)
            .Distinct()
            .OrderBy(h => h)
            .ToList();
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
