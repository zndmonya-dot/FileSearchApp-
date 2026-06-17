namespace FullTextSearch.Core.UI;

/// <summary>フォルダ一覧テーブル（名前・内容・更新日時）の列幅計算。</summary>
public static class FolderTableColumnLayout
{
    public const int ColMinName = 96;
    public const int ColMinPreview = 80;
    public const int ColMinDate = 112;
    public const int ColMaxDate = 168;
    /// <summary>更新日時列の初期幅（px）。</summary>
    public const int ColDateDefault = 136;

    /// <summary>初回表示用の列幅を配分する。</summary>
    public static (int Name, int Preview, int Date) CreateInitial(int tableWidth)
    {
        if (tableWidth <= 0)
            return (ColMinName, ColMinPreview, ColDateDefault);

        var date = ColDateDefault;
        var maxPair = tableWidth - date;
        if (maxPair <= ColMinName + ColMinPreview)
            return (ColMinName, Math.Max(ColMinPreview, maxPair - ColMinName), date);

        var name = Math.Max(ColMinName, maxPair * 28 / 100);
        var preview = Math.Max(ColMinPreview, maxPair - name);
        return (name, preview, date);
    }

    /// <summary>テーブル幅に収める。余白は内容列へ（初期化・ウィンドウリサイズ時のみ）。</summary>
    public static (int Name, int Preview, int Date) FitToTable(
        int tableWidth, int name, int preview, int date, bool absorbSlackIntoPreview)
    {
        if (tableWidth <= 0)
            return (name, preview, date);

        date = Math.Clamp(date, ColMinDate, ColMaxDate);
        var maxPair = tableWidth - date;
        if (maxPair <= ColMinName + ColMinPreview)
            return (ColMinName, Math.Max(ColMinPreview, maxPair - ColMinName), date);

        name = Math.Clamp(name, ColMinName, maxPair - ColMinPreview);
        preview = Math.Clamp(preview, ColMinPreview, maxPair - ColMinName);

        var over = name + preview - maxPair;
        if (over > 0)
        {
            var fromPreview = Math.Min(over, preview - ColMinPreview);
            preview -= fromPreview;
            over -= fromPreview;
            if (over > 0)
                name = Math.Max(ColMinName, name - over);
        }
        else if (absorbSlackIntoPreview && name + preview < maxPair)
            preview = maxPair - name;

        return (name, preview, date);
    }

    /// <summary>隣接2列の合計幅を保ったまま境界を動かす（エクスプローラー式）。</summary>
    public static (int Primary, int Secondary) ResizeAdjacent(
        int startPrimary, int startSecondary, int delta,
        int minPrimary, int minSecondary, int? maxSecondary = null)
    {
        var total = startPrimary + startSecondary;
        if (total <= 0)
            return (startPrimary, startSecondary);

        var maxPrimary = total - minSecondary;
        var primary = Math.Clamp(startPrimary + delta, minPrimary, maxPrimary);
        var secondary = total - primary;

        if (secondary < minSecondary)
        {
            secondary = minSecondary;
            primary = total - secondary;
        }
        else if (maxSecondary.HasValue && secondary > maxSecondary.Value)
        {
            secondary = maxSecondary.Value;
            primary = total - secondary;
        }

        primary = Math.Clamp(primary, minPrimary, total - minSecondary);
        secondary = total - primary;
        return (primary, secondary);
    }
}
