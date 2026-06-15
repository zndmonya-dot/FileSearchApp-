using FileSearch.Messages;
using FullTextSearch.Core.Preview;

namespace FullTextSearch.Core.UI;

/// <summary>
/// 日付・ファイル種別アイコン分類の表示用フォーマット。
/// </summary>
public static class DisplayFormatters
{
    /// <summary>日付を yyyy/MM/dd HH:mm で表示する。</summary>
    public static string FormatDate(DateTime d) =>
        d.ToLocalTime().ToString("yyyy/MM/dd HH:mm");

    /// <summary>インデックス最終更新の短い表示（未実行／たった今／分前／時間前／日前／日時）</summary>
    public static string FormatLastIndexUpdate(DateTime? lastUpdate)
    {
        if (!lastUpdate.HasValue) return UserMessages.LastIndexNeverRun;
        var diff = DateTime.Now - lastUpdate.Value;
        if (diff.TotalMinutes < 1) return UserMessages.LastIndexJustNow;
        if (diff.TotalMinutes < 60) return UserMessages.FormatMinutesAgo((int)diff.TotalMinutes);
        if (diff.TotalHours < 24) return UserMessages.FormatHoursAgo((int)diff.TotalHours);
        if (diff.TotalDays < 7) return UserMessages.FormatDaysAgo((int)diff.TotalDays);
        return lastUpdate.Value.ToString("MM/dd HH:mm");
    }

    /// <summary>ファイル名の拡張子からプレビュー用のアイコン CSS クラスを返す。</summary>
    public static string GetFileIconClass(string name) => PreviewHelper.GetFileIconClass(name);
}
