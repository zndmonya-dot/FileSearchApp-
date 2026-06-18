# UML 図・シーケンス図

本ドキュメントは、Panoleon の構造と振る舞いを UML 図で表現する。  
図は Mermaid 記法で記述しており、GitHub や VS Code のプレビューで表示可能。

---

## 1. ユースケース図

```mermaid
graph LR
    User((利用者))

    subgraph Panoleon
        UC1[キーワード検索]
        UC2[検索結果の閲覧]
        UC3[ファイルプレビュー]
        UC4[ファイルを開く]
        UC5[フォルダを開く]
        UC7[インデックス再構築]
        UC8[インデックス差分更新]
        UC9[インデックス構築キャンセル]
        UC10[設定変更]
        UC11[フォルダ参照ダイアログ]
    end

    User --> UC1
    User --> UC2
    User --> UC3
    User --> UC4
    User --> UC5
    User --> UC7
    User --> UC8
    User --> UC9
    User --> UC10
    User --> UC11

    UC1 -->|結果を表示| UC2
    UC2 -->|ファイル選択| UC3
    UC10 -->|フォルダ選択| UC11
```

---

## 2. パッケージ図（レイヤー構成）

```mermaid
graph TB
    subgraph UI ["FileSearch.Blazor（UI 層）"]
        direction TB
        Pages["Components/Pages<br/>Home"]
        Shared["Components/Shared<br/>SearchSidebar, SearchResultTree,<br/>FilePreviewView, FolderListView,<br/>SettingsModal, IndexUpdateDialog,<br/>AppHeader"]
        BlazorSvc["Services<br/>PreviewService,<br/>PreviewJsInterop"]
        MAUI["App, MainPage, MauiProgram"]
    end

    subgraph Core ["FullTextSearch.Core（ドメイン層）"]
        direction TB
        CoreIdx["Index<br/>IIndexService, IndexProgress,<br/>IndexRebuildOptions, SkippedFileEntry"]
        CoreSearch["Search<br/>ISearchService, SearchOptions,<br/>SearchResult, SearchMode"]
        CorePreview["Preview<br/>IPreviewService, PreviewHelper"]
        CoreExt["Extractors<br/>ITextExtractor,<br/>TextExtractorFactory"]
        CoreModels["Models<br/>AppSettings, IndexedDocument,<br/>SearchResultItem, PreviewResult"]
        CoreUI["UI<br/>TreeBuilder, DisplayFormatters"]
        CoreConst["ContentLimits, DefaultPaths"]
    end

    subgraph Infra ["FullTextSearch.Infrastructure（インフラ層）"]
        direction TB
        InfraLucene["Lucene<br/>LuceneIndexService,<br/>LuceneSearchService"]
        InfraSudachi["Sudachi<br/>SudachiAnalyzer,<br/>SudachiTokenizer,<br/>SudachiNative,<br/>ListTokenStream"]
        InfraExt["Extractors<br/>OfficeExtractor,<br/>PdfExtractor,<br/>TextFileExtractor"]
        InfraSettings["Settings<br/>IAppSettingsService,<br/>AppSettingsService,<br/>IAppModeService, AppModeService"]
    end

    subgraph External ["外部"]
        Lucene["Lucene.NET 4.8"]
        SudachiDll["sudachi_ffi.dll"]
        FS["ファイルシステム"]
    end

    UI --> Core
    UI --> Infra
    Infra --> Core
    InfraLucene --> Lucene
    InfraSudachi --> SudachiDll
    InfraExt --> FS
```

---

## 3. クラス図

### 3.1 Core 層

```mermaid
classDiagram
    direction TB

    class IIndexService {
        <<interface>>
        +InitializeAsync(indexPath, readOnly, ct)
        +LastInitializeFailed : bool
        +RebuildIndexAsync(folders, progress, options, ct)
        +UpdateIndexAsync(folders, progress, options, ct)
        +LastSkippedFiles : IReadOnlyList~SkippedFileEntry~
        +ListIndexedItems(targetFolders, targetExtensions) IReadOnlyList~SearchResultItem~
    }

    class ISearchService {
        <<interface>>
        +SearchAsync(query, options, progress, ct) Task~SearchResult~
        +RefreshIndex()
        +Warmup()
        +TryGetStoredContentAsync(filePath, ct)
        +TryGetContentPreviewsAsync(paths, query, mode, ct)
        +GetHighlightTerms(query, mode)
    }

    class IPreviewService {
        <<interface>>
        +GetPreviewAsync(path, searchQuery, ct) Task~PreviewResult~
    }

    class ITextExtractor {
        <<interface>>
        +SupportedExtensions
        +CanExtract(extension) bool
        +ExtractTextAsync(filePath, ct) Task~string~
    }

    class TextExtractorFactory {
        -_extractors : IEnumerable~ITextExtractor~
        +GetExtractor(extension) ITextExtractor?
        +GetAllSupportedExtensions()
    }

    class TreeBuilder {
        <<static>>
        +BuildFullFolderTree(...)
        +ExpandPathToFile(...)
        +ExpandPathToFolder(...)
    }

    class DisplayFormatters {
        <<static>>
        +FormatDate(d) string
        +FormatLastIndexUpdate(lastUpdate) string
        +GetFileIconClass(name) string
    }

    class PreviewHelper {
        <<static>>
        +NormalizeExtension(extOrPath)
        +GetFileIconClass(name)
    }

    class ContentLimits {
        <<static>>
        +IndexMaxFileBytesForExtract : long = 1MB
        +MaxTextFileBytesToRead : long = 1MB
        +LuceneMaxTermUtf8Bytes : int = 32765
    }

    class IndexProgress {
        +ProcessedFiles : int
        +TotalFiles : int
        +CurrentFile : string?
        +ErrorCount : int
        +NoChanges : bool
    }

    class IndexRebuildOptions {
        +TargetExtensions : IReadOnlyList~string~?
    }

    class SearchOptions {
        +MaxResults : int
        +SearchMode : SearchMode
    }

    class SearchMode {
        <<enum>>
        Keyword
        Any
        Phrase
    }

    class SearchResult {
        +Items : List~SearchResultItem~
    }

    class AppSettings {
        +TargetFolders : List~string~
        +TargetExtensions : List~string~
        +IndexPath : string
        +LastIndexUpdate : DateTime?
        +AutoRebuildDailyHours : List~int~
        +ThemeMode : string
    }

    class IndexedDocument {
        +FilePath : string
        +FileName : string
        +FolderPath : string
        +Content : string
        +FileSize : long
        +LastModified : DateTime
        +FileType : string
        +IndexedAt : DateTime
    }

    class SearchResultItem {
        +FilePath : string
        +FileName : string
        +FolderPath : string
        +FileSize : long
        +LastModified : DateTime
    }

    class PreviewResult {
        +Content : string
        +LineStartOffsets : int[]
        +MatchLineNumbers : IReadOnlyList~int~
        +IsError : bool
        +SearchTerms : string[]
    }

    TextExtractorFactory --> ITextExtractor : 複数保持
    IIndexService ..> IndexProgress
    IIndexService ..> IndexRebuildOptions
    IIndexService ..> SearchResultItem
    ISearchService ..> SearchOptions
    ISearchService ..> SearchResult
    ISearchService ..> SearchMode
    SearchResult --> SearchResultItem
    SearchOptions --> SearchMode
    IPreviewService ..> PreviewResult
    DisplayFormatters ..> PreviewHelper
```

### 3.2 Infrastructure 層

```mermaid
classDiagram
    direction TB

    class LuceneIndexService {
        -_extractorFactory : TextExtractorFactory
        -_writer : IndexWriter?
        -_analyzer : Analyzer?
        -_skippedFiles : List~SkippedFileEntry~
        +InitializeAsync(indexPath, readOnly, ct)
        +RebuildIndexAsync()
        +UpdateIndexAsync()
        +ListIndexedItems()
        +LastSkippedFiles : IReadOnlyList~SkippedFileEntry~
        -ProcessChunkAsync(chunk, ct) Task~int~
        -TryGetIndexedDocumentAsync(path, ct) Task~IndexedDocument?~
        -AddDocumentsToWriterWithoutCommit(docs) int
        -CreateLuceneDocument(doc) Document
        -WriteSkippedLog()
    }

    class LuceneSearchService {
        -_settingsService : IAppSettingsService
        -_reader : DirectoryReader?
        +SearchAsync(query, options, ct) Task~SearchResult~
        +RefreshIndex()
        +Warmup()
    }

    class SudachiAnalyzer {
        +CreateComponents(fieldName, reader) TokenStreamComponents
    }

    class SudachiTokenizer {
        +PoolSize : int
        +IncrementToken() bool
        +Reset()
        +Warmup()$
    }

    class SudachiNative {
        <<static>>
        +TryEnsureInitialized()$ bool
        +CreateContext()$ IntPtr
        +Tokenize(ctx, text)$ List~string~
    }

    class ListTokenStream {
        +IncrementToken() bool
        +Reset()
    }

    class OfficeExtractor {
        +SupportedExtensions
        +ExtractTextAsync(filePath, ct) Task~string~
    }

    class PdfExtractor {
        +SupportedExtensions
        +ExtractTextAsync(filePath, ct) Task~string~
    }

    class TextFileExtractor {
        +SupportedExtensions
        +ExtractTextAsync(filePath, ct) Task~string~
    }

    class IAppSettingsService {
        <<interface>>
        +Settings : AppSettings
        +LoadAsync(ct) Task
        +SaveAsync(ct) Task
    }

    class AppSettingsService {
        +Settings : AppSettings
        +LoadAsync(ct) Task
        +SaveAsync(ct) Task
    }

    IIndexService <|.. LuceneIndexService
    ISearchService <|.. LuceneSearchService
    IAppSettingsService <|.. AppSettingsService
    ITextExtractor <|.. OfficeExtractor
    ITextExtractor <|.. PdfExtractor
    ITextExtractor <|.. TextFileExtractor

    LuceneIndexService --> TextExtractorFactory : 注入
    LuceneIndexService --> SudachiAnalyzer : 生成
    LuceneIndexService ..> ListTokenStream : 生成
    LuceneSearchService --> IAppSettingsService : 注入
    SudachiAnalyzer --> SudachiTokenizer : 生成
    SudachiTokenizer --> SudachiNative : P/Invoke
```

### 3.3 Blazor UI 層（コンポーネント構成）

```mermaid
classDiagram
    direction TB

    class Home {
        -searchQuery : string
        -treeNodes : List~TreeNode~
        -selectedFile : SearchResultItem?
        -selectedFolder : TreeNode?
        -isIndexing : bool
        -isSearching : bool
        -indexSkipCount : int
        +ExecuteSearch()
        +HandleBrowseFolder()
        +HandleAddFolder()
        +RunIndexUpdateAsync()
        +LoadPreview(path)
        +SaveSettings()
    }

    class SearchSidebar {
        +SidebarWidth : int
        +SearchQuery : string
        +TreeNodes : IReadOnlyList~TreeNode~
        +IsIndexing : bool
        +IndexSkipCount : int
        +OnOpenSkippedLog : EventCallback
        +OnSearchKeyDown : EventCallback
        +OnRequestRebuildIndex : EventCallback
    }

    class SearchResultTree {
        +Nodes : IReadOnlyList~TreeNode~
        +OnToggleNode : EventCallback~TreeNode~
        +OnSelectFile : EventCallback~TreeNode~
    }

    class FilePreviewView {
        +SelectedFile : SearchResultItem
        +PreviewLines : IReadOnlyList
        +OnOpenFile : EventCallback
        +OnOpenFolder : EventCallback
    }

    class FolderListView {
        +SelectedFolder : TreeNode
        +OnRowClick : EventCallback~TreeNode~
        +OnSort : EventCallback~string~
    }

    class SettingsModal {
        +Visible : bool
        +State : SettingsEditState
        +OnAddFolder : EventCallback
        +OnBrowseFolder : EventCallback
        +OnSaveRequested : EventCallback
    }

    class IndexUpdateDialog {
        +Visible : bool
        +FullRebuildSelected : bool
        +OnConfirm : EventCallback
        +OnCancel : EventCallback
    }

    class AppHeader {
        +OnOpenSettings : EventCallback
    }

    Home --> SearchSidebar
    Home --> SearchResultTree
    Home --> FilePreviewView
    Home --> FolderListView
    Home --> SettingsModal
    Home --> IndexUpdateDialog
    Home --> AppHeader
    SearchSidebar --> SearchResultTree
```

---

## 4. シーケンス図

### 4.1 検索実行

```mermaid
sequenceDiagram
    actor User as 利用者
    participant Home as Home.razor
    participant Search as ISearchService<br/>(LuceneSearchService)
    participant Analyzer as SudachiAnalyzer
    participant Sudachi as SudachiTokenizer
    participant Lucene as Lucene.NET
    participant Tree as TreeBuilder

    User->>Home: キーワード入力 + Enter
    Home->>Home: ExecuteSearch()
    Home->>Search: SearchAsync(query, options, ct)
    Search->>Search: BuildPartialMatchQuery（部分一致・ファイル名ブースト）
    Search->>Analyzer: GetTokenStream 等でクエリ語をトークン化
    Note over Analyzer,Sudachi: 日本語は Sudachi ネイティブ（sudachi_ffi.dll）経由
    Search->>Lucene: BooleanQuery で検索
    Lucene-->>Search: TopDocs（ヒット一覧）
    opt ハイライト有効（SkipHighlights でない）
        Search->>Search: ハイライト抜粋（Lucene Highlighter）
    end
    Search-->>Home: SearchResult
    Home->>Tree: BuildTree(folders, items)
    Tree-->>Home: List<TreeNode>
    Home->>Home: StateHasChanged()（ツリー表示更新）
```

### 4.2 インデックス再構築

```mermaid
sequenceDiagram
    actor User as 利用者
    participant Home as Home.razor
    participant Dialog as IndexUpdateDialog
    participant Index as IIndexService<br/>(LuceneIndexService)
    participant Factory as TextExtractorFactory
    participant Ext as ITextExtractor
    participant Lucene as Lucene.NET
    participant Log as skipped_files.log

    User->>Home: 「再構築」ボタン
    Home->>Dialog: ダイアログ表示
    User->>Dialog: 「全体を再構築」→「実行」
    Dialog->>Home: ConfirmIndexUpdateAsync()
    Home->>Index: RebuildIndexAsync(folders, progress, options, ct)
    Index->>Lucene: DeleteAll()
    Index->>Index: フォルダ走査・ファイル列挙

    loop チャンク単位（48ファイルずつ）
        Index->>Index: ProcessChunkAsync(chunk, ct)

        par 並列抽出
            Index->>Factory: GetExtractor(extension)
            Factory-->>Index: ITextExtractor
            Index->>Ext: ExtractTextAsync(filePath, ct)
            Ext-->>Index: テキスト
        end

        alt 1MB超 or 抽出エラー
            Index->>Index: _skippedFiles に記録
        else 正常
            Index->>Index: CreateLuceneDocument
            Index->>Lucene: UpdateDocument
        end

        Index-->>Home: progress.Report(ProcessedFiles, ErrorCount)
        Home->>Home: フッター進捗更新
    end

    Index->>Lucene: Commit()
    Index->>Log: WriteSkippedLog()
    Index-->>Home: 完了

    Home->>Home: indexSkipCount 更新（フッター）
    Home->>Home: StateHasChanged()
```

### 4.3 インデックス差分更新

```mermaid
sequenceDiagram
    actor User as 利用者
    participant Home as Home.razor
    participant Index as IIndexService<br/>(LuceneIndexService)
    participant Lucene as Lucene.NET
    participant Log as skipped_files.log

    User->>Home: 「差分更新」→「実行」
    Home->>Index: UpdateIndexAsync(folders, progress, options, ct)
    Index->>Lucene: DirectoryReader.Open(writer)
    Index->>Index: GetIndexedPathsAndLastModified()
    Index->>Index: ディスク上のファイルと比較

    Note over Index: 削除対象: インデックスにあるがディスクにない
    Note over Index: 追加/更新対象: ディスクにあるが未インデックス or 更新日時が異なる

    loop 削除対象
        Index->>Lucene: DeleteDocuments(filepath)
    end

    loop 追加/更新（チャンク単位）
        Index->>Index: ProcessChunkAsync(chunk, ct)
        Index-->>Home: progress.Report(ProcessedFiles, ErrorCount)
    end

    Index->>Lucene: Commit()
    Index->>Log: WriteSkippedLog()
    Index-->>Home: 完了
```

### 4.4 ファイルプレビュー

```mermaid
sequenceDiagram
    actor User as 利用者
    participant Home as Home.razor
    participant Timer as デバウンスTimer
    participant Preview as IPreviewService<br/>(PreviewService)
    participant Factory as TextExtractorFactory
    participant Ext as ITextExtractor

    User->>Home: ツリーでファイルをクリック
    Home->>Home: SelectFile(node)
    Home->>Home: isLoadingPreview = true
    Home->>Timer: SchedulePreviewLoad(path)

    Note over Timer: 200ms デバウンス待機

    Timer->>Home: LoadPreview(path)
    Home->>Preview: GetPreviewAsync(path, searchQuery, ct)
    Preview->>Factory: GetExtractor(extension)
    Factory-->>Preview: ITextExtractor
    Preview->>Ext: ExtractTextAsync(path, ct)
    Ext-->>Preview: テキスト（50,000文字で打ち切り）
    Preview->>Preview: 行分割 + 検索語を<mark>でハイライト
    Preview-->>Home: PreviewResult(Lines, LineCount)

    Home->>Home: isLoadingPreview = false
    Home->>Home: StateHasChanged()
```

### 4.5 設定保存

```mermaid
sequenceDiagram
    actor User as 利用者
    participant Home as Home.razor
    participant Modal as SettingsModal
    participant Settings as IAppSettingsService<br/>(AppSettingsService)
    participant Index as IIndexService
    participant Search as ISearchService

    User->>Home: 設定ボタン
    Home->>Home: OpenSettings()
    Home->>Modal: Visible = true

    User->>Modal: 各項目を編集
    User->>Modal: 「保存」クリック
    Modal->>Home: OnSaveRequested

    Home->>Home: SaveSettings()
    Home->>Settings: Settings に反映
    Home->>Settings: SaveAsync()
    Settings->>Settings: JSON ファイルに書き込み

    Home->>Index: InitializeAsync(indexPath)
    Home->>Search: RefreshIndex()
    Home->>Home: テーマ適用
    Home->>Home: showSettings = false
```

### 4.6 フォルダ参照ダイアログ

```mermaid
sequenceDiagram
    actor User as 利用者
    participant Modal as SettingsModal
    participant Home as Home.razor
    participant Picker as Windows FolderPicker

    User->>Modal: 「参照」ボタンをクリック
    Modal->>Home: OnBrowseFolder
    Home->>Home: HandleBrowseFolder()
    Home->>Picker: PickSingleFolderAsync()
    Picker->>User: フォルダ選択ダイアログ表示
    User->>Picker: フォルダを選択
    Picker-->>Home: folder.Path

    alt 重複あり
        Home->>Home: FolderMessage = "既に追加されています"
    else 正常
        Home->>Home: TargetFolders.Add(path)
    end

    Home->>Home: StateHasChanged()
```

### 4.7 形態素解析（Sudachi ネイティブ）

```mermaid
sequenceDiagram
    participant Analyzer as SudachiAnalyzer
    participant Tokenizer as SudachiTokenizer
    participant Native as SudachiNative
    participant Dll as sudachi_ffi.dll

    Analyzer->>Tokenizer: Reset() / IncrementToken()
    Tokenizer->>Native: Tokenize(ThreadContext, text)
    Native->>Dll: sudachi_tokenize(ctx, text, ...)
    Dll-->>Native: トークン列（改行区切り UTF-8）
    Native-->>Tokenizer: List<string>
    Tokenizer-->>Analyzer: ICharTermAttribute に surface を設定
```

---

## 5. 状態遷移図

### 5.1 インデックス構築の状態

```mermaid
stateDiagram-v2
    [*] --> アイドル

    アイドル --> ダイアログ表示 : 再構築ボタン押下
    ダイアログ表示 --> アイドル : キャンセル

    ダイアログ表示 --> 構築中 : 実行（再構築 or 差分更新）
    構築中 --> 完了 : 正常終了
    構築中 --> キャンセル済み : キャンセルボタン押下
    構築中 --> エラー : 例外発生

    完了 --> アイドル : フッター更新（件数・スキップ警告）
    キャンセル済み --> アイドル : 「キャンセルしました」表示
    エラー --> アイドル : エラーメッセージ表示
```

### 5.2 プレビューの状態

```mermaid
stateDiagram-v2
    [*] --> 未選択

    未選択 --> デバウンス待機 : ファイル選択
    デバウンス待機 --> 読み込み中 : 200ms 経過
    デバウンス待機 --> デバウンス待機 : 別ファイル選択（リセット）
    読み込み中 --> 表示中 : 取得成功
    読み込み中 --> エラー表示 : 取得失敗
    読み込み中 --> キャンセル : 別ファイル選択

    表示中 --> デバウンス待機 : 別ファイル選択
    エラー表示 --> デバウンス待機 : 別ファイル選択
    キャンセル --> デバウンス待機 : 新しいファイルの読み込み開始
```

---

## 6. DI コンテナ構成図

```mermaid
graph LR
    subgraph Singleton
        TEF[TextExtractorFactory]
        OE[OfficeExtractor]
        PE[PdfExtractor]
        TFE[TextFileExtractor]
        LIS[LuceneIndexService]
        LSS[LuceneSearchService]
        ASS[AppSettingsService]
    end

    subgraph Scoped
        PS[PreviewService]
    end

    TEF -->|保持| OE
    TEF -->|保持| PE
    TEF -->|保持| TFE
    LIS -->|注入| TEF
    LSS -->|注入| ASS
    PS -->|注入| TEF

    OE -.->|実装| ITE[ITextExtractor]
    PE -.->|実装| ITE
    TFE -.->|実装| ITE
    LIS -.->|実装| IIS[IIndexService]
    LSS -.->|実装| ISS[ISearchService]
    ASS -.->|実装| IASS[IAppSettingsService]
    PS -.->|実装| IPS[IPreviewService]
```

---

## 7. 参照

- [要件定義書](要件定義.md)
- [外部設計書](外部設計.md)
- [詳細設計書](詳細設計.md)
- [静的定義一覧](静的定義一覧.md)
