using Nexus.Infrastructure;
using System;
using System.Collections.Generic;

namespace Nexus.Core
{
    public record ExportParameters
    {
        /// <example>2020-02-01T00:00:00Z</example>
        public DateTime Begin { get; set; } = DateTime.UtcNow.Date.AddDays(-2);

        /// <example>2020-02-02T00:00:00Z</example>
        public DateTime End { get; set; } = DateTime.UtcNow.Date.AddDays(-1);

        /// <example>Hour</example>
        public FileGranularity FileGranularity { get; set; } = FileGranularity.SingleFile;

        /// <example>CSV</example>
        public FileFormat FileFormat { get; set; } = FileFormat.CSV;

        /// <example>Web</example>
        public ExportMode ExportMode { get; set; } = ExportMode.Web;

        /// <example>["/IN_MEMORY/TEST/ACCESSIBLE/T1/1 s_mean", "/IN_MEMORY/TEST/ACCESSIBLE/V1/1 s_mean"]</example>
        public List<string> ChannelPaths { get; set; } = new List<string>();

        /// <example>{ "RowIndexFormat": "Index", "SignificantFigures": "4" }</example>
        public Dictionary<string, string> Configuration { get; set; } = new Dictionary<string, string>();
    }

    public static class ExportParametersExtensions
    {
        public static ExportParameters UpdateVersion(this ExportParameters parameters)
        {
            // here we could adapt old parameter dictionary names or initialize fields that have been added later
            return parameters;
        }
    }
}
