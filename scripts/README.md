# scripts

リポジトリルートから実行する PowerShell / バッチの一覧。

| スクリプト | 用途 | 実行例 |
|-----------|------|--------|
| `build-sudachi-native.ps1` | Sudachi DLL・辞書のビルド（ビルド担当者向け） | `pwsh -File scripts/build-sudachi-native.ps1` |
| `build-dist.ps1` | スタンドアロン exe の publish と ZIP 作成 | `pwsh -File scripts/build-dist.ps1` |
| `build-dist.bat` | 上記のラッパー（cmd 用） | `scripts\build-dist.bat` |
| `check-webview-strings.ps1` | `index.html` と `UserMessages` の文言一致検査 | `pwsh -File scripts/check-webview-strings.ps1` |
| `create-cert-for-msix.ps1` | MSIX 署名用証明書の新規作成 | `.\scripts\create-cert-for-msix.ps1` |
| `export-cert-pfx-cer.ps1` | 既存証明書の PFX / CER エクスポート | `.\scripts\export-cert-pfx-cer.ps1 -Thumbprint <拇印>` |

## 配布 ZIP

```powershell
pwsh -File scripts/build-dist.ps1
# → installers\dist\Panoleon_win-x64.zip
```

## Sudachi 同梱物の更新

通常の開発者は実行不要。`tools/sudachi/` はリポジトリに含まれる。

```powershell
pwsh -File scripts/build-sudachi-native.ps1
# 完了後 tools/sudachi/ をリポジトリにコミット
```

## テスト用

| パス | 用途 |
|------|------|
| `testdata/New-SkipReproFiles.ps1` | スキップ再現用ファイル生成 |
| `testdata/README.md` | 詳細 |

## 共通

- `_repo.ps1` … 各 `.ps1` から読み込むヘルパー（直接実行しない）
