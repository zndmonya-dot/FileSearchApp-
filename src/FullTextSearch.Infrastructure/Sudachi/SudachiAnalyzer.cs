using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Util;

namespace FullTextSearch.Infrastructure.Sudachi;

/// <summary>
/// Sudachi（モード C）を用いた Lucene アナライザ。
/// SudachiPy をサブプロセスで呼び出すカスタム Tokenizer を使用する。
/// 半角英数字も検索できるようトークンを小文字に正規化する。
/// </summary>
public sealed class SudachiAnalyzer : Analyzer
{
    private const LuceneVersion MatchVersion = LuceneVersion.LUCENE_48;

    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        var tokenizer = new SudachiTokenizer(reader);
        var lowerCase = new LowerCaseFilter(MatchVersion, tokenizer);
        return new TokenStreamComponents(tokenizer, lowerCase);
    }
}
