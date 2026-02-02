using System.Text;
using System.Text.RegularExpressions;
using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using FullTextSearch.Infrastructure.Extractors;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace FileSearch.Blazor.Services;

/// <summary>
/// ファイルプレビュー取得サービス（画像／Excel／テキスト／コード）
/// </summary>
public class PreviewService : IPreviewService
{
    private const int PreviewMaxChars = 50_000;
    private const int PreviewMaxLinesForHighlight = 500;
    private const int DiagnosticHtmlSampleLength = 400;

    private readonly TextExtractorFactory _extractorFactory;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<PreviewService>? _logger;

    public PreviewService(TextExtractorFactory extractorFactory, IJSRuntime jsRuntime, ILogger<PreviewService>? logger = null)
    {
        _extractorFactory = extractorFactory;
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task<PreviewResult> GetPreviewAsync(string path, string? searchQuery, CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        // 画像: Data URL で img 表示
        if (PreviewHelper.IsImageFile(ext))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return CreateErrorResult("[キャンセル]");
                var base64 = Convert.ToBase64String(bytes);
                var mime = PreviewHelper.GetImageMimeType(ext);
                return new PreviewResult
                {
                    Mode = "image",
                    ImageDataUrl = $"data:{mime};base64,{base64}"
                };
            }
            catch
            {
                return CreateErrorResult("[画像の読み込みに失敗しました]");
            }
        }

        // 抽出器で種別判定（Office/PDF/テキスト）
        var extractor = _extractorFactory.GetExtractor(ext);
        if (extractor?.PreviewCategory == PreviewCategory.Office && ext == ".xlsx")
        {
            try
            {
                var query = searchQuery?.Trim();
                var html = OfficeExtractor.ExtractExcelAsHtml(path, string.IsNullOrWhiteSpace(query) ? null : query);
                return new PreviewResult { Mode = "html", Html = html };
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"[Excelプレビューエラー] {ex.Message}");
            }
        }

        // テキスト／コード（抽出器が対応する拡張子のみ）
        string content;
        if (extractor != null)
            content = await extractor.ExtractTextAsync(path, cancellationToken);
        else
            content = "[プレビュー不可]";
        if (cancellationToken.IsCancellationRequested) return CreateErrorResult("[キャンセル]");

        if (content.Length > PreviewMaxChars) content = content.Substring(0, PreviewMaxChars) + "\n... (省略)";

        // 検索語と本文を NFC 正規化して、合成／分解の違いでハイライトが外れるのを防ぐ
        content = content.IsNormalized(NormalizationForm.FormC) ? content : content.Normalize(NormalizationForm.FormC);
        var isSourceCode = PreviewHelper.IsCodeFile(ext);
        var currentLanguage = PreviewHelper.GetLanguage(ext);
        var searchTerms = string.IsNullOrWhiteSpace(searchQuery)
            ? Array.Empty<string>()
            : searchQuery.Split(' ', '　')
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.IsNormalized(NormalizationForm.FormC) ? t : t.Normalize(NormalizationForm.FormC))
                .ToArray();

        var lines = content.Split('\n');
        string[] highlightedLines;
        if (isSourceCode)
        {
            // 行ごとに Highlight.js を呼び、元行と必ず1対1で対応させる（全文渡しだと改行数がずれて「tes」等がハイライトされないことがある）
            highlightedLines = new string[lines.Length];
            var limit = Math.Min(lines.Length, PreviewMaxLinesForHighlight);
            try
            {
                for (int i = 0; i < limit; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var line = lines[i].TrimEnd('\r');
                    var hl = await _jsRuntime.InvokeAsync<string>("highlightCode", cancellationToken, new object[] { line, currentLanguage });
                    highlightedLines[i] = hl.TrimEnd('\r');
                }
                for (int i = limit; i < lines.Length; i++)
                    highlightedLines[i] = System.Net.WebUtility.HtmlEncode(lines[i].TrimEnd('\r'));
            }
            catch
            {
                highlightedLines = lines.Select(l => System.Net.WebUtility.HtmlEncode(l.TrimEnd('\r'))).ToArray();
                isSourceCode = false;
            }
        }
        else
        {
            highlightedLines = lines.Select(l => System.Net.WebUtility.HtmlEncode(l.TrimEnd('\r'))).ToArray();
        }

        var resultLines = new List<PreviewLineResult>();
        for (int i = 0; i < lines.Length; i++)
        {
            var originalLine = lines[i].TrimEnd('\r');
            var displayLine = isSourceCode && i < highlightedLines.Length
                ? highlightedLines[i].TrimEnd('\r')
                : System.Net.WebUtility.HtmlEncode(originalLine);
            var hasMatch = false;
            foreach (var term in searchTerms)
            {
                var pattern = Regex.Escape(term);
                if (Regex.IsMatch(originalLine, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    hasMatch = true;
                    if (isSourceCode)
                        displayLine = HighlightSearchInSyntax(displayLine, term);
                    else
                    {
                        var encodedTerm = System.Net.WebUtility.HtmlEncode(term);
                        displayLine = Regex.Replace(displayLine, Regex.Escape(encodedTerm), m => $"<mark>{m.Value}</mark>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    }
                }
            }
            resultLines.Add(new PreviewLineResult(displayLine, hasMatch));
        }

        // 調査用: マッチしたのに <mark> が付いていない行を記録（HTML 構造でマッチしていない原因特定用）
        if (_logger != null && isSourceCode && searchTerms.Length > 0)
        {
            for (int i = 0; i < resultLines.Count; i++)
            {
                var r = resultLines[i];
                if (r.HasMatch && !r.Content.Contains("<mark>", StringComparison.Ordinal))
                {
                    var sample = r.Content.Length <= DiagnosticHtmlSampleLength ? r.Content : r.Content.Substring(0, DiagnosticHtmlSampleLength) + "...";
                    _logger.LogWarning("[PreviewHighlight] ハイライト未付与 path={Path} lineIndex1based={Line} terms={Terms} htmlSample={Html}", path, i + 1, string.Join(",", searchTerms), sample);
                }
            }
        }

        return new PreviewResult
        {
            Mode = "text",
            Lines = resultLines,
            LineCount = lines.Length
        };
    }

    private static PreviewResult CreateErrorResult(string message)
    {
        return new PreviewResult
        {
            Mode = "text",
            Lines = new List<PreviewLineResult> { new(message, false) },
            LineCount = 1
        };
    }

    private static string HighlightSearchInSyntax(string htmlLine, string term)
    {
        if (string.IsNullOrEmpty(term)) return htmlLine;
        try
        {
            // シンタックスハイライト済み HTML 内では <, >, & 等がエスケープされているため、検索語もエンコードしてマッチさせる
            var encodedTerm = System.Net.WebUtility.HtmlEncode(term);
            // 1. まず「1つのテキストノード内に語が丸ごとある」場合を試す
            var singleRunPattern = $"(?<=>|^)([^<]*?)({Regex.Escape(encodedTerm)})([^<]*?)(?=<|$)";
            var singleRun = Regex.Replace(htmlLine, singleRunPattern, m => $"{m.Groups[1].Value}<mark>{m.Groups[2].Value}</mark>{m.Groups[3].Value}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (singleRun != htmlLine) return singleRun;
            // 2. 語が複数の <span> に分かれている場合: 文字ごとに「閉タグ＋開タグ」を許容してマッチ
            var betweenTags = "(?:</[^>]*>\\s*<[^>]*>)*";
            var charGroups = string.Join(betweenTags, encodedTerm.Select(c => $"({Regex.Escape(c.ToString())})"));
            var spanSplitPattern = $"({charGroups})";
            var replacement = "<mark>" + string.Join("", Enumerable.Range(1, encodedTerm.Length).Select(i => $"${i}")) + "</mark>";
            var spanSplit = Regex.Replace(htmlLine, spanSplitPattern, replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (spanSplit != htmlLine) return spanSplit;
            // 3. 任意のタグ・空白を挟んで文字が並ぶ場合（Highlight.js がトークンごとに分割しているとき）
            var anyTags = "(?:<[^>]*>|</[^>]*>|\\s)*";
            var charGroupsAny = string.Join(anyTags, encodedTerm.Select(c => $"({Regex.Escape(c.ToString())})"));
            var anySplitPattern = $"({charGroupsAny})";
            return Regex.Replace(htmlLine, anySplitPattern, replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch { return htmlLine; }
    }

}
