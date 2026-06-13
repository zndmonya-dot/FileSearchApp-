// Word/Excel/PowerPoint（OOXML ＋ 旧形式）からテキストを抽出。
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using FullTextSearch.Core;
using FullTextSearch.Core.Extractors;
using NPOI.HSSF.UserModel;
using NPOI.HWPF;
using NPOI.HWPF.Extractor;
using NPOI.SS.UserModel;

namespace FullTextSearch.Infrastructure.Extractors;

/// <summary>
/// Office 文書用のテキスト抽出器。OOXML は Open XML SDK、旧 .doc/.xls は NPOI。
/// </summary>
public class OfficeExtractor : ITextExtractor
{
    /// <inheritdoc />
    public IEnumerable<string> SupportedExtensions => SupportedExtensionSets.Office;

    /// <inheritdoc />
    public bool CanExtract(string extension) => SupportedExtensionSets.Office.Contains(extension);

    /// <inheritdoc />
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var text = extension switch
        {
            ".docx" => ExtractFromWord(filePath, cancellationToken),
            ".doc" => ExtractFromLegacyWord(filePath),
            ".xlsx" or ".xlsm" => ExtractFromExcel(filePath, cancellationToken),
            ".xls" => ExtractFromLegacyExcel(filePath, cancellationToken),
            ".pptx" => ExtractFromPowerPoint(filePath, cancellationToken),
            _ => string.Empty
        };

        return Task.FromResult(text);
    }

    private static string ExtractFromLegacyWord(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var doc = new HWPFDocument(stream);
        return new WordExtractor(doc).Text;
    }

    private static string ExtractFromLegacyExcel(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        var workbook = new HSSFWorkbook(stream);
        return ExtractNpoiWorkbook(workbook, cancellationToken);
    }

    private static string ExtractNpoiWorkbook(IWorkbook workbook, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        for (var sheetIndex = 0; sheetIndex < workbook.NumberOfSheets; sheetIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheet = workbook.GetSheetAt(sheetIndex);
            foreach (IRow row in sheet)
            {
                var rowTexts = new List<string>();
                foreach (var cell in row.Cells)
                {
                    var value = GetNpoiCellValue(cell);
                    if (!string.IsNullOrEmpty(value))
                        rowTexts.Add(value);
                }
                if (rowTexts.Count > 0)
                {
                    sb.AppendLine(string.Join("\t", rowTexts));
                    if (sb.Length >= ContentLimits.ExtractMaxChars)
                        return sb.ToString();
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string GetNpoiCellValue(ICell cell) =>
        cell.CellType switch
        {
            NPOI.SS.UserModel.CellType.String => cell.StringCellValue,
            NPOI.SS.UserModel.CellType.Numeric => DateUtil.IsCellDateFormatted(cell)
                ? cell.DateCellValue.ToString("yyyy-MM-dd HH:mm:ss")
                : cell.NumericCellValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NPOI.SS.UserModel.CellType.Boolean => cell.BooleanCellValue ? "TRUE" : "FALSE",
            NPOI.SS.UserModel.CellType.Formula => cell.CachedFormulaResultType switch
            {
                NPOI.SS.UserModel.CellType.String => cell.StringCellValue,
                NPOI.SS.UserModel.CellType.Numeric => cell.NumericCellValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NPOI.SS.UserModel.CellType.Boolean => cell.BooleanCellValue ? "TRUE" : "FALSE",
                _ => cell.ToString() ?? string.Empty
            },
            _ => cell.ToString() ?? string.Empty
        };

    private static string ExtractFromWord(string filePath, CancellationToken cancellationToken = default)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body == null)
            return string.Empty;

        var sb = new StringBuilder();
        var count = 0;
        foreach (var para in body.Elements<Paragraph>())
        {
            if (++count % 50 == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var text = para.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
                if (sb.Length >= ContentLimits.ExtractMaxChars)
                    break;
            }
        }
        return sb.ToString();
    }

    private static string ExtractFromExcel(string filePath, CancellationToken cancellationToken = default)
    {
        using var doc = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart == null)
            return string.Empty;

        var sb = new StringBuilder();
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var rowCount = 0;

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheetData = worksheetPart.Worksheet.Elements<SheetData>().FirstOrDefault();
            if (sheetData == null)
                continue;

            foreach (var row in sheetData.Elements<Row>())
            {
                if (++rowCount % 100 == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                var rowTexts = new List<string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    var cellValue = GetCellValue(cell, sharedStrings);
                    if (!string.IsNullOrEmpty(cellValue))
                        rowTexts.Add(cellValue);
                }

                if (rowTexts.Count > 0)
                {
                    sb.AppendLine(string.Join("\t", rowTexts));
                    if (sb.Length >= ContentLimits.ExtractMaxChars)
                        break;
                }
            }

            if (sb.Length >= ContentLimits.ExtractMaxChars)
                break;
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.Text ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString && sharedStrings != null &&
            int.TryParse(value, out var index))
        {
            return sharedStrings.ElementAtOrDefault(index)?.InnerText ?? string.Empty;
        }
        return value;
    }

    private static string ExtractFromPowerPoint(string filePath, CancellationToken cancellationToken = default)
    {
        using var doc = PresentationDocument.Open(filePath, false);
        var presentationPart = doc.PresentationPart;
        if (presentationPart == null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var slidePart in presentationPart.SlideParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slide = slidePart.Slide;
            if (slide == null)
                continue;

            foreach (var text in slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>())
            {
                if (!string.IsNullOrWhiteSpace(text.Text))
                {
                    sb.AppendLine(text.Text);
                    if (sb.Length >= ContentLimits.ExtractMaxChars)
                        break;
                }
            }

            if (sb.Length >= ContentLimits.ExtractMaxChars)
                break;
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
