using FullTextSearch.Core.UI;
using Xunit;

namespace FullTextSearch.Tests;

public class LoadingEtaTests
{
    [Fact]
    public void TryEstimateRemaining_returns_null_when_too_few_processed()
    {
        var start = DateTime.UtcNow.AddSeconds(-10);
        Assert.Null(LoadingEta.TryEstimateRemaining(start, 2, 100));
    }

    [Fact]
    public void TryEstimateRemaining_estimates_from_rate()
    {
        var start = DateTime.UtcNow.AddSeconds(-10);
        var remaining = LoadingEta.TryEstimateRemaining(start, 10, 100);
        Assert.NotNull(remaining);
        Assert.InRange(remaining!.Value.TotalSeconds, 70, 110);
    }
}
