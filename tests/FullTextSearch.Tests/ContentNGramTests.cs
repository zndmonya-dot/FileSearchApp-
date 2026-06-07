using FullTextSearch.Infrastructure.Lucene;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary>完全一致候補絞り込み用バイグラム生成の検証。</summary>
public class ContentNGramTests
{
    [Fact]
    public void BuildQueryGrams_returns_empty_for_single_char()
    {
        Assert.Empty(ContentNGram.BuildQueryGrams("a"));
        Assert.Empty(ContentNGram.BuildQueryGrams(""));
        Assert.Empty(ContentNGram.BuildQueryGrams(null));
    }

    [Fact]
    public void BuildQueryGrams_splits_into_adjacent_bigrams()
    {
        var grams = ContentNGram.BuildQueryGrams("abcd");
        Assert.Equal(new[] { "ab", "bc", "cd" }, grams.OrderBy(g => g));
    }

    [Fact]
    public void BuildQueryGrams_is_lowercased()
    {
        Assert.Contains("im", ContentNGram.BuildQueryGrams("Import"));
        Assert.DoesNotContain("Im", ContentNGram.BuildQueryGrams("Import"));
    }

    [Fact]
    public void IndexTokens_superset_contains_query_grams_for_substring()
    {
        // 「東京都」を索引した文書は、部分文字列「京都」のバイグラムをすべて含む（取りこぼさない）。
        var indexTokens = ContentNGram.BuildIndexTokens("これは東京都の文書です", "report.txt");
        var queryGrams = ContentNGram.BuildQueryGrams("京都");

        Assert.NotEmpty(queryGrams);
        Assert.All(queryGrams, g => Assert.Contains(g, indexTokens));
    }

    [Fact]
    public void IndexTokens_include_file_name_grams()
    {
        var indexTokens = ContentNGram.BuildIndexTokens("", "import_sys.py");
        var queryGrams = ContentNGram.BuildQueryGrams("sys");
        Assert.All(queryGrams, g => Assert.Contains(g, indexTokens));
    }

    [Fact]
    public void IndexTokens_superset_holds_for_spaced_phrase()
    {
        var indexTokens = ContentNGram.BuildIndexTokens("line with import sys call", "");
        var queryGrams = ContentNGram.BuildQueryGrams("import sys");
        Assert.All(queryGrams, g => Assert.Contains(g, indexTokens));
    }
}
