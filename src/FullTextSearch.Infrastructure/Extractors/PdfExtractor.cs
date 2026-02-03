// PDF からテキストを抽出する実装（PdfPig 使用）。
using System.Text;
using FullTextSearch.Core.Extractors;
using UglyToad.PdfPig;

namespace FullTextSearch.Infrastructure.Extractors;

/// <summary>
/// PDF ファイル用のテキスト抽出器。全ページのテキストを連結して返す。
/// </summary>
public class PdfExtractor : ITextExtractor
{
    private static readonly HashSet<string> SupportedExtensionSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    public IEnumerable<string> SupportedExtensions => SupportedExtensionSet;

    public PreviewCategory PreviewCategory => PreviewCategory.Pdf;

    public bool CanExtract(string extension)
    {
        return SupportedExtensionSet.Contains(extension);
    }

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

            sb.AppendLine(); // ページ間に空行
        }

        return Task.FromResult(sb.ToString());
    }
}

