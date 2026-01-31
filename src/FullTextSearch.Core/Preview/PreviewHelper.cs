namespace FullTextSearch.Core.Preview;

/// <summary>
/// プレビュー用の拡張子・言語マップなどの定数とヘルパー
/// </summary>
public static class PreviewHelper
{
    public static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf" };

    /// <summary>画像ファイルの拡張子（プレビューで img 表示するもの）</summary>
    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".svg" };

    public static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".csv", ".log", ".md", ".json", ".xml", ".yaml", ".yml", ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h",
        ".pas", ".dpr", ".dpk",
        ".html", ".css", ".sql", ".sh", ".bat", ".ps1", ".ini", ".cfg", ".config"
    };

    public static readonly IReadOnlyDictionary<string, string> LanguageMap = new Dictionary<string, string>
    {
        { ".cs", "csharp" }, { ".js", "javascript" }, { ".ts", "typescript" }, { ".py", "python" },
        { ".java", "java" }, { ".cpp", "cpp" }, { ".c", "c" }, { ".h", "cpp" }, { ".go", "go" },
        { ".rs", "rust" }, { ".rb", "ruby" }, { ".php", "php" }, { ".swift", "swift" }, { ".kt", "kotlin" },
        { ".scala", "scala" }, { ".vb", "vb" }, { ".fs", "fsharp" }, { ".html", "html" }, { ".htm", "html" },
        { ".css", "css" }, { ".scss", "scss" }, { ".less", "less" }, { ".xml", "xml" }, { ".json", "json" },
        { ".yaml", "yaml" }, { ".yml", "yaml" }, { ".sql", "sql" }, { ".sh", "bash" }, { ".bat", "batch" },
        { ".ps1", "powershell" }, { ".md", "markdown" }, { ".jsx", "jsx" }, { ".tsx", "tsx" },
        { ".vue", "xml" }, { ".r", "r" }, { ".m", "objectivec" }, { ".lua", "lua" }, { ".pl", "perl" },
        { ".pas", "delphi" }, { ".dpr", "delphi" }, { ".dpk", "delphi" }
    };

    public static readonly HashSet<string> CodeExtensions = new(LanguageMap.Keys, StringComparer.OrdinalIgnoreCase);

    public static string GetLanguage(string extension)
    {
        var ext = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        return LanguageMap.TryGetValue(ext, out var lang) ? lang : "plaintext";
    }

    public static bool IsCodeFile(string extension)
    {
        var ext = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        return CodeExtensions.Contains(ext);
    }

    public static bool IsOfficeFile(string extension)
    {
        var ext = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        return OfficeExtensions.Contains(ext);
    }

    public static bool IsTextFile(string extension)
    {
        var ext = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        return TextExtensions.Contains(ext);
    }

    public static bool IsImageFile(string extension)
    {
        var ext = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        return ImageExtensions.Contains(ext);
    }

    /// <summary>Data URL 用の MIME タイプ</summary>
    public static string GetImageMimeType(string extension)
    {
        var ext = (extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}
