namespace FullTextSearch.Infrastructure.Extractors;

/// <summary>抽出器が対応する拡張子の定義（設定ピッカー・インデックス対象の基準）。</summary>
internal static class SupportedExtensionSets
{
    /// <summary>Office。</summary>
    internal static readonly HashSet<string> Office = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx",
        ".xls", ".xlsx", ".xlsm",
        ".pptx",
    };

    /// <summary>PDF。</summary>
    internal static readonly HashSet<string> Pdf = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
    };

    /// <summary>Outlook メール。</summary>
    internal static readonly HashSet<string> OutlookMsg = new(StringComparer.OrdinalIgnoreCase)
    {
        ".msg",
    };

    /// <summary>プレーンテキスト・スクリプト。</summary>
    internal static readonly HashSet<string> TextAndScript = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".csv",
        ".bat", ".ps1", ".sh",
    };

    /// <summary>ソースコード・マークアップ・設定ファイル。</summary>
    internal static readonly HashSet<string> SourceCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".xml",
        ".cs", ".java",
        ".dfm", ".pas", ".dpr", ".dpk",
        ".ini", ".env",
        ".py", ".css", ".sql",
    };

    internal static readonly HashSet<string> TextFile = new(StringComparer.OrdinalIgnoreCase);
    static SupportedExtensionSets()
    {
        TextFile.UnionWith(TextAndScript);
        TextFile.UnionWith(SourceCode);
    }
}
