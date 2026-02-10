// ファイルパスからプレビュー用の行テキストを取得。抽出器でテキスト抽出し、検索語だけを <mark> で強調する。
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using FullTextSearch.Infrastructure.Extractors;
using Microsoft.Extensions.Logging;

namespace FileSearch.Blazor.Services;

/// <summary>
/// ファイルプレビュー取得サービス。Office / PDF / テキスト・コードをすべて行テキスト（Mode=text）で返す。
/// </summary>
public class PreviewService : IPreviewService
{
    /// <summary>プレビューに使う最大文字数（超えた分は省略）</summary>
    private const int PreviewMaxChars = 50_000;

    private readonly TextExtractorFactory _extractorFactory;
    private readonly ILogger<PreviewService>? _logger;

    /// <summary>抽出器ファクトリとログを注入する。</summary>
    public PreviewService(TextExtractorFactory extractorFactory, ILogger<PreviewService>? logger = null)
    {
        _extractorFactory = extractorFactory;
        _logger = logger;
    }

    /// <summary>指定パスのファイルをテキスト抽出し、検索語でハイライトした行リストを返す。構文色分けは行わない。</summary>
    public async Task<PreviewResult> GetPreviewAsync(string path, string? searchQuery, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
            return CreateErrorResult("ファイルパスが指定されていません");

        var ext = Path.GetExtension(path);
        var extractor = _extractorFactory.GetExtractor(ext);

        string content;
        try
        {
            if (extractor != null)
                content = await extractor.ExtractTextAsync(path, cancellationToken);
            else
                content = "[プレビュー不可]";
        }
        catch (OperationCanceledException)
        {
            return CreateErrorResult("[キャンセル]");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Preview extraction failed: {Path}", path);
            return CreateErrorResult($"[エラー] {ex.Message}");
        }

        if (cancellationToken.IsCancellationRequested)
            return CreateErrorResult("[キャンセル]");

        if (content.Length > PreviewMaxChars)
            content = content.Substring(0, PreviewMaxChars) + "\n... (省略)";

        // 検索語と本文を NFC 正規化して、合成／分解の違いでハイライトが外れるのを防ぐ
        content = content.IsNormalized(NormalizationForm.FormC) ? content : content.Normalize(NormalizationForm.FormC);

        var searchTerms = string.IsNullOrWhiteSpace(searchQuery)
            ? Array.Empty<string>()
            : searchQuery.Split(' ', '　')
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.IsNormalized(NormalizationForm.FormC) ? t : t.Normalize(NormalizationForm.FormC))
                .ToArray();

        // 検索語ごとにパターンとエンコード済み文字列をループ外で1回だけ用意（行×語の重複計算を避ける）
        var escapedPatterns = new string[searchTerms.Length];
        var encodedTerms = new string[searchTerms.Length];
        for (var t = 0; t < searchTerms.Length; t++)
        {
            escapedPatterns[t] = Regex.Escape(searchTerms[t]);
            encodedTerms[t] = System.Net.WebUtility.HtmlEncode(searchTerms[t]);
        }

        var lines = content.Split('\n');
        var resultLines = new PreviewLineResult[lines.Length];

        for (int i = 0; i < lines.Length; i++)
        {
            var originalLine = lines[i].TrimEnd('\r');
            var encodedLine = System.Net.WebUtility.HtmlEncode(originalLine);
            var hasMatch = false;

            for (var t = 0; t < searchTerms.Length; t++)
            {
                if (Regex.IsMatch(originalLine, escapedPatterns[t], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    hasMatch = true;
                    encodedLine = Regex.Replace(
                        encodedLine,
                        Regex.Escape(encodedTerms[t]),
                        m => $"<mark>{m.Value}</mark>",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
            }

            resultLines[i] = new PreviewLineResult(encodedLine, hasMatch);
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
}
