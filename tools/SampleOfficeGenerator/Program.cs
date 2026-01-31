using System;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

// 実行場所 (bin/Debug/net8.0) からソリューション直下の samples へ（5階層上）
var baseDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples");
var samplesDir = Path.GetFullPath(baseDir);
Directory.CreateDirectory(samplesDir);

Console.WriteLine("サンプル Office ファイルを生成します: " + samplesDir);

// Word (.docx)
var docxPath = Path.Combine(samplesDir, "sample.docx");
CreateSampleDocx(docxPath);
Console.WriteLine("  created: sample.docx");

// Excel (.xlsx)
var xlsxPath = Path.Combine(samplesDir, "sample.xlsx");
CreateSampleXlsx(xlsxPath);
Console.WriteLine("  created: sample.xlsx");

Console.WriteLine("完了しました。");

static void CreateSampleDocx(string filePath)
{
    using var doc = WordprocessingDocument.Create(filePath, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
    var mainPart = doc.AddMainDocumentPart();
    mainPart.Document = new Document(
        new Body(
            new Paragraph(new W.Run(new W.Text("サンプル文書（Word）"))),
            new Paragraph(new W.Run(new W.Text(""))),
            new Paragraph(new W.Run(new W.Text("消費税について"))),
            new Paragraph(new W.Run(new W.Text("消費税は10%です。軽減税率は8%です。"))),
            new Paragraph(new W.Run(new W.Text(""))),
            new Paragraph(new W.Run(new W.Text("全文検索システムのテスト用サンプルです。")))));
    mainPart.Document.Save();
}

static void CreateSampleXlsx(string filePath)
{
    using var doc = SpreadsheetDocument.Create(filePath, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
    var workbookPart = doc.AddWorkbookPart();
    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    var relId = workbookPart.GetIdOfPart(worksheetPart);
    workbookPart.Workbook = new Workbook(
        new Sheets(
            new Sheet { Name = "Sheet1", SheetId = 1, Id = relId }));
    var sheetData = new SheetData();
    var row1 = new Row { RowIndex = 1 };
    row1.Append(
        MakeCell("A1", "項目"),
        MakeCell("B1", "金額"),
        MakeCell("C1", "消費税"));
    var row2 = new Row { RowIndex = 2 };
    row2.Append(
        MakeCell("A2", "商品A"),
        new Cell { CellReference = "B2", CellValue = new CellValue("1000") },
        new Cell { CellReference = "C2", CellValue = new CellValue("100") });
    var row3 = new Row { RowIndex = 3 };
    row3.Append(
        MakeCell("A3", "商品B"),
        new Cell { CellReference = "B3", CellValue = new CellValue("2000") },
        new Cell { CellReference = "C3", CellValue = new CellValue("200") });
    sheetData.Append(row1, row2, row3);
    worksheetPart.Worksheet = new Worksheet(sheetData);
    worksheetPart.Worksheet.Save();
    workbookPart.Workbook.Save();
}

static Cell MakeCell(string ref_, string text)
{
    return new Cell
    {
        CellReference = ref_,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(text))
    };
}

