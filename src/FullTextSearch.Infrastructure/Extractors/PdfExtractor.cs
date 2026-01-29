using System.Text;
using FullTextSearch.Core.Extractors;
using UglyToad.PdfPig;

namespace FullTextSearch.Infrastructure.Extractors;

/// <summary>
/// PDFファイル用のテキスト抽出器
/// </summary>
public class PdfExtractor : ITextExtractor
{
    private static readonly HashSet<string> SupportedExtensionSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    public IEnumerable<string> SupportedExtensions => SupportedExtensionSet;

    public bool CanExtract(string extension)
    {
        return SupportedExtensionSet.Contains(extension);
    }

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

