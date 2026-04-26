// 本文・ファイルサイズの上限を一元定義。Lucene の UTF-8 トークン制限（32766 バイト）やメモリ対策のため。

namespace FullTextSearch.Core;

/// <summary>
/// コンテンツ関連の上限値（インデックス・プレビュー・抽出で共通参照）。
/// Lucene は 1 トークンあたり UTF-8 で最大 32766 バイト（公式上限）。これを超えると "immense term" で例外になるため、
/// 32765 バイト以下に抑え、分割時は必ず UTF-8 の文字境界で切るようにしている。
/// </summary>
public static class ContentLimits
{
    /// <summary>Lucene の 1 トークンあたり最大バイト数（UTF-8）。公式上限 32766 を超えないよう 32765 に設定。これを超えるトークンは文字境界で分割する。</summary>
    public const int LuceneMaxTermUtf8Bytes = 32765;

    /// <summary>1 ドキュメントあたりインデックスに格納する最大文字数。超えた分は打ち切り。</summary>
    public const int IndexMaxContentChars = 100_000;

    /// <summary>このサイズ（バイト）を超えるファイルは本文抽出せず、パス・ファイル名等のメタデータのみインデックスする。プレビュー・テキスト読み込み上限（各 10MB）と同じしきい値に揃えている。</summary>
    public static readonly long IndexMaxFileBytesForExtract = 10L * 1024 * 1024; // 10MB

    /// <summary>
    /// 本文抽出（全文読込）に対象にしないサイズとみなすか。要件 REQ-2.5。判定は厳密に <b>閾値より大きい</b>場合のみ true
    /// （ちょうど 10,485,760 バイト＝ 10MB のファイルは本文抽出の対象。インデックスのスキップロジックと同じ式）。
    /// </summary>
    public static bool ExceedsIndexTextExtractionFileSizeLimit(long fileSizeBytes) =>
        fileSizeBytes > IndexMaxFileBytesForExtract;

    /// <summary>抽出器が返す最大文字数（Office/PDF）。これ以上は打ち切る。</summary>
    public const int ExtractMaxChars = 100_000;

    /// <summary>プレビューを試みる最大ファイルサイズ（バイト）。超える場合は抽出せずエラー表示。</summary>
    public static readonly long PreviewMaxFileBytes = 10L * 1024 * 1024; // 10MB

    /// <summary>テキストファイルとして読み込む最大ファイルサイズ（バイト）。超えると読み込まない。</summary>
    public static readonly long MaxTextFileBytesToRead = 10L * 1024 * 1024; // 10MB

    /// <summary>プレビュー表示に使う最大文字数。超えた分は省略表示。</summary>
    public const int PreviewMaxChars = 50_000;

    /// <summary>シンタックスハイライトをかける最大行数（それ以降はエスケープのみ）。プレビュー最大文字数と同程度にし、途中でハイライトが切れないようにする。</summary>
    public const int PreviewMaxLinesForHighlight = 50_000;

    /// <summary>プレビュー行の高さ（px）。Virtualize の ItemSize と CSS の line-height で揃える。</summary>
    public const int PreviewLineHeightPx = 20;
}
