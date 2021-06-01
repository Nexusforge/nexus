using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Extensibility
{
    public record DataAccessDescriptions()
    {
        public GetDataAccessDescriptionDelegate Single { get; set; }
        public List<DataAccessDescription> All { get; set; }
    }

    public delegate DataAccessDescription GetDataAccessDescriptionDelegate(Dataset dataset);

    public record ReadInfo<T>(
        string FilePath,
        Dataset Dataset,
        Memory<T> Data,
        Memory<byte> Status,
        DateTime FileBegin,
        long Offset,
        long ExpectedTotalSize
    ) where T : unmanaged;

    public record DataSourceRegistration
    {
        public string RootPath { get; set; }
        public string DataSourceId { get; set; }
    }

    public record DataAccessDescription(
        string[] PathSegments,
        string FileTemplate,
        string? FileDateTimePreselector,
        string? FileDateTimeSelector,
        TimeSpan FilePeriod,
        TimeSpan UtcOffset
    );
}
