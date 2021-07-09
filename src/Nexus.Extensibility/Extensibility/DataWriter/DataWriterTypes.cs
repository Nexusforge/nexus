using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Extensibility
{
    public record DataWriterContext()
    {
        public Uri ResourceLocator { get; init; }
        public Dictionary<string, string> Configuration { get; init; }
        public ILogger Logger { get; init; }
    }

    public record RepresentationRecordGroup(
        Catalog Catalog,
        string License, 
        RepresentationRecord[] RepresentationRecords);

    public record WriteRequestGroup(
        Catalog Catalog,
        WriteRequest[] Requests);

    public record WriteRequest(
        RepresentationRecord RepresentationRecord,
        ReadOnlyMemory<double> Data);
}
