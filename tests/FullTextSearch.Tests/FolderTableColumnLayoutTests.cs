using FullTextSearch.Core.UI;
using Xunit;

namespace FullTextSearch.Tests;

public class FolderTableColumnLayoutTests
{
    [Fact]
    public void ResizeAdjacent_preserves_total_width()
    {
        var (name, preview) = FolderTableColumnLayout.ResizeAdjacent(
            200, 400, 50,
            FolderTableColumnLayout.ColMinName,
            FolderTableColumnLayout.ColMinPreview);
        Assert.Equal(600, name + preview);
        Assert.Equal(250, name);
        Assert.Equal(350, preview);
    }

    [Fact]
    public void FitToTable_does_not_reset_preview_when_not_absorbing_slack()
    {
        var (name, preview, date) = FolderTableColumnLayout.FitToTable(
            1000, 200, 500, 136, absorbSlackIntoPreview: false);
        Assert.Equal(200, name);
        Assert.Equal(500, preview);
        Assert.Equal(136, date);
    }

    [Fact]
    public void FitToTableTwoColumn_absorbs_slack_into_name_on_init()
    {
        var (name, date) = FolderTableColumnLayout.FitToTableTwoColumn(
            1000, 200, 136, absorbSlackIntoName: true);
        Assert.Equal(864, name);
        Assert.Equal(136, date);
    }
}
