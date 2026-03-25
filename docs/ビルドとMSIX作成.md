# ビルドと MSIX 作成（開発者向け）

`dotnet build` だけでは **MSIX は生成されません**。配布用パッケージを出すには **`dotnet publish`** が必要です。証明書の用意・拇印の設定・利用者への .cer 配布などの全体フローは **[社内配布手順.md](社内配布手順.md)** を参照してください。本書は **publish コマンドと成果物の取り出し** に絞る。

---

## 前提

- Windows、**.NET 8 SDK** が入っていること
- MSIX に署名するには、**個人証明書ストア** に秘密鍵付き証明書があり、`FileSearch.Blazor.csproj` の **`PackageCertificateThumbprint`** がそれを指していること（詳細は社内配布手順の「3. 配布者の作業」）

---

## MSIX を生成する

リポジトリルートで:

```powershell
dotnet publish src\FileSearch.Blazor\FileSearch.Blazor.csproj -f net8.0-windows10.0.19041.0 -c Release
```

- フレームワーク識別子（`-f`）はプロジェクトに合わせる。SDK やワークロードの案内に従い、必要なら `dotnet workload restore` 等を実行する。
- **「証明書が見つからない」** 場合は、ストアと Thumbprint を社内配布手順に沿って確認する。

### 出力パスの例

`Package.appxmanifest` の `Identity` / `Version` によりパスは変わる。例:

`src\FileSearch.Blazor\bin\Release\net8.0-windows10.0.19041.0\win10-x64\AppPackages\FileSearch.Blazor_1.0.0.0_Test\FileSearch.Blazor_1.0.0.0_x64.msix`

- バージョンを上げる: `Platforms\Windows\Package.appxmanifest` の `Version` を変更してから再度 `publish` する。

---

## デバッグ実行（MSIX 不要）

```powershell
dotnet run --project src\FileSearch.Blazor
```

---

## 証明書なしの ZIP 配布（参考）

社内で .cer を渡したくない場合は、ルートの **`scripts\build-dist.bat`**（または `build-dist.ps1`）でスタンドアロン ZIP を生成できる。publish 前に `check-webview-strings.ps1` が走る。詳細は **[社内配布手順.md](社内配布手順.md)** の「5. 証明書なしの配布（参考）」を参照。

---

## 関連

- [インストールと環境構築.md](インストールと環境構築.md)（利用者向け MSIX インストールと開発者向けの読み分け）
- [README.md](../README.md)（プロジェクト概要・`dotnet build` の例）
