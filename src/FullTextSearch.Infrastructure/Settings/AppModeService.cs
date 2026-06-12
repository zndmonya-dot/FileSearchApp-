// アプリの動作モードの実装。実行ファイルと同じフォルダの appmode.json から共有インデックスパスを読み、
// 実行ユーザーがローカルアカウント（=管理者）かドメインアカウント（=非管理者）かを判定する。
using System.Text.Json;

namespace FullTextSearch.Infrastructure.Settings;

/// <summary>
/// <see cref="IAppModeService"/> の実装。appmode.json の読み込みと管理者判定を行う。
/// 管理者判定は AD への問い合わせを行わず、OS が返すユーザーのドメイン名（ローカル時は PC 名）と
/// PC 名の比較だけで「ローカルアカウント＝管理者／ドメインアカウント＝非管理者」を動的に決める。
/// </summary>
public class AppModeService : IAppModeService
{
    /// <summary>動作モード設定ファイル名（実行ファイルと同じフォルダに配置）。</summary>
    public const string AppModeFileName = "appmode.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _appModePath;
    private bool _initialized;

    /// <inheritdoc />
    public bool IsAdmin { get; private set; }

    /// <inheritdoc />
    public string? SharedIndexPath { get; private set; }

    /// <summary>動作モード設定ファイルのパスを指定して初期化する。</summary>
    /// <param name="appModeFilePath">appmode.json のパス。未指定のとき実行フォルダ直下（単体テストでは一時パスを渡せる）。</param>
    public AppModeService(string? appModeFilePath = null)
    {
        _appModePath = string.IsNullOrWhiteSpace(appModeFilePath)
            ? Path.Combine(AppContext.BaseDirectory, AppModeFileName)
            : appModeFilePath;
    }

    /// <inheritdoc />
    public void Initialize()
    {
        if (_initialized) return;
        var config = LoadAppModeConfig();
        SharedIndexPath = config?.IndexPath;
        IsAdmin = config?.ForceNonAdmin == true ? false : DetermineIsAdmin();
        _initialized = true;
    }

    /// <summary>appmode.json を読み込む。読めない場合は null。</summary>
    private AppModeConfig? LoadAppModeConfig()
    {
        try
        {
            if (!File.Exists(_appModePath)) return null;
            var json = File.ReadAllText(_appModePath);
            var config = JsonSerializer.Deserialize<AppModeConfig>(json, JsonOptions);
            if (config == null) return null;
            var path = config.IndexPath?.Trim();
            config.IndexPath = string.IsNullOrWhiteSpace(path) ? null : path;
            return config;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// ローカルアカウント（ユーザーのドメイン名が PC 名と一致）なら管理者、ドメインアカウントなら非管理者。
    /// 取得失敗時は安全側に倒して非管理者扱い。
    /// </summary>
    private static bool DetermineIsAdmin()
    {
        try
        {
            return string.Equals(
                Environment.UserDomainName,
                Environment.MachineName,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>appmode.json のスキーマ。</summary>
    private sealed class AppModeConfig
    {
        /// <summary>共有インデックスのパス（ファイルサーバ等）。</summary>
        public string? IndexPath { get; set; }

        /// <summary>
        /// true のとき管理者判定を無視して非管理者モードで起動する（配布前の UI 確認用）。
        /// 本番の一般ユーザー向け配布物では false または省略すること。
        /// </summary>
        public bool ForceNonAdmin { get; set; }
    }
}
