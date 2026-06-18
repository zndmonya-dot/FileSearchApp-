# appmode.json と shared.json の設定

Panoleon を **管理者1台 + 利用者複数台** で使うときの設定の読み方。

---

## MSIX で使う場合（EXE・ZIP は不要）

社内配布が **MSIX のみ** のとき、ユーザーがファイルを置く場所は **ありません**。

| 誰 | やること |
|----|---------|
| **配布担当** | 管理者用 / 利用者用の **2種類の MSIX** をインストールしてもらう（中に設定が入っている） |
| **管理者** | アプリの **設定画面で保存** → ファイルサーバの `shared.json` が自動作成・更新される |
| **利用者** | インストールして起動するだけ。設定変更後は **再起動** |

```
ファイルサーバ（ここだけ全員共通）
  \\fileserver\panoleon\shared.json   ← 管理者の「保存」で自動作成
  \\fileserver\panoleon\index\        ← インデックス本体

各 PC
  Panoleon（MSIX）をインストールするだけ。exe 横に何か置く必要はない
```

`shared.json` の **場所（UNC パス）** は MSIX を作るときに配布担当が決め、パッケージに埋め込みます。  
**中身**（検索フォルダ・インデックス場所）は管理者がアプリから更新します。ファイルサイズ上限は実行時 **1MB 固定**（`indexMaxFileBytes` は JSON 例のみ）。

設定画面の「共有設定ファイル」に、サーバ上のパスが表示されます（MSIX に共有設定が含まれる場合のみ）。

---

## 全体像

```
ファイルサーバ
  \\fileserver\panoleon\
    shared.json   ← インデックス場所・検索フォルダ（全員共通）
    index\        ← 実際のインデックスデータ

管理者 PC（exe 横）
  appmode.json    ← mode=admin + shared.json の場所

利用者 PC（exe 横）
  appmode.json    ← mode=reference + shared.json の場所（パスは管理者と同じ）
```

| ファイル | 置き場所 | 誰が触る | 内容 |
|---------|---------|---------|------|
| **appmode.json** | 各 PC の exe 横 | 配布担当が手で置く | この PC が管理者か利用者か + shared.json のパス |
| **shared.json** | ファイルサーバ | **管理者が設定画面で保存** | インデックスの場所 + 検索対象フォルダ |

利用者は `shared.json` を**手で編集しません**。管理者が設定を変えて保存 → 利用者は**アプリを再起動**すれば追従します。

### 運用で「変えるもの」と「変えないもの」

| 項目 | いつ決める | あとから変わる？ | 配布先への反映 |
|------|-----------|----------------|---------------|
| **mode**（admin / reference） | MSIX/ZIP 配布時 | **変えない** | パッケージごとに固定 |
| **indexPath** | 管理者が設定画面で保存 | 変わりうる | `shared.json` 更新 → 利用者は**再起動** |
| **targetFolders** | 同上 | 変わりうる | 同上 |

MSIX では `appmode.json` はインストール後に編集できませんが、**変わるのはサーバ上の `shared.json` だけ**なので問題ありません。  
配布先の MSIX に入れるのは `sharedConfig`（`shared.json` の UNC パス）と `mode` だけで足ります。

```
管理者が設定変更 → 保存
       ↓
\\fileserver\panoleon\shared.json が更新される
       ↓
（必要なら再構築でインデックス本体も更新）
       ↓
利用者は Panoleon を再起動 → 最新の indexPath / targetFolders を読む
```

リアルタイム同期はしません。**再起動のタイミングで反映**します。

---

## 1. appmode.json（各 PC）

### 管理者 PC の例

`installers/社内配布/appmode.admin.example.json` を `appmode.json` にコピーして編集。

```json
{
  "mode": "admin",
  "sharedConfig": "\\\\fileserver\\panoleon\\shared.json"
}
```

### 利用者 PC の例

`installers/社内配布/appmode.reference.example.json` を `appmode.json` にコピー。

```json
{
  "mode": "reference",
  "sharedConfig": "\\\\fileserver\\panoleon\\shared.json"
}
```

### キー一覧

| キー | 必須 | 説明 |
|------|------|------|
| `mode` | 推奨 | `admin` = 管理者 / `reference` = 利用者（参照専用） |
| `sharedConfig` | 社内運用では必須 | 共有設定 `shared.json` の UNC パス |

`mode` を省略すると管理者扱いになります。

JSON 内の `//` コメントはそのまま書いて構いません（アプリが読み飛ばします）。

---

## 2. shared.json（ファイルサーバ上・1つ）

例: `installers/社内配布/shared.example.json`

```json
{
  "indexPath": "\\\\fileserver\\panoleon\\index",
  "targetFolders": [
    "\\\\fileserver\\share\\documents"
  ]
}
```

| キー | 説明 |
|------|------|
| `indexPath` | インデックスのフォルダ（UNC） |
| `targetFolders` | 検索対象フォルダの配列 |
| `indexMaxFileBytes` | **（将来／例示のみ）** JSON 例に含められるが、現行の `AppModeService` は読み込まない。実行時の上限は `ContentLimits.IndexMaxFileBytesForExtract`（**1MB 固定**） |

```json
{
  "indexPath": "\\\\fileserver\\panoleon\\index",
  "targetFolders": ["\\\\fileserver\\share\\documents"],
  "indexMaxFileBytes": 1048576
}
```

上記の `indexMaxFileBytes` はスキーマ例であり、**現行ビルドでは未参照**。サイズ上限を変える機能は将来用に JSON に残しているに留まる。

---

## 3. 初回セットアップ手順

1. ファイルサーバに `\\fileserver\panoleon\` フォルダを用意する
2. **管理者 PC** の exe 横に `appmode.json`（`mode: admin` + `sharedConfig`）を置く
3. Panoleon を起動 → 設定で検索フォルダ・インデックスパスを入力 → **保存**
4. **再構築**でインデックスを作成
5. **利用者 PC** の exe 横に `appmode.json`（`mode: reference` + **同じ** `sharedConfig`）を置く
6. 利用者は起動するだけ（設定変更は不要）

---

## 4. 管理者があとから変えたとき

1. 管理者 PC で設定を変更して **保存**（`shared.json` が更新される）
2. 必要なら **再構築**
3. 利用者には **アプリの再起動** を案内する

リアルタイム同期はしません。再起動時に `shared.json` を読み直します。

---

## 5. 開発用（1台だけで試す）

exe 横の `appmode.json` で `sharedConfig` を**書かない**（またはコメントアウト）:

```json
{
  "mode": "admin"
}
```

このときは従来どおり `%LocalAppData%\FullTextSearch\settings.json` の設定を使います。

---

## 6. モードごとにできること

| 操作 | admin | reference |
|------|-------|-----------|
| 検索・プレビュー | ○（件数上限なし） | ○（件数上限なし） |
| インデックス再構築 | ○ | × |
| フォルダ・インデックスパス変更 | ○ | ×（閲覧のみ） |
| テーマ・対象拡張子 | ○ | ○（個人設定） |

---

## 7. MSIX で配布するとき

ZIP（`scripts/build-dist.ps1`）と MSIX では **appmode.json の扱いが違います**。

### 違いの要点

| 項目 | ZIP 配布 | MSIX 配布 |
|------|---------|-----------|
| `appmode.json` の場所 | ZIP を展開したフォルダ（exe 横） | **パッケージ内に同梱**（インストール先は読み取り専用） |
| インストール後の編集 | フォルダ内の `appmode.json` を書き換え可能 | **通常は編集できない**（`WindowsApps` 配下） |
| `mode` の切り替え | exe 横の JSON を差し替える | **publish 前に決めて MSIX に焼き込む** |
| `shared.json`（サーバ） | 同じ（UNC・管理者が保存で更新） | 同じ |

`shared.json` はファイルサーバ上なので、MSIX でも ZIP でも動きは同じです。  
違うのは **各 PC が管理者か利用者か（`mode`）をあとから変えられるか** だけです。

### MSIX の推奨: 2種類のパッケージを作る

利用者と管理者で **別々の .msix** を配布します。

```
Panoleon_Admin.msix      … appmode.json に mode: admin
Panoleon_Reference.msix  … appmode.json に mode: reference
```

どちらも `sharedConfig` は**同じ UNC** を指します。

#### 手順（管理者用 MSIX）

1. `src\FileSearch.Blazor\appmode.json` を管理者用に編集する

```json
{
  "mode": "admin",
  "sharedConfig": "\\\\fileserver\\panoleon\\shared.json"
}
```

2. `dotnet publish` で MSIX を生成（[ビルドとMSIX作成.md](ビルドとMSIX作成.md) 参照）
3. できた `.msix` を `Panoleon_Admin.msix` などの名前で配布

#### 手順（利用者用 MSIX）

1. 同じく `appmode.json` を利用者用に編集する

```json
{
  "mode": "reference",
  "sharedConfig": "\\\\fileserver\\panoleon\\shared.json"
}
```

2. 再度 `dotnet publish` して別の `.msix` を生成
3. `Panoleon_Reference.msix` として配布

`appmode.json` は csproj で MSIX に同梱されます（`CopyToPublishDirectory`）。  
**publish 直前の内容がそのままパッケージに入ります。**

### MSIX 利用者の初回〜運用

1. `.cer` を信頼 → 利用者用 `.msix` をインストール
2. 起動すると `shared.json` からインデックス・フォルダを読む（管理者が先に設定・再構築済みであること）
3. 管理者がフォルダ等を変えたとき → 利用者は **アプリ再起動**（MSIX の再インストールは不要）

### ZIP のほうが向いている場合

- 1つの ZIP を配って、**展開先で `appmode.json` だけ差し替え**たい
- MSIX を2種類作らず運用したい
- 証明書配布を避けたい（`scripts\build-dist.ps1`）

ZIP 配布では `installers/社内配布/appmode.*.example.json` をコピーして exe 横に置くだけで済みます。

### まとめ（MSIX）

```
[配布担当]
  publish 前に appmode.json を編集
    → Admin 用 MSIX / Reference 用 MSIX の2本
  sharedConfig は全員同じ UNC

[管理者 PC]
  Admin 用 MSIX をインストール
  設定保存 → \\fileserver\panoleon\shared.json 更新
  再構築

[利用者 PC]
  Reference 用 MSIX をインストール
  起動するだけ（再起動で shared.json に追従）
```
