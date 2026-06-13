using FullTextSearch.Core.Index;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary><see cref="IndexDiffPlanner"/> の安全ガードと削除判定。</summary>
public class IndexDiffPlannerTests
{
    [Fact]
    public void Plan_aborts_when_scan_empty_but_indexed_files_still_exist()
    {
        var indexed = new Dictionary<string, IndexDiffPlanner.IndexedFileEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\data\\a.txt"] = new("C:\\data\\a.txt", 100, 0)
        };
        var disk = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var folders = new[] { "C:\\data" };

        var plan = IndexDiffPlanner.Plan(indexed, disk, folders, currentIndexVersion: 1, _ => true);

        Assert.True(plan.Aborted);
        Assert.Empty(plan.ToDeleteStoredPaths);
        Assert.Empty(plan.ToAddOrUpdatePaths);
    }

    [Fact]
    public void Plan_deletes_missing_file_not_in_disk_scan()
    {
        var indexed = new Dictionary<string, IndexDiffPlanner.IndexedFileEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\data\\gone.txt"] = new("C:\\data\\gone.txt", 100, 0)
        };
        var disk = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\data\\keep.txt"] = 200
        };
        var folders = new[] { "C:\\data" };

        var plan = IndexDiffPlanner.Plan(indexed, disk, folders, currentIndexVersion: 1, _ => false);

        Assert.False(plan.Aborted);
        Assert.Single(plan.ToDeleteStoredPaths, "C:\\data\\gone.txt");
    }

    [Fact]
    public void Plan_keeps_index_when_file_exists_but_not_in_scan()
    {
        var indexed = new Dictionary<string, IndexDiffPlanner.IndexedFileEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\data\\a.txt"] = new("C:\\data\\a.txt", 100, 0),
            ["C:\\data\\b.txt"] = new("C:\\data\\b.txt", 100, 0)
        };
        var disk = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\data\\a.txt"] = 100
        };
        var folders = new[] { "C:\\data" };

        var plan = IndexDiffPlanner.Plan(indexed, disk, folders, currentIndexVersion: 0, _ => true, _ => false);

        Assert.False(plan.Aborted);
        Assert.Empty(plan.ToDeleteStoredPaths);
        Assert.Empty(plan.ToAddOrUpdatePaths);
    }

    [Fact]
    public void Plan_deletes_when_file_exists_but_extension_excluded()
    {
        var indexed = new Dictionary<string, IndexDiffPlanner.IndexedFileEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\data\\old.bin"] = new("C:\\data\\old.bin", 100, 0)
        };
        var disk = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\data\\keep.txt"] = 200
        };
        var folders = new[] { "C:\\data" };

        var plan = IndexDiffPlanner.Plan(
            indexed,
            disk,
            folders,
            currentIndexVersion: 1,
            _ => true,
            path => path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));

        Assert.False(plan.Aborted);
        Assert.Single(plan.ToDeleteStoredPaths, "C:\\data\\old.bin");
    }

    [Fact]
    public void Plan_updates_when_index_version_differs()
    {
        var indexed = new Dictionary<string, IndexDiffPlanner.IndexedFileEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\data\\a.txt"] = new("C:\\data\\a.txt", 100, 0)
        };
        var disk = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\data\\a.txt"] = 100
        };
        var folders = new[] { "C:\\data" };

        var plan = IndexDiffPlanner.Plan(indexed, disk, folders, currentIndexVersion: 1, _ => true);

        Assert.False(plan.Aborted);
        Assert.Empty(plan.ToDeleteStoredPaths);
        Assert.Single(plan.ToAddOrUpdatePaths);
    }
}
