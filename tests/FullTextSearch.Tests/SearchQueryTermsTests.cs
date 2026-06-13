using FullTextSearch.Core.Search;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary><see cref="SearchQueryTerms"/> の語分割。</summary>
public class SearchQueryTermsTests
{
    [Fact]
    public void GetTerms_without_space_is_single_keyword()
    {
        var terms = SearchQueryTerms.GetTerms("ライセンス情報");
        Assert.Single(terms);
        Assert.Equal("ライセンス情報", terms[0]);
    }

    [Fact]
    public void GetTerms_with_space_splits_words()
    {
        var terms = SearchQueryTerms.GetTerms("import sys");
        Assert.Equal(2, terms.Count);
        Assert.Equal("import", terms[0]);
        Assert.Equal("sys", terms[1]);
    }

    [Fact]
    public void IsSingleKeyword_true_when_no_space()
    {
        Assert.True(SearchQueryTerms.IsSingleKeyword("ライセンス情報"));
        Assert.False(SearchQueryTerms.IsSingleKeyword("契約 見積"));
    }
}
