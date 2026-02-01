using System.Text;
using System.Text.RegularExpressions;
using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using FullTextSearch.Infrastructure.Extractors;
using Microsoft.JSInterop;

namespace FileSearch.Blazor.Services;

/// <summary>
/// ファイルプレビュー取得サービス（画像／Excel／テキスト／コード）
/// </summary>
public class PreviewService : IPreviewService
{
    private const int PreviewMaxChars = 50_000;
    private const int PreviewMaxLinesForHighlight = 500;

    private readonly TextExtractorFactory _extractorFactory;
    private readonly IJSRuntime _jsRuntime;

    public PreviewService(TextExtractorFactory extractorFactory, IJSRuntime jsRuntime)
    {
        _extractorFactory = extractorFactory;
        _jsRuntime = jsRuntime;
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

        // Excel: HTML テーブルで直感的に表示
        if (ext == ".xlsx")
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

        // テキスト／コード
        string content;
        var extractor = _extractorFactory.GetExtractor(ext);
        if (extractor != null)
            content = await extractor.ExtractTextAsync(path, cancellationToken);
        else if (PreviewHelper.IsTextFile(ext) || IsTextFile(path))
            content = await File.ReadAllTextAsync(path, cancellationToken);
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
            var toHighlight = lines.Length <= PreviewMaxLinesForHighlight
                ? content
                : string.Join("\n", lines.Take(PreviewMaxLinesForHighlight)) + "\n";
            try
            {
                var highlighted = await _jsRuntime.InvokeAsync<string>("highlightCode", cancellationToken, new object[] { toHighlight, currentLanguage });
                highlightedLines = highlighted.Split('\n');
                if (lines.Length > PreviewMaxLinesForHighlight)
                {
                    var rest = lines.Skip(PreviewMaxLinesForHighlight).Select(l => System.Net.WebUtility.HtmlEncode(l.TrimEnd('\r')));
                    highlightedLines = highlightedLines.Concat(rest).ToArray();
                }
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
                if (Regex.IsMatch(originalLine, pattern, RegexOptions.IgnoreCase))
                {
                    hasMatch = true;
                    if (isSourceCode)
                        displayLine = HighlightSearchInSyntax(displayLine, term);
                    else
                        displayLine = Regex.Replace(displayLine, Regex.Escape(System.Net.WebUtility.HtmlEncode(term)), m => $"<mark>{m.Value}</mark>", RegexOptions.IgnoreCase);
                }
            }
            resultLines.Add(new PreviewLineResult(displayLine, hasMatch));
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
        try
        {
            var pattern = $"(?<=>|^)([^<]*?)({Regex.Escape(term)})([^<]*?)(?=<|$)";
            return Regex.Replace(htmlLine, pattern, m => $"{m.Groups[1].Value}<mark>{m.Groups[2].Value}</mark>{m.Groups[3].Value}", RegexOptions.IgnoreCase);
        }
        catch { return htmlLine; }
    }

    private static bool IsTextFile(string path)
    {
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var b = new byte[8192];
            return !b.Take(s.Read(b, 0, b.Length)).Any(x => x == 0);
        }
        catch { return false; }
    }
}
