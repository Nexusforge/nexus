using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Nexus.Extensibility
{
    public record DataWriterContext()
    {
        public string TargetFolder { get; init; }
        public Dictionary<string, string> Configuration { get; init; }
        public ILogger Logger { get; init; }
    }
}
