﻿using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;

namespace Nexus.Core
{
    public record AggregationSetup
    {
        /// <example>2020-02-01T00:00:00Z</example>
        public DateTime Begin { get; set; } = DateTime.UtcNow.Date.AddDays(-2);

        /// <example>2020-02-02T00:00:00Z</example>
        public DateTime End { get; set; } = DateTime.UtcNow.Date.AddDays(-1);

        /// <example>false</example>
        public bool Force { get; set; } = false;

        public List<Aggregation> Aggregations { get; set; } = new List<Aggregation>();
    }

    public record Aggregation
    {
        /// <example>/IN_MEMORY/TEST/ACCESSIBLE</example>
        public string CatalogId { get; set; } = string.Empty;

        /// <example>{ Mean: null, MeanPolar: "360" }</example>
        public Dictionary<AggregationMethod, string> Methods { get; set; } = new Dictionary<AggregationMethod, string>();

        /// <example>{ "IncludeGroup": "GroupA|GroupB", "ExcludeUnit": "deg", "IncludeResources": "T1" }</example>
        public Dictionary<AggregationFilter, string> Filters { get; set; } = new Dictionary<AggregationFilter, string>();

        /// <example>[ 00:00:01, 00:01:00, 00:10:00 ]</example>
        public List<TimeSpan> Periods { get; set; } = new List<TimeSpan>();
    }

    public enum AggregationMethod
    {
        Mean = 0,
        MeanPolar = 1,
        Min = 2,
        Max = 3,
        Std = 4,
        Rms = 5,
        MinBitwise = 6,
        MaxBitwise = 7,
        SampleAndHold = 8,
        Sum = 9
    }

    public enum AggregationFilter
    {
        IncludeResource = 0,
        ExcludeResource = 1,
        IncludeGroup = 2,
        ExcludeGroup = 3,
        IncludeUnit = 4,
        ExcludeUnit = 5
    }

    internal record AggregationResource
    {
        public Resource Resource { get; init; }

        public List<Aggregation> Aggregations { get; init; }
    }

    internal record AggregationUnit
    {
        public Aggregation Aggregation { get; init; }

        public TimeSpan Period { get; init; }

        public AggregationMethod Method { get; init; }

        public string Argument { get; init; }

        public double[] Buffer { get; init; }

        public int BufferPosition { get; set; }

        public string TargetFilePath { get; init; }
    }

    internal record AggregationInstruction(CatalogContainer Container, Dictionary<BackendSource, List<AggregationResource>> DataReaderToAggregationsMap);
}
