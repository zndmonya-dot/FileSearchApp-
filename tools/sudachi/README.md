# Sudachi ネイティブ（sudachi.rs）

形態素解析は **Python 不要** のネイティブ DLL（`sudachi_ffi.dll`）で実行します。

## ビルド

```powershell
pwsh -File scripts/build-sudachi-native.ps1
```

- Rust（stable）が必要（初回 `dotnet build` でも DLL が無ければ自動実行）
- Sudachi core 辞書（約 70MB）を CloudFront から取得し `resources/system_core.dic` に配置

## 同梱物

| ファイル | 説明 |
|---------|------|
| `sudachi_ffi.dll` | sudachi.rs v0.6.11 ベースの FFI |
| `resources/sudachi.json` | 設定 |
| `resources/char.def` / `unk.def` | リソース |
| `resources/system_core.dic` | 辞書（gitignore） |
