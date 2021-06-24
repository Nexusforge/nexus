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

    public delegate ConfigurationUnit GetConfigurationUnitDelegate(Dataset dataset);

    public record ReadInfo(
        string FilePath,
        Dataset Dataset,
        Memory<byte> Data,
        Memory<byte> Status,
        DateTime FileBegin,
        long FileOffset,
        long FileLength,
        long FileTotalLength
    );

    public record BackendSource
    {
        public string Type { get; init; }
        public Uri ResourceLocator { get; init; }
        public Dictionary<string, string>? Configuration { get; init; }
    }

    public record ConfigurationUnit(
        string[] PathSegments,
        string FileTemplate,
        string? FileDateTimePreselector,
        string? FileDateTimeSelector,
        TimeSpan FilePeriod,
        TimeSpan UtcOffset
    );
}
