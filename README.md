# Panoleon（社内ファイルサーバ向け全文検索）

Windows向けの高速全文検索アプリケーション **Panoleon**（Panoptic + Chameleon）です。ファイルサーバ内のWord、Excel、PDF、テキストファイルなどをキーワードで検索し、ツリーでファイルを選ぶと右側にテキスト抽出プレビューが表示され、目的のファイルを見つけることができます。

## 機能

- **ウィンドウタイトル**: アプリ名「Panoleon」をウィンドウのタイトルバーおよび Alt+Tab に表示
- **高速全文検索**: Lucene.NET + 日本語形態素解析（Sudachi・モード C）による高速検索
- **テキスト抽出プレビュー**: 全形式（Word/Excel/PDF/テキスト）をテキスト抽出した行表示でプレビュー。表形式・ネイティブプレビューは行わない
- **ツリー操作**: 検索結果ツリーでフォルダをクリックして開閉。フォルダを開くと右側にフォルダ一覧が表示される
- **キーボード・操作**: 検索欄で Enter で検索実行、Esc で閲覧モードへ復帰（クエリ・結果・選択をクリア）。ファイルを開く・フォルダを開くはプレビュー上の「開く」「フォルダ」ボタン
- **マッチ箇所表示**: 検索キーワードがヒットした箇所をハイライト付きで抜粋表示
- **行単位ハイライトナビゲーション**: プレビュー画面で「次へ」「前へ」ボタンにより、ハイライト行単位で移動（WinMerge風）。同じ行内の複数のマッチをスキップして次の行に移動
- **二重起動防止**: 同一ユーザーセッション内で 1 インスタンスのみ（2 回目は既存ウィンドウを前面表示）
- **定期インデックス更新**: 設定で JST 0–23 時の実行時刻を指定すると、**差分更新**がタイマーで実行される（空リストで無効）。検索中・直近の検索操作から一定時間以内は見送る

## 対応ファイル形式（計 31 種）

- **Office**: Word (.doc, .docx), Excel (.xls, .xlsx, .xlsm), PowerPoint (.pptx)
- **PDF**: .pdf
- **Outlook**: .msg
- **テキスト・スクリプト**: .txt, .md, .log, .csv, .bat, .ps1, .sh
- **ソース・設定**: .html, .xml, .cs, .java, .ts, .dfm, .pas, .dpr, .dpk, .ini, .env, .json, .py, .css, .sql

正は `src/FullTextSearch.Infrastructure/Extractors/SupportedExtensions.cs`。詳細は [docs/静的定義一覧.md](docs/静的定義一覧.md) を参照。

## 動作要件

- **Windows 専用**（Windows 10/11）
- **開発・実行時**: .NET 8 SDK（Sudachi 同梱物は `tools/sudachi/`。手順は [docs/インストールと環境構築.md](docs/インストールと環境構築.md)）
- **配布版 MSIX / ZIP の利用時**: 利用者 PC に Python / Rust は不要（辞書・`sudachi_ffi.dll` は同梱）
- （任意）Microsoft Office … 検索・プレビューはテキスト抽出のため不要。ファイルを開く際に利用

**インストール（利用者）と環境構築（開発者）の違い**は [docs/インストールと環境構築.md](docs/インストールと環境構築.md) を参照。

**管理者／利用者の配布設定（appmode.json）**は [docs/appmode設定.md](docs/appmode設定.md) を参照。

## セットアップ（開発者向け環境構築）

### 1. .NET 8 SDK と MAUI ワークロード

https://dotnet.microsoft.com/download/dotnet/8.0 から .NET 8 SDK をインストールし、MAUI ワークロードを入れる。

```powershell
dotnet workload install maui
```

### 2. ビルド

ソースは社内 SVN で **checkout / update** 済みであること（手順は [docs/インストールと環境構築.md](docs/インストールと環境構築.md) の 2.4）。

```powershell
cd C:\全文検索システム
dotnet restore
dotnet build
```

初回ビルド時に Sudachi 辞書 ZIP（`tools/sudachi/resources/system_core.dic.zip`）が自動展開される。  
環境構築の詳細（トラブルシュート含む）は [docs/インストールと環境構築.md](docs/インストールと環境構築.md) を参照。

### 3. 実行

```powershell
dotnet run --project src\FileSearch.Blazor
```

**注意**: アナライザを変更した場合は、既存のインデックスは互換性がありません。設定画面で「インデックスを再構築」を実行してください。

## 使い方

### 初回設定

1. 右上の設定ボタンをクリック
2. 「検索対象フォルダ」にファイルサーバのパスを追加（例: `\\server\share`）
3. 「保存」をクリック
4. サイドバーフッターの「再構築」ボタンをクリックし、ダイアログで「全体を再構築」を実行（初回）

### 検索

1. 検索ボックスにキーワードを入力してEnterキーを押す
2. 検索結果が左側のツリーに表示される
3. ツリーのフォルダノードをクリックすると開閉し、開いたフォルダは右側にフォルダ一覧が表示される
4. ツリーのファイルノードをクリックして選択すると、右側にプレビューが表示される
5. プレビュー画面で「次へ」「前へ」ボタンにより、ハイライト行単位で移動できる（WinMerge風）。同じ行内の複数のマッチをスキップして次の行に移動

### キーボード

| キー | 動作（検索欄にフォーカスがあるとき） |
|------|--------------------------------------|
| Enter | 検索実行 |
| Esc | 検索語をクリアし閲覧モードへ戻る（拡張子フィルタ開時は先に閉じる） |

ファイルを開く・フォルダを開くはプレビュー画面のボタンを使用する。グローバルな Ctrl+Enter / Ctrl+C / Ctrl+F は **未実装**。

## プロジェクト構成

```
（リポジトリルート）/
├── native/sudachi-ffi/               # Sudachi ネイティブ FFI（Rust）
├── scripts/                          # ビルド・配布・検証（一覧は scripts/README.md）
├── tools/sudachi/                    # sudachi_ffi.dll・辞書リソース（publish 同梱）
├── src/
│   ├── FileSearch.Blazor/            # Blazor Hybrid (MAUI) アプリ（メインUI）
│   ├── FullTextSearch.Core/           # コアロジック（インターフェース、モデル）
│   ├── FullTextSearch.Infrastructure/  # インフラ実装（Lucene、Sudachi、抽出器）
│   └── SudachiNative.targets          # MSBuild: Sudachi 同梱
└── tests/
    └── FullTextSearch.Tests/          # ユニットテスト
```

### Blazor MAUI の実行・インストーラ

- **実行**: `dotnet run --project src\FileSearch.Blazor`
- **Release ビルド**: `dotnet build FullTextSearch.sln -c Release`  
  → Windows の MSIX パッケージは `src\FileSearch.Blazor\bin\Release\...\*.msix` に出力されます。
- **インストーラを Git で管理する場合**: ビルド後に `installers/` フォルダを作成し、そこに `.msix` をコピーしてコミットできます（ルートの `.gitignore` では `installers/` は無視していません）。
- **ビルド・MSIX 作成**: [docs/ビルドとMSIX作成.md](docs/ビルドとMSIX作成.md)（`dotnet publish` で .msix を出す手順）
- **社内配布**: MSIX と証明書（配布元で用意した .cer）をセットで配布する手順・利用者向けインストールは [docs/社内配布手順.md](docs/社内配布手順.md) を参照。配布用ファイル（.msix / .cer / 手順）は `installers/社内配布/` に置く想定。証明書不要のスタンドアロン exe（ZIP）配布は `scripts\build-dist.bat` で生成できます。
- **ドキュメント一覧**: 設計書・配布手順・調査メモの一覧は [docs/README.md](docs/README.md) を参照。

## 使用ライブラリ

- Lucene.NET 4.8 - 全文検索エンジン
- Sudachi（sudachi.rs ネイティブ / モード C）- 日本語形態素解析（Python 不要）
- DocumentFormat.OpenXml - Office 文書テキスト抽出（.docx / .xlsx / .pptx）
- NPOI - レガシー Office（.doc / .xls）テキスト抽出
- PdfPig - PDFテキスト抽出
- UTF.Unknown (UtfUnknown) - テキストファイルのエンコーディング自動判定

