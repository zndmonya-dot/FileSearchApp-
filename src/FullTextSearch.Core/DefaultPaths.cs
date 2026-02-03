// アプリのデフォルトパスを一元定義。インデックス保存先など。
namespace FullTextSearch.Core;

/// <summary>
/// アプリケーションのデフォルトパス（一元定義）。LocalApplicationData 配下を使用する。
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
