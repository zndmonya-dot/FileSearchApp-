namespace FullTextSearch.Core.Index;

/// <summary>インデックス差分比較用のパス正規化。</summary>
public static class IndexPaths
{
    /// <summary>C: や C:\ をドライブルート C:\ に正規化する。</summary>
    public static string NormalizeFolderPath(string folder)
    {
        var s = folder.TrimEnd('\\', '/').Trim();
        if (s.Length == 2 && char.IsLetter(s[0]) && s[1] == ':')
            return s + "\\";
        if (s.Length == 1 && char.IsLetter(s[0]))
            return s + ":\\";
        return Path.GetFullPath(s);
    }

    /// <summary>ファイルパスをフルパスに正規化する（\\?\ 長いパス接頭辞を除去）。</summary>
    public static string NormalizeFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var trimmed = path.Trim();
        if (trimmed.StartsWith(@"\\?\", StringComparison.Ordinal))
            trimmed = trimmed[4..];

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    /// <summary>ファイルパスが、正規化済みフォルダ一覧のいずれかの配下（または同一）か。</summary>
    public static bool IsPathUnderAnyFolder(string filePath, IReadOnlyList<string> normalizedFolderPaths)
    {
        var full = NormalizeFilePath(filePath);
        foreach (var folder in normalizedFolderPaths)
        {
            if (full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase)
                || full.Equals(folder, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
