// アプリの動作モードの実装。実行ファイルと同じフォルダの appmode.json から
// モードを読み、sharedConfig またはインデックスフォルダ内の shared.json で共有設定を参照する。
using System.Text.Json;
using FullTextSearch.Core;

namespace FullTextSearch.Infrastructure.Settings;

/// <summary>
/// <see cref="IAppModeService"/> の実装。
/// </summary>
public class AppModeService : IAppModeService
{
    /// <summary>動作モード設定ファイル名（実行ファイルと同じフォルダに配置）。</summary>
    public const string AppModeFileName = "appmode.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _appModePath;
    private bool _initialized;

    /// <inheritdoc />
    public bool IsAdmin { get; private set; }

    /// <inheritdoc />
    public string? SharedIndexPath { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<string> SharedTargetFolders { get; private set; } = Array.Empty<string>();

    /// <inheritdoc />
    public IReadOnlyList<int>? SharedAutoRebuildDailyHours { get; private set; }

    /// <inheritdoc />
    public string? SharedConfigPath { get; private set; }

    /// <summary>動作モード設定ファイルのパスを指定して初期化する。</summary>
    /// <param name="appModeFilePath">appmode.json のパス。未指定のとき実行フォルダ直下（単体テストでは一時パスを渡せる）。</param>
    public AppModeService(string? appModeFilePath = null)
    {
        _appModePath = string.IsNullOrWhiteSpace(appModeFilePath)
            ? Path.Combine(AppContext.BaseDirectory, AppModeFileName)
            : appModeFilePath;
    }

    /// <inheritdoc />
    public void Initialize()
    {
        if (_initialized) return;
        ApplyConfig(LoadEffectiveConfig());
        _initialized = true;
    }

    /// <inheritdoc />
    public bool TryLoadSharedConfigFromIndexPath(string? indexPath)
    {
        var targetPath = ResolveSharedConfigPath(indexPath);
        if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
            return false;

        var shared = LoadAppModeFile(targetPath);
        if (shared == null)
            return false;

        ApplySharedValues(shared);
        return true;
    }

    /// <inheritdoc />
    public string? ResolveSharedConfigPath(string? indexPath)
    {
        if (!string.IsNullOrWhiteSpace(SharedConfigPath))
            return SharedConfigPath;

        var normalizedIndexPath = indexPath?.Trim().TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(normalizedIndexPath))
            return null;

        return Path.Combine(normalizedIndexPath, DefaultPaths.SharedConfigFileName);
    }

    /// <inheritdoc />
    public bool TrySaveSharedConfig(
        string indexPath,
        IReadOnlyList<string> targetFolders,
        IReadOnlyList<int> autoRebuildDailyHours)
    {
        var targetPath = ResolveSharedConfigPath(indexPath);
        if (string.IsNullOrWhiteSpace(targetPath))
            return false;

        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var normalizedHours = AutoRebuildSchedule.NormalizeDailyHours(autoRebuildDailyHours);
            var payload = new SharedConfigPayload
            {
                IndexPath = indexPath.Trim(),
                TargetFolders = targetFolders
                    .Select(f => f.Trim().TrimEnd('\\', '/'))
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                AutoRebuildDailyHours = normalizedHours
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(targetPath, json);
            SharedIndexPath = payload.IndexPath;
            SharedTargetFolders = payload.TargetFolders ?? new List<string>();
            SharedAutoRebuildDailyHours = normalizedHours;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyConfig(AppModeConfig? config)
    {
        SharedConfigPath = NormalizePath(config?.SharedConfig);
        ApplySharedValues(config);
        IsAdmin = ResolveIsAdmin(config);
    }

    private void ApplySharedValues(AppModeConfig? config)
    {
        SharedIndexPath = config?.IndexPath;
        SharedTargetFolders = config?.TargetFolders as IReadOnlyList<string> ?? Array.Empty<string>();
        SharedAutoRebuildDailyHours = config?.AutoRebuildDailyHours != null
            ? AutoRebuildSchedule.NormalizeDailyHours(config.AutoRebuildDailyHours)
            : null;
    }

    /// <summary>ローカル appmode.json と sharedConfig をマージした有効設定を返す。</summary>
    private AppModeConfig? LoadEffectiveConfig()
    {
        var local = LoadAppModeFile(_appModePath);
        if (local == null) return null;

        var sharedPath = ResolveSharedConfigPathForLoad(local);
        if (string.IsNullOrWhiteSpace(sharedPath) || !File.Exists(sharedPath))
            return local;

        var shared = LoadAppModeFile(sharedPath);
        if (shared == null) return local;

        if (!string.IsNullOrWhiteSpace(shared.IndexPath))
            local.IndexPath = shared.IndexPath;
        if (shared.TargetFolders is { Count: > 0 })
            local.TargetFolders = shared.TargetFolders;
        if (shared.AutoRebuildDailyHours != null)
            local.AutoRebuildDailyHours = AutoRebuildSchedule.NormalizeDailyHours(shared.AutoRebuildDailyHours);

        return local;
    }

    private static AppModeConfig? LoadAppModeFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AppModeConfig>(json, JsonOptions);
            if (config == null) return null;

            var indexPath = config.IndexPath?.Trim();
            config.IndexPath = string.IsNullOrWhiteSpace(indexPath) ? null : indexPath;
            config.SharedConfig = NormalizePath(config.SharedConfig);

            if (config.TargetFolders is { Count: > 0 })
            {
                config.TargetFolders = config.TargetFolders
                    .Select(f => f.Trim().TrimEnd('\\', '/'))
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (config.AutoRebuildDailyHours != null)
                config.AutoRebuildDailyHours = AutoRebuildSchedule.NormalizeDailyHours(config.AutoRebuildDailyHours);

            return config;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizePath(string? path)
    {
        var trimmed = path?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    /// <summary>起動時に shared.json を探すパス（appmode の sharedConfig または indexPath 配下）。</summary>
    private static string? ResolveSharedConfigPathForLoad(AppModeConfig local)
    {
        var configured = NormalizePath(local.SharedConfig);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var indexPath = local.IndexPath?.Trim().TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(indexPath))
            return null;

        return Path.Combine(indexPath, DefaultPaths.SharedConfigFileName);
    }

    /// <summary>
    /// appmode.json の mode から管理者/参照を決定する。
    /// - 未指定: 管理者
    /// - admin / mother / local: 管理者
    /// - reference / nonadmin / client / shared: 参照（非管理者）
    /// </summary>
    private static bool ResolveIsAdmin(AppModeConfig? config)
    {
        if (config?.ForceNonAdmin == true) return false;
        var mode = (config?.Mode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(mode))
            return true;

        if (string.Equals(mode, "mother", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "local", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(mode, "reference", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "nonadmin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "client", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "shared", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>ローカル appmode.json のスキーマ。</summary>
    private sealed class AppModeConfig
    {
        public string? IndexPath { get; set; }
        public List<string>? TargetFolders { get; set; }
        public List<int>? AutoRebuildDailyHours { get; set; }
        public bool ForceNonAdmin { get; set; }
        public string? Mode { get; set; }

        /// <summary>サーバ上の共有設定 JSON のパス（UNC 可）。</summary>
        public string? SharedConfig { get; set; }
    }

    /// <summary>sharedConfig が指す共有設定ファイルのスキーマ。</summary>
    private sealed class SharedConfigPayload
    {
        public string? IndexPath { get; set; }
        public List<string>? TargetFolders { get; set; }
        public List<int>? AutoRebuildDailyHours { get; set; }
    }
}
