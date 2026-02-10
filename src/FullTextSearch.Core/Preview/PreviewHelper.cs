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
}
