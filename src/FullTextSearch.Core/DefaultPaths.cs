namespace FullTextSearch.Core;

/// <summary>
/// アプリケーションのデフォルトパス（一元定義）
/// </summary>
public static class DefaultPaths
{
    /// <summary>
    /// インデックス保存先のデフォルトフォルダ
    /// </summary>
    public static string IndexPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FullTextSearch", "Index");
}
