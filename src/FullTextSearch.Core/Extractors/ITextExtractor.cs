namespace FullTextSearch.Core.Extractors;

/// <summary>
/// プレビュー種別（抽出器ごとの表示カテゴリ）
/// </summary>
public enum PreviewCategory
{
    /// <summary>テキスト／コードとしてプレビュー</summary>
    Text,
    /// <summary>Office（Word/Excel/PowerPoint）としてプレビュー</summary>
    Office,
    /// <summary>PDF としてプレビュー</summary>
    Pdf
}

/// <summary>
/// テキスト抽出器のインターフェース
/// </summary>
public interface ITextExtractor
{
    /// <summary>
    /// サポートする拡張子のリスト
    /// </summary>
    IEnumerable<string> SupportedExtensions { get; }

    /// <summary>
    /// プレビュー表示時の種別（テキスト／Office／PDF）
    /// </summary>
    PreviewCategory PreviewCategory { get; }

    /// <summary>
    /// 指定した拡張子をサポートしているか
    /// </summary>
    bool CanExtract(string extension);

    /// <summary>
    /// ファイルからテキストを抽出
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>抽出されたテキスト</returns>
    Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// テキスト抽出器のファクトリ
/// </summary>
public class TextExtractorFactory
{
    private readonly IEnumerable<ITextExtractor> _extractors;

    public TextExtractorFactory(IEnumerable<ITextExtractor> extractors)
    {
        _extractors = extractors;
    }

    /// <summary>
    /// 指定した拡張子に対応する抽出器を取得
    /// </summary>
    public ITextExtractor? GetExtractor(string extension)
    {
        return _extractors.FirstOrDefault(e => e.CanExtract(extension));
    }

    /// <summary>
    /// サポートする全ての拡張子を取得
    /// </summary>
    public IEnumerable<string> GetAllSupportedExtensions()
    {
        return _extractors.SelectMany(e => e.SupportedExtensions).Distinct();
    }
}


