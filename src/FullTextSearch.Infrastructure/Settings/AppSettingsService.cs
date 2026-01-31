using System.Text.Json;
using FullTextSearch.Core.Models;

namespace FullTextSearch.Infrastructure.Settings;

/// <summary>
/// アプリケーション設定サービスの実装
/// </summary>
public class AppSettingsService : IAppSettingsService
{
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

    public AppSettings Settings { get; private set; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                // デフォルト設定を保存
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
                }
            }
        }
        catch (Exception)
        {
            // 設定の読み込みに失敗した場合はデフォルト値を使用
            Settings = new AppSettings();
        }
    }

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

}

