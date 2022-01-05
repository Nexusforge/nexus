using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Models;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    internal interface IDataSourceController : IDisposable
    {
        Task InitializeAsync(ConcurrentDictionary<string, ResourceCatalog> catalogs, ILogger logger, CancellationToken cancellationToken);
        Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken);
        Task<ResourceCatalog> GetCatalogAsync(string catalogId, CancellationToken cancellationToken);
        Task<AvailabilityResponse> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken);
        Task<TimeRangeResponse> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken);
        Task<bool> IsDataOfDayAvailableAsync(string catalogId, DateTime day, CancellationToken cancellationToken);
        Task ReadAsync(DateTime begin, DateTime end, TimeSpan samplePeriod, CatalogItemPipeWriter[] catalogItemPipeWriters, IProgress<double> progress, CancellationToken cancellationToken);
    }
}