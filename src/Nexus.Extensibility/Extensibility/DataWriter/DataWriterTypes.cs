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

    public record WriteRequest(
        DatasetRecord DatasetRecord,
        ReadOnlyMemory<double> data);
}
