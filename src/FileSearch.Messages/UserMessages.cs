// 画面 UI の文言。インデックスログ等は FullTextSearch.Core.IndexMessages。
namespace FileSearch.Messages;

/// <summary>
/// 画面に表示するユーザー向け文言の一元定義（<c>docs/メッセージ一覧.md</c> の ID と対応）。
/// </summary>
/// <remarks>
/// <para><b>保守:</b> 文言を追加・変更したら <c>メッセージ一覧.md</c> を同じ PR / 同じ変更で更新する。</para>
/// <para><b>インデックス・スキップログ:</b> ここではなく <c>FullTextSearch.Core.IndexMessages</c>（Lucene 出力）。</para>
/// <para><b>静的 HTML:</b> <c>wwwroot/index.html</c> のタイトル等は手動同期。検証は <c>scripts/check-webview-strings.ps1</c>。</para>
/// </remarks>
public static class UserMessages
{
    /// <summary>アプリ名（<c>App.xaml.cs</c> / ヘッダー / <c>index.html</c> の title と揃える。<c>ApplicationTitle</c> も同文言）</summary>
    public const string AppTitle = "全文検索システム";

    /// <summary>検索 API 失敗時（E-01）。</summary>
    public const string SearchFailed = "検索に失敗しました。";
    /// <summary>対象フォルダ未設定（E-02 等）。</summary>
    public const string NoTargetFolders = "対象フォルダを設定してください。";
    /// <summary>差分更新失敗（E-03）。</summary>
    public const string UpdateFailed = "差分更新に失敗しました。";
    /// <summary>全体再構築失敗（E-04）。</summary>
    public const string RebuildFailed = "インデックス構築に失敗しました。";
    /// <summary>インデックスパス未設定。</summary>
    public const string IndexPathNotSet = "インデックス保存先が未設定です。";
    /// <summary>スキップログファイルなし。</summary>
    public const string SkipLogNotFound = "スキップログが見つかりません。";
    /// <summary>スキップログを開けない。</summary>
    public const string SkipLogOpenFailed = "スキップログを開けませんでした。";
    /// <summary>プレビュー読み込みキャンセル。</summary>
    public const string PreviewLoadCancelled = "読み込みをキャンセルしました";
    /// <summary>フォルダ追加でパス空。</summary>
    public const string FolderPathRequired = "フォルダパスを入力してください";
    /// <summary>フォルダが存在しない。</summary>
    public const string FolderNotFound = "フォルダが見つかりません。パスを確認してください。";
    /// <summary>重複追加。</summary>
    public const string AlreadyAdded = "既に追加されています";
    /// <summary>インデックス処理キャンセル完了。</summary>
    public const string IndexCancelled = "キャンセルしました";
    /// <summary>差分検出中表示。</summary>
    public const string DiffDetecting = "差分を検出中...";
    /// <summary>処理準備中。</summary>
    public const string Preparing = "準備中...";
    /// <summary>進捗の単位「ファイル」。</summary>
    public const string FileUnit = "ファイル";
    /// <summary>件数の単位「件」。</summary>
    public const string PieceUnit = "件";

    /// <summary>サイドバー・ツリー（メッセージ一覧 I-01〜I-05 相当）</summary>
    public const string SidebarTitleSearchResults = "検索結果";
    /// <summary>検索ボックスのプレースホルダ。</summary>
    public const string SearchInputPlaceholder = "キーワードで検索（Enter）";
    /// <summary>インデックス構築中は検索不可（W-01）。</summary>
    public const string CannotSearchWhileIndexing = "再構築中は検索できません。";
    /// <summary>検索実行中表示。</summary>
    public const string Searching = "検索中...";
    /// <summary>キーワード未入力時の空ツリー案内。</summary>
    public const string TreeEmptyKeywordPrompt = "検索キーワードを入力";
    /// <summary>検索済みで 0 件。</summary>
    public const string TreeEmptyNoResults = "結果なし";
    /// <summary>未検索で Enter 促し。</summary>
    public const string TreeEmptyPressEnterToSearch = "Enter で検索";

    /// <summary>フッター構築キャンセルボタンの title。</summary>
    public const string IndexBuildCancelTitle = "インデックス構築をキャンセル";
    /// <summary>再構築ボタンの title。</summary>
    public const string RebuildButtonTitle = "インデックスを最初から作り直す（全件再スキャン）";
    /// <summary>再構築ボタンラベル。</summary>
    public const string Rebuild = "再構築";
    /// <summary>キャンセル。</summary>
    public const string Cancel = "キャンセル";
    /// <summary>最終更新日時の接尾辞。</summary>
    public const string LastUpdateSuffix = " に更新";

    /// <summary>最終更新から1分未満のときの相対表示（<c>DisplayFormatters.FormatLastIndexUpdate</c>）</summary>
    public const string LastIndexJustNow = "たった今";

    /// <summary>「N分前」。</summary>
    public static string FormatMinutesAgo(int minutes) => $"{minutes}分前";
    /// <summary>「N時間前」。</summary>
    public static string FormatHoursAgo(int hours) => $"{hours}時間前";
    /// <summary>「N日前」。</summary>
    public static string FormatDaysAgo(int days) => $"{days}日前";

    /// <summary>インデックス進捗行（<c>ErrorCount</c> はスキップ件数として表示に使う）</summary>
    public static string FormatIndexProgressCounts(int processed, int total, string countUnit, int skipCount) =>
        skipCount > 0
            ? $"{processed:N0} / {total:N0} {countUnit}（スキップ {skipCount:N0} 件）"
            : $"{processed:N0} / {total:N0} {countUnit}";

    /// <summary>ハイライトナビ：JS から返る行番号付き</summary>
    public static string FormatHighlightNavWithLine(int lineNum, int current, int total) =>
        $"{lineNum} 行目 ({current}/{total})";

    /// <summary>ハイライト位置のみ（current/total）。</summary>
    public static string FormatHighlightNavCountsOnly(int current, int total) => $"{current}/{total}";

    /// <summary>メインエリア未選択時のタイトル</summary>
    public const string EmptyMainSelectFileTitle = "ファイルを選択";
    /// <summary>メインエリア未選択時のヒント</summary>
    public const string EmptyMainSelectFileHint = "左側のツリーからファイルをクリック";

    /// <summary>フッター：スキップログを開くボタンの title</summary>
    public static string SkippedLogOpenTooltip(string logFileName) =>
        $"{logFileName} を既定アプリで開きます";

    /// <summary>Blazor WebView 初期表示（<c>wwwroot/index.html</c>）。<see cref="PreviewLoading"/> と同じ文言</summary>
    public const string WebViewLoadError = "エラーが発生しました。";
    /// <summary>WebView 再読み込み。</summary>
    public const string WebViewReload = "再読み込み";

    /// <summary>「構築中 N%」。</summary>
    public static string FormatBuildingPercent(int percent) => $"構築中 {percent}%";
    /// <summary>登録件数表示。</summary>
    public static string FormatRegisteredCount(int count) => $"{count:N0} 件登録済み";

    /// <summary>フォルダピッカー失敗メッセージ。</summary>
    public static string FolderPickerFailed(string detail) => $"フォルダの選択に失敗しました: {detail}";

    /// <summary>フッター: スキップ件数（<c>N0</c> 区切り）。</summary>
    public static string FormatSkippedCountLine(int skipCount) =>
        skipCount <= 0 ? "" : $"{skipCount:N0} 件がスキップされました";

    /// <summary>設定モーダル：ダイアログタイトル</summary>
    public const string SettingsTitle = "設定";
    /// <summary>表示セクション見出し</summary>
    public const string SettingsSectionDisplay = "表示";
    /// <summary>表示セクション説明</summary>
    public const string SettingsDescriptionDisplay = "アプリのテーマを選択します";
    /// <summary>テーマラベル</summary>
    public const string ThemeLabel = "テーマ";
    /// <summary>テーマ：ダーク</summary>
    public const string ThemeDark = "ダークモード";
    /// <summary>テーマ：ライト</summary>
    public const string ThemeLight = "ライトモード";
    /// <summary>テーマ：システム</summary>
    public const string ThemeSystem = "システムに従う";
    /// <summary>検索対象フォルダ見出し</summary>
    public const string SettingsSectionTargetFolders = "検索対象フォルダ";
    /// <summary>検索対象フォルダ説明</summary>
    public const string SettingsDescriptionTargetFolders = "インデックス作成対象のフォルダを指定します";
    /// <summary>追加ボタン</summary>
    public const string Add = "追加";
    /// <summary>参照ボタン</summary>
    public const string Browse = "参照";
    /// <summary>フォルダパス入力プレースホルダ</summary>
    public const string SettingsPlaceholderFolderPath = @"C:\Users\...";
    /// <summary>対象拡張子見出し</summary>
    public const string SettingsSectionExtensions = "対象拡張子";
    /// <summary>対象拡張子説明</summary>
    public const string SettingsDescriptionExtensions = "インデックスする拡張子。空なら抽出器の対応拡張子を使用。";
    /// <summary>拡張子変更後の注意</summary>
    public const string SettingsNoteAfterExtensionChange = "追加・削除した拡張子を反映するには、必ず「保存」を押したあと、インデックスの「再構築」または「差分更新」を実行してください。";
    /// <summary>拡張子入力例</summary>
    public const string SettingsPlaceholderExtensionExample = "例: .txt";
    /// <summary>インデックス保存先見出し</summary>
    public const string SettingsSectionIndexPath = "インデックス保存先";
    /// <summary>インデックス保存先説明</summary>
    public const string SettingsDescriptionIndexPath = "変更後はインデックスの再構築が必要です";
    /// <summary>フォルダを開くボタン</summary>
    public const string OpenIndexFolder = "フォルダを開く";
    /// <summary>インデックスフォルダを開くボタンの title</summary>
    public const string OpenIndexFolderTitle = "インデックスフォルダをエクスプローラーで開く";
    /// <summary>インデックス状態セクション見出し</summary>
    public const string SettingsSectionIndex = "インデックス";
    /// <summary>インデックス状態説明</summary>
    public const string SettingsDescriptionIndex = "状態と定期再構築の間隔";
    /// <summary>最終更新ラベル</summary>
    public const string LabelLastIndexUpdate = "最終更新";
    /// <summary>定期再構築ラベル</summary>
    public const string LabelAutoRebuild = "定期再構築";
    /// <summary>インデックス未実行</summary>
    public const string LastIndexNeverRun = "未実行";
    /// <summary>保存ボタン</summary>
    public const string Save = "保存";
    /// <summary>チップ削除の aria-label</summary>
    public const string AriaRemove = "削除";
    /// <summary>定期再構築：オフ</summary>
    public const string AutoRebuildOff = "無効";
    /// <summary>定期再構築：30分</summary>
    public const string AutoRebuild30m = "30分";
    /// <summary>定期再構築：1時間</summary>
    public const string AutoRebuild1h = "1時間";
    /// <summary>定期再構築：2時間</summary>
    public const string AutoRebuild2h = "2時間";
    /// <summary>定期再構築：6時間</summary>
    public const string AutoRebuild6h = "6時間";
    /// <summary>定期再構築：12時間</summary>
    public const string AutoRebuild12h = "12時間";
    /// <summary>定期再構築：24時間</summary>
    public const string AutoRebuild24h = "24時間";
    /// <summary>定期再構築：1週間</summary>
    public const string AutoRebuild1w = "1週間";

    /// <summary>インデックス更新ダイアログ：タイトル</summary>
    public const string IndexUpdateDialogTitle = "インデックスの更新";
    /// <summary>インデックス更新ダイアログ：説明</summary>
    public const string IndexUpdateDialogDescription = "更新方法を選んでください。";
    /// <summary>差分更新オプション：タイトル</summary>
    public const string IndexUpdateDiffTitle = "差分更新";
    /// <summary>差分更新オプション：説明</summary>
    public const string IndexUpdateDiffDescription = "追加・変更・削除されたファイルのみ反映。速く完了します。";
    /// <summary>全体再構築オプション：タイトル</summary>
    public const string IndexUpdateFullRebuildTitle = "全体を再構築";
    /// <summary>全体再構築オプション：説明</summary>
    public const string IndexUpdateFullRebuildDescription = "対象フォルダを全件スキャンし直します。時間がかかります。";
    /// <summary>閉じる</summary>
    public const string CloseDialog = "閉じる";
    /// <summary>実行</summary>
    public const string Execute = "実行";

    /// <summary>プレビュー：行数表示</summary>
    public static string FormatLineCount(int lines) => $"{lines} 行";
    /// <summary>フォルダ一覧：子件数</summary>
    public static string FormatChildCount(int count) => $"{count} 件";
    /// <summary>ハイライト前へボタン title</summary>
    public const string PreviewGoPrevTitle = "前へ（一致→ファイル）";
    /// <summary>ハイライト次へボタン title</summary>
    public const string PreviewGoNextTitle = "次へ（一致→ファイル）";
    /// <summary>ナビ位置の title</summary>
    public const string PreviewNavPositionTitle = "現在位置";
    /// <summary>前へ</summary>
    public const string Prev = "前へ";
    /// <summary>次へ</summary>
    public const string Next = "次へ";
    /// <summary>ファイルを開く</summary>
    public const string OpenFile = "開く";
    /// <summary>フォルダを開く（短縮）</summary>
    public const string OpenFolderShort = "フォルダ";
    /// <summary>ファイルを開く title</summary>
    public const string OpenFileTitle = "ファイルを開く";
    /// <summary>フォルダを開く title</summary>
    public const string OpenFolderTitle = "フォルダを開く";
    /// <summary>プレビュー読み込み中</summary>
    public const string PreviewLoading = "読み込み中...";
    /// <summary>親フォルダへ title</summary>
    public const string FolderListGoParentTitle = "親フォルダへ戻る";
    /// <summary>親フォルダへラベル</summary>
    public const string FolderListGoParent = "親フォルダへ";
    /// <summary>列：名前</summary>
    public const string ColumnName = "名前";
    /// <summary>列：更新日時</summary>
    public const string ColumnDate = "更新日時";
    /// <summary>列：拡張子</summary>
    public const string ColumnExtension = "拡張子";
    /// <summary>列：サイズ</summary>
    public const string ColumnSize = "サイズ";
    /// <summary>フィルターボタン title</summary>
    public const string FilterTitle = "フィルター";
    /// <summary>フィルター：すべて</summary>
    public const string FilterAll = "すべて";
    /// <summary>種別：フォルダ</summary>
    public const string KindFolder = "フォルダ";

    /// <summary>プレビュー：パス未指定（E 系の起点）</summary>
    public const string PreviewPathRequired = "ファイルパスが指定されていません";
    /// <summary>プレビュー不可プレースホルダ</summary>
    public const string PreviewNotAvailable = "[プレビュー不可]";
    /// <summary>キャンセル行</summary>
    public const string PreviewCancelledBracket = "[キャンセル]";
    /// <summary>本文省略の接尾辞</summary>
    public const string PreviewTruncatedEllipsis = "\n... (省略)";
    /// <summary>プレビュー行のエラー表示（E-05/E-06）</summary>
    public static string PreviewErrorLine(string message) => $"[エラー] {message}";
}
