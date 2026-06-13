// ファイルプレビュー取得のインターフェース。パスと検索語からプレビュー結果を返す。
namespace FullTextSearch.Core.Preview;

using FullTextSearch.Core.Models;
using FullTextSearch.Core.Search;

/// <summary>
/// ファイルプレビュー取得サービス。指定パスの内容を行テキストで返す。
/// </summary>
public interface IPreviewService
{
    /// <summary>
    /// 指定パスのプレビューを取得する。
    /// </summary>
    /// <param name="path">ファイルパス</param>
    /// <param name="searchQuery">検索クエリ（ハイライト用、省略可）</param>
    /// <param name="cancellationToken">キャンセル</param>
    /// <param name="searchMode">検索モード（ハイライト分割に使用）</param>
    /// <returns>プレビュー結果</returns>
    Task<PreviewResult> GetPreviewAsync(
        string path,
        string? searchQuery,
        CancellationToken cancellationToken = default,
        SearchMode searchMode = SearchMode.Keyword);
}
