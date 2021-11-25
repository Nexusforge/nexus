using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Extensibility
{
    /// <summary>
    /// The starter package for a data source.
    /// </summary>
    /// <param name="ResourceLocator">The resource locator.</param>
    /// <param name="Configuration">The configuration.</param>
    /// <param name="Logger">The logger.</param>
    public record DataSourceContext(
        Uri ResourceLocator, 
        Dictionary<string, string> Configuration,
        ILogger Logger);

    /// <summary>
    /// A read request.
    /// </summary>
    /// <param name="CatalogItem">The <paramref name="CatalogItem"/> to be read.</param>
    /// <param name="Data">The data buffer.</param>
    /// <param name="Status">The status buffer. A value of 0x01 ('1') indicates that the corresponding value in the data buffer is valid, otherwise it is treated as <see cref="double.NaN"/>.</param>
    public record ReadRequest(
        CatalogItem CatalogItem,
        Memory<byte> Data,
        Memory<byte> Status);

    internal sealed record BackendSource(string Type, Uri ResourceLocator, Dictionary<string, string> Configuration, bool IsEnabled = true)
    {
        public override int GetHashCode()
        {
            return HashCode.Combine(this.Type, this.ResourceLocator);
        }

        public bool Equals(BackendSource? other)
        {
            if (other is null)
                return false;

            var typeEquals = this.Type == other.Type;
            var resourceLocatorEquals = this.ResourceLocator.Equals(other.ResourceLocator);
            var configurationEquals = this.Configuration
                                          .OrderBy(entry => entry.Key)
                                          .SequenceEqual(other.Configuration.OrderBy(entry => entry.Key));

            return typeEquals && resourceLocatorEquals && configurationEquals;
        }
    }
}
