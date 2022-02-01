using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nexus.Writers
{
    internal record struct Dialect(
        string CommentPrefix,
        int SkipRows,
        bool Header,
        int HeaderRowCount);

    internal record struct Column(
        string Titles,
        string DataType,
        IReadOnlyDictionary<string, string>? Properties);

    internal record struct TableSchema(
        Column[] Columns,
        IReadOnlyDictionary<string, string>? Properties);

    internal record struct CsvMetadata(
        [property: JsonPropertyName("@context")] string Context,
        string Url,
        Dialect Dialect,
        TableSchema TableSchema);
}
