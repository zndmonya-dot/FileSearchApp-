# Panoleon — SVN 用ビルド最小ソース

`dotnet build` に必要な **ソースだけ** です。`bin/` / `obj/` 等のビルド成果物は **含みません**。

## 含まれるもの

| パス | 内容 |
|------|------|
| `FullTextSearch.sln` | ソリューション（テスト無し） |
| `Directory.Build.props` | 共通ビルド設定 |
| `src/` | 4 プロジェクト + `SudachiNative.targets` |
| `tools/sudachi/sudachi_ffi.dll` | Sudachi ネイティブ DLL |
| `tools/sudachi/resources/*.zip` 等 | 辞書 ZIP・設定（**展開済み `.dic` は含めない**） |
| `FILE_LIST.md` | 末端ファイル一覧（**sync 実行のたびに自動更新**） |

## 含まれないもの

- `bin/` / `obj/` / `publish/` / `.vs/`
- `tests/` / `docs/` / `scripts/` / `installers/`
- `tools/sudachi/resources/system_core.dic`（初回ビルド時に ZIP から展開）

## 再生成

```powershell
pwsh -File scripts/sync-svn-export.ps1
```

## ビルド（checkout 先で）

```powershell
dotnet restore
dotnet build
```

要: .NET 8 SDK + `dotnet workload install maui`

## SVN

- `svn-ignore.txt` で `bin` / `obj` / `system_core.dic` を無視すること
- **ビルド後に `svn add` しない**（成果物を誤って載せない）
