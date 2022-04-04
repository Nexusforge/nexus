using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using System.Diagnostics;
using System.Security.Claims;

namespace Nexus.Core
{
    [DebuggerDisplay("{Id,nq}")]
    internal class CatalogContainer
    {
        private SemaphoreSlim _semaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        private CatalogInfo? _catalogInfo;
        private CatalogContainer[]? _childCatalogContainers;
        private ICatalogManager _catalogManager;
        private IDatabaseManager _databaseManager;
        private IDataControllerService _dataControllerService;

        public CatalogContainer(
            CatalogRegistration catalogRegistration,
            ClaimsPrincipal? owner,
            DataSourceRegistration dataSourceRegistration,
            CatalogMetadata metadata,
            ICatalogManager catalogManager,
            IDatabaseManager databaseManager,
            IDataControllerService dataControllerService)
        {
            Id = catalogRegistration.Path;
            IsTransient = catalogRegistration.IsTransient;
            Owner = owner;
            DataSourceRegistration = dataSourceRegistration;
            Metadata = metadata;

            _catalogManager = catalogManager;
            _databaseManager = databaseManager;
            _dataControllerService = dataControllerService;
        }

        public string Id { get; }

        public bool IsTransient { get; }

        public ClaimsPrincipal? Owner { get; }

        public string PhysicalName => Id.TrimStart('/').Replace('/', '_');

        public DataSourceRegistration DataSourceRegistration { get; }

        public CatalogMetadata Metadata { get; internal set; }

        public static CatalogContainer CreateRoot(ICatalogManager catalogManager, IDatabaseManager databaseManager)
        {
            return new CatalogContainer(new CatalogRegistration("/"), null!, null!, null!, catalogManager, databaseManager, null!);
        }

        public async Task<IEnumerable<CatalogContainer>> GetChildCatalogContainersAsync(
            CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync();

            try
            {
                if (IsTransient || _childCatalogContainers is null)
                    _childCatalogContainers = await _catalogManager.GetCatalogContainersAsync(this, cancellationToken);

                return _childCatalogContainers;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<CatalogInfo> GetCatalogInfoAsync(CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync();

            try
            {
                await EnsureCatalogInfoAsync(cancellationToken);

                var catalogInfo = _catalogInfo;

                if (catalogInfo is null)
                    throw new Exception("this should never happen");

                return catalogInfo;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task UpdateMetadataAsync(CatalogMetadata metadata)
        {
            await _semaphore.WaitAsync();

            try
            {
                // persist
                using var stream = _databaseManager.WriteCatalogMetadata(Id);
                await JsonSerializerHelper.SerializeIntendedAsync(stream, metadata);

                // assign
                Metadata = metadata;

                // trigger merging of catalog and catalog overrides
                _catalogInfo = null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task EnsureCatalogInfoAsync(CancellationToken cancellationToken)
        {
            if (IsTransient || _catalogInfo is null)
            {
                var catalogBegin = default(DateTime);
                var catalogEnd = default(DateTime);

                using var controller = await _dataControllerService.GetDataSourceControllerAsync(DataSourceRegistration, cancellationToken);
                var catalog = await controller.GetCatalogAsync(Id, cancellationToken);

                // get begin and end of project
                var catalogTimeRange = await controller.GetTimeRangeAsync(catalog.Id, cancellationToken);

                // merge time range
                if (catalogBegin == DateTime.MinValue)
                    catalogBegin = catalogTimeRange.Begin;

                else
                    catalogBegin = new DateTime(Math.Min(catalogBegin.Ticks, catalogTimeRange.Begin.Ticks));

                if (catalogEnd == DateTime.MinValue)
                    catalogEnd = catalogTimeRange.End;

                else
                    catalogEnd = new DateTime(Math.Max(catalogEnd.Ticks, catalogTimeRange.End.Ticks));

                // merge catalog
                if (Metadata?.Overrides is not null)
                    catalog = catalog.Merge(Metadata.Overrides, MergeMode.NewWins);

                _catalogInfo = new CatalogInfo(catalogBegin, catalogEnd, catalog);
            }
        }
    }
}
