namespace FullTextSearch.Infrastructure.Preview;

/// <summary>
/// プレビューサービスのインターフェース
/// </summary>
public interface IPreviewService
{
    /// <summary>
    /// プレビューを表示
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <param name="hostHandle">プレビューを表示するウィンドウハンドル</param>
    /// <param name="bounds">表示領域</param>
    /// <returns>成功した場合true</returns>
    Task<bool> ShowPreviewAsync(string filePath, IntPtr hostHandle, PreviewBounds bounds);

    /// <summary>
    /// プレビューをクリア
    /// </summary>
    void ClearPreview();

    /// <summary>
    /// 指定したファイルがプレビュー可能か
    /// </summary>
    bool CanPreview(string filePath);
}

/// <summary>
/// プレビュー表示領域
/// </summary>
public struct PreviewBounds
{
    public int Left { get; init; }
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}


