using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Extensibility
{
    public record DataSourceContext(
        Uri ResourceLocator, 
        Dictionary<string, string> Configuration,
        ILogger Logger);

    public record ReadRequest(
        CatalogItem CatalogItem,
        Memory<byte> Data,
        Memory<byte> Status);

    public sealed record BackendSource(string Type, Uri ResourceLocator, Dictionary<string, string> Configuration, bool IsEnabled = true)
    {
        public override int GetHashCode()
        {
            return HashCode.Combine(this.Type, this.ResourceLocator);
        }

        public bool Equals(BackendSource? other)
        {
            return other is not null &&
                this.Type == other.Type &&
                this.ResourceLocator == other.ResourceLocator;
        }
    }
}
