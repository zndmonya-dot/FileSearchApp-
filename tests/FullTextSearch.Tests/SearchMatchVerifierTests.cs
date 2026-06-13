using FullTextSearch.Core.Search;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary><see cref="SearchMatchVerifier"/> の行単位 AND 検証。</summary>
public class SearchMatchVerifierTests
{
    [Fact]
    public void Matches_single_and_keyword_requires_contiguous_substring()
    {
        const string changelog = "AquesTalkライセンス文書\n別の行に情報あり";
        Assert.False(SearchMatchVerifier.Matches(changelog, "ChangeLog.txt", ["ライセンス情報"], SearchMode.Keyword));
        Assert.True(SearchMatchVerifier.Matches("ライセンス情報の説明", "a.txt", ["ライセンス情報"], SearchMode.Keyword));
    }

    [Fact]
    public void Matches_spaced_and_false_when_terms_on_different_lines()
    {
        const string content = "uses import module\nthen sys call";
        Assert.False(SearchMatchVerifier.Matches(content, "a.txt", ["import", "sys"], SearchMode.Keyword));
    }

    [Fact]
    public void Matches_spaced_and_true_when_all_terms_on_same_line()
    {
        const string content = "uses import module and sys call";
        Assert.True(SearchMatchVerifier.Matches(content, "a.txt", ["import", "sys"], SearchMode.Keyword));
    }

    [Fact]
    public void Matches_spaced_and_true_when_all_terms_in_filename()
    {
        var ok = SearchMatchVerifier.Matches("", "import_sys.txt", ["import", "sys"], SearchMode.Keyword);
        Assert.True(ok);
    }

    [Fact]
    public void Matches_single_or_keyword_requires_contiguous_substring()
    {
        Assert.True(SearchMatchVerifier.Matches("ライセンス情報の説明", "a.txt", ["ライセンス情報"], SearchMode.Any));
        Assert.False(SearchMatchVerifier.Matches("ライセンスと情報", "a.txt", ["ライセンス情報"], SearchMode.Any));
    }

    [Fact]
    public void Matches_any_true_when_one_term_on_one_line()
    {
        const string content = "AquesTalkライセンス文書\n別の行";
        var ok = SearchMatchVerifier.Matches(content, "a.txt", ["ライセンス", "情報"], SearchMode.Any);
        Assert.True(ok);
    }
}
