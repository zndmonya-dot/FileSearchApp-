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

        var saved = service.TrySaveSharedConfig(indexPath, ["C:\\docs"]);

        Assert.True(saved);
        var sharedPath = Path.Combine(indexPath, DefaultPaths.SharedConfigFileName);
        Assert.True(File.Exists(sharedPath));
        Assert.Equal(sharedPath, service.ResolveSharedConfigPath(indexPath));

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
              "targetFolders": ["\\\\server\\docs"]
            }
            """);

        var appModePath = Path.Combine(root, AppModeService.AppModeFileName);
        File.WriteAllText(appModePath, """{ "mode": "reference" }""");

        var service = new AppModeService(appModePath);
        service.Initialize();

        Assert.True(service.TryLoadSharedConfigFromIndexPath(indexPath));
        Assert.Equal(@"\\server\index", service.SharedIndexPath);
        Assert.Single(service.SharedTargetFolders);

        Directory.Delete(root, recursive: true);
    }
}
