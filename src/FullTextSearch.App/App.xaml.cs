using System.Windows;
using System.Windows.Threading;
using FullTextSearch.App.ViewModels;
using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Logging;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Extractors;
using FullTextSearch.Infrastructure.FileSystem;
using FullTextSearch.Infrastructure.Logging;
using FullTextSearch.Infrastructure.Lucene;
using FullTextSearch.Infrastructure.Preview;
using FullTextSearch.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FullTextSearch.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Logging
                services.AddSingleton<ILogService, FileLogService>();

                // Settings
                services.AddSingleton<IAppSettingsService, AppSettingsService>();

                // Core Services
                services.AddSingleton<ISearchService, LuceneSearchService>();
                services.AddSingleton<IIndexService, LuceneIndexService>();

                // Text Extractors
                services.AddSingleton<ITextExtractor, OfficeExtractor>();
                services.AddSingleton<ITextExtractor, PdfExtractor>();
                services.AddSingleton<ITextExtractor, TextFileExtractor>();
                services.AddSingleton<TextExtractorFactory>();

                // Infrastructure
                services.AddSingleton<IFileWatcherService, FileWatcherService>();
                services.AddSingleton<IPreviewService, PreviewService>();

                // ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<SettingsViewModel>();
            })
            .Build();

        Services = _host.Services;

        // グローバルな例外ハンドリング
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logService = Services.GetService<ILogService>();
        logService?.Error("Unhandled dispatcher exception", e.Exception);

        MessageBox.Show(
            $"エラーが発生しました。\n\n{e.Exception.Message}",
            "エラー",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var logService = Services.GetService<ILogService>();
        logService?.Error("Unhandled exception", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var logService = Services.GetService<ILogService>();
        logService?.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}


