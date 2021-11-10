using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Extensibility
{
    public record FileSourceProvider(
        Func<CatalogItem, FileSource> Single, 
        Dictionary<string, FileSource[]> All);

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
