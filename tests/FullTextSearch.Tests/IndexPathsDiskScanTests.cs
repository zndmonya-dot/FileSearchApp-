using FullTextSearch.Core.Index;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary><see cref="IndexPaths.IsRepresentedInDiskScan"/> の照合。</summary>
public class IndexPathsDiskScanTests
{
    [Fact]
    public void IsRepresentedInDiskScan_matches_hard_linked_path()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "fts-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var primary = Path.Combine(tempDir, "a.txt");
        var alias = Path.Combine(tempDir, "b", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(alias)!);
        try
        {
            File.WriteAllText(primary, "x");
            File.WriteAllText(alias, "x");
            var stamp = DateTime.UtcNow.AddSeconds(-10);
            File.SetLastWriteTimeUtc(primary, stamp);
            File.SetLastWriteTimeUtc(alias, stamp);

            var ticks = new FileInfo(primary).LastWriteTimeUtc.Ticks;
            var disk = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                [IndexPaths.NormalizeFilePath(alias)] = ticks
            };

            Assert.True(IndexPaths.IsRepresentedInDiskScan(primary, disk));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }
}
