namespace FullTextSearch.Infrastructure.FileSystem;

/// <summary>
/// ファイル監視サービスのインターフェース
/// </summary>
public interface IFileWatcherService
{
    /// <summary>
    /// ファイル変更イベント
    /// </summary>
    event EventHandler<FileChangedEventArgs>? FileChanged;

    /// <summary>
    /// 監視を開始
    /// </summary>
    void StartWatching(IEnumerable<string> folders);

    /// <summary>
    /// 監視を停止
    /// </summary>
    void StopWatching();

    /// <summary>
    /// 監視中かどうか
    /// </summary>
    bool IsWatching { get; }
}

/// <summary>
/// ファイル変更イベントの引数
/// </summary>
public class FileChangedEventArgs : EventArgs
{
    /// <summary>
    /// ファイルパス
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 変更の種類
    /// </summary>
    public required FileChangeType ChangeType { get; init; }

    /// <summary>
    /// 変更前のパス（リネーム時のみ）
    /// </summary>
    public string? OldFilePath { get; init; }
}

/// <summary>
/// ファイル変更の種類
/// </summary>
public enum FileChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed
}


