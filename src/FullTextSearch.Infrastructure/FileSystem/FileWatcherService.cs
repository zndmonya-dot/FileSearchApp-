using System.Collections.Concurrent;

namespace FullTextSearch.Infrastructure.FileSystem;

/// <summary>
/// ファイル監視サービスの実装
/// </summary>
public class FileWatcherService : IFileWatcherService, IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, DateTime> _recentChanges = new();
    private readonly Timer _debounceTimer;
    private readonly ConcurrentQueue<FileChangedEventArgs> _pendingEvents = new();
    private bool _disposed;

    // 同じファイルの連続した変更を無視する時間（ミリ秒）
    private const int DebounceIntervalMs = 500;

    public event EventHandler<FileChangedEventArgs>? FileChanged;

    public bool IsWatching => _watchers.Count > 0;

    public FileWatcherService()
    {
        _debounceTimer = new Timer(ProcessPendingEvents, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void StartWatching(IEnumerable<string> folders)
    {
        StopWatching();

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.DirectoryName,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnFileCreated;
                watcher.Changed += OnFileChanged;
                watcher.Deleted += OnFileDeleted;
                watcher.Renamed += OnFileRenamed;
                watcher.Error += OnError;

                _watchers.Add(watcher);
            }
            catch (Exception)
            {
                // フォルダの監視開始に失敗した場合はスキップ
            }
        }
    }

    public void StopWatching()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        _recentChanges.Clear();
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!ShouldProcessFile(e.FullPath))
        {
            return;
        }

        EnqueueEvent(new FileChangedEventArgs
        {
            FilePath = e.FullPath,
            ChangeType = FileChangeType.Created
        });
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!ShouldProcessFile(e.FullPath))
        {
            return;
        }

        EnqueueEvent(new FileChangedEventArgs
        {
            FilePath = e.FullPath,
            ChangeType = FileChangeType.Modified
        });
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        EnqueueEvent(new FileChangedEventArgs
        {
            FilePath = e.FullPath,
            ChangeType = FileChangeType.Deleted
        });
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        EnqueueEvent(new FileChangedEventArgs
        {
            FilePath = e.FullPath,
            OldFilePath = e.OldFullPath,
            ChangeType = FileChangeType.Renamed
        });
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // エラーログを記録（将来的にはログサービスを使用）
        var ex = e.GetException();
        System.Diagnostics.Debug.WriteLine($"FileWatcher error: {ex.Message}");
    }

    private void EnqueueEvent(FileChangedEventArgs args)
    {
        // デバウンス: 同じファイルの連続した変更を無視
        var now = DateTime.UtcNow;
        if (_recentChanges.TryGetValue(args.FilePath, out var lastChange))
        {
            if ((now - lastChange).TotalMilliseconds < DebounceIntervalMs)
            {
                return;
            }
        }

        _recentChanges[args.FilePath] = now;
        _pendingEvents.Enqueue(args);

        // デバウンスタイマーをリセット
        _debounceTimer.Change(DebounceIntervalMs, Timeout.Infinite);
    }

    private void ProcessPendingEvents(object? state)
    {
        while (_pendingEvents.TryDequeue(out var args))
        {
            try
            {
                FileChanged?.Invoke(this, args);
            }
            catch (Exception)
            {
                // イベントハンドラの例外は無視
            }
        }

        // 古い変更履歴をクリーンアップ
        var threshold = DateTime.UtcNow.AddSeconds(-10);
        var oldKeys = _recentChanges
            .Where(kvp => kvp.Value < threshold)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldKeys)
        {
            _recentChanges.TryRemove(key, out _);
        }
    }

    private static bool ShouldProcessFile(string filePath)
    {
        // ディレクトリは無視
        if (Directory.Exists(filePath))
        {
            return false;
        }

        // 一時ファイルは無視
        var fileName = Path.GetFileName(filePath);
        if (fileName.StartsWith("~$") || fileName.StartsWith("."))
        {
            return false;
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension == ".tmp" || extension == ".temp")
        {
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopWatching();
        _debounceTimer.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

