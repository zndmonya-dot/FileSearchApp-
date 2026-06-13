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

    [Fact]
    public void Plan_deletes_when_folder_removed_from_settings()
    {
        var indexed = new Dictionary<string, IndexDiffPlanner.IndexedFileEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\old\\keep.txt"] = new("C:\\old\\keep.txt", 100, 1)
        };
        var disk = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\new\\other.txt"] = 200
        };
        var folders = new[] { "C:\\new" };

        var plan = IndexDiffPlanner.Plan(indexed, disk, folders, currentIndexVersion: 1, _ => true);

        Assert.False(plan.Aborted);
        Assert.Single(plan.ToDeleteStoredPaths, "C:\\old\\keep.txt");
    }

    [Fact]
    public void Plan_keeps_when_same_physical_file_scanned_under_different_path_key()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "fts-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var primary = Path.Combine(tempDir, "doc.txt");
        var aliasDir = Path.Combine(tempDir, "alias");
        Directory.CreateDirectory(aliasDir);
        var alias = Path.Combine(aliasDir, "doc.txt");
        try
        {
            File.WriteAllText(primary, "sample");
            File.WriteAllText(alias, "sample");
            var stamp = DateTime.UtcNow.AddSeconds(-10);
            File.SetLastWriteTimeUtc(primary, stamp);
            File.SetLastWriteTimeUtc(alias, stamp);

            var primaryNorm = IndexPaths.NormalizeFilePath(primary);
            var aliasNorm = IndexPaths.NormalizeFilePath(alias);
            var ticks = new FileInfo(primary).LastWriteTimeUtc.Ticks;

            var indexed = new Dictionary<string, IndexDiffPlanner.IndexedFileEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [primaryNorm] = new(primary, ticks, 1)
            };
            var disk = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                [aliasNorm] = ticks
            };
            var folders = new[] { tempDir };

            var plan = IndexDiffPlanner.Plan(indexed, disk, folders, currentIndexVersion: 1, File.Exists);

            Assert.False(plan.Aborted);
            Assert.Empty(plan.ToDeleteStoredPaths);
            Assert.Single(plan.ToAddOrUpdatePaths, aliasNorm);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ShouldAbortWouldWipeIndex_true_when_all_deleted_without_reindex()
    {
        var indexed = new Dictionary<string, IndexDiffPlanner.IndexedFileEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\data\\a.txt"] = new("C:\\data\\a.txt", 100, 1)
        };
        var disk = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\data\\b.txt"] = 200
        };

        Assert.True(IndexDiffPlanner.ShouldAbortWouldWipeIndex(
            indexed,
            ["C:\\data\\a.txt"],
            [],
            disk));
    }
}
