# ビルドと MSIX の作り方

開発者・配布担当者向け。**利用者が MSIX をインストールする手順**は [社内配布手順.md](社内配布手順.md) の「利用者向け」を参照する。

---

## 必要なもの

- Windows 10 / 11
- **.NET 8 SDK**（[ダウンロード](https://dotnet.microsoft.com/download/dotnet/8.0)）
- **.NET MAUI / Windows アプリ SDK** など、ソリューションをビルドできるワークロード（Visual Studio インストーラーで「.NET Multi-platform App UI 開発」等を有効化していること）

---

## 通常のビルド（.msix を作らない）

リポジトリのルートで:

```powershell
dotnet restore
dotnet build FullTextSearch.sln -c Release
```

実行確認のみなら:

```powershell
dotnet run --project src\FileSearch.Blazor\FileSearch.Blazor.csproj -c Release
```

---

## MSIX ファイルの作成（配布用）

`dotnet build` だけでは **.msix は生成されません**。**`dotnet publish`** を使います。

### 手順の流れ

1. **署名用証明書**を用意し、`FileSearch.Blazor.csproj` の `PackageCertificateThumbprint` と一致させる（新規なら `scripts\create-cert-for-msix.ps1`）。  
   → 詳細は [社内配布手順.md](社内配布手順.md) の「配布者向け → A. 証明書を用意する」
2. リポジトリのルートで次を実行する。

```powershell
dotnet publish src\FileSearch.Blazor\FileSearch.Blazor.csproj `
  -f net8.0-windows10.0.19041.0 `
  -c Release
```

3. 生成された **.msix** を次のようなパスから取り出す（バージョン番号は `Package.appxmanifest` の `Identity Version` に一致）。

```
src\FileSearch.Blazor\bin\Release\net8.0-windows10.0.19041.0\win10-x64\AppPackages\
  FileSearch.Blazor_1.0.0.0_Test\FileSearch.Blazor_1.0.0.0_x64.msix
```

4. 配布用に `installers\社内配布\` などへコピーし、同じフォルダに **.cer** と **インストール手順.txt** を揃える（[社内配布手順.md](社内配布手順.md) の「C. 配布用にまとめる」）。

### バージョンを上げるとき

`src\FileSearch.Blazor\Platforms\Windows\Package.appxmanifest` の `Identity` の **`Version="1.0.0.0"`** を変更してから、再度 `dotnet publish` する。

### トラブル

| 症状 | 対処 |
|------|------|
| 証明書が見つからない | 拇印と `create-cert-for-msix.ps1` / 証明書ストアを確認（[社内配布手順.md](社内配布手順.md)） |
| publish は成功するが .msix がない | `AppPackages` 配下のフォルダ名（`FileSearch.Blazor_x.x.x.x_Test`）をエクスプローラーで確認 |

---

## 証明書なし：ZIP で配布する exe（参考）

MSIX ではなく、自己完結型 exe を ZIP にまとめる場合:

```powershell
.\scripts\build-dist.bat
```

または

```powershell
.\scripts\build-dist.ps1
```

出力例: `installers\dist\FileSearch_win-x64.zip`（中身を解凍して `FileSearch.Blazor.exe` を実行）

詳細はスクリプト内コメントと [社内配布手順.md](社内配布手順.md) の「補足：証明書なしで配布したい場合」。

---

## 参照

- [社内配布手順.md](社内配布手順.md) — 証明書作成・拇印の csproj 反映・利用者向けインストール
- [インストールと環境構築.md](インストールと環境構築.md) — 開発環境の全体像
- `scripts\create-cert-for-msix.ps1` — 配布用 .cer の生成
- `scripts\build-dist.ps1` / `build-dist.bat` — ZIP 配布用ビルド
