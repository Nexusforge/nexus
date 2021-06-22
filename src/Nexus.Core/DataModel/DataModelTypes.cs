using Nexus.Extensibility;
using System;
using System.Collections.Generic;

namespace Nexus.DataModel
{
    public record NexusDatabaseConfig()
    {
        public string AggregationDataReaderRootPath { get; set; }
        public List<DataSourceRegistration> DataSourceRegistrations { get; set; }
    }

    public enum AvailabilityGranularity
    {
        Day,
        Month
    }

    public enum CatalogLicensingScheme
    {
        None = 0,
        ManualRequest = 1,
        AcceptLicense = 2
    }

    public record CatalogSettings(string Id)
    {
        public string Contact { get; set; }
        public bool IsQualityControlled { get; set; }
        public bool IsHidden { get; set; }
        public CatalogLicense License { get; set; }
        public List<string> Logbook { get; set; }
        public List<string> GroupMemberships { get; set; }
    }

    public record CatalogLicense
    {
        public CatalogLicensingScheme LicensingScheme { get; init; } = CatalogLicensingScheme.None;
        public string DisplayMessage { get; init; } = string.Empty;
        public string FileMessage { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
    }

    public record AvailabilityResult
    {
        public DataSourceRegistration DataSourceRegistration { get; set; }
        public Dictionary<DateTime, double> Data { get; set; }
    }

    public record TimeRangeResult
    {
        public DataSourceRegistration DataSourceRegistration { get; set; }
        public DateTime Begin { get; set; }
        public DateTime End { get; set; }
    }
}
