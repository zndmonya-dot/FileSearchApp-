using FileSearch.Messages;
using Xunit;

namespace FullTextSearch.Tests;

public class BootThemeResolverTests
{
    [Theory]
    [InlineData("Light", false, "light")]
    [InlineData("light", true, "light")]
    [InlineData("Dark", true, "dark")]
    [InlineData("Chameleon", false, "dark")]
    [InlineData("System", true, "light")]
    [InlineData("System", false, "dark")]
    [InlineData(null, false, "dark")]
    public void ResolveBootTheme_maps_theme_mode(string? themeMode, bool systemPrefersLight, string expected)
    {
        Assert.Equal(expected, BootThemeResolver.ResolveBootTheme(themeMode, systemPrefersLight));
    }
}
