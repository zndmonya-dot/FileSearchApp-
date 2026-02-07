# ユニコードに応じた対応の調査

全文検索システムにおける Unicode 関連の扱いを調査した結果です。

---

## 1. 現状の対応一覧

| 箇所 | 対応内容 | ファイル・行 |
|------|----------|--------------|
| **プレビュー・ハイライト** | **NFC 正規化**（本文・検索語を FormC に統一） | `PreviewService.cs` 81–89 行 |
| **検索クエリ** | 全角スペース U+3000 → 半角スペースに統一 | `LuceneSearchService.cs` NormalizeQueryString 384–391 行 |
| **インデックス** | Unicode 正規化なし（抽出テキストをそのまま投入） | `LuceneIndexService.cs` TryGetIndexedDocumentAsync → CreateLuceneDocument |
| **Sudachi トークン** | UTF-8 バイト数制限（32765 バイト）で分割、C# 側で正規化なし | `SudachiTokenizer.cs` |
| **テキストファイル読み込み** | UtfUnknown でエンコーディング検出、デコード結果は正規化なし | `TextFileExtractor.cs` ReadTextWithAutoEncoding |
| **文字列比較** | パス・拡張子などで `StringComparison.OrdinalIgnoreCase` を使用 | 各所 |

---

## 2. プレビュー・ハイライト（NFC 正規化）

**目的**: 検索語と本文の Unicode 正規化形式（合成形 NFC / 分解形 NFD）が異なると、`Regex` でマッチせずハイライトが付かない問題を防ぐ。

**実装**:
- 本文 `content` を `NormalizationForm.FormC` (NFC) で正規化（未正規化の場合のみ）。
- 検索語（スペース・全角スペースで分割した各語）も同様に NFC で正規化。
- その後、`Regex.Escape(term)` と `RegexOptions.IgnoreCase` でハイライト用マッチを実施。

**効果**: 同じ文字でも「1 文字 1 コードポイント（NFC）」と「基底文字 + 結合文字（NFD）」の違いでハイライトが外れる事象を軽減している。

---

## 3. 検索クエリの正規化

**NormalizeQueryString** で実施しているのは次のみ:
- 前後の空白 `Trim`
- 全角スペース `\u3000` を半角スペースに置換（トークン分割の一貫性のため）

**未実施**:
- クエリ文字列の NFC/NFD 正規化  
  → ユーザー入力やコピー元が NFD（例: macOS のファイル名由来）の場合、インデックス側が NFC だと Sudachi のトークンが一致せず、ヒットしなくなる可能性がある。

---

## 4. インデックス（ドキュメント本文）

- **Content** は各 Extractor（`TextFileExtractor`, `OfficeExtractor`, `PdfExtractor` など）の `ExtractTextAsync` の戻り値をそのまま使用。
- インデックス投入前に Unicode 正規化（NFC/NFD/NFKC 等）は行っていない。
- ファイルのエンコーディングや、Office/PDF 内部の文字列が NFD や NFKC で出てきた場合、その形式のまま Lucene に渡り、Sudachi でトークン化される。

**影響**: 同じ語が「NFC でインデックスされたドキュメント」と「NFD で入力された検索語」のように形式が食い違うと、トークンが一致しない可能性がある。

---

## 5. Sudachi（形態素解析）

- **入力**: C# から Python へ UTF-8 でテキストを渡している（`StandardInputEncoding = Encoding.UTF8` 等）。
- **C# 側**: 渡す前に Unicode 正規化はしていない。UTF-8 バイト数が Lucene の制限（32765 バイト）を超えるトークンは、バイト境界を考慮して分割している（`SplitAtMaxUtf8Bytes` 等）。
- **Python/Sudachi 側**: 公式の正規化仕様は未確認。Sudachi が内部で NFKC 等を行っている場合は、インデックス・検索とも同じ正規化がかかるため、一貫性は保たれやすい。

---

## 6. テキストファイルのエンコーディング

- **TextFileExtractor**: UtfUnknown（CharsetDetector）でエンコーディングを検出し、`Encoding.GetString(bytes)` でデコード。
- 検出失敗時は UTF-8 を仮定。デコード例外時も UTF-8 で再試行。
- デコード結果の文字列に対して Unicode 正規化は行っていない。BOM や NFD を含むファイルは、そのままの並びでインデックス・表示される。

---

## 7. 考慮すべき点（今後の対応案）

1. **検索クエリの NFC 正規化**  
   `NormalizeQueryString` 内で、クエリ文字列を `NormalizationForm.FormC` で正規化すると、ユーザー入力が NFD でもインデックス（NFC または Sudachi 内部正規化）と揃いやすくなる。

2. **インデックス前の NFC 正規化**  
   `TryGetIndexedDocumentAsync` で `content` を取得したあと、`content.Normalize(NormalizationForm.FormC)` してから `IndexedDocument` に渡すと、インデックス側の形式を NFC に統一できる。検索クエリも NFC にすれば、検索・ハイライトの一貫性が取りやすい。

3. **NFKC の検討**  
   全角英数字・半角カナなどの互換文字を揃えたい場合は、`NormalizationForm.FormKC` (NFKC) を検討できる。ただし、意図しない文字の同一視（例: 半角/全角の差を無くす）になるため、検索挙動の仕様として合意した上で導入する必要がある。

4. **大文字・小文字**  
   検索では `RegexOptions.IgnoreCase` や `ToLowerInvariant()` を使用している箇所がある。Unicode の case folding（トルコの İ/i 等）まで厳密に扱う必要がある場合は、`CompareOptions` や `CultureInfo` を明示した比較の検討が必要。

5. **絵文字・結合文字**  
   絵文字（複数コードポイント）やアクセント付き文字（NFD）は、NFC に正規化しておくと「1 見た目 = 1 正規化列」に近づき、検索・表示の挙動を揃えやすい。

---

## 8. まとめ

- **すでに対応しているもの**: プレビュー・ハイライトでの NFC 正規化（検索語と本文の合成/分解の違いによるハイライト漏れの防止）。
- **未対応で影響しうるもの**: 検索クエリとインデックス本文の正規化形式の揃え（NFC 推奨）。クエリ・インデックス双方で NFC をかけると、検索ヒットとハイライトの一貫性が向上する。
- **エンコーディング**: テキストファイルは UtfUnknown で検出し UTF-8 等でデコード。正規化は別途、上記のとおりインデックス/クエリで検討可能。

必要に応じて、検索クエリの NFC 正規化とインデックス前の NFC 正規化の実装案（パッチレベル）もまとめられます。

---

## 9. ファイルサーバでの UTF-8 と Shift_JIS（SJIS）

弊社ファイルサーバでは主に **UTF-8** と **Shift_JIS（SJIS）** の二パターンのエンコーディングのファイルが存在する前提での対応状況です。

### 現状の対応

| エンコーディング | 対応 | 説明 |
|------------------|------|------|
| **UTF-8** | ✅ 対応 | UtfUnknown で検出し、`Encoding.UTF8` でデコード。検出失敗時のフォールバックも UTF-8。 |
| **Shift_JIS（SJIS）** | ✅ 対応 | UtfUnknown（UTF.Unknown 2.6.0）が Shift_JIS を検出可能。`result.Detected.Encoding` または `Encoding.GetEncoding(detected.EncodingName)` で CP932/Shift_JIS を取得しデコード。 |

**実装箇所**: `TextFileExtractor.ReadTextWithAutoEncoding`（`FullTextSearch.Infrastructure`）

- バイト列を `CharsetDetector.DetectFromBytes(bytes)` に渡してエンコーディングを自動検出。
- 検出結果の `Encoding` が null の場合は `EncodingName` から `Encoding.GetEncoding(encodingName)` で取得。
- 取得失敗時は UTF-8 を使用（`encoding ??= Encoding.UTF8`）。
- デコード例外時も UTF-8 で再試行（`catch { return Encoding.UTF8.GetString(bytes); }`）。

### 動的検出の精度はどの程度か

- **一般的な目安**  
  UtfUnknown（chardet 系）は、**数百バイト以上の日本語テキスト**があれば、UTF-8 と Shift_JIS の判定はおおむね安定します。公式ドキュメントでも「十分なデータ量があるほど信頼性が上がる」とされています。
- **短いファイル**  
  数十バイト程度の短いファイルでは、統計的に判断するため誤判定が起きやすく、精度は落ちます。UTF-8 と Shift_JIS はどちらも日本語のバイト列を取りうるため、短いほど曖昧になります。
- **信頼度（Confidence）**  
  UtfUnknown の検出結果には **Confidence**（0～1）が含まれます。現状の実装では `result.Detected` のみ利用していますが、信頼度が低い場合（例: 0.5 未満）に UTF-8 でデコードしてから SJIS で再試行するなど、フォールバックを入れると精度を補強できます。
- **結論**  
  - **長めのテキスト（数百バイト以上）**: 動的検出の精度は実用上十分なことが多い。  
  - **短いテキスト**: 誤判定の可能性あり。必要なら「短いファイルは SJIS 固定」などのモードや、Confidence によるフォールバックを検討。

### 注意点

1. **CP932 と Shift_JIS**: Windows では Shift_JIS は多くの場合 CP932（Windows-31J）として扱われます。.NET の `Encoding.GetEncoding("shift_jis")` は Windows 上では CP932 を返すため、日本語ファイルサーバでよくある「SJIS で保存された .txt / .csv」はそのまま正しくデコードされます。
2. **BOM なし UTF-8**: BOM のない UTF-8 も UtfUnknown で検出対象です。UTF-8 と Shift_JIS の両方が混在するフォルダでも、ファイルごとに自動判定されるため、二パターン混在環境でそのまま利用できます。
3. **Office / PDF**: .docx / .xlsx / .pdf などは内部で Unicode（UTF-8 等）でテキストを保持しているため、ファイルサーバ上の「UTF-8 / SJIS」の区別は主に **テキストファイル（.txt, .csv, .log, .md 等）** の読み込み時に効いています。

### モードで分けるか、動的で二判定か

| 方式 | 内容 | メリット | デメリット |
|------|------|----------|------------|
| **動的で二判定（現状）** | ファイルごとに UtfUnknown で UTF-8 / Shift_JIS を自動検出 | フォルダ混在・拡張子混在でもそのまま使える。設定不要。誤判定時のみ問題。 | まれに誤判定（短いファイル・特殊なバイト列）。検出コストがファイル読み込みごとにかかる。 |
| **モードで分ける** | 設定で「UTF-8 優先」「SJIS 優先」「フォルダごと」などを選択 | 誤判定を減らせる。フォルダ単位でエンコーディングが分かっている場合は確実。 | 混在フォルダではどちらか一方が文字化けする。設定の手間・運用ミスの可能性。 |

**推奨の考え方**

- **フォルダやサーバ単位でエンコーディングがほぼ統一されている場合**  
  → モードで分ける（「このフォルダは SJIS」「このフォルダは UTF-8」など）と、意図が明確で誤判定を避けやすい。
- **UTF-8 と SJIS が同じフォルダに混在している場合**  
  → **動的で二判定（現状の自動検出）** を維持する方が現実的。モードで分けると「どちらを選んでも一部が文字化けする」になりやすい。
- **ハイブリッド**  
  → デフォルトは動的検出のままにしつつ、**設定で「このフォルダは SJIS 固定」など上書きできるようにする**と、混在フォルダは自動検出、確実に SJIS のフォルダだけモードで固定、という使い分けができる。

**結論**

- 現状どおり **動的で二判定** を基本にし、必要に応じて「フォルダごと／グローバルでエンコーディングモードを選べる」オプションを追加する形がバランスが良いです。
- まずは動的検出のまま運用し、誤判定や文字化けが報告されたフォルダだけ「SJIS 固定」などのモードを後から付けられる設計にしておくのがおすすめです。

### まとめ

- UTF-8 と Shift_JIS の二パターンが混在するファイルサーバでも、テキストファイルは **自動エンコーディング検出** により適切に読み取り・インデックス・プレビューされる想定です。
- 特定拡張子だけ SJIS 固定にしたい等の要件があれば、`TextFileExtractor` で拡張子に応じた `Encoding.GetEncoding("shift_jis")` の優先など、追加対応が可能です。
- モードで分けるか動的で二判定かは、上記「モードで分けるか、動的で二判定か」の表と推奨を参照して、運用に合わせて選べます。
