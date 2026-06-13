using FileSearch.Messages;

namespace FileSearch.Blazor;

/// <summary>
/// 日付・サイズ・ファイル種別・アイコン分類の表示用フォーマット。他コンポーネントから再利用する。
/// インデックス「最終更新」の相対表示（たった今／分前…）は <see cref="FileSearch.Messages.UserMessages"/> の文言に依存する。
/// </summary>
public static class DisplayFormatters
{
    /// <summary>バイト数を B / KB / MB で短く表示する。</summary>
    public static string FormatSize(long b) =>
        b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024} KB" : $"{b / 1048576.0:F1} MB";

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

    /// <summary>ファイル名の拡張子からプレビュー用のアイコン CSS クラス（word / excel / ppt / pdf / code / text）を返す。</summary>
    public static string GetFileIconClass(string name) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".doc" or ".docx" => "word",
            ".xls" or ".xlsx" or ".xlsm" => "excel",
            ".pptx" => "ppt",
            ".pdf" => "pdf",
            ".cs" or ".py" or ".pas" or ".dfm" or ".sql" or ".html" or ".xml" or ".css" => "code",
            _ => "text"
        };
}
