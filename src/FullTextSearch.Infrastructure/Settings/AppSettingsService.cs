// アプリ設定の永続化。LocalApplicationData/FullTextSearch/settings.json に JSON で保存。
using System.Text.Json;
using FullTextSearch.Core.Extractors;
using FullTextSearch.Core.Models;
using FullTextSearch.Core.Preview;

namespace FullTextSearch.Infrastructure.Settings;

/// <summary>
/// アプリケーション設定サービスの実装。JSON ファイルの読み書きと初回時の拡張子初期化を行う。
/// </summary>
public class AppSettingsService : IAppSettingsService
{
    /// <summary>設定ファイルのパス（LocalApplicationData/FullTextSearch/settings.json）</summary>
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FullTextSearch",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _lock = new();
    private readonly TextExtractorFactory _extractorFactory;

    public AppSettings Settings { get; private set; } = new();

    /// <summary>抽出器ファクトリは初回読み込み時に対象拡張子を初期化するために使用する。</summary>
    public AppSettingsService(TextExtractorFactory extractorFactory)
    {
        _extractorFactory = extractorFactory;
    }

    /// <summary>設定ファイルを読み込む。存在しない場合は初回用に拡張子を設定して保存する。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                // 初回: 対象拡張子は抽出器が対応する全拡張子を動的に設定
                lock (_lock)
                {
                    Settings = new AppSettings();
                    Settings.TargetExtensions = NormalizeExtensions(_extractorFactory.GetAllSupportedExtensions().ToList());
                }
                await SaveAsync(cancellationToken);
                return;
            }

            var json = await File.ReadAllTextAsync(SettingsPath, cancellationToken);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

            if (settings != null)
            {
                lock (_lock)
                {
                    Settings = settings;
                    Settings.TargetExtensions = NormalizeExtensions(Settings.TargetExtensions ?? new List<string>());
                }
            }
        }
        catch (Exception)
        {
            // 設定の読み込みに失敗した場合はデフォルト値を使用
            Settings = new AppSettings();
        }
    }

    /// <summary>現在の設定を JSON ファイルに保存する。</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(Settings, JsonOptions);
            }

            await File.WriteAllTextAsync(SettingsPath, json, cancellationToken);
        }
        catch (Exception)
        {
            // 設定の保存に失敗した場合は無視
        }
    }

    /// <summary>拡張子を「.」+ 小文字に正規化し重複を除く</summary>
    private static List<string> NormalizeExtensions(List<string> extensions)
    {
        if (extensions == null || extensions.Count == 0) return new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var e in extensions)
        {
            var x = PreviewHelper.NormalizeExtension(e);
            if (string.IsNullOrEmpty(x) || !seen.Add(x)) continue;
            result.Add(x);
        }
        return result;
    }

}

