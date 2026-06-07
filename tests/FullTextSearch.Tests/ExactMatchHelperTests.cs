using FullTextSearch.Infrastructure.Lucene;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary>ExactMatchHelper の文字列完全一致判定。</summary>
public class ExactMatchHelperTests
{
    [Fact]
    public void ContainsLiteral_finds_spaced_phrase_in_content()
    {
        Assert.True(ExactMatchHelper.ContainsLiteral("line with import sys call", "import sys"));
    }

    [Fact]
    public void ContainsLiteral_normalizes_full_width_space()
    {
        Assert.True(ExactMatchHelper.ContainsLiteral("a\u3000b", "a b"));
    }

    [Fact]
    public void ContainsLiteral_is_case_insensitive_for_ascii()
    {
        Assert.True(ExactMatchHelper.ContainsLiteral("IMPORT SYS", "import sys"));
    }

    [Fact]
    public void ContainsLiteral_does_not_match_import_or_sys_only()
    {
        Assert.False(ExactMatchHelper.ContainsLiteral("uses import module", "import sys"));
        Assert.False(ExactMatchHelper.ContainsLiteral("calls sys.exit()", "import sys"));
        Assert.False(ExactMatchHelper.ContainsLiteral("import and sys in separate sentences", "import sys"));
    }

    [Fact]
    public void ContainsLiteral_does_not_match_partial_and_keyword()
    {
        Assert.False(ExactMatchHelper.ContainsLiteral("importsystem", "import sys"));
    }

    [Fact]
    public void MatchesContentOrFileName_checks_file_name()
    {
        Assert.True(ExactMatchHelper.MatchesContentOrFileName("", "report import sys.txt", "import sys"));
    }
}
