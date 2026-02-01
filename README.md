# 社内ファイルサーバ向け全文検索システム

Windows向けの高速全文検索アプリケーションです。ファイルサーバ内のWord、Excel、PDF、テキストファイルなどをキーワードで検索し、上下キーで素早くプレビューを切り替えながら目的のファイルを見つけることができます。

## 機能

- **高速全文検索**: Lucene.NET + 日本語形態素解析（Sudachi・モード C）による高速検索
- **様式保持プレビュー**: Windowsプレビューハンドラを使用し、Office 365と同等の品質でプレビュー
- **キーボード操作**: 上下キーで検索結果を移動、即座にプレビュー切り替え
- **マッチ箇所表示**: 検索キーワードがヒットした箇所をハイライト付きで抜粋表示
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
- Microsoft Office 365（プレビュー機能用）

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

1. 検索ボックスにキーワードを入力
2. 検索結果が左側のリストに表示
3. 上下キーで結果を移動、右側にプレビューが表示

### キーボードショートカット

| ショートカット | 動作 |
|--------------|------|
| 上下キー | 検索結果を移動（プレビュー即時切り替え） |
| Enter | 関連アプリでファイルを開く |
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

## 使用ライブラリ

- Lucene.NET 4.8 - 全文検索エンジン
- Sudachi（SudachiPy） - 日本語形態素解析（モード C）
- DocumentFormat.OpenXml - Office文書テキスト抽出
- PdfPig - PDFテキスト抽出
- MaterialDesignThemes - Material Design UI
- CommunityToolkit.Mvvm - MVVMフレームワーク

