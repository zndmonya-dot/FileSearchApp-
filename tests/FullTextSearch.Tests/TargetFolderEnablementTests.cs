using FullTextSearch.Core;
using Xunit;

namespace FullTextSearch.Tests;

public class TargetFolderEnablementTests
{
    [Fact]
    public void GetActiveFolders_excludes_disabled_paths()
    {
        var all = new[] { @"D:\Share\A", @"D:\Share\B" };
        var disabled = new List<string> { @"D:\Share\B" };

        var active = TargetFolderEnablement.GetActiveFolders(all, disabled);

        Assert.Single(active);
        Assert.Equal(@"D:\Share\A", active[0]);
    }

    [Fact]
    public void SetEnabled_adds_and_removes_disabled_entry()
    {
        var disabled = new List<string>();
        TargetFolderEnablement.SetEnabled(disabled, @"D:\Share\A", enabled: false);
        Assert.Single(disabled);
        TargetFolderEnablement.SetEnabled(disabled, @"D:\Share\A", enabled: true);
        Assert.Empty(disabled);
    }

    [Fact]
    public void PruneDisabled_removes_stale_paths()
    {
        var disabled = new List<string> { @"D:\Old", @"D:\Share\A" };
        var all = new[] { @"D:\Share\A" };

        TargetFolderEnablement.PruneDisabled(disabled, all);

        Assert.Single(disabled);
        Assert.Equal(@"D:\Share\A", disabled[0]);
    }
}
