using FullTextSearch.Core.Models;

namespace FullTextSearch.Infrastructure.Settings;

/// <summary>
/// アプリケーション設定サービスのインターフェース
/// </summary>
public interface IAppSettingsService
{
    /// <summary>
    /// 現在の設定
    /// </summary>
    AppSettings Settings { get; }

    /// <summary>
    /// 設定を読み込み
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 設定を保存
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
