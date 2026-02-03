// Blazor Hybrid (MAUI) のエントリポイント。DI 登録（抽出器・検索・インデックス・設定・プレビュー）を行う。
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

/// <summary>
/// MAUI アプリのビルダーとサービス登録。Core/Infrastructure の実装を Singleton または Scoped で登録する。
/// </summary>
public static class MauiProgram
{
    /// <summary>アプリケーションのビルドと DI コンテナの構成を行う。</summary>
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

        // テキスト抽出器（Office / PDF / テキスト）
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
