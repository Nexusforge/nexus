using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;

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
}
