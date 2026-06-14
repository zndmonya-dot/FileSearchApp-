// 全文検索のインターフェースと検索オプション・結果型。
using FullTextSearch.Core.Models;

namespace FullTextSearch.Core.Search;

/// <summary>
/// 検索サービスのインターフェース
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// 全文検索を実行
    /// </summary>
    /// <param name="query">検索クエリ</param>
    /// <param name="options">検索オプション</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検索結果</returns>
    Task<SearchResult> SearchAsync(string query, SearchOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// インデックスキャッシュを破棄し、次回検索で最新のインデックスを読み直す（パス変更時などに使用）。
    /// </summary>
    void RefreshIndex();

    /// <summary>
    /// 検索用の Reader / Analyzer を事前に用意し、初回検索の遅延を軽減する。
    /// </summary>
    void Warmup();

    /// <summary>
    /// インデックスに格納された本文を取得する。未登録の場合は null。
    /// メタデータのみ登録（本文空）の場合は空文字列。
    /// </summary>
    Task<string?> TryGetStoredContentAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>インデックスから本文抜粋をまとめて取得する。searchQuery 指定時はマッチ行、未指定時は先頭行。</summary>
    Task<IReadOnlyDictionary<string, string>> TryGetContentPreviewsAsync(
        IReadOnlyList<string> filePaths,
        string? searchQuery = null,
        SearchMode searchMode = SearchMode.Keyword,
        CancellationToken cancellationToken = default);

    /// <summary>プレビューハイライト用の検索語。</summary>
    IReadOnlyList<string> GetHighlightTerms(string query, SearchMode mode);
}

/// <summary>
/// 検索オプション
/// </summary>
public record SearchOptions
{
    /// <summary>
    /// 最大取得件数
    /// </summary>
    public int MaxResults { get; init; } = 1000;

    /// <summary>
    /// 検索モード（キーワード AND / 語句 / いずれか OR）
    /// </summary>
    public SearchMode SearchMode { get; init; } = SearchMode.Keyword;
}

/// <summary>
/// 検索結果
/// </summary>
public class SearchResult
{
    /// <summary>
    /// 検索結果のリスト
    /// </summary>
    public List<SearchResultItem> Items { get; init; } = [];
}


