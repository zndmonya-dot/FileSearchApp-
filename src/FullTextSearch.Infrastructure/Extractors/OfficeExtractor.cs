using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using FullTextSearch.Core.Extractors;

namespace FullTextSearch.Infrastructure.Extractors;

/// <summary>
/// Office文書（Word/Excel/PowerPoint）用のテキスト抽出器
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
    /// ExcelをHTMLテーブル形式で取得
    /// </summary>
    public static string ExtractExcelAsHtml(string filePath, string? searchQuery = null)
    {
        if (!File.Exists(filePath))
        {
            return "<p>ファイルが見つかりません</p>";
        }

        try
        {
            using var doc = SpreadsheetDocument.Open(filePath, false);
            var workbookPart = doc.WorkbookPart;

            if (workbookPart == null)
            {
                return "<p>ワークブックが空です</p>";
            }

            var sb = new StringBuilder();
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
            var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToList() ?? new List<Sheet>();
            
            int sheetIndex = 0;
            foreach (var worksheetPart in workbookPart.WorksheetParts)
            {
                var sheetData = worksheetPart.Worksheet.Elements<SheetData>().FirstOrDefault();
                if (sheetData == null) continue;

                var rows = sheetData.Elements<Row>().ToList();
                if (rows.Count == 0) continue;

                // シート名を取得
                var sheetName = sheetIndex < sheets.Count ? sheets[sheetIndex].Name?.Value ?? $"Sheet{sheetIndex + 1}" : $"Sheet{sheetIndex + 1}";
                sb.AppendLine($"<div class=\"excel-sheet\">");
                sb.AppendLine($"<div class=\"sheet-name\">{System.Net.WebUtility.HtmlEncode(sheetName)}</div>");
                sb.AppendLine("<div class=\"table-wrapper\"><table class=\"excel-table\">");

                // 最大列数を計算
                int maxCol = 0;
                foreach (var row in rows)
                {
                    var cells = row.Elements<Cell>().ToList();
                    foreach (var cell in cells)
                    {
                        var colIndex = GetColumnIndex(cell.CellReference?.Value ?? "A1");
                        if (colIndex > maxCol) maxCol = colIndex;
                    }
                }

                // ヘッダー行（列番号）
                sb.AppendLine("<thead><tr><th class=\"row-header\"></th>");
                for (int c = 0; c <= maxCol && c < 50; c++)
                {
                    sb.AppendLine($"<th>{GetColumnName(c)}</th>");
                }
                sb.AppendLine("</tr></thead>");

                sb.AppendLine("<tbody>");
                int rowNum = 0;
                foreach (var row in rows.Take(500)) // 最大500行
                {
                    rowNum++;
                    var rowIndex = row.RowIndex?.Value ?? (uint)rowNum;
                    sb.Append($"<tr><td class=\"row-header\">{rowIndex}</td>");

                    // セルを列位置に配置
                    var cellDict = new Dictionary<int, string>();
                    foreach (var cell in row.Elements<Cell>())
                    {
                        var colIndex = GetColumnIndex(cell.CellReference?.Value ?? "A1");
                        var value = GetCellValue(cell, sharedStrings);
                        cellDict[colIndex] = value;
                    }

                    for (int c = 0; c <= maxCol && c < 50; c++)
                    {
                        var cellValue = cellDict.TryGetValue(c, out var v) ? v : "";
                        var escapedValue = System.Net.WebUtility.HtmlEncode(cellValue);
                        
                        // 検索ハイライト
                        if (!string.IsNullOrEmpty(searchQuery) && !string.IsNullOrEmpty(escapedValue))
                        {
                            escapedValue = HighlightText(escapedValue, searchQuery);
                        }
                        
                        sb.Append($"<td>{escapedValue}</td>");
                    }
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</tbody></table></div></div>");
                sheetIndex++;
            }

            return sb.Length > 0 ? sb.ToString() : "<p>データがありません</p>";
        }
        catch (Exception ex)
        {
            return $"<p class=\"error\">エラー: {System.Net.WebUtility.HtmlEncode(ex.Message)}</p>";
        }
    }

    private static string HighlightText(string text, string query)
    {
        var terms = query.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            var pattern = Regex.Escape(term);
            text = Regex.Replace(text, pattern, 
                m => $"<span class=\"highlight\">{m.Value}</span>", 
                RegexOptions.IgnoreCase);
        }
        return text;
    }

    private static int GetColumnIndex(string cellReference)
    {
        var match = Regex.Match(cellReference, @"^([A-Z]+)");
        if (!match.Success) return 0;
        
        var col = match.Value;
        int index = 0;
        for (int i = 0; i < col.Length; i++)
        {
            index = index * 26 + (col[i] - 'A' + 1);
        }
        return index - 1;
    }

    private static string GetColumnName(int index)
    {
        var name = "";
        index++;
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }
        return name;
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

