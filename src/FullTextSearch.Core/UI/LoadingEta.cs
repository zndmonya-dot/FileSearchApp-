namespace FullTextSearch.Core.UI;

/// <summary>読み込み・構築の残り時間見積もり。</summary>
public static class LoadingEta
{
    /// <summary>件数ベースの見積もりを出す最小処理件数。</summary>
    public const int MinProcessedForEstimate = 5;

    /// <summary>処理件数と経過時間から残り時間を推定する。</summary>
    public static TimeSpan? TryEstimateRemaining(DateTime startUtc, int processed, int total)
    {
        if (total <= 0 || processed < MinProcessedForEstimate || processed >= total)
            return null;

        var elapsed = DateTime.UtcNow - startUtc;
        if (elapsed.TotalSeconds < 0.5)
            return null;

        var secondsPerItem = elapsed.TotalSeconds / processed;
        var remainingSeconds = secondsPerItem * (total - processed);
        if (double.IsNaN(remainingSeconds) || remainingSeconds < 1)
            return TimeSpan.FromSeconds(1);

        return TimeSpan.FromSeconds(Math.Min(remainingSeconds, 24 * 3600));
    }
}
