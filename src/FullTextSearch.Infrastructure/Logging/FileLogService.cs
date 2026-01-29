using System.Collections.Concurrent;
using System.Text;
using FullTextSearch.Core.Logging;

namespace FullTextSearch.Infrastructure.Logging;

/// <summary>
/// ファイルベースのログサービス実装
/// </summary>
public class FileLogService : ILogService, IDisposable
{
    private readonly string _logDirectory;
    private readonly ConcurrentQueue<LogEntry> _logQueue = new();
    private readonly Timer _flushTimer;
    private readonly object _writeLock = new();
    private bool _disposed;

    public string LogFilePath { get; }

    public FileLogService()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FullTextSearch",
            "Logs");

        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }

        // 日付ごとのログファイル
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        LogFilePath = Path.Combine(_logDirectory, $"log-{date}.txt");

        // 定期的にログをフラッシュ（1秒ごと）
        _flushTimer = new Timer(FlushLogs, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        // 古いログファイルをクリーンアップ（30日以上前）
        CleanupOldLogs();
    }

    public void Info(string message)
    {
        Log(LogLevel.Info, message);
    }

    public void Warning(string message)
    {
        Log(LogLevel.Warning, message);
    }

    public void Error(string message, Exception? exception = null)
    {
        var fullMessage = exception != null
            ? $"{message}\n{exception}"
            : message;

        Log(LogLevel.Error, fullMessage);
    }

    public void Debug(string message)
    {
#if DEBUG
        Log(LogLevel.Debug, message);
#endif
    }

    private void Log(LogLevel level, string message)
    {
        _logQueue.Enqueue(new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message
        });
    }

    private void FlushLogs(object? state)
    {
        if (_logQueue.IsEmpty)
        {
            return;
        }

        var sb = new StringBuilder();

        while (_logQueue.TryDequeue(out var entry))
        {
            sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] {entry.Message}");
        }

        if (sb.Length > 0)
        {
            lock (_writeLock)
            {
                try
                {
                    File.AppendAllText(LogFilePath, sb.ToString());
                }
                catch
                {
                    // ログの書き込みに失敗した場合は無視
                }
            }
        }
    }

    private void CleanupOldLogs()
    {
        try
        {
            var threshold = DateTime.Now.AddDays(-30);
            var oldFiles = Directory.GetFiles(_logDirectory, "log-*.txt")
                .Where(f => File.GetCreationTime(f) < threshold);

            foreach (var file in oldFiles)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // 削除に失敗した場合は無視
                }
            }
        }
        catch
        {
            // クリーンアップに失敗した場合は無視
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _flushTimer.Dispose();
        FlushLogs(null); // 残りのログをフラッシュ

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    private class LogEntry
    {
        public DateTime Timestamp { get; init; }
        public LogLevel Level { get; init; }
        public required string Message { get; init; }
    }
}

