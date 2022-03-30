using Nexus.Core;
using Nexus.DataModel;
using System.Collections.Concurrent;

namespace Nexus.Extensibility
{
    internal interface IDataSourceController : IDisposable
    {
        Task InitializeAsync(ConcurrentDictionary<string, ResourceCatalog> catalogs, ILogger logger, CancellationToken cancellationToken);
        Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken);
        Task<ResourceCatalog> GetCatalogAsync(string catalogId, CancellationToken cancellationToken);
        Task<CatalogAvailability> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken);
        Task<CatalogTimeRange> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken);
        Task<bool> IsDataOfDayAvailableAsync(string catalogId, DateTime day, CancellationToken cancellationToken);
        Task ReadAsync(DateTime begin, DateTime end, TimeSpan samplePeriod, CatalogItemRequestPipeWriter[] catalogItemRequestPipeWriters, IProgress<double> progress, CancellationToken cancellationToken);
    }
}