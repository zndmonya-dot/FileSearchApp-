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

    /// <summary>スキップログ 3 行目（列形式の説明）</summary>
    public const string SkippedLogFormatHint = "（1行1件: パス[TAB]理由）";

    /// <summary>スキップログ 1 行分（パスと理由をタブ区切り）</summary>
    public static string SkippedLogLine(string path, string reason) => $"{path}\t{reason}";

    /// <summary>ファイルサイズ超過</summary>
    public static string SkippedReasonFileTooLarge(long fileSizeBytes) =>
        $"{ContentLimits.GetIndexMaxFileBytesDisplayLabel()}を超えるため（{fileSizeBytes:N0} バイト）";

    /// <summary>テキスト抽出失敗</summary>
    public const string SkippedReasonExtractFailed = "テキスト抽出に失敗";

    /// <summary>Lucene 登録失敗</summary>
    public const string SkippedReasonIndexWriteFailed = "インデックスへの登録に失敗";

    /// <summary>ファイル不存在</summary>
    public const string SkippedReasonFileNotFound = "ファイルが存在しない";

    /// <summary>アクセス拒否</summary>
    public const string SkippedReasonAccessDenied = "アクセス権限がない";

    /// <summary>差分更新を安全のため中止（スキャン0件だがファイルはディスク上に残存）</summary>
    public static string DiffAbortedFilesStillExistOnDisk(int indexedCount) =>
        $"安全のため差分更新を中止しました。インデックス済み {indexedCount:N0} 件はディスク上に残っていますが、スキャン結果が 0 件でした。対象フォルダ・拡張子を確認し、「全体を再構築」を実行してください。";

    /// <summary>差分更新を安全のため中止（処理後にインデックスが空になった）</summary>
    public static string DiffAbortedResultEmpty(int previousCount) =>
        $"安全のため差分更新を中止しました。処理後にインデックスが 0 件になるため変更を破棄しました（更新前 {previousCount:N0} 件）。「全体を再構築」を実行してください。";

    /// <summary>差分更新を安全のため中止（スキャン結果があるのに全件削除のみとなる）</summary>
    public static string DiffAbortedWouldWipeIndex(int indexedCount) =>
        $"安全のため差分更新を中止しました。インデックス済み {indexedCount:N0} 件をすべて削除する計画になったため中止しました。対象フォルダ・拡張子を確認し、「全体を再構築」を実行してください。";
}
