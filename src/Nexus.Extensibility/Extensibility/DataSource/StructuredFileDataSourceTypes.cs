using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Extensibility
{
    public record Configuration()
    {
        public GetConfigurationUnitDelegate Single { get; set; }
        public List<ConfigurationUnit> All { get; set; }
    }

    public delegate ConfigurationUnit GetConfigurationUnitDelegate(DatasetRecord datasetRecord);

    public record ReadInfo(
        string FilePath,
        DatasetRecord DatasetRecord,
        Memory<byte> Data,
        Memory<byte> Status,
        DateTime FileBegin,
        long FileOffset,
        long FileLength,
        long FileTotalLength
    );

    public record ConfigurationUnit(
        string[] PathSegments,
        string FileTemplate,
        string? FileDateTimePreselector,
        string? FileDateTimeSelector,
        TimeSpan FilePeriod,
        TimeSpan UtcOffset
    );
}
