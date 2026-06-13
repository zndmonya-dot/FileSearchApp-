namespace FullTextSearch.Core.Index;

/// <summary>差分更新がデータ消失を防ぐため中止された場合。</summary>
public sealed class IndexUpdateAbortedException : Exception
{
    /// <summary>中止理由メッセージで例外を生成する。</summary>
    public IndexUpdateAbortedException(string message) : base(message) { }
}
