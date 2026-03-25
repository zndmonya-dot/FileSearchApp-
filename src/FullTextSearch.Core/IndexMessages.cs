// インデックス処理・スキップログ用の文言（UI の UserMessages と分離）。
namespace FullTextSearch.Core;

/// <summary>
/// インデックス処理・スキップログなど Core / Infrastructure が扱うユーザー向け（またはユーザーが開くログ）の文言。
/// UI の <c>FileSearch.Messages.UserMessages</c> と役割を分け、レイヤ横断で参照する。
/// </summary>
/// <remarks>
/// 変更時は <c>docs/メッセージ一覧.md</c> の「ファイル出力・ログ」節と、<c>LuceneIndexService</c> の呼び出し元を確認する。
/// </remarks>
public static class IndexMessages
{
    /// <summary>スキップログ 1 行目（<c>skipped_files.log</c>）</summary>
    public static string SkippedLogHeaderLine(DateTime timestampUtcOrLocal) =>
        $"スキップファイル一覧 - {timestampUtcOrLocal:yyyy-MM-dd HH:mm:ss}";

    /// <summary>スキップログ 2 行目</summary>
    public static string SkippedLogTotalLine(int count) => $"合計: {count} 件";

    /// <summary>拡張子から Lucene の filetype フィールド用の表示名を返す。</summary>
    public static string GetFileTypeDisplayName(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".docx" => "Word文書",
            ".xlsx" => "Excelブック",
            ".pptx" => "PowerPointプレゼンテーション",
            ".pdf" => "PDFファイル",
            ".txt" => "テキストファイル",
            ".csv" => "CSVファイル",
            ".log" => "ログファイル",
            ".md" => "Markdownファイル",
            ".cs" => "C#ソースコード",
            ".js" => "JavaScriptファイル",
            ".ts" => "TypeScriptファイル",
            ".py" => "Pythonファイル",
            ".java" => "Javaファイル",
            ".html" => "HTMLファイル",
            ".css" => "CSSファイル",
            ".xml" => "XMLファイル",
            ".json" => "JSONファイル",
            ".yaml" or ".yml" => "YAMLファイル",
            ".pas" or ".dpr" or ".dpk" => "Pascal/Delphi",
            _ => "ファイル"
        };
    }
}
