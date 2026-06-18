// プレビュー・設定で共有する拡張子の正規化。
namespace FullTextSearch.Core.Preview;

/// <summary>
/// プレビュー用の拡張子ヘルパー。
/// </summary>
public static class PreviewHelper
{
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

    /// <summary>抽出器対応拡張子に、設定の TargetExtensions を適用した集合を返す。</summary>
    public static HashSet<string> BuildTargetExtensionSet(
        IEnumerable<string> supportedExtensions,
        IReadOnlyList<string>? targetExtensions = null)
    {
        var allowed = supportedExtensions
            .Select(NormalizeExtension)
            .Where(e => !string.IsNullOrEmpty(e))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (targetExtensions is not { Count: > 0 })
            return allowed;

        var filtered = targetExtensions
            .Select(NormalizeExtension)
            .Where(e => !string.IsNullOrEmpty(e) && allowed.Contains(e))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return filtered.Count > 0 ? filtered : allowed;
    }

    /// <summary>ファイル名の拡張子からプレビュー用のアイコン CSS クラス（word / excel / ppt / pdf / code / text）を返す。</summary>
    public static string GetFileIconClass(string name) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".doc" or ".docx" => "word",
            ".xls" or ".xlsx" or ".xlsm" => "excel",
            ".pptx" => "ppt",
            ".pdf" => "pdf",
            ".msg" => "text",
            ".cs" or ".java" or ".py" or ".pas" or ".dfm" or ".sql" or ".html" or ".xml" or ".css" => "code",
            _ => "text"
        };
}
