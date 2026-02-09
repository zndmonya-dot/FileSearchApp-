# 社内ファイルサーバ向け全文検索システム

Windows向けの高速全文検索アプリケーションです。ファイルサーバ内のWord、Excel、PDF、テキストファイルなどをキーワードで検索し、ツリーでファイルを選ぶと右側にテキスト抽出プレビューが表示され、目的のファイルを見つけることができます。

## 機能

- **ウィンドウタイトル**: アプリ名「全文検索システム」をウィンドウのタイトルバーおよび Alt+Tab に表示
- **高速全文検索**: Lucene.NET + 日本語形態素解析（Sudachi・モード C）による高速検索
- **テキスト抽出プレビュー**: 全形式（Word/Excel/PDF/テキスト）をテキスト抽出した行表示でプレビュー。表形式・ネイティブプレビューは行わない
- **ツリー操作**: 検索結果ツリーでフォルダをクリックして開閉。フォルダを開くと右側にフォルダ一覧が表示される
- **キーボード操作**: 検索結果はツリーをクリックで選択。Enter で検索実行・ファイルを開く、Ctrl+Enter でフォルダを開く
- **マッチ箇所表示**: 検索キーワードがヒットした箇所をハイライト付きで抜粋表示
- **行単位ハイライトナビゲーション**: プレビュー画面で「次へ」「前へ」ボタンにより、ハイライト行単位で移動（WinMerge風）。同じ行内の複数のマッチをスキップして次の行に移動
- **自動インデックス更新**: ファイル変更を検知して自動的にインデックスを更新

## 対応ファイル形式

- Word (.docx)
- Excel (.xlsx)
- PowerPoint (.pptx)
- PDF (.pdf)
- テキストファイル (.txt, .csv, .log, .md)
- ソースコード (.cs, .js, .ts, .py, .java, .htmlなど)

## 動作要件

- **Windows 専用**（Windows 10/11）
- .NET 8 SDK
- **Python 3**（形態素解析用）
- **SudachiPy** および辞書（`pip install sudachipy sudachidict_core`）
- （任意）Microsoft Office … 検索・プレビューはテキスト抽出のため不要。ファイルを開く際に利用

## セットアップ

### 1. .NET 8 SDKのインストール

https://dotnet.microsoft.com/download/dotnet/8.0 から.NET 8 SDKをダウンロードしてインストールしてください。

### 2. ビルド

```powershell
cd C:\全文検索システム
dotnet restore
dotnet build
```

### 3. SudachiPy のインストール（形態素解析用）

```powershell
pip install sudachipy sudachidict_core
```

### 4. 実行

```powershell
dotnet run --project src\FileSearch.Blazor
```

**注意**: アナライザを変更した場合は、既存のインデックスは互換性がありません。設定画面で「インデックスを再構築」を実行してください。

## 使い方

### 初回設定

1. 右上の設定ボタンをクリック
2. 「検索対象フォルダ」にファイルサーバのパスを追加（例: `\\server\share`）
3. 「保存」をクリック
4. メイン画面下部の「インデックスを再構築」ボタンをクリック

### 検索

1. 検索ボックスにキーワードを入力してEnterキーを押す
2. 検索結果が左側のツリーに表示される
3. ツリーのフォルダノードをクリックすると開閉し、開いたフォルダは右側にフォルダ一覧が表示される
4. ツリーのファイルノードをクリックして選択すると、右側にプレビューが表示される
5. プレビュー画面で「次へ」「前へ」ボタンにより、ハイライト行単位で移動できる（WinMerge風）。同じ行内の複数のマッチをスキップして次の行に移動

### キーボードショートカット

| ショートカット | 動作 |
|--------------|------|
| Enter（検索欄で） | 検索実行 |
| Enter（ファイル選択時） | 関連アプリでファイルを開く |
| Ctrl+Enter | フォルダを開く |
| Ctrl+C | ファイルパスをコピー |
| Ctrl+F | 検索ボックスにフォーカス |
| Esc | 検索クリア |

## プロジェクト構成

```
FullTextSearch/
├── src/
│   ├── FileSearch.Blazor/            # Blazor Hybrid (MAUI) アプリ（メインUI）
│   ├── FullTextSearch.Core/           # コアロジック（インターフェース、モデル）
│   └── FullTextSearch.Infrastructure/  # インフラ実装（Lucene、プレビュー）
└── tests/
    └── FullTextSearch.Tests/          # ユニットテスト
```

### Blazor MAUI の実行・インストーラ

- **実行**: `dotnet run --project src\FileSearch.Blazor`
- **Release ビルド**: `dotnet build FullTextSearch.sln -c Release`  
  → Windows の MSIX パッケージは `src\FileSearch.Blazor\bin\Release\...\*.msix` に出力されます。
- **インストーラを Git で管理する場合**: ビルド後に `installers/` フォルダを作成し、そこに `.msix` をコピーしてコミットできます（ルートの `.gitignore` では `installers/` は無視していません）。
- **社内配布**: MSIX と証明書（配布元で用意した .cer）をセットで配布する手順・利用者向けインストールは [docs/社内配布手順.md](docs/社内配布手順.md) を参照。配布用ファイル（.msix / .cer / 手順）は `installers/社内配布/` に置く想定。証明書不要のスタンドアロン exe（ZIP）配布は `scripts\build-dist.bat` で生成できます。
- **ドキュメント一覧**: 設計書・配布手順・調査メモの一覧は [docs/README.md](docs/README.md) を参照。

## 使用ライブラリ

- Lucene.NET 4.8 - 全文検索エンジン
- Sudachi（SudachiPy） - 日本語形態素解析（モード C、Python で実行）
- DocumentFormat.OpenXml - Office文書テキスト抽出
- PdfPig - PDFテキスト抽出
- UTF.Unknown (UtfUnknown) - テキストファイルのエンコーディング自動判定

