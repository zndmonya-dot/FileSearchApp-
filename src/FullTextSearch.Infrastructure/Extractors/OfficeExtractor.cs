// Word/Excel/PowerPoint（.docx, .xlsx, .pptx）から DocumentFormat.OpenXml でテキストを抽出。
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using FullTextSearch.Core.Extractors;

namespace FullTextSearch.Infrastructure.Extractors;

/// <summary>
/// Office 文書（Word / Excel / PowerPoint）用のテキスト抽出器。段落・セル・スライドのテキストを連結して返す。
/// </summary>
public class OfficeExtractor : ITextExtractor
{
    private static readonly HashSet<string> SupportedExtensionSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".xlsx", ".pptx"
    };

    public IEnumerable<string> SupportedExtensions => SupportedExtensionSet;

    public PreviewCategory PreviewCategory => PreviewCategory.Office;

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

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        var text = extension switch
        {
            ".docx" => ExtractFromWord(filePath, cancellationToken),
            ".xlsx" => ExtractFromExcel(filePath, cancellationToken),
            ".pptx" => ExtractFromPowerPoint(filePath, cancellationToken),
            _ => string.Empty
        };

        return Task.FromResult(text);
    }

    /// <summary>
    /// Word文書からテキストを抽出
    /// </summary>
    private static string ExtractFromWord(string filePath, CancellationToken cancellationToken = default)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document.Body;

        if (body == null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        int count = 0;

        foreach (var para in body.Elements<Paragraph>())
        {
            if (++count % 50 == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var text = para.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Excelブックからテキストを抽出
    /// </summary>
    private static string ExtractFromExcel(string filePath, CancellationToken cancellationToken = default)
    {
        using var doc = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = doc.WorkbookPart;

        if (workbookPart == null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        int rowCount = 0;

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sheetData = worksheetPart.Worksheet.Elements<SheetData>().FirstOrDefault();
            if (sheetData == null)
            {
                continue;
            }

            foreach (var row in sheetData.Elements<Row>())
            {
                if (++rowCount % 100 == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                var rowTexts = new List<string>();

                foreach (var cell in row.Elements<Cell>())
                {
                    var cellValue = GetCellValue(cell, sharedStrings);
                    if (!string.IsNullOrEmpty(cellValue))
                    {
                        rowTexts.Add(cellValue);
                    }
                }

                if (rowTexts.Count > 0)
                {
                    sb.AppendLine(string.Join("\t", rowTexts));
                }
            }

            sb.AppendLine(); // シート間に空行
        }

        return sb.ToString();
    }

    /// <summary>
    /// セルの値を取得
    /// </summary>
    private static string GetCellValue(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.Text ?? string.Empty;

        // 共有文字列テーブルへの参照の場合
        if (cell.DataType?.Value == CellValues.SharedString && sharedStrings != null)
        {
            if (int.TryParse(value, out var index))
            {
                var item = sharedStrings.ElementAtOrDefault(index);
                return item?.InnerText ?? string.Empty;
            }
        }

        return value;
    }

    /// <summary>
    /// PowerPointプレゼンテーションからテキストを抽出
    /// </summary>
    private static string ExtractFromPowerPoint(string filePath, CancellationToken cancellationToken = default)
    {
        using var doc = PresentationDocument.Open(filePath, false);
        var presentationPart = doc.PresentationPart;

        if (presentationPart == null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        int slideIndex = 0;

        foreach (var slidePart in presentationPart.SlideParts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var slide = slidePart.Slide;
            if (slide == null)
            {
                continue;
            }

            var texts = slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>();
            foreach (var text in texts)
            {
                if (!string.IsNullOrWhiteSpace(text.Text))
                {
                    sb.AppendLine(text.Text);
                }
            }

            sb.AppendLine(); // スライド間に空行
            slideIndex++;
        }

        return sb.ToString();
    }
}

