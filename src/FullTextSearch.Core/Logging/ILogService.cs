namespace FullTextSearch.Core.Logging;

/// <summary>
/// ログサービスのインターフェース
/// </summary>
public interface ILogService
{
    /// <summary>
    /// 情報ログを記録
    /// </summary>
    void Info(string message);

    /// <summary>
    /// 警告ログを記録
    /// </summary>
    void Warning(string message);

    /// <summary>
    /// エラーログを記録
    /// </summary>
    void Error(string message, Exception? exception = null);

    /// <summary>
    /// デバッグログを記録
    /// </summary>
    void Debug(string message);

    /// <summary>
    /// ログファイルのパスを取得
    /// </summary>
    string LogFilePath { get; }
}

