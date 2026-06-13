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
            var root = NormalizeFolderPath(folder);
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)
                || full.Equals(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
