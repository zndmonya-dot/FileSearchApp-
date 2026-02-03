namespace FullTextSearch.Core.Preview;

/// <summary>
/// プレビュー用の拡張子・言語マップなどの定数とヘルパー
/// </summary>
public static class PreviewHelper
{
    /// <summary>拡張子 → Highlight.js 用言語名のマップ（シンタックスハイライト用）</summary>
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

    /// <summary>シンタックスハイライト対象の拡張子一覧（LanguageMap のキー）</summary>
    public static readonly HashSet<string> CodeExtensions = new(LanguageMap.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>拡張子から Highlight.js 用の言語名を取得する。未対応の場合は "plaintext"。</summary>
    public static string GetLanguage(string extension)
    {
        var ext = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        return LanguageMap.TryGetValue(ext, out var lang) ? lang : "plaintext";
    }

    /// <summary>指定拡張子がコードファイル（シンタックスハイライト対象）かどうか。</summary>
    public static bool IsCodeFile(string extension)
    {
        var ext = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        return CodeExtensions.Contains(ext);
    }

    /// <summary>拡張子を「.」+ 小文字に正規化（パスまたは拡張子文字列を受け取る）</summary>
    public static string NormalizeExtension(string extensionOrPath)
    {
        var raw = string.IsNullOrEmpty(extensionOrPath) ? "" : extensionOrPath.Trim();
        if (raw.Length > 0 && (raw.Contains(Path.DirectorySeparatorChar) || raw.Contains(Path.AltDirectorySeparatorChar)))
            raw = Path.GetExtension(raw);
        if (string.IsNullOrEmpty(raw)) return "";
        if (!raw.StartsWith(".", StringComparison.Ordinal)) raw = "." + raw;
        return raw.ToLowerInvariant();
    }
}
