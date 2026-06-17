// アプリの動作モード（共有インデックス参照／管理者判定）を提供するインターフェース。
namespace FullTextSearch.Infrastructure.Settings;

/// <summary>
/// アプリの動作モードを提供する。appmode.json（および任意の共有設定ファイル）から
/// インデックスパス・対象フォルダを読み、管理者/参照モードを決める。
/// </summary>
public interface IAppModeService
{
    /// <summary>管理者モードか。<see cref="Initialize"/> 後に有効。</summary>
    bool IsAdmin { get; }

    /// <summary>共有インデックスのパス（未指定なら null）。</summary>
    string? SharedIndexPath { get; }

    /// <summary>検索対象フォルダ（未指定なら空）。</summary>
    IReadOnlyList<string> SharedTargetFolders { get; }

    /// <summary>
    /// サーバ上の共有設定ファイルのパス（appmode.json の sharedConfig）。
    /// 設定されているとき、インデックスパスと対象フォルダは起動時にここから読む。
    /// </summary>
    string? SharedConfigPath { get; }

    /// <summary>appmode.json（と共有設定）の読み込みとモード判定を行う（冪等）。</summary>
    void Initialize();

    /// <summary>
    /// 共有設定のインデックス対象最大ファイルサイズ（バイト）。null=既定、0=無制限。
    /// </summary>
    long? SharedIndexMaxFileBytes { get; }

    /// <summary>
    /// 管理者が設定保存したとき、<see cref="SharedConfigPath"/> へ共有設定を書き込む。
    /// </summary>
    bool TrySaveSharedConfig(string indexPath, IReadOnlyList<string> targetFolders, long? indexMaxFileBytes);
}
