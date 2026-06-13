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
    /// <summary>設定ファイルのパス（LocalApplicationData/FullTextSearch/settings.json）。<see cref="AppSettingsService(TextExtractorFactory, string?)"/> の <c>settingsFilePath</c> で上書き可能（単体テスト用）。</summary>
    private static readonly string DefaultSettingsPath = Path.Combine(
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
    private readonly string _settingsPath;

    /// <summary>現在メモリ上の設定（<see cref="LoadAsync"/> / <see cref="SaveAsync"/> と同期）。</summary>
    public AppSettings Settings { get; private set; } = new();

    /// <summary>抽出器ファクトリを指定してサービスを初期化する。</summary>
    /// <param name="extractorFactory">抽出器ファクトリ。初回読み込み時の対象拡張子初期化に使う。</param>
    /// <param name="settingsFilePath">設定 JSON のパス。未指定のとき <c>LocalApplicationData/FullTextSearch/settings.json</c>（単体テストでは一時パスを渡せる）。</param>
    public AppSettingsService(TextExtractorFactory extractorFactory, string? settingsFilePath = null)
    {
        _extractorFactory = extractorFactory;
        _settingsPath = string.IsNullOrWhiteSpace(settingsFilePath) ? DefaultSettingsPath : settingsFilePath;
    }

    /// <summary>設定ファイルを読み込む。存在しない場合は初回用に拡張子を設定して保存する。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_settingsPath))
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

            var json = await File.ReadAllTextAsync(_settingsPath, cancellationToken);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

            if (settings != null)
            {
                var needsResave = false;
                lock (_lock)
                {
                    Settings = settings;
                    var before = NormalizeExtensions(Settings.TargetExtensions ?? new List<string>());
                    var sanitized = SanitizeTargetExtensions(before);
                    Settings.TargetExtensions = sanitized;
                    needsResave = sanitized.Count != before.Count;
                }
                if (needsResave)
                    await SaveAsync(cancellationToken);
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
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json;
            lock (_lock)
            {
                Settings.TargetExtensions = SanitizeTargetExtensions(
                    NormalizeExtensions(Settings.TargetExtensions ?? new List<string>()));
                json = JsonSerializer.Serialize(Settings, JsonOptions);
            }

            await File.WriteAllTextAsync(_settingsPath, json, cancellationToken);
        }
        catch (Exception)
        {
            // 設定の保存に失敗した場合は無視
        }
    }

    /// <summary>抽出器が対応する拡張子だけに絞る（UI に無い .bin 等の残骸を除去）。</summary>
    private List<string> SanitizeTargetExtensions(List<string> extensions)
    {
        if (extensions.Count == 0) return extensions;
        var allowed = GetAllowedExtensionSet();
        return extensions.Where(allowed.Contains).ToList();
    }

    private HashSet<string> GetAllowedExtensionSet() =>
        _extractorFactory.GetAllSupportedExtensions()
            .Select(PreviewHelper.NormalizeExtension)
            .Where(e => !string.IsNullOrEmpty(e))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

