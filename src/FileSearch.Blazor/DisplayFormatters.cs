namespace FileSearch.Blazor;

/// <summary>
/// 日付・サイズ・ファイル種別・アイコン分類の表示用フォーマット。他コンポーネントから再利用する。
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
        if (!lastUpdate.HasValue) return "未実行";
        var diff = DateTime.Now - lastUpdate.Value;
        if (diff.TotalMinutes < 1) return "たった今";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}分前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}時間前";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}日前";
        return lastUpdate.Value.ToString("MM/dd HH:mm");
    }

    /// <summary>ファイル名の拡張子からプレビュー用のアイコン CSS クラス（word / excel / ppt / pdf / code / text）を返す。</summary>
    public static string GetFileIconClass(string name) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".doc" or ".docx" => "word",
            ".xls" or ".xlsx" => "excel",
            ".ppt" or ".pptx" => "ppt",
            ".pdf" => "pdf",
            ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".c" or ".h" or ".go" or ".rs" or ".rb" or ".php" or ".swift" or ".kt" or ".scala" or ".vb" or ".fs" => "code",
            _ => "text"
        };
}
