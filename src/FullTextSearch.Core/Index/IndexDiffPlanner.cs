namespace FullTextSearch.Core.Index;

using FullTextSearch.Core;

/// <summary>差分更新の削除・更新対象を安全に判定する。</summary>
public static class IndexDiffPlanner
{
    /// <summary>インデックス済みファイル 1 件分のメタデータ。</summary>
    public readonly record struct IndexedFileEntry(string StoredPath, long LastModifiedTicks, int IndexVersion);

    /// <summary>
    /// 差分更新計画。削除は Lucene に保存されている StoredPath を Term に使う。
    /// </summary>
    public readonly record struct DiffPlan(
        IReadOnlyList<string> ToDeleteStoredPaths,
        IReadOnlyList<string> ToAddOrUpdatePaths,
        bool Aborted,
        string? AbortReason);

    /// <summary>
    /// 差分計画を作成する。
    /// </summary>
    /// <param name="indexedMap">キーは正規化済みフルパス。</param>
    /// <param name="diskFiles">キーは正規化済みフルパス。</param>
    /// <param name="normalizedFolders">スキャン対象フォルダ（正規化済み）。</param>
    /// <param name="currentIndexVersion">現在のインデックスバージョン。</param>
    /// <param name="fileExists">ファイル存在確認。省略時は <see cref="File.Exists"/>。</param>
    /// <param name="isExcludedFromScan">スキャン対象外か。null のときはディスク上に残るファイルは削除しない。</param>
    public static DiffPlan Plan(
        IReadOnlyDictionary<string, IndexedFileEntry> indexedMap,
        IReadOnlyDictionary<string, long> diskFiles,
        IReadOnlyList<string> normalizedFolders,
        int currentIndexVersion,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? isExcludedFromScan = null)
    {
        fileExists ??= File.Exists;
        var normalizedFolderRoots = normalizedFolders
            .Select(IndexPaths.NormalizeFolderPath)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ShouldAbortEmptyScan(indexedMap, diskFiles, fileExists))
        {
            return new DiffPlan([], [], true,
                IndexMessages.DiffAbortedFilesStillExistOnDisk(indexedMap.Count));
        }

        var toDelete = new List<string>();
        foreach (var (normalizedPath, entry) in indexedMap)
        {
            if (diskFiles.ContainsKey(normalizedPath))
                continue;

            if (!fileExists(entry.StoredPath))
            {
                toDelete.Add(entry.StoredPath);
                continue;
            }

            if (isExcludedFromScan?.Invoke(entry.StoredPath) == true)
            {
                toDelete.Add(entry.StoredPath);
                continue;
            }

            if (!IndexPaths.IsPathUnderAnyFolder(normalizedPath, normalizedFolderRoots))
            {
                // UNC とドライブレターなど表記が違うだけの同一ファイルは削除しない
                if (!IndexPaths.IsRepresentedInDiskScan(entry.StoredPath, diskFiles))
                    toDelete.Add(entry.StoredPath);
                continue;
            }

            // ディスク上に残り、対象フォルダ配下だが今回のスキャンに含まれない → 保持
        }

        var toAddOrUpdate = diskFiles.Keys
            .Where(path => !indexedMap.TryGetValue(path, out var entry)
                || entry.LastModifiedTicks != diskFiles[path]
                || entry.IndexVersion != currentIndexVersion)
            .ToList();

        if (ShouldAbortWouldWipeIndex(indexedMap, toDelete, toAddOrUpdate, diskFiles))
        {
            return new DiffPlan([], [], true,
                IndexMessages.DiffAbortedWouldWipeIndex(indexedMap.Count));
        }

        return new DiffPlan(toDelete, toAddOrUpdate, false, null);
    }

    /// <summary>
    /// インデックスに件数があるのにスキャン 0 件で、かつインデックス済みファイルがディスク上に残っている場合は中止する。
    /// </summary>
    internal static bool ShouldAbortEmptyScan(
        IReadOnlyDictionary<string, IndexedFileEntry> indexedMap,
        IReadOnlyDictionary<string, long> diskFiles,
        Func<string, bool> fileExists)
    {
        if (indexedMap.Count == 0 || diskFiles.Count > 0)
            return false;

        return indexedMap.Values.Any(entry => fileExists(entry.StoredPath));
    }

    /// <summary>
    /// スキャン結果はあるのに全件削除・再登録なしとなる計画は中止する。
    /// </summary>
    internal static bool ShouldAbortWouldWipeIndex(
        IReadOnlyDictionary<string, IndexedFileEntry> indexedMap,
        IReadOnlyList<string> toDelete,
        IReadOnlyList<string> toAddOrUpdate,
        IReadOnlyDictionary<string, long> diskFiles)
    {
        return indexedMap.Count > 0
            && toDelete.Count == indexedMap.Count
            && toAddOrUpdate.Count == 0
            && diskFiles.Count > 0;
    }
}
