using Microsoft.Extensions.Logging;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Preview;
using FullTextSearch.Core.Search;
using FullTextSearch.Core.Extractors;
using FileSearch.Blazor.Services;
using FullTextSearch.Infrastructure.Lucene;
using FullTextSearch.Infrastructure.Extractors;
using FullTextSearch.Infrastructure.Settings;

namespace FileSearch.Blazor;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // テキスト抽出器
        builder.Services.AddSingleton<ITextExtractor, OfficeExtractor>();
        builder.Services.AddSingleton<ITextExtractor, PdfExtractor>();
        builder.Services.AddSingleton<ITextExtractor, TextFileExtractor>();
        builder.Services.AddSingleton<TextExtractorFactory>();

        // 検索サービス
        builder.Services.AddSingleton<IIndexService, LuceneIndexService>();
        builder.Services.AddSingleton<ISearchService, LuceneSearchService>();
        builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
        builder.Services.AddScoped<IPreviewService, PreviewService>();

        return builder.Build();
    }
}
