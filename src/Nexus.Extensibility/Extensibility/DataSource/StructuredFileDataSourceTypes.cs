using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Extensibility
{
    public record FileSourceProvider()
    {
        public Func<CatalogItem, FileSource> Single { get; set; }
        public Dictionary<string, FileSource[]> All { get; set; }
    }

    public record ReadInfo(
        string FilePath,
        CatalogItem CatalogItem,
        Memory<byte> Data,
        Memory<byte> Status,
        DateTime FileBegin,
        long FileOffset,
        long FileBlock,
        long FileLength
    );

    public record FileSource(
        string[] PathSegments,
        string FileTemplate,
        string? FileDateTimePreselector,
        string? FileDateTimeSelector,
        TimeSpan FilePeriod,
        TimeSpan UtcOffset
    );
}
