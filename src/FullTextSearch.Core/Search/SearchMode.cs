namespace FullTextSearch.Core.Search;

/// <summary>
/// 検索モード。UI のラジオボタンと <see cref="SearchOptions.SearchMode"/> で指定する。
/// </summary>
public enum SearchMode
{
    /// <summary>スペースなし＝1語の部分一致。スペースあり＝各語が同一行（またはファイル名）にすべて含まれる。</summary>
    Keyword,

    /// <summary>入力文字列が本文・ファイル名に連続してそのまま含まれる（Lucene トークン検索は使わない）。</summary>
    Phrase,

    /// <summary>スペース区切り OR・部分一致。1語は入力全体を1キーワードとして部分一致。</summary>
    Any,
}
