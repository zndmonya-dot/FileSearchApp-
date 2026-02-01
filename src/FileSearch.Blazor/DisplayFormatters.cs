namespace FileSearch.Blazor;

/// <summary>
/// 日付・サイズ・ファイル種別・アイコン分類の表示用フォーマット。他コンポーネントから再利用する。
/// </summary>
public static class DisplayFormatters
{
    public static string FormatSize(long b) =>
        b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024} KB" : $"{b / 1048576.0:F1} MB";

    public static string FormatDate(DateTime d) =>
        d.ToLocalTime().ToString("yyyy/MM/dd HH:mm");

    public static string FormatRelativeDate(DateTime d)
    {
        var local = d.ToLocalTime();
        var diff = DateTime.Now - local;
        if (diff.TotalMinutes < 1) return "たった今";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}分前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}時間前";
        if (diff.TotalDays < 2) return "昨日";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}日前";
        if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)}週間前";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}ヶ月前";
        return local.ToString("yyyy/MM/dd");
    }

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

    public static string GetFileExt(string name) =>
        Path.GetExtension(name).TrimStart('.').ToUpperInvariant();

    public static string GetFileType(string name) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".doc" or ".docx" => "Word文書",
            ".xls" or ".xlsx" => "Excelブック",
            ".ppt" or ".pptx" => "PowerPoint",
            ".pdf" => "PDF",
            ".txt" => "テキスト",
            ".csv" => "CSV",
            ".md" => "Markdown",
            ".json" => "JSON",
            ".xml" => "XML",
            ".html" or ".htm" => "HTML",
            ".css" => "CSS",
            ".js" => "JavaScript",
            ".ts" => "TypeScript",
            ".cs" => "C#",
            ".py" => "Python",
            ".java" => "Java",
            ".cpp" or ".c" or ".h" => "C/C++",
            ".go" => "Go",
            ".rs" => "Rust",
            ".rb" => "Ruby",
            ".php" => "PHP",
            ".swift" => "Swift",
            ".kt" => "Kotlin",
            ".sql" => "SQL",
            ".sh" => "Shell",
            ".ps1" => "PowerShell",
            ".yaml" or ".yml" => "YAML",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => "画像",
            ".mp3" or ".wav" or ".flac" => "音声",
            ".mp4" or ".avi" or ".mov" => "動画",
            ".zip" or ".rar" or ".7z" => "圧縮",
            _ => Path.GetExtension(name).TrimStart('.').ToUpperInvariant() + "ファイル"
        };
}
