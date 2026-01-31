# Office サンプルファイル

全文検索システムのプレビュー・検索テスト用のサンプルです。

## 生成方法

```powershell
cd tools\SampleOfficeGenerator
dotnet run
```

上記で `sample.docx` と `sample.xlsx` がこのフォルダに作成されます。

## ファイル

- **sample.docx** … Word 文書（消費税などのテキスト入り）
- **sample.xlsx** … Excel ブック（項目・金額・消費税の表）
- **sample.pptx** … PowerPoint は手動で「新規→タイトルだけ入力→保存」するか、既存の .pptx をここに置いてください。

## 使い方

1. 全文検索アプリの「設定」で「対象フォルダ」にこの `samples` フォルダを追加
2. 「インデックス更新」を実行
3. 検索で「消費税」などでヒットするか、ファイルを選んでプレビューを確認
