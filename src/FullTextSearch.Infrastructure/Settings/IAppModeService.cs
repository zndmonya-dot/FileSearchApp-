// アプリの動作モード（共有インデックス参照／管理者判定）を提供するインターフェース。
namespace FullTextSearch.Infrastructure.Settings;

/// <summary>
/// アプリの動作モードを提供する。共有インデックスのパスと、実行ユーザーが管理者かどうかを保持する。
/// 管理者判定は「ローカルアカウント＝管理者／ドメインアカウント＝非管理者」で動的に行う。
/// </summary>
public interface IAppModeService
{
    /// <summary>実行ユーザーが管理者か（ローカルアカウントなら true）。<see cref="Initialize"/> 後に有効。</summary>
    bool IsAdmin { get; }

    /// <summary>appmode.json で指定された共有インデックスのパス（未指定なら null）。</summary>
    string? SharedIndexPath { get; }

    /// <summary>appmode.json で指定された検索対象フォルダ（未指定なら空）。共有インデックス配布時に管理者が記載。</summary>
    IReadOnlyList<string> SharedTargetFolders { get; }

    /// <summary>appmode.json の読み込みと管理者判定を行う（冪等）。</summary>
    void Initialize();

    /// <summary>
    /// appmode.json を再読み込みする。<see cref="SharedIndexPath"/> / <see cref="SharedTargetFolders"/> が
    /// 変化した場合は true を返す。管理者判定はログイン種別に依存するため変化しない。
    /// </summary>
    bool Reload();
}
