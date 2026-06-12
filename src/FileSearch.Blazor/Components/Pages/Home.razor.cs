// メイン画面のコードビハインド。状態・ライフサイクル・テーマ。処理本体は Home.*.cs の partial に分割。
//
// 【partial の役割（このフォルダ内）】
//   Home.razor.cs     … 状態フィールド・Dispose・テーマ・ライフサイクル（分割の起点）
//   Home.Search.Tree  … 検索実行・ツリー・フォルダ一覧のソート/フィルター/選択
//   Home.Preview      … プレビュー・ハイライト移動・ファイル/フォルダを開く
//   Home.Index        … インデックス差分/再構築・進捗・スキップログ・更新ダイアログ
//   Home.Settings     … 設定モーダル・フォルダ/拡張子・保存
//   Home.Resize       … サイドバー幅ドラッグ・プレビュー行の表示用キャッシュ
//
// 【文言】画面の日本語は FileSearch.Messages.UserMessages。変更時は docs/メッセージ一覧.md の ID を更新。
// 【設計メモ】docs/静的定義一覧.md・docs/外部設計.md
using FullTextSearch.Core;
using FullTextSearch.Core.Index;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;
using FullTextSearch.Core.Search;
using FullTextSearch.Infrastructure.Settings;
using FileSearch.Blazor.Components.Shared;
using FileSearch.Blazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace FileSearch.Blazor.Components.Pages;

/// <summary>
/// メイン画面（/）。検索入力・結果ツリー・プレビュー・サイドバー・設定・インデックス更新を統合する。
/// ロジックは <c>Home.*.cs</c> の partial に分割（ファイル先頭コメント参照）。
/// </summary>
public partial class Home : IDisposable
{
    #region 状態（検索・選択・プレビュー・設定・UI）

    // --- 検索入力・結果ツリー ---
    private string searchQuery = "";
    private SearchMode searchMode = SearchMode.Keyword;
    private List<TreeNode> treeNodes = new();
    private SearchResultItem? selectedFile;
    private TreeNode? selectedFolder;
    private int totalFileCount = 0;
    /// <summary>検索実行中（サイドバーにスピナー等を出す）</summary>
    private bool isSearching = false;
    private string? searchErrorMessage = null;
    /// <summary>最後に実際に実行した検索クエリ（入力中は未実行と区別するため）</summary>
    private string? _lastExecutedSearchQuery;

    // --- プレビュー（_previewLines が生データ、表示用は Home.Resize の previewLinesDisplay） ---
    private IReadOnlyList<PreviewLineResult> _previewLines = Array.Empty<PreviewLineResult>();
    private int previewLineCount = 0;
    private bool isLoadingPreview = false;
    private IReadOnlyList<PreviewLineDisplay>? _previewLinesDisplayCache;

    // --- インデックス・フッター ---
    private int indexCount = 0;
    private bool isIndexing = false;
    private int indexProgressPercent = 0;
    private string indexProgressText = "";
    private string? indexErrorMessage = null;
    private int indexSkipCount;

    // --- 設定モーダル・テーマ ---
    private bool showSettings = false;
    private bool isDarkMode = true;
    private readonly SettingsEditState _settingsEdit = new();

    /// <summary>実行ユーザーが管理者か。非管理者は共有インデックスの参照専用（設定編集・再構築不可）。</summary>
    private bool isAdmin = false;

    // --- レイアウト（サイドバー幅・リサイズ） ---
    private int sidebarWidth = 300;
    private bool isResizing = false;
    private double resizeStartX = 0;
    private int resizeStartWidth = 0;

    // --- キャンセル・タイマー（検索 / プレビュー / インデックス / 定期再構築） ---
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _indexCts;
    private Timer? _previewDebounceTimer;
    private string? _pendingPreviewPath;
    private Timer? _autoRebuildTimer;
    private const int PreviewDebounceMs = 200;
    private const int ProgressReportInterval = 500;
    private const int ProgressReportThrottleMs = 250;
    private int _lastReportedProgressCount = -1;
    private DateTime _lastReportedProgressTime;

    /// <summary>
    /// 検索関連の最終操作時刻（UTC）。検索入力・キー入力・検索実行のたびに更新する。
    /// 定期再構築（自動更新）は、この時刻から <see cref="AutoRebuildIdleSeconds"/> 秒以内であれば見送る。
    /// </summary>
    private DateTime _lastSearchActivityUtc = DateTime.MinValue;
    /// <summary>定期再構築を見送るアイドル判定（秒）。検索操作からこの秒数だけ静かであれば自動更新を許可する。</summary>
    private const int AutoRebuildIdleSeconds = 30;

    // --- フォルダ一覧（ソート・フィルター・選択行） ---
    private string sortColumn = "name";
    private bool sortAscending = true;
    private string filterType = "";
    private bool showFilterMenu = false;
    private int selectedFolderRowIndex = -1;

    // --- ハイライトナビ（JS） / ファイル間ナビ ---
    private string? _lastHighlightNavFilePath;
    private string? _highlightNavInfo;
    private bool _hasTriedInitialHighlightScroll;
    private List<TreeNode>? _fileNavList;
    private int _fileNavIndex = -1;

    // --- インデックス更新ダイアログ ---
    private bool _showRebuildConfirm;
    private bool _indexUpdateFullRebuild;

    /// <summary>現在の入力がすでに検索実行済みか（未実行なら「Enter で検索」と表示）</summary>
    private bool HasSearchedCurrentQuery => _lastExecutedSearchQuery != null && (searchQuery?.Trim() ?? "") == _lastExecutedSearchQuery;

    #endregion

    #region ライフサイクル（初回テーマ・ハイライトスクロール）

    /// <summary>ツリー展開の同期、System テーマの初回取得、ファイル切替時のハイライトナビリセットと初回スクロール。</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var treeChanged = false;
        if (selectedFile != null && !string.IsNullOrEmpty(selectedFile.FilePath) && TreeBuilder.ExpandPathToFile(treeNodes, selectedFile.FilePath))
            treeChanged = true;
        if (selectedFolder != null && !string.IsNullOrEmpty(selectedFolder.FullPath) && TreeBuilder.ExpandPathToFolder(treeNodes, selectedFolder.FullPath))
            treeChanged = true;
        if (treeChanged)
        {
            await Task.Yield();
            StateHasChanged();
        }
        if (firstRender && string.Equals(SettingsService.Settings.ThemeMode, "System", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var systemDark = await GetPreferredColorSchemeFromSystemAsync();
                if (isDarkMode != systemDark) { isDarkMode = systemDark; StateHasChanged(); }
            }
            catch { /* JS not ready */ }
        }
        if (!firstRender && selectedFile?.FilePath != _lastHighlightNavFilePath)
        {
            _lastHighlightNavFilePath = selectedFile?.FilePath;
            _highlightNavInfo = null;
            _hasTriedInitialHighlightScroll = false;
            try { await JSRuntime.InvokeVoidAsync("resetHighlightNav"); }
            catch { /* JS not ready */ }
        }
        if (!firstRender && !isLoadingPreview && ShowHighlightNav && !_hasTriedInitialHighlightScroll)
        {
            _hasTriedInitialHighlightScroll = true;
            try
            {
                var result = await JSRuntime.InvokeAsync<string?>("scrollToFirstHighlightInstant");
                if (!string.IsNullOrEmpty(result))
                {
                    _highlightNavInfo = FormatHighlightNavInfo(result);
                    StateHasChanged();
                }
            }
            catch { /* JS not ready */ }
        }
    }

    /// <summary>設定読み込み、インデックス初期化、定期再構築用タイマー開始。</summary>
    protected override async Task OnInitializedAsync()
    {
        await SettingsService.LoadAsync();

        // 動作モードの確定: 共有インデックスパスの反映と管理者判定。
        AppMode.Initialize();
        isAdmin = AppMode.IsAdmin;
        if (!string.IsNullOrWhiteSpace(AppMode.SharedIndexPath))
        {
            // 共有インデックスを参照する。インストール先は基本このインデックスのみを使う。
            SettingsService.Settings.IndexPath = AppMode.SharedIndexPath;
            if (AppMode.SharedTargetFolders.Count > 0 && SettingsService.Settings.TargetFolders.Count == 0)
                SettingsService.Settings.TargetFolders = AppMode.SharedTargetFolders.ToList();
        }

        ApplyThemeFromSettings();
        var indexPath = SettingsService.Settings.IndexPath;
        if (!string.IsNullOrWhiteSpace(indexPath))
        {
            await IndexService.InitializeAsync(indexPath, readOnly: !isAdmin);
            indexCount = IndexService.GetStats().DocumentCount;
            SearchService.Warmup();
        }
        _autoRebuildTimer = new Timer(OnAutoRebuildTick, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    #endregion

    /// <summary>タイマーとキャンセルトークンを解放。</summary>
    public void Dispose()
    {
        _autoRebuildTimer?.Dispose();
        _autoRebuildTimer = null;
        _previewDebounceTimer?.Dispose();
        _previewDebounceTimer = null;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
        _indexCts?.Cancel();
        _indexCts?.Dispose();
        _indexCts = null;
    }

    /// <summary>ThemeMode に応じて isDarkMode を設定。System のときは OnAfterRender で JS から上書きしうる。</summary>
    private void ApplyThemeFromSettings()
    {
        var mode = SettingsService.Settings.ThemeMode ?? "System";
        if (string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase))
            isDarkMode = true;
        else if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase))
            isDarkMode = false;
        else
            isDarkMode = true; // System: 初期値はダーク。OnAfterRenderAsync で JS から取得して更新
    }

    /// <summary>ブラウザの prefers-color-scheme を返す（index.html の getPreferredColorScheme）。</summary>
    private async Task<bool> GetPreferredColorSchemeFromSystemAsync()
    {
        var scheme = await JSRuntime.InvokeAsync<string>("getPreferredColorScheme");
        return string.Equals(scheme, "dark", StringComparison.OrdinalIgnoreCase);
    }
}
