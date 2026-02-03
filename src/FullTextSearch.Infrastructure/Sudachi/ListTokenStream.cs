using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;

namespace FullTextSearch.Infrastructure.Sudachi;

/// <summary>
/// 既に取得したトークン列をそのまま返す TokenStream。検索ハイライトの一括トークン化結果を Highlighter に渡すために使用。
/// </summary>
public sealed class ListTokenStream : TokenStream
{
    private readonly ICharTermAttribute _termAttr;
    private readonly IReadOnlyList<string> _tokens;
    private int _index;

    /// <summary>事前に取得したトークン列を Lucene の TokenStream として返すために使用する。</summary>
    public ListTokenStream(IReadOnlyList<string> tokens)
        : base(AttributeFactory.DEFAULT_ATTRIBUTE_FACTORY)
    {
        _termAttr = AddAttribute<ICharTermAttribute>();
        _tokens = tokens ?? [];
    }

    public override bool IncrementToken()
    {
        ClearAttributes();
        if (_tokens == null || _index >= _tokens.Count)
            return false;
        var term = _tokens[_index++];
        if (string.IsNullOrEmpty(term))
            return IncrementToken();
        _termAttr.SetEmpty().Append(term);
        return true;
    }

    public override void Reset()
    {
        base.Reset();
        _index = 0;
    }
}
