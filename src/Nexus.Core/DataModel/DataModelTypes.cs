using Nexus.Extensibility;
using System;
using System.Collections.Generic;

namespace Nexus.DataModel
{
    public record NexusDatabaseConfig()
    {
        public string AggregationDataReaderRootPath { get; set; }
        public List<Dictionary<string, string>> ExtensionReferences { get; set; }
        public List<BackendSource> BackendSources { get; set; }
    }

    public enum AvailabilityGranularity
    {
        Day,
        Month
    }

    public record CatalogProperties
    {
        public string Id { get; set; }
        public bool IsQualityControlled { get; set; }
        public bool IsHidden { get; set; }
        public List<string> Logbook { get; set; }
        public List<string> GroupMemberships { get; set; }
    }

    public record AvailabilityResult
    {
        public BackendSource BackendSource { get; set; }
        public Dictionary<DateTime, double> Data { get; set; }
    }

    public record TimeRangeResult
    {
        public BackendSource BackendSource { get; set; }
        public DateTime Begin { get; set; }
        public DateTime End { get; set; }
    }
}
