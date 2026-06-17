// ファイルサイズ上限と Lucene トークン制限。

namespace FullTextSearch.Core;

/// <summary>
/// コンテンツ関連の上限値。インデックス・プレビュー・抽出で共通参照。
/// </summary>
/// <remarks>
/// <para><b>方針:</b> ファイルサイズが <see cref="EffectiveIndexMaxFileBytes"/> 以下なら、
/// 抽出した本文を<b>全文</b>インデックス・検索（Sudachi）・プレビュー表示する。文字数での打ち切りは行わない。</para>
/// <para>サイズ上限は <see cref="ConfigureIndexMaxFileBytes"/> で変更可能（shared.json / 設定画面）。</para>
/// </remarks>
public static class ContentLimits
{
    /// <summary>既定のファイルサイズ上限（10MB）。</summary>
    public const long DefaultIndexMaxFileBytes = 10L * 1024 * 1024;

    private static long _effectiveIndexMaxFileBytes = DefaultIndexMaxFileBytes;

    /// <summary>本文抽出・インデックス・テキスト読込・プレビュー試行の有効なファイルサイズ上限（バイト）。</summary>
    public static long EffectiveIndexMaxFileBytes => _effectiveIndexMaxFileBytes;

    /// <summary>テキストファイル読込上限。<see cref="EffectiveIndexMaxFileBytes"/> と同値。</summary>
    public static long MaxTextFileBytesToRead => EffectiveIndexMaxFileBytes;

    /// <summary>後方互換の定数名。</summary>
    public static long IndexMaxFileBytesForExtract => DefaultIndexMaxFileBytes;

    /// <summary>
    /// インデックス対象の最大ファイルサイズを設定する。
    /// <paramref name="bytes"/> が null のとき既定 10MB、0 以下のとき無制限（スキップなし）。
    /// </summary>
    public static void ConfigureIndexMaxFileBytes(long? bytes)
    {
        _effectiveIndexMaxFileBytes = bytes switch
        {
            null => DefaultIndexMaxFileBytes,
            <= 0 => long.MaxValue,
            _ => bytes.Value
        };
    }

    /// <summary>スキップ理由・UI 用のファイルサイズ上限表示。</summary>
    public static string GetIndexMaxFileBytesDisplayLabel()
    {
        var bytes = EffectiveIndexMaxFileBytes;
        if (bytes >= long.MaxValue / 2)
            return "制限なし";
        if (bytes % (1024 * 1024) == 0)
            return $"{bytes / (1024 * 1024)}MB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):0.##}MB";
        if (bytes >= 1024)
            return $"{bytes / 1024}KB";
        return $"{bytes}B";
    }

    /// <summary>
    /// 本文抽出対象外か。判定は<b>閾値より大きい</b>場合のみ true（ちょうど上限は対象）。
    /// </summary>
    public static bool ExceedsIndexTextExtractionFileSizeLimit(long fileSizeBytes) =>
        fileSizeBytes > EffectiveIndexMaxFileBytes;

    /// <summary>Lucene 1 トークンあたり最大 UTF-8 バイト数（公式上限 32766 未満）。</summary>
    public const int LuceneMaxTermUtf8Bytes = 32765;

    /// <summary>Sudachi へ渡す 1 チャンクあたりの最大文字数（メモリ・ピーク抑制。境界は改行優先）。</summary>
    public const int SudachiTokenizeChunkChars = 100_000;

    /// <summary>フォルダ一覧のファイル名横に表示する先頭行プレビューの最大文字数。</summary>
    public const int FolderListPreviewMaxChars = 80;

    /// <summary>検索結果の件数上限なし（Lucene コレクタ用の実用上限）。</summary>
    public const int UnlimitedSearchResults = int.MaxValue / 4;
}
