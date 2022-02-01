using Nexus.Core;
using Nexus.DataModel;
using Nexus.Writers;
using System;
using System.Collections.Generic;

namespace Nexus.Services
{
    public record ExportParameters
    {
        /// <example>2020-02-01T00:00:00Z</example>
        public DateTime Begin { get; set; } = DateTime.UtcNow.Date.AddDays(-2);

        /// <example>2020-02-02T00:00:00Z</example>
        public DateTime End { get; set; } = DateTime.UtcNow.Date.AddDays(-1);

        /// <example>00:00:00</example>
        public TimeSpan FilePeriod { get; set; }

        /// <example>Nexus.Writers.Csv</example>
        public string Type { get; set; } = typeof(Csvw).FullName ?? throw new Exception("full name is null");

        /// <example>["/IN_MEMORY/TEST/ACCESSIBLE/T1/1_s_mean", "/IN_MEMORY/TEST/ACCESSIBLE/V1/1_s_mean"]</example>
        public string[] ResourcePaths { get; set; } = new string[0];

        /// <example>{ "RowIndexFormat": "Index", "SignificantFigures": "4" }</example>
        public Dictionary<string, string> Configuration { get; set; } = new Dictionary<string, string>();
    }

    internal static class ExportParametersExtensions
    {
        public static ExportParameters UpdateVersion(this ExportParameters parameters)
        {
            // here we could adapt old parameter dictionary names or initialize fields that have been added later
            return parameters;
        }
    }

    internal record ExportContext(
        TimeSpan SamplePeriod,
        Dictionary<CatalogContainer, IEnumerable<CatalogItem>> CatalogItemsMap,
        ExportParameters ExportParameters);
}
