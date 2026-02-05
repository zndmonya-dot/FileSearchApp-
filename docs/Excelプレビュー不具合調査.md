# Excel プレビューできない不具合 原因調査

**注**: 現行実装では Excel 専用の HTML（表形式）プレビューは行っておらず、.xlsx も他形式と同様に**テキスト抽出の行表示**のみです。本ドキュメントは過去の設計・調査の記録として残しています。

## 事象

- .xlsx ファイルを選択してもプレビューが表示されない（または空白・エラーになる）。

---

## 処理の流れ

1. ユーザーが検索結果などからファイルを選択 → `LoadPreview(path)` が呼ばれる。
2. `PreviewService.GetPreviewAsync(path, searchQuery)` が呼ばれる。
3. `ext = Path.GetExtension(path).ToLowerInvariant()` → `.xlsx` の場合は `".xlsx"`。
4. `_extractorFactory.GetExtractor(ext)` で抽出器を取得。
   - **登録順**: `MauiProgram` で `OfficeExtractor` → `PdfExtractor` → `TextFileExtractor`。
   - **GetExtractor**: `FirstOrDefault(e => e.CanExtract(extension))`。
   - `.xlsx` を CanExtract するのは **OfficeExtractor のみ**（TextFileExtractor の対象には .xlsx は含まれない）。
   - → **extractor は必ず OfficeExtractor**。
5. 条件 `extractor?.PreviewCategory == PreviewCategory.Office && ext == ".xlsx"` を判定。
   - OfficeExtractor.PreviewCategory は `Office` → **条件は真**。
6. `OfficeExtractor.ExtractExcelAsHtml(path, query)` を実行。
   - 成功時: HTML 文字列を返す（空でも `<p>データがありません</p>` や `<p class="error">...</p>` のいずれか）。
   - 例外時: メソッド内で catch し、`"<p class=\"error\">エラー: ..."` を返す（null は返さない）。
7. `PreviewService` で `new PreviewResult { Mode = "html", Html = html, ... }` を返す。
8. `Home.razor.cs` で `previewMode = result.Mode`, `previewHtml = result.Html`, `_previewLines = result.Lines` を設定。
9. `FilePreviewView` で `PreviewMode == "html" && !string.IsNullOrEmpty(PreviewHtml)` のとき HTML を表示。

---

## 想定した原因

### 1. 【最も有力】HTML が空のときにコードビューに落ち、空テーブルに見えていた

- **内容**: `PreviewMode == "html"` だが `PreviewHtml` が null または空文字の場合、従来は `else` に入り「コードビュー」（行リスト）を表示していた。
- コードビューでは `PreviewLines`（＝ Excel 時は未設定で空配列）を表示するため、**何も表示されない空のテーブル**になる。
- ユーザーから見ると「プレビューできない」と判断しうる。
- **補足**: `ExtractExcelAsHtml` の実装上は null を返さないが、戻り値を渡す過程や将来の変更で null/空になる可能性を考慮。

### 2. result.Lines が未設定による null 参照

- **内容**: Excel 用の `PreviewResult` で `Lines` を明示していなかったため、初期化の仕方によっては `result.Lines` が null と解釈される可能性があった。
- **影響**: `_previewLines = result.Lines` で null が入り、`previewLinesDisplay` の `_previewLines.Select(...)` で NullReferenceException。
- **補足**: `PreviewResult.Lines` の既定値は `Array.Empty<PreviewLineResult>()` のため、通常は null にはならないが、防御的に対応。

### 3. 拡張子の大文字小文字

- **内容**: 稀に `.XLSX` など大文字で渡る場合に、以前の `ext == ".xlsx"` だけでは一致しない可能性があった。
- **対応**: `string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase)` に変更済み（かつ `ext` は既に `ToLowerInvariant()` 済みのため二重の安全策）。

### 4. その他（ファイル・環境要因）

- **SpreadsheetDocument.Open の例外**: ファイルがロック中・破損・パス誤りなどで例外 → `ExtractExcelAsHtml` 内で catch され、エラー用 HTML が返る。プレビューには「エラー: ...」と出る想定。
- **キャンセル**: 別ファイルにすぐ切り替えると `GetPreviewAsync` 内でキャンセルされ、結果を反映しないまま return する場合あり。その場合は「前のファイルの表示のまま」や「読み込み中のまま」になりうる。

---

## 実施した修正（要約）

| 箇所 | 内容 |
|------|------|
| PreviewService | Excel 時は `Lines` / `LineCount` を明示。`Html` が null のときのフォールバック文字列を設定。拡張子は OrdinalIgnoreCase で比較。 |
| Home.razor.cs | `_previewLines = result.Lines ?? Array.Empty<...>()`。`previewLinesDisplay` で `_previewLines` を null 安全に参照。 |
| FilePreviewView | `PreviewMode == "html"` のとき、`PreviewHtml` が空でも HTML 用ブロックに入るように変更。空の場合は「プレビューを読み込めませんでした」を表示。コードビューで `PreviewLines` を null 安全に参照。 |
| preview.css | `.preview-empty` のスタイルを追加。 |

---

## 再現確認のポイント

- .xlsx を選択したとき、「表形式」と出て HTML プレビューが表示されるか。
- エラー時は「[Excelプレビューエラー] ...」または「エラー: ...」のメッセージが出るか。
- 高速で別ファイルに切り替えた場合、キャンセルで結果が未反映になっていないか。

---

## 関連コード

| ファイル | 役割 |
|----------|------|
| PreviewService.GetPreviewAsync | 拡張子判定・Excel 時は ExtractExcelAsHtml 呼び出しと PreviewResult 返却 |
| OfficeExtractor.ExtractExcelAsHtml | Open XML で .xlsx を開き HTML テーブル生成 |
| Home.LoadPreview | 結果を previewMode / previewHtml / _previewLines に反映 |
| FilePreviewView.razor | Mode=html 時は HTML 表示、それ以外は行リスト表示 |
