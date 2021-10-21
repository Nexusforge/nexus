using Nexus.DataModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    internal interface IDataSourceController : IDisposable
    {
        Task InitializeAsync(ResourceCatalog[] catalogs, CancellationToken cancellationToken);
        Task<ResourceCatalog[]> GetCatalogsAsync(CancellationToken cancellationToken);
        Task<AvailabilityResult> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, AvailabilityGranularity granularity, CancellationToken cancellationToken);
        Task<TimeRangeResult> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken);
        Task<bool> IsDataOfDayAvailableAsync(string catalogId, DateTime day, CancellationToken cancellationToken);
        Task ReadAsync(DateTime begin, DateTime end, TimeSpan samplePeriod, CatalogItemPipeWriter[] catalogItemPipeWriters, IProgress<double> progress, CancellationToken cancellationToken);
    }
}