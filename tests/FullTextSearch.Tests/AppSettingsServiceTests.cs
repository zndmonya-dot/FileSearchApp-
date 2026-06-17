using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using FullTextSearch.Infrastructure.Settings;
using Xunit;

namespace FullTextSearch.Tests;

/// <summary>UT-INFRA-01, UT-INFRA-02</summary>
public class AppSettingsServiceTests
{
    [Fact]
    public async Task Load_creates_file_with_normalized_extensions_on_first_run()
    {
        var path = Path.Combine(Path.GetTempPath(), "fts-app-set", Guid.NewGuid().ToString("N"), "settings.json");
        var factory = new TextExtractorFactory(new ITextExtractor[] { new FakeExtractor() });
        var svc = new AppSettingsService(factory, path);
        await svc.LoadAsync();
        Assert.True(File.Exists(path));
        Assert.NotNull(svc.Settings.TargetExtensions);
        Assert.Contains(".txt", svc.Settings.TargetExtensions, StringComparer.Ordinal);
        Assert.Contains(".md", svc.Settings.TargetExtensions, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Save_and_second_Load_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), "fts-app-set", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new TextExtractorFactory(new ITextExtractor[] { new FakeExtractor() });
        var a = new AppSettingsService(factory, path);
        await a.LoadAsync();
        a.Settings.TargetFolders.Add("D:\\X");
        a.Settings.IndexPath = "D:\\Idx";
        a.Settings.TargetExtensions = new List<string> { "TXT" };
        await a.SaveAsync();
        var b = new AppSettingsService(factory, path);
        await b.LoadAsync();
        Assert.Single(b.Settings.TargetFolders, x => x == "D:\\X");
        Assert.Equal("D:\\Idx", b.Settings.IndexPath);
        Assert.Contains(".txt", b.Settings.TargetExtensions, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Load_malformed_json_does_not_throw_uses_fresh_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), "fts-app-set", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ this is not json }");
        var factory = new TextExtractorFactory(new ITextExtractor[] { new FakeExtractor() });
        var svc = new AppSettingsService(factory, path);
        var ex = await Record.ExceptionAsync(() => svc.LoadAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task Load_strips_extensions_not_supported_by_extractors()
    {
        var path = Path.Combine(Path.GetTempPath(), "fts-app-set", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = """
            {"targetExtensions":[".txt",".bin",".docx"],"targetFolders":[],"indexPath":"","themeMode":"System"}
            """;
        await File.WriteAllTextAsync(path, json);
        var factory = new TextExtractorFactory(new ITextExtractor[] { new FakeExtractor() });
        var svc = new AppSettingsService(factory, path);
        await svc.LoadAsync();
        Assert.Contains(".txt", svc.Settings.TargetExtensions, StringComparer.Ordinal);
        Assert.DoesNotContain(".bin", svc.Settings.TargetExtensions, StringComparer.Ordinal);
        Assert.DoesNotContain(".docx", svc.Settings.TargetExtensions, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Load_preserves_user_selected_extension_subset()
    {
        var path = Path.Combine(Path.GetTempPath(), "fts-app-set", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var factory = new TextExtractorFactory(new ITextExtractor[] { new FakeExtractor() });
        var svc = new AppSettingsService(factory, path);
        await svc.LoadAsync();
        svc.Settings.TargetExtensions = new List<string> { ".txt" };
        await svc.SaveAsync();

        var reloaded = new AppSettingsService(factory, path);
        await reloaded.LoadAsync();

        Assert.Equal(new[] { ".txt" }, reloaded.Settings.TargetExtensions);
        Assert.DoesNotContain(".md", reloaded.Settings.TargetExtensions, StringComparer.Ordinal);
    }

    private sealed class FakeExtractor : ITextExtractor
    {
        public IEnumerable<string> SupportedExtensions { get; } = new[] { ".txt", ".md" };
        public bool CanExtract(string extension)
        {
            var n = PreviewHelper.NormalizeExtension(extension);
            return SupportedExtensions.Any(e => e.Equals(n, StringComparison.OrdinalIgnoreCase));
        }
        public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult("");
    }
}
