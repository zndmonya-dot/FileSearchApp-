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
        if (string.IsNullOrWhiteSpace(filePath) || normalizedFolderPaths == null || normalizedFolderPaths.Count == 0)
            return false;

        var full = NormalizeFilePath(filePath);
        foreach (var folder in normalizedFolderPaths)
        {
            if (IsPathUnderFolder(full, NormalizeFolderPath(folder)))
                return true;
        }
        return false;
    }

    /// <summary>任意のパスがフォルダルート配下（または同一）か。正規化して比較する。</summary>
    public static bool IsPathUnderFolderRoot(string path, string folderRoot) =>
        IsPathUnderFolder(NormalizeFilePath(path), NormalizeFolderPath(folderRoot));

    /// <summary>正規化済みファイルパスが、正規化済みフォルダルート配下か。</summary>
    public static bool IsPathUnderFolder(string normalizedFilePath, string normalizedFolderRoot)
    {
        if (string.IsNullOrWhiteSpace(normalizedFolderRoot))
            return false;

        if (normalizedFilePath.Equals(normalizedFolderRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        // ドライブルート（C:\）は同一ドライブ上のすべてを含める。
        // 従来の root + "\\" 照合では C:\Users\... が C:\\ で始まらず常に不一致になっていた。
        if (IsDriveRootPath(normalizedFolderRoot))
            return IsOnDrive(normalizedFilePath, normalizedFolderRoot[0]);

        var root = normalizedFolderRoot.TrimEnd('\\', '/');
        var prefixBackslash = root + Path.DirectorySeparatorChar;
        if (normalizedFilePath.StartsWith(prefixBackslash, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefixSlash = root + '/';
        return normalizedFilePath.StartsWith(prefixSlash, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDriveRootPath(string normalizedFolderRoot) =>
        normalizedFolderRoot.Length == 3
        && char.IsLetter(normalizedFolderRoot[0])
        && normalizedFolderRoot[1] == ':'
        && (normalizedFolderRoot[2] == '\\' || normalizedFolderRoot[2] == '/');

    private static bool IsOnDrive(string normalizedFilePath, char driveLetter)
    {
        if (normalizedFilePath.Length < 2)
            return false;
        if (char.ToUpperInvariant(normalizedFilePath[0]) != char.ToUpperInvariant(driveLetter)
            || normalizedFilePath[1] != ':')
            return false;
        return normalizedFilePath.Length == 2
            || normalizedFilePath[2] == Path.DirectorySeparatorChar
            || normalizedFilePath[2] == '/';
    }

    /// <summary>
    /// インデックス保存パスと別表記だが、今回のスキャン結果に同一実ファイルとして含まれるか。
    /// UNC とドライブレターなどの不一致で誤削除しないための照合。
    /// </summary>
    public static bool IsRepresentedInDiskScan(
        string storedPath,
        IReadOnlyDictionary<string, long> diskFiles)
    {
        if (diskFiles.Count == 0 || string.IsNullOrWhiteSpace(storedPath))
            return false;

        var indexedNorm = NormalizeFilePath(storedPath);
        if (diskFiles.ContainsKey(indexedNorm))
            return true;

        FileInfo indexedInfo;
        try
        {
            indexedInfo = new FileInfo(storedPath);
            if (!indexedInfo.Exists)
                return false;
        }
        catch
        {
            return false;
        }

        var indexedTicks = indexedInfo.LastWriteTimeUtc.Ticks;
        var indexedLength = indexedInfo.Length;

        foreach (var (diskPath, ticks) in diskFiles)
        {
            if (string.Equals(indexedNorm, diskPath, StringComparison.OrdinalIgnoreCase))
                return true;

            if (ticks != indexedTicks)
                continue;

            try
            {
                var diskInfo = new FileInfo(diskPath);
                if (!diskInfo.Exists)
                    continue;
                if (diskInfo.Length == indexedLength && diskInfo.LastWriteTimeUtc.Ticks == ticks)
                    return true;
            }
            catch
            {
                // 照合失敗時は次候補へ
            }
        }

        return false;
    }
}
