using FullTextSearch.Core;
using FullTextSearch.Core.Models;
using FullTextSearch.Infrastructure.Sudachi;
using Lucene.Net.Documents;

namespace FullTextSearch.Infrastructure.Lucene;

/// <summary><see cref="IndexedDocument"/> を Lucene <see cref="Document"/> に変換する。</summary>
internal static class LuceneDocumentBuilder
{
    public static Document Create(IndexedDocument doc) =>
        new()
        {
            new StringField(LuceneIndexService.FieldFilePath, doc.FilePath, Field.Store.YES),
            new TextField(LuceneIndexService.FieldFileName, doc.FileName, Field.Store.YES),
            new StringField(LuceneIndexService.FieldFileNameLc, doc.FileName.ToLowerInvariant(), Field.Store.NO),
            new StringField(LuceneIndexService.FieldFolderPath, doc.FolderPath, Field.Store.YES),
            new TextField(LuceneIndexService.FieldContent, doc.Content, Field.Store.YES),
            new StringField(LuceneIndexService.FieldContentPreview, ContentPreviewHelper.ExtractFirstLine(doc.Content), Field.Store.YES),
            new TextField(
                LuceneIndexService.FieldContentNGram,
                new ListTokenStream(ContentNGram.BuildIndexTokens(doc.Content, doc.FileName))),
            new Int64Field(LuceneIndexService.FieldFileSize, doc.FileSize, Field.Store.YES),
            new Int64Field(LuceneIndexService.FieldLastModified, doc.LastModified.Ticks, Field.Store.YES),
            new Int32Field(LuceneIndexService.FieldIndexVersion, LuceneIndexService.CurrentIndexVersion, Field.Store.YES)
        };
}
