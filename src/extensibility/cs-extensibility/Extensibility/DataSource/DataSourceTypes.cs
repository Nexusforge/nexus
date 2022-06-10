using Nexus.DataModel;
using System.Text.Json;

namespace Nexus.Extensibility
{
    /// <summary>
    /// The starter package for a data source.
    /// </summary>
    /// <param name="ResourceLocator">The resource locator.</param>
    /// <param name="SystemConfiguration">The system configuration.</param>
    /// <param name="SourceConfiguration">The source configuration.</param>
    /// <param name="RequestConfiguration">The request configuration.</param>
    public record DataSourceContext(
        Uri ResourceLocator, 
        JsonElement? SystemConfiguration,
        JsonElement? SourceConfiguration,
        JsonElement? RequestConfiguration);

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

    /// <summary>
    /// Reads the requested data.
    /// </summary>
    /// <param name="resourcePath">The path to the resource data to stream.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns></returns>
    public delegate Task<double[]> ReadDataHandler(
        string resourcePath,
        DateTime begin,
        DateTime end,
        CancellationToken cancellationToken);
}
