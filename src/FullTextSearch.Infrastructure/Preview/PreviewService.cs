namespace FullTextSearch.Infrastructure.Preview;

/// <summary>
/// プレビューサービスの実装
/// </summary>
public class PreviewService : IPreviewService, IDisposable
{
    private PreviewHandlerHost? _currentHost;
    private string? _currentFilePath;
    private readonly object _lock = new();
    private bool _disposed;

    public async Task<bool> ShowPreviewAsync(string filePath, IntPtr hostHandle, PreviewBounds bounds)
    {
        return await Task.Run(() =>
        {
            lock (_lock)
            {
                // 同じファイルなら何もしない
                if (_currentFilePath == filePath && _currentHost != null)
                {
                    var rect = new RECT
                    {
                        Left = bounds.Left,
                        Top = bounds.Top,
                        Right = bounds.Right,
                        Bottom = bounds.Bottom
                    };
                    _currentHost.SetBounds(rect);
                    return true;
                }

                // 既存のプレビューをクリア
                ClearPreviewInternal();

                // 新しいプレビューを開始
                _currentHost = new PreviewHandlerHost();
                var initRect = new RECT
                {
                    Left = bounds.Left,
                    Top = bounds.Top,
                    Right = bounds.Right,
                    Bottom = bounds.Bottom
                };

                var result = _currentHost.Initialize(filePath, hostHandle, initRect);

                if (result)
                {
                    _currentFilePath = filePath;
                }
                else
                {
                    _currentHost.Dispose();
                    _currentHost = null;
                    _currentFilePath = null;
                }

                return result;
            }
        });
    }

    public void ClearPreview()
    {
        lock (_lock)
        {
            ClearPreviewInternal();
        }
    }

    private void ClearPreviewInternal()
    {
        if (_currentHost != null)
        {
            _currentHost.Dispose();
            _currentHost = null;
            _currentFilePath = null;
        }
    }

    public bool CanPreview(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        var extension = Path.GetExtension(filePath);
        return PreviewHandlerHost.CanPreview(extension);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ClearPreview();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

