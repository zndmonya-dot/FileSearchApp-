namespace FullTextSearch.Core.UI;

/// <summary>フォルダ一覧の表示用ヘルパー。</summary>
public static class FolderListDisplay
{
    /// <summary>UI 表示用のパス区切り（パンくずと統一）。</summary>
    public const char DisplaySeparator = '/';

    /// <summary>一覧行の表示（ファイル名 + 任意の親フォルダプレフィックス）。</summary>
    public readonly record struct FileDisplayParts(string FileName, string? FolderPrefix);

    /// <summary>パスを UI 表示用に正規化する（<c>\</c> → <c>/</c>）。</summary>
    public static string NormalizeDisplayPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        return path.Trim().Replace('\\', DisplaySeparator);
    }

    /// <summary>一覧行の表示パーツを返す。直下ファイルは <see cref="FileDisplayParts.FolderPrefix"/> が null。</summary>
    public static FileDisplayParts FormatFileDisplay(string selectedFolderPath, string? filePath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return new FileDisplayParts(fileName, null);

        var relative = GetRelativePath(selectedFolderPath, filePath);
        if (string.IsNullOrEmpty(relative))
            return new FileDisplayParts(fileName, null);

        var normalizedFileName = NormalizeDisplayPath(fileName);
        if (relative.Equals(normalizedFileName, StringComparison.OrdinalIgnoreCase))
            return new FileDisplayParts(normalizedFileName, null);

        var lastSep = relative.LastIndexOf(DisplaySeparator);
        if (lastSep < 0)
            return new FileDisplayParts(normalizedFileName, null);

        var prefix = relative[..lastSep];
        return new FileDisplayParts(normalizedFileName, prefix);
    }

    /// <summary>選択フォルダからの相対パス（表示用 <c>/</c> 区切り）。</summary>
    public static string FormatFileName(string selectedFolderPath, string? filePath, string fileName)
    {
        var parts = FormatFileDisplay(selectedFolderPath, filePath, fileName);
        return parts.FolderPrefix == null
            ? parts.FileName
            : $"{parts.FolderPrefix}{DisplaySeparator}{parts.FileName}";
    }

    /// <summary><paramref name="filePath"/> を <paramref name="folderPath"/> 基準の相対パス（<c>/</c> 区切り）にする。</summary>
    public static string GetRelativePath(string folderPath, string filePath)
    {
        var root = folderPath.Trim().TrimEnd('\\', '/');
        var file = filePath.Trim().TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(file))
            return NormalizeDisplayPath(Path.GetFileName(file));

        if (file.Equals(root, StringComparison.OrdinalIgnoreCase))
            return NormalizeDisplayPath(Path.GetFileName(file));

        var prefix = root + Path.DirectorySeparatorChar;
        if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !file.StartsWith(root + '/', StringComparison.OrdinalIgnoreCase))
            return NormalizeDisplayPath(Path.GetFileName(file));

        var relative = file.Length > root.Length ? file[(root.Length + 1)..] : Path.GetFileName(file);
        return NormalizeDisplayPath(relative);
    }
}
