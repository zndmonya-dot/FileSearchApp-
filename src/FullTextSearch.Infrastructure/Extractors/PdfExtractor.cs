// PDF からテキストを抽出する実装（PdfPig 使用）。
using System.Text;
using FullTextSearch.Core;
using FullTextSearch.Core.Extractors;
using UglyToad.PdfPig;

namespace FullTextSearch.Infrastructure.Extractors;

/// <summary>
/// PDF ファイル用のテキスト抽出器。全ページのテキストを連結して返す。
/// </summary>
public class PdfExtractor : ITextExtractor
{
    public IEnumerable<string> SupportedExtensions => SupportedExtensionSets.Pdf;

    /// <inheritdoc />
    public bool CanExtract(string extension) => SupportedExtensionSets.Pdf.Contains(extension);

    /// <summary>PDF の全ページからテキストを抽出する。</summary>
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found", filePath);
        }

        var sb = new StringBuilder();

        using var document = PdfDocument.Open(filePath);

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
            }

            if (sb.Length >= ContentLimits.ExtractMaxChars)
                break;

            sb.AppendLine(); // ページ間に空行
        }

        return Task.FromResult(sb.ToString());
    }
}

