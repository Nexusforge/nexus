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
        public string DataSourceId { get; init; }
        public Uri ResourceLocator { get; init; }
        public Dictionary<string, string>? Parameters { get; init; }
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
