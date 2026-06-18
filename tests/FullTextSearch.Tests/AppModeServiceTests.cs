using FullTextSearch.Core;
using FullTextSearch.Infrastructure.Settings;
using Xunit;

namespace FullTextSearch.Tests;

public class AppModeServiceTests
{
    [Fact]
    public void TrySaveSharedConfig_WritesToIndexFolder_WhenSharedConfigNotInAppMode()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppModeTests", Guid.NewGuid().ToString("N"));
        var indexPath = Path.Combine(root, "index");
        Directory.CreateDirectory(indexPath);

        var appModePath = Path.Combine(root, AppModeService.AppModeFileName);
        File.WriteAllText(appModePath, """{ "mode": "admin" }""");

        var service = new AppModeService(appModePath);
        service.Initialize();

        var saved = service.TrySaveSharedConfig(indexPath, ["C:\\docs"], [0, 6, 12]);

        Assert.True(saved);
        var sharedPath = Path.Combine(indexPath, DefaultPaths.SharedConfigFileName);
        Assert.True(File.Exists(sharedPath));
        Assert.Equal(sharedPath, service.ResolveSharedConfigPath(indexPath));
        Assert.Equal([0, 6, 12], service.SharedAutoRebuildDailyHours);
        var json = File.ReadAllText(sharedPath);
        Assert.Contains("\"autoRebuildDailyHours\"", json, StringComparison.OrdinalIgnoreCase);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void TryLoadSharedConfigFromIndexPath_LoadsSharedJsonInIndexFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppModeTests", Guid.NewGuid().ToString("N"));
        var indexPath = Path.Combine(root, "index");
        Directory.CreateDirectory(indexPath);

        var sharedPath = Path.Combine(indexPath, DefaultPaths.SharedConfigFileName);
        File.WriteAllText(sharedPath,
            """
            {
              "indexPath": "\\\\server\\index",
              "targetFolders": ["\\\\server\\docs"],
              "autoRebuildDailyHours": [0, 18]
            }
            """);

        var appModePath = Path.Combine(root, AppModeService.AppModeFileName);
        File.WriteAllText(appModePath, """{ "mode": "reference" }""");

        var service = new AppModeService(appModePath);
        service.Initialize();

        Assert.True(service.TryLoadSharedConfigFromIndexPath(indexPath));
        Assert.Equal(@"\\server\index", service.SharedIndexPath);
        Assert.Single(service.SharedTargetFolders);
        Assert.Equal([0, 18], service.SharedAutoRebuildDailyHours);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Initialize_MergesAutoRebuildDailyHours_FromSharedConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppModeTests", Guid.NewGuid().ToString("N"));
        var indexPath = Path.Combine(root, "index");
        Directory.CreateDirectory(indexPath);

        var sharedPath = Path.Combine(indexPath, DefaultPaths.SharedConfigFileName);
        File.WriteAllText(sharedPath,
            """
            {
              "indexPath": "\\\\server\\index",
              "targetFolders": ["\\\\server\\docs"],
              "autoRebuildDailyHours": [6, 12]
            }
            """);

        var appModePath = Path.Combine(root, AppModeService.AppModeFileName);
        File.WriteAllText(appModePath,
            $$"""
            {
              "mode": "reference",
              "sharedConfig": "{{sharedPath.Replace("\\", "\\\\")}}"
            }
            """);

        var service = new AppModeService(appModePath);
        service.Initialize();

        Assert.Equal([6, 12], service.SharedAutoRebuildDailyHours);

        Directory.Delete(root, recursive: true);
    }
}
