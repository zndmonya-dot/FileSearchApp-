// 画面 UI の文言。インデックスログ等は FullTextSearch.Core.IndexMessages。
namespace FileSearch.Messages;

/// <summary>
/// 画面に表示するユーザー向け文言の一元定義（<c>docs/メッセージ一覧.md</c> の ID と対応）。
/// </summary>
/// <remarks>
/// <para><b>保守:</b> 文言を追加・変更したら <c>メッセージ一覧.md</c> を同じ PR / 同じ変更で更新する。</para>
/// <para><b>インデックス・スキップログ:</b> ここではなく <c>FullTextSearch.Core.IndexMessages</c>（Lucene 出力）。</para>
/// <para><b>静的 HTML:</b> <c>wwwroot/index.html</c> の AppTitle / PreviewLoading / WebViewLoadError / WebViewReload は手動同期。検証は <c>scripts/check-webview-strings.ps1</c>。</para>
/// </remarks>
public static class UserMessages
{
    /// <summary>アプリ名（Panoptic + Chameleon）。ウィンドウタイトル・パッケージ表示等。</summary>
    public const string AppTitle = "Panoleon";
    /// <summary>起動スプラッシュ副題（<c>wwwroot/index.html</c> と手動同期）</summary>
    public const string BootSplashTagline = "全文検索";
    /// <summary>起動スプラッシュ版表示（<c>wwwroot/index.html</c> と手動同期）</summary>
    public const string BootSplashVersion = "v2.0";
    /// <summary>起動スプラッシュ：初期表示</summary>
    public const string BootSplashStarting = "起動しています...";
    /// <summary>起動スプラッシュ：設定読み込み</summary>
    public const string BootSplashLoadingSettings = "設定を読み込んでいます...";
    /// <summary>起動スプラッシュ：インデックスオープン</summary>
    public const string BootSplashOpeningIndex = "インデックスを開いています...";
    /// <summary>起動スプラッシュ：フォルダ一覧準備</summary>
    public const string BootSplashPreparingFolders = "フォルダ一覧を準備しています...";
    /// <summary>起動スプラッシュ：完了</summary>
    public const string BootSplashReady = "準備完了";

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
    public const string AlreadyAdded = "既に追加されています";
    /// <summary>インデックス保存先が未指定（W-07）。</summary>
    public const string IndexPathRequired = "インデックス保存先を指定してください";
    /// <summary>インデックス保存先が存在しない（W-08）。</summary>
    public const string IndexPathNotFoundSaveError = "インデックス保存先のフォルダが見つかりません。パスを確認してください。";
    /// <summary>インデックスを開けない（未到達・破損・権限等）。</summary>
    public const string IndexLoadFailed = "インデックスを開けません。設定から保存先を確認してください。";
    /// <summary>インデックスエラー時に設定を開く。</summary>
    public const string OpenSettingsFromIndexError = "設定を開く";
    /// <summary>インデックス処理キャンセル完了。</summary>
    public const string IndexCancelled = "キャンセルしました";
    /// <summary>差分検出中表示。</summary>
    public const string DiffDetecting = "差分を検出中...";
    /// <summary>差分更新で変更がなかった場合。</summary>
    public const string IndexDiffNoChanges = "差分更新: 変更はありませんでした。";
    /// <summary>処理準備中。</summary>
    public const string Preparing = "準備中...";
    /// <summary>進捗の単位「ファイル」。</summary>
    public const string FileUnit = "ファイル";
    /// <summary>件数の単位「件」。</summary>
    public const string PieceUnit = "件";

    /// <summary>サイドバー・ツリー（メッセージ一覧 I-01〜I-05 相当）</summary>
    public const string SidebarTitleSearchResults = "検索結果";
    /// <summary>検索ボックスのプレースホルダ。</summary>
    public const string SearchInputPlaceholder = "キーワードを入力…";
    /// <summary>検索ボタンのラベル。</summary>
    public const string SearchButtonLabel = "検索";
    /// <summary>検索ボタンのツールチップ。</summary>
    public const string SearchButtonTitle = "検索を実行（Enter でも可）";
    /// <summary>検索モード: AND検索（部分一致）。</summary>
    public const string SearchModeKeyword = "AND検索";
    /// <summary>検索モード: 完全一致検索（スペース含む語句）。</summary>
    public const string SearchModePhrase = "完全一致検索";
    /// <summary>検索モード: OR検索（部分一致）。</summary>
    public const string SearchModeAny = "OR検索";
    /// <summary>検索モード「AND検索」のツールチップ。</summary>
    public const string SearchModeKeywordTitle = "スペースなし＝入力1語の部分一致、スペースあり＝各語が同一行（またはファイル名）にすべて含まれる（例: ライセンス情報 / import sys）";
    /// <summary>検索モード「完全一致検索」のツールチップ。</summary>
    public const string SearchModePhraseTitle = "入力文字列がそのまま連続して含まれる（本文またはファイル名。例: import sys のみ）";
    /// <summary>検索モード「OR検索」のツールチップ。</summary>
    public const string SearchModeAnyTitle = "スペースなし＝入力1語の部分一致、スペースあり＝いずれかの語が同一行（またはファイル名）に含まれる（例: ライセンス情報 / 契約 見積）";
    /// <summary>検索モード選択の aria-label。</summary>
    public const string SearchModeGroupLabel = "検索方法";
    /// <summary>インデックス構築中は検索不可（W-01）。</summary>
    public const string CannotSearchWhileIndexing = "再構築中は検索できません。";
    /// <summary>検索実行中表示。</summary>
    public const string Searching = "検索中...";

    /// <summary>キーワード未入力時の空ツリー案内。</summary>
    public const string TreeEmptyKeywordPrompt = "検索キーワードを入力";
    /// <summary>検索済みで 0 件。</summary>
    public const string TreeEmptyNoResults = "結果なし";
    /// <summary>未検索で実行促し。</summary>
    public const string TreeEmptyPressEnterToSearch = "検索ボタンまたは Enter で検索";
    /// <summary>検索前のフォルダ体系を一括読み込み中</summary>
    public const string FolderTreeLoading = "フォルダを読み込み中...";

    /// <summary>フッター構築キャンセルボタンの title。</summary>
    public const string IndexBuildCancelTitle = "インデックス構築をキャンセル";
    /// <summary>再構築ボタンの title。</summary>
    public const string RebuildButtonTitle = "インデックスを最初から作り直す（全件再スキャン）";
    /// <summary>再構築ボタン非活性時のツールチップ（管理者のみ実行可）。</summary>
    public const string RebuildButtonDisabledTitle = "管理者のみ実行できます";
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

    /// <summary>メインエリア未選択時のヒント（対象フォルダ未設定）</summary>
    public const string EmptyMainNoFoldersHint = "設定から検索対象フォルダを追加してください";

    /// <summary>フッター：スキップログを開くボタンの title</summary>
    public static string SkippedLogOpenTooltip(string logFileName) =>
        $"{logFileName} を既定アプリで開きます";

    /// <summary>Blazor WebView 初期表示（<c>wwwroot/index.html</c>）。<see cref="PreviewLoading"/> と同じ文言</summary>
    public const string WebViewLoadError = "エラーが発生しました。";
    /// <summary>WebView 再読み込み。</summary>
    public const string WebViewReload = "再読み込み";

    /// <summary>「構築中 N%」。</summary>
    public static string FormatBuildingPercent(int percent) => $"構築中 {percent}%";

    /// <summary>読み込み・検索中の残り時間または経過時間（括弧付きサフィックス）。</summary>
    public static string FormatLoadingEtaHint(TimeSpan? remaining, TimeSpan elapsed) =>
        !string.IsNullOrEmpty(FormatRemainingApprox(remaining))
            ? FormatRemainingApprox(remaining)
            : FormatElapsedHint(elapsed);

    /// <summary>残り時間のおおよその表示。</summary>
    public static string FormatRemainingApprox(TimeSpan? remaining)
    {
        if (!remaining.HasValue)
            return "";

        var r = remaining.Value;
        if (r.TotalHours >= 2)
            return $"（あと約{(int)Math.Round(r.TotalHours)}時間）";
        if (r.TotalMinutes >= 2)
            return $"（あと約{(int)Math.Round(r.TotalMinutes)}分）";
        if (r.TotalSeconds >= 30)
            return $"（あと約{(int)Math.Round(r.TotalSeconds / 10.0) * 10}秒）";
        if (r.TotalSeconds >= 10)
            return $"（あと約{(int)Math.Ceiling(r.TotalSeconds)}秒）";
        return "（あと少し）";
    }

    /// <summary>経過時間の表示（3秒未満は空）。</summary>
    public static string FormatElapsedHint(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 3)
            return "";
        if (elapsed.TotalMinutes < 1)
            return $"（{(int)elapsed.TotalSeconds}秒経過）";
        return $"（{(int)elapsed.TotalMinutes}分{elapsed.Seconds}秒経過）";
    }

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
    public const string SettingsDescriptionTargetFolders = "インデックス対象のフォルダを複数指定できます";
    /// <summary>検索対象フォルダ説明（利用者・個人フィルタ）</summary>
    public const string SettingsDescriptionTargetFoldersReference = "管理者が登録したフォルダのうち、この PC で検索・閲覧に使うフォルダを選べます（チェックの変更はこの PC にのみ保存されます）";
    /// <summary>対象フォルダ有効化チェックボックスのラベル</summary>
    public const string TargetFolderEnableLabel = "このフォルダを検索対象にする";
    /// <summary>有効な対象フォルダが 0 件（すべてオフ）</summary>
    public const string NoActiveTargetFolders = "有効な対象フォルダがありません。設定でフォルダを選んでください。";
    /// <summary>検索対象フォルダ未登録時</summary>
    public const string TargetFoldersEmpty = "フォルダが登録されていません";
    /// <summary>検索対象フォルダを追加（フォルダ選択ダイアログ）</summary>
    public const string AddFolder = "フォルダを追加";
    /// <summary>一覧行の削除ボタン（短）</summary>
    public const string ActionDelete = "削除";
    /// <summary>インデックス保存先を選択（未設定時）</summary>
    public const string SelectIndexPath = "保存先を選択";
    /// <summary>インデックス保存先を変更（設定済み時）</summary>
    public const string ChangeIndexPath = "保存先を変更";
    /// <summary>対象拡張子未登録時</summary>
    public const string ExtensionsEmpty = "拡張子が登録されていません（空の場合は抽出器の対応拡張子を使用）";
    /// <summary>対象拡張子見出し</summary>
    public const string SettingsSectionExtensions = "対象拡張子";
    /// <summary>対象拡張子説明</summary>
    public const string SettingsDescriptionExtensions = "抽出器が対応する拡張子から選んで追加。空ならすべての対応拡張子を使用。";
    /// <summary>選択中の対象拡張子をすべて外す</summary>
    public const string ExtensionsClearAll = "すべて外す";
    /// <summary>インデックス保存先見出し</summary>
    public const string SettingsSectionIndexPath = "インデックス保存先";
    /// <summary>インデックス保存先説明</summary>
    public const string SettingsDescriptionIndexPath = "インデックスを保存するフォルダを1つ指定します。変更後は再構築が必要です";
    /// <summary>インデックス保存先未設定時（設定画面）</summary>
    public const string IndexPathEmpty = "未設定";
    /// <summary>インデックス更新間隔セクション見出し</summary>
    public const string SettingsSectionIndex = "インデックスの更新間隔";
    /// <summary>インデックス更新間隔セクション説明</summary>
    public const string SettingsDescriptionIndex = "最終更新と自動更新の時刻を設定します";
    /// <summary>共有設定ファイルの書き込み失敗。</summary>
    public static string SharedConfigSaveFailed(string path) =>
        $"共有設定ファイルを書き込めませんでした: {path}";
    /// <summary>自動更新の説明</summary>
    public const string SettingsHintAutoRebuild = "管理者PCで検索していないときに差分更新します。利用者の検索は影響しません";
    /// <summary>最終更新ラベル</summary>
    public const string LabelLastIndexUpdate = "最終更新";
    /// <summary>定期再構築ラベル</summary>
    public const string LabelAutoRebuild = "自動更新";
    /// <summary>自動更新の有効化チェック</summary>
    public const string AutoRebuildEnableLabel = "毎日、指定した時刻に差分更新する";
    /// <summary>時刻グリッドの aria-label</summary>
    public const string AutoRebuildHourGridLabel = "自動更新の時刻（日本標準時）";
    /// <summary>インデックス未実行</summary>
    public const string LastIndexNeverRun = "未実行";
    /// <summary>保存ボタン</summary>
    public const string Save = "保存";
    /// <summary>チップ削除の aria-label</summary>
    public const string AriaRemove = "削除";

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

    /// <summary>フォルダ一覧：子件数</summary>
    public static string FormatChildCount(int count) => $"{count} 件";
    /// <summary>フォルダ一覧：絞り込み後の件数</summary>
    public static string FormatFilteredChildCount(int filtered, int total) =>
        filtered == total ? $"{total} 件" : $"{filtered} / {total} 件";
    /// <summary>フォルダ一覧：ファイル名絞り込みプレースホルダ</summary>
    public const string FolderListFileSearchPlaceholder = "ファイル名で絞り込み...";
    /// <summary>フォルダ一覧：対象ファイルなし</summary>
    public const string FolderListEmpty = "このフォルダに対象ファイルはありません";
    /// <summary>フォルダ一覧：絞り込み結果なし</summary>
    public const string FolderListEmptyFiltered = "条件に一致するファイルはありません";
    /// <summary>ハイライト前へボタン title</summary>
    public const string PreviewGoPrevTitle = "前の一致へ（上）";
    /// <summary>ハイライト次へボタン title</summary>
    public const string PreviewGoNextTitle = "次の一致へ（下）";
    /// <summary>プレビューツールバー・一致移動グループ aria-label</summary>
    public const string PreviewNavGroupLabel = "検索一致の移動";
    /// <summary>プレビューツールバー・ファイル操作グループ aria-label</summary>
    public const string PreviewOpenGroupLabel = "ファイル操作";
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
    /// <summary>パンくずナビの aria-label</summary>
    public const string FolderListBreadcrumbLabel = "フォルダの場所";
    /// <summary>プレビュー中ファイルのパンくず aria-label</summary>
    public const string PreviewFolderPathLabel = "ファイルの場所";
    /// <summary>列：名前</summary>
    public const string ColumnName = "名前";
    /// <summary>列：先頭行プレビュー（検索時はマッチ行）</summary>
    public const string ColumnPreview = "内容";
    /// <summary>列幅リサイズハンドルの title</summary>
    public const string ColumnResizeHandleTitle = "列の境界をドラッグして幅を変更";
    /// <summary>列：更新日時</summary>
    public const string ColumnDate = "更新日時";
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
    /// <summary>サイズ上限超過</summary>
    public static string FormatPreviewFileTooLarge(string limitLabel) =>
        $"{limitLabel}を超えるためプレビューできません";
    /// <summary>キャンセル行</summary>
    public const string PreviewCancelledBracket = "[キャンセル]";
    /// <summary>本文省略の接尾辞</summary>
    /// <summary>プレビュー行数が多いときの省略メッセージ。</summary>
    public static string PreviewTooManyLinesLine(int totalLines, int shownLines) =>
        $"（行数が多いため先頭 {shownLines:N0} 行のみ表示。全 {totalLines:N0} 行）";
    /// <summary>プレビュー行のエラー表示（E-05/E-06）</summary>
    public static string PreviewErrorLine(string message) => $"[エラー] {message}";
}
