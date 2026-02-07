// ファイルパスからプレビュー用の行テキストを取得。抽出器でテキスト抽出し、コードは Highlight.js でハイライト。
using System.Text;
using System.Text.RegularExpressions;
using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using FullTextSearch.Infrastructure.Extractors;
using FullTextSearch.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace FileSearch.Blazor.Services;

/// <summary>
/// ファイルプレビュー取得サービス。Office / PDF / テキスト・コードをすべて行テキスト（Mode=text）で返す。
/// </summary>
public class PreviewService : IPreviewService
{
    /// <summary>プレビューに使う最大文字数（超えた分は省略）</summary>
    private const int PreviewMaxChars = 50_000;
    /// <summary>シンタックスハイライトをかける最大行数（それ以降はエスケープのみ）</summary>
    private const int PreviewMaxLinesForHighlight = 500;
    /// <summary>Highlight.js をバッチ呼び出しする行数（JS往復回数を減らしてプレビューを速くする）</summary>
    private const int HighlightBatchSize = 50;
    private const int DiagnosticHtmlSampleLength = 400;

    private readonly TextExtractorFactory _extractorFactory;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<PreviewService>? _logger;
    private readonly IAppSettingsService _settingsService;

    /// <summary>抽出器ファクトリ、JS ランタイム、設定サービス（拡張子→言語マップ用）を注入する。</summary>
    public PreviewService(TextExtractorFactory extractorFactory, IJSRuntime jsRuntime, IAppSettingsService settingsService, ILogger<PreviewService>? logger = null)
    {
        _extractorFactory = extractorFactory;
        _jsRuntime = jsRuntime;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>指定パスのファイルをテキスト抽出し、検索語でハイライトした行リストを返す。コードは JS でシンタックスハイライト。</summary>
    public async Task<PreviewResult> GetPreviewAsync(string path, string? searchQuery, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
            return CreateErrorResult("ファイルパスが指定されていません");

        var ext = PreviewHelper.NormalizeExtension(path);

        // 抽出器で種別判定（Office/PDF/テキスト）。Excel も Word/PPT と同様にテキストでプレビュー
        var extractor = _extractorFactory.GetExtractor(ext);

        string content;
        if (extractor != null)
            content = await extractor.ExtractTextAsync(path, cancellationToken);
        else
            content = "[プレビュー不可]";
        if (cancellationToken.IsCancellationRequested) return CreateErrorResult("[キャンセル]");

        if (content.Length > PreviewMaxChars) content = content.Substring(0, PreviewMaxChars) + "\n... (省略)";

        // 検索語と本文を NFC 正規化して、合成／分解の違いでハイライトが外れるのを防ぐ
        content = content.IsNormalized(NormalizationForm.FormC) ? content : content.Normalize(NormalizationForm.FormC);
        var languageMap = GetMergedLanguageMap();
        var isSourceCode = IsCodeFileWithMap(ext, languageMap);
        var currentLanguage = GetLanguageWithMap(ext, languageMap);
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
            highlightedLines = new string[lines.Length];
            var limit = Math.Min(lines.Length, PreviewMaxLinesForHighlight);
            for (int i = limit; i < lines.Length; i++)
                highlightedLines[i] = System.Net.WebUtility.HtmlEncode(lines[i].TrimEnd('\r'));
            try
            {
                for (int chunkStart = 0; chunkStart < limit && !cancellationToken.IsCancellationRequested; chunkStart += HighlightBatchSize)
                {
                    var chunkLen = Math.Min(HighlightBatchSize, limit - chunkStart);
                    var chunk = new string[chunkLen];
                    for (int i = 0; i < chunkLen; i++)
                        chunk[i] = lines[chunkStart + i].TrimEnd('\r');
                    var highlightedChunk = await _jsRuntime.InvokeAsync<string[]>("highlightCodeBatch", cancellationToken, new object[] { chunk, currentLanguage });
                    for (int i = 0; i < highlightedChunk.Length && (chunkStart + i) < limit; i++)
                        highlightedLines[chunkStart + i] = (highlightedChunk[i] ?? chunk[i]).TrimEnd('\r');
                }
                for (int i = 0; i < limit; i++)
                    if (string.IsNullOrEmpty(highlightedLines[i]))
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

        // 検索語ごとにパターンとエンコード済み文字列をループ外で1回だけ用意（行×語の重複計算を避ける）
        var escapedPatterns = new string[searchTerms.Length];
        var encodedTerms = new string[searchTerms.Length];
        for (var t = 0; t < searchTerms.Length; t++)
        {
            escapedPatterns[t] = Regex.Escape(searchTerms[t]);
            encodedTerms[t] = System.Net.WebUtility.HtmlEncode(searchTerms[t]);
        }

        var resultLines = new PreviewLineResult[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            var originalLine = lines[i].TrimEnd('\r');
            var displayLine = isSourceCode && i < highlightedLines.Length
                ? highlightedLines[i].TrimEnd('\r')
                : System.Net.WebUtility.HtmlEncode(originalLine);
            var hasMatch = false;
            for (var t = 0; t < searchTerms.Length; t++)
            {
                if (Regex.IsMatch(originalLine, escapedPatterns[t], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    hasMatch = true;
                    if (isSourceCode)
                        displayLine = HighlightSearchInSyntax(displayLine, searchTerms[t]);
                    else
                        displayLine = Regex.Replace(displayLine, Regex.Escape(encodedTerms[t]), m => $"<mark>{m.Value}</mark>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
            }
            resultLines[i] = new PreviewLineResult(displayLine, hasMatch);
        }

#if DEBUG
        if (_logger != null && isSourceCode && searchTerms.Length > 0)
        {
            for (int i = 0; i < resultLines.Length; i++)
            {
                var r = resultLines[i];
                if (r.HasMatch && !r.Content.Contains("<mark>", StringComparison.Ordinal))
                {
                    var sample = r.Content.Length <= DiagnosticHtmlSampleLength ? r.Content : r.Content.Substring(0, DiagnosticHtmlSampleLength) + "...";
                    _logger.LogWarning("[PreviewHighlight] ハイライト未付与 path={Path} lineIndex1based={Line} terms={Terms} htmlSample={Html}", path, i + 1, string.Join(",", searchTerms), sample);
                }
            }
        }
#endif
        return new PreviewResult
        {
            Mode = "text",
            Lines = resultLines,
            LineCount = lines.Length
        };
    }

    /// <summary>組み込み LanguageMap と設定の ExtensionLanguageMap をマージしたマップを返す。設定のキーは正規化して上書きする。</summary>
    private IReadOnlyDictionary<string, string> GetMergedLanguageMap()
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in PreviewHelper.LanguageMap)
            merged[kv.Key] = kv.Value;
        var custom = _settingsService.Settings.ExtensionLanguageMap;
        if (custom != null && custom.Count > 0)
        {
            foreach (var kv in custom)
            {
                var key = PreviewHelper.NormalizeExtension(kv.Key);
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(kv.Value))
                    merged[key] = kv.Value;
            }
        }
        return merged;
    }

    private static string GetLanguageWithMap(string extension, IReadOnlyDictionary<string, string> languageMap)
    {
        var ext = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        return languageMap.TryGetValue(ext, out var lang) ? lang : "plaintext";
    }

    private static bool IsCodeFileWithMap(string extension, IReadOnlyDictionary<string, string> languageMap)
    {
        var ext = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        return languageMap.ContainsKey(ext);
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
