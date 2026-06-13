// ファイルサイズ上限（10MB）と Lucene トークン制限。

namespace FullTextSearch.Core;

/// <summary>
/// コンテンツ関連の上限値。インデックス・プレビュー・抽出で共通参照。
/// </summary>
/// <remarks>
/// <para><b>方針:</b> ファイルサイズが <see cref="IndexMaxFileBytesForExtract"/>（10MB）以下なら、
/// 抽出した本文を<b>全文</b>インデックス・検索（Sudachi）・プレビュー表示する。文字数での打ち切りは行わない。</para>
/// <para>Lucene は 1 トークンあたり UTF-8 最大 32766 バイト。超える語は <see cref="LuceneMaxTermUtf8Bytes"/> で分割。</para>
/// </remarks>
public static class ContentLimits
{
    /// <summary>本文抽出・インデックス・テキスト読込・プレビュー試行のファイルサイズ上限（バイト）。超過分はスキップ。</summary>
    public static readonly long IndexMaxFileBytesForExtract = 10L * 1024 * 1024; // 10MB

    /// <summary>テキストファイル読込上限。<see cref="IndexMaxFileBytesForExtract"/> と同値。</summary>
    public static readonly long MaxTextFileBytesToRead = IndexMaxFileBytesForExtract;

    /// <summary>スキップ理由・UI 用のファイルサイズ上限表示。</summary>
    public const string IndexMaxFileBytesDisplayLabel = "10MB";

    /// <summary>
    /// 本文抽出対象外か。REQ-2.5。判定は<b>閾値より大きい</b>場合のみ true（ちょうど 10MB は対象）。
    /// </summary>
    public static bool ExceedsIndexTextExtractionFileSizeLimit(long fileSizeBytes) =>
        fileSizeBytes > IndexMaxFileBytesForExtract;

    /// <summary>Lucene 1 トークンあたり最大 UTF-8 バイト数（公式上限 32766 未満）。</summary>
    public const int LuceneMaxTermUtf8Bytes = 32765;

    /// <summary>Sudachi へ渡す 1 チャンクあたりの最大文字数（メモリ・ピーク抑制。境界は改行優先）。</summary>
    public const int SudachiTokenizeChunkChars = 100_000;
}
