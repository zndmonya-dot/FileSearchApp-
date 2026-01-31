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
    /// ファイル種類フィルター（null = すべて）
    /// </summary>
    public List<string>? FileTypeFilter { get; init; }

    /// <summary>
    /// 日付範囲（開始）
    /// </summary>
    public DateTime? DateFrom { get; init; }

    /// <summary>
    /// 日付範囲（終了）
    /// </summary>
    public DateTime? DateTo { get; init; }

    /// <summary>
    /// フォルダフィルター（null = すべて）
    /// </summary>
    public string? FolderFilter { get; init; }

    /// <summary>
    /// ハイライトをスキップする（一覧のみでプレビュー不要な場合の高速化）
    /// </summary>
    public bool SkipHighlights { get; init; }
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

    /// <summary>
    /// 総ヒット件数
    /// </summary>
    public int TotalHits { get; init; }

    /// <summary>
    /// 検索にかかった時間（ミリ秒）
    /// </summary>
    public long ElapsedMilliseconds { get; init; }

    /// <summary>
    /// 検索クエリ
    /// </summary>
    public required string Query { get; init; }
}


