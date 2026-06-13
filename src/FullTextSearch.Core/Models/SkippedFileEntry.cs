namespace FullTextSearch.Core.Models;

/// <summary>インデックス処理でスキップされたファイル 1 件（パスと理由）。</summary>
public sealed record SkippedFileEntry(string Path, string Reason);
