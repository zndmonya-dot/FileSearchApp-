// =============================================================================
// PreviewService.cs
// =============================================================================
// 役割: インデックス済み本文またはファイルからテキストを取得し、行境界・マッチ行を返す。
// 表示は wwwroot/js/preview.js が WebView 上で直接 DOM 構築する（Blazor MarkupString は使わない）。
// =============================================================================
using System.IO;
using System.Text;
using FullTextSearch.Core;
using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using FullTextSearch.Core.Search;
using FileSearch.Messages;
using Microsoft.Extensions.Logging;

namespace FileSearch.Blazor.Services;

/// <summary>
/// ファイルプレビュー取得サービス。Office / PDF / テキスト・コードをすべて行テキスト（Mode=text）で返す。
/// </summary>
public class PreviewService : IPreviewService
{
    private readonly TextExtractorFactory _extractorFactory;
    private readonly ISearchService _searchService;
    private readonly ILogger<PreviewService>? _logger;

    /// <summary>抽出器・検索サービス・ログを注入する。</summary>
    public PreviewService(
        TextExtractorFactory extractorFactory,
        ISearchService searchService,
        ILogger<PreviewService>? logger = null)
    {
        _extractorFactory = extractorFactory;
        _searchService = searchService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PreviewResult> GetPreviewAsync(
        string path,
        string? searchQuery,
        CancellationToken cancellationToken = default,
        SearchMode searchMode = SearchMode.Keyword)
    {
        if (string.IsNullOrEmpty(path))
            return CreateErrorResult(UserMessages.PreviewPathRequired);

        var fileTooLarge = File.Exists(path)
            && ContentLimits.ExceedsIndexTextExtractionFileSizeLimit(new FileInfo(path).Length);

        string content;
        try
        {
            content = await ResolveContentAsync(path, fileTooLarge, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return CreateErrorResult(UserMessages.PreviewCancelledBracket);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Preview extraction failed: {Path}", path);
            return CreateErrorResult(UserMessages.PreviewErrorLine(ex.Message));
        }

        if (cancellationToken.IsCancellationRequested)
            return CreateErrorResult(UserMessages.PreviewCancelledBracket);

        if (fileTooLarge && string.IsNullOrEmpty(content))
            return CreateErrorResult(UserMessages.FormatPreviewFileTooLarge(
                ContentLimits.GetIndexMaxFileBytesDisplayLabel()));

        content = content.IsNormalized(NormalizationForm.FormC) ? content : content.Normalize(NormalizationForm.FormC);
        var matchTerms = NormalizeTerms(_searchService.GetHighlightTerms(searchQuery ?? "", searchMode));
        var displayTerms = matchTerms.OrderByDescending(t => t.Length).ToArray();
        var lineStarts = PreviewLineBuilder.BuildLineStartOffsets(content);
        var matchLines = PreviewLineBuilder.CollectMatchLineNumbers(content, lineStarts, matchTerms, searchMode);

        return new PreviewResult
        {
            Content = content,
            LineStartOffsets = lineStarts,
            MatchLineNumbers = matchLines,
            SearchTerms = displayTerms
        };
    }

    private static string[] NormalizeTerms(IEnumerable<string> terms) =>
        terms.Select(t => t.IsNormalized(NormalizationForm.FormC) ? t : t.Normalize(NormalizationForm.FormC)).ToArray();

    private async Task<string> ResolveContentAsync(string path, bool fileTooLarge, CancellationToken cancellationToken)
    {
        var stored = await _searchService.TryGetStoredContentAsync(path, cancellationToken).ConfigureAwait(false);
        if (stored != null)
        {
            if (stored.Length > 0 || fileTooLarge)
                return stored;
        }

        var ext = Path.GetExtension(path);
        var extractor = _extractorFactory.GetExtractor(ext);
        if (extractor == null)
            return UserMessages.PreviewNotAvailable;

        return await extractor.ExtractTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static PreviewResult CreateErrorResult(string message) =>
        new()
        {
            Content = message,
            LineStartOffsets = [0],
            IsError = true
        };
}
