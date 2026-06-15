# Sudachi ネイティブ（sudachi.rs）

形態素解析は **Python 不要** のネイティブ DLL（`sudachi_ffi.dll`）で実行します。

## 同梱方針

`tools/sudachi/` 一式は **リポジトリにコミット** します。辞書は `system_core.dic.zip` として同梱し、初回 `dotnet build` で `resources/` に展開されます。展開済みの `system_core.dic` は `.gitignore` 対象です。

```
[ビルド担当 PC]  build-sudachi-native.ps1（初回 or DLL 更新時のみ）
        ↓
[リポジトリ]     tools/sudachi/ をコミット
        ↓
[開発者 PC]      checkout / update → dotnet build
```

社内では **SVN**、開発用ミラーで Git など、利用する VCS は環境に合わせてください。手順は「コミットして共有する」点だけ同じです。

### リポジトリに含めるもの

| ファイル | 必須 | 説明 |
|---------|------|------|
| `sudachi_ffi.dll` | はい | win-x64 用ネイティブ DLL |
| `resources/system_core.dic.zip` | はい | 辞書 ZIP（約 73MB。ビルド時に展開） |
| `resources/sudachi.json` | はい | 設定 |
| `resources/char.def` | はい | リソース |
| `resources/unk.def` | はい | リソース |

### DLL の更新頻度

- **通常の開発** … 更新不要
- **再ビルド** … Sudachi バージョン変更（現在 v0.6.11）、`native/sudachi-ffi/` の変更時
- **辞書のみ差し替え** … 可能。トークン分割が変わる場合はインデックスの全体再構築が必要

---

## ビルド担当者向け（初回・更新時）

外部から Sudachi 辞書等を取得できる環境でのみ実行します。

更新時は `-Rebuild` で DLL を強制再ビルドできます。

```powershell
pwsh -File scripts/build-sudachi-native.ps1        # 不足分のみ取得・ビルド
pwsh -File scripts/build-sudachi-native.ps1 -Rebuild
```

### SVN で登録する例

```text
svn add tools\sudachi\sudachi_ffi.dll tools\sudachi\resources\system_core.dic.zip
svn commit -m "Sudachi 同梱物を更新（v0.6.11）"
```

### Git で登録する例

```powershell
git add tools/sudachi/
git commit -m "Sudachi 同梱物を更新（v0.6.11）"
```

## バージョン

| 項目 | 値 |
|------|-----|
| sudachi.rs | v0.6.11 |
| モード | C |
| プラットフォーム | Windows x64 |
