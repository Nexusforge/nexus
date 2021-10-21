using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Services
{
    public record ExportParameters
    {
        /// <example>2020-02-01T00:00:00Z</example>
        public DateTime Begin { get; init; } = DateTime.UtcNow.Date.AddDays(-2);

        /// <example>2020-02-02T00:00:00Z</example>
        public DateTime End { get; init; } = DateTime.UtcNow.Date.AddDays(-1);

        /// <example>00:00:00</example>
        public TimeSpan FilePeriod { get; init; }

        /// <example>Nexus.Builtin.Csv</example>
        public string Writer { get; init; } = "Nexus.Builtin.Csv";

        /// <example>["/IN_MEMORY/TEST/ACCESSIBLE/T1/1_s_mean", "/IN_MEMORY/TEST/ACCESSIBLE/V1/1_s_mean"]</example>
        public string[] ResourcePaths { get; init; }

        /// <example>{ "RowIndexFormat": "Index", "SignificantFigures": "4" }</example>
        public Dictionary<string, string> Configuration { get; init; }
    }

    public static class ExportParametersExtensions
    {
        public static ExportParameters UpdateVersion(this ExportParameters parameters)
        {
            // here we could adapt old parameter dictionary names or initialize fields that have been added later
            return parameters;
        }
    }

    internal record ExportContext(
        TimeSpan SamplePeriod,
        IEnumerable<CatalogItem> CatalogItems,
        ExportParameters ExportParameters);
}
